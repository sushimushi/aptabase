using System.Net;
using Aptabase.Features.GeoIP;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.Ingestion;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EventsController : Controller
{
    private readonly ILogger _logger;
    private readonly IIngestionValidator _validator;
    private readonly IIngestionClient _ingestionClient;
    private readonly IUserHashService _userHashService;
    private readonly IGeoIPClient _geoIP;

    public EventsController(IIngestionValidator validator,
                            IIngestionClient ingestionClient,
                            IUserHashService userHashService,
                            IGeoIPClient geoIP,
                            ILogger<EventsController> logger)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _ingestionClient = ingestionClient ?? throw new ArgumentNullException(nameof(ingestionClient));
        _userHashService = userHashService ?? throw new ArgumentNullException(nameof(userHashService));
        _geoIP = geoIP ?? throw new ArgumentNullException(nameof(geoIP));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("/api/v0/event")]
    [EnableCors("AllowAny")]
    [EnableRateLimiting("EventIngestion")]
    public async Task<IActionResult> Single(
        [FromHeader(Name = "App-Key")] string? appKey,
        [FromHeader(Name = "User-Agent")] string? userAgent,
        [FromBody] EventBody body,
        CancellationToken cancellationToken
    )
    {
        appKey = appKey?.ToUpper() ?? "";

        var (valid, validationMessage) = _validator.IsValidBody(body);
        if (!valid)
        {
            _logger.LogWarning(validationMessage);
            return BadRequest(validationMessage);
        }

        var (appId, statusCode, message) = await ValidateAppKey(appKey);
        if (statusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning(message);
            return StatusCode((int)statusCode, message);
        }

        // We never expect the Web SDK to send the OS name, so it's safe to assume that if it's missing the event is coming from a browser
        var isWeb = string.IsNullOrEmpty(body.SystemProps.OSName);

        // For web events, we need to parse the user agent to get the OS name and version
        if (isWeb && !string.IsNullOrEmpty(userAgent))
        {
            var (osName, osVersion) = UserAgentParser.ParseOperatingSystem(userAgent);
            body.SystemProps.OSName = osName;
            body.SystemProps.OSVersion = osVersion;

            var (engineName, engineVersion) = UserAgentParser.ParseBrowser(userAgent);
            body.SystemProps.EngineName = engineName;
            body.SystemProps.EngineVersion = engineVersion;
        }

        // We can't rely on User-Agent header sent by the SDK for non-web events, so we fabricate one
        // This can be removed when this issue is implemented: https://github.com/aptabase/aptabase/issues/23
        if (!isWeb)
            userAgent = $"{body.SystemProps.OSName}/{body.SystemProps.OSVersion} {body.SystemProps.EngineName}/{body.SystemProps.EngineVersion} {body.SystemProps.Locale}";

        var location = _geoIP.GetClientLocation(HttpContext);
        var header = new EventHeader(appId, location.CountryCode, location.RegionName);
        var userId = await _userHashService.CalculateHash(body.Timestamp, appId, body.SessionId, userAgent ?? "");
        var row = NewEventRow(userId, header, body);
        await _ingestionClient.SendSingleAsync(row, cancellationToken);

        return Ok(new { });
    }

    [HttpPost("/api/v0/events")]
    [EnableRateLimiting("EventIngestion")]
    public async Task<IActionResult> Multiple(
        [FromHeader(Name = "App-Key")] string? appKey,
        [FromHeader(Name = "User-Agent")] string? userAgent,
        [FromBody] EventBody[] events,
        CancellationToken cancellationToken
    )
    {
        appKey = appKey?.ToUpper() ?? "";

        if (events.Length > 25)
            return BadRequest($"Too many events in request. Maximum is 25.");

        var validEvents = events.Where(e => { 
            var (valid, validationMessage) = _validator.IsValidBody(e);
            if (!valid)
                _logger.LogWarning(validationMessage);
            return valid;
        }).ToArray();

        if (!validEvents.Any())
            return Ok(new { });

        var (appId, statusCode, message) = await ValidateAppKey(appKey);
        if (statusCode != HttpStatusCode.OK)
        {
            _logger.LogWarning(message);
            return StatusCode((int)statusCode, message);
        }

        var location = _geoIP.GetClientLocation(HttpContext);
        var header = new EventHeader(appId, location.CountryCode, location.RegionName);

        var rows = await Task.WhenAll(validEvents.Select(async e => {
            var userId = await _userHashService.CalculateHash(e.Timestamp, appId, e.SessionId, userAgent ?? "");
            return NewEventRow(userId, header, e);
        }));

        await _ingestionClient.SendMultipleAsync(rows, cancellationToken);

        return Ok(new { });
    }

    private async Task<(string, HttpStatusCode, string)> ValidateAppKey(string appKey)
    {
        var (appId, status) = await _validator.IsAppKeyValid(appKey);

        (HttpStatusCode, string) result = status switch
        {
            AppKeyStatus.Missing => (HttpStatusCode.BadRequest, "Missing App-Key header. Find your app key on Aptabase console."),
            AppKeyStatus.InvalidFormat => (HttpStatusCode.BadRequest, $"Invalid format for app key '{appKey}'. Find your app key on Aptabase console."),
            AppKeyStatus.InvalidRegion => (HttpStatusCode.BadRequest, $"Invalid region for App Key '{appKey}'. This key is meant for another region. Find your app key on Aptabase console."),
            AppKeyStatus.NotFound => (HttpStatusCode.NotFound, $"Appplication not found with given app key '{appKey}'. Find your app key on Aptabase console."),
            _ => (HttpStatusCode.OK, string.Empty)
        };

        return (appId, result.Item1, result.Item2);
    }

    private EventRow NewEventRow(string userId, EventHeader header, EventBody body)
    {
        var appId = body.SystemProps.IsDebug ? $"{header.AppId}_DEBUG" : header.AppId;

        var locale = LocaleFormatter.FormatLocale(body.SystemProps.Locale);
        if (locale is null)
            _logger.LogWarning("Invalid locale {Locale} received from {OS} using {SdkVersion}", locale, body.SystemProps.OSName, body.SystemProps.SdkVersion);

        var (stringProps, numericProps) = body.SplitProps();
        return new EventRow
        {
            AppId = appId,
            EventName = body.EventName,
            Timestamp = body.Timestamp.ToUniversalTime().ToString("o"),
            UserId = userId,
            SessionId = body.SessionId,
            OSName = body.SystemProps.OSName ?? "",
            OSVersion = body.SystemProps.OSVersion ?? "",
            Locale = locale ?? "",
            AppVersion = body.SystemProps.AppVersion ?? "",
            EngineName = body.SystemProps.EngineName ?? "",
            EngineVersion = body.SystemProps.EngineVersion ?? "",
            AppBuildNumber = body.SystemProps.AppBuildNumber ?? "",
            SdkVersion = body.SystemProps.SdkVersion ?? "",
            CountryCode = header.CountryCode ?? "",
            RegionName = header.RegionName ?? "",
            City = "",
            StringProps = stringProps.ToJsonString(),
            NumericProps = numericProps.ToJsonString(),
            TTL = body.Timestamp.ToUniversalTime().Add(body.TTL).ToString("o"),
        };
    }
}
