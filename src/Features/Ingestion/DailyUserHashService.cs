using Aptabase.Data;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Dapper;

namespace Aptabase.Features.Ingestion;

public interface IUserHashService
{
    Task<string> CalculateHash(DateTime timestamp, string appId, string sessionId, string userAgent);
}

public class DailyUserHashService : IUserHashService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DailyUserHashService(IMemoryCache cache, IDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public async Task<string> CalculateHash(DateTime timestamp, string appId, string sessionId, string userAgent)
    {
        var cacheKey = $"USERID-{appId}-{sessionId}";

        // If we already have a cached user ID for this session, return it immediately
        // This avoid issues with the user ID changing in the middle of a session because of an IP change
        if (_cache.TryGetValue(cacheKey, out string? userId) && !string.IsNullOrEmpty(userId))
            return userId;

        var clientIP = _httpContextAccessor.HttpContext?.ResolveClientIpAddress() ?? "";
        var salt = await GetSaltFor(timestamp.Date.ToString("yyyy-MM-dd"), appId);
        var bytes = Encoding.UTF8.GetBytes($"{clientIP}|${userAgent}");
        var id = SHA256.HashData(bytes.Concat(salt).ToArray());

        userId = Convert.ToHexString(id);
        _cache.Set(cacheKey, userId, TimeSpan.FromHours(24));
        return userId;
    }

    private async Task<byte[]> GetSaltFor(string date, string appId)
    {
        var cacheKey = $"DAILY-SALT-{appId}-{date}";
        if (_cache.TryGetValue(cacheKey, out byte[]? cachedSalt) && cachedSalt != null)
            return cachedSalt;

        var storedSalt = await ReadOrCreateSalt(date, appId);
        _cache.Set(cacheKey, storedSalt, TimeSpan.FromDays(2));
        return storedSalt;
    }

    private async Task<byte[]> ReadOrCreateSalt(string date, string appId)
    {
        var newSalt = RandomNumberGenerator.GetBytes(16);
        await _db.Connection.ExecuteAsync($"INSERT INTO app_salts (app_id, date, salt) VALUES (@appId, @date, @newSalt) ON CONFLICT DO NOTHING", new { appId, date, newSalt });
        return await _db.Connection.ExecuteScalarAsync<byte[]>($"SELECT salt FROM app_salts WHERE app_id = @appId AND date = @date", new { appId, date });
    }
}