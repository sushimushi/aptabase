import { TopNChart } from "./charts";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { topAppVersions } from "./query";
import { useApps } from "../apps";

type Props = {
  appId: string;
};

export function TopAppVersionsWidget(props: Props) {
  const { buildMode } = useApps();
  const [searchParams] = useSearchParams();
  const period = searchParams.get("period") || "";
  const countryCode = searchParams.get("countryCode") || "";
  const appVersion = searchParams.get("appVersion") || "";
  const eventName = searchParams.get("eventName") || "";
  const osName = searchParams.get("osName") || "";

  const {
    isLoading,
    isError,
    data: rows,
  } = useQuery(
    ["top-appversions", buildMode, props.appId, period, countryCode, appVersion, eventName, osName],
    () =>
      topAppVersions({
        buildMode,
        appId: props.appId,
        period,
        countryCode,
        appVersion,
        eventName,
        osName,
      })
  );

  return (
    <TopNChart
      title="App Versions"
      searchParamKey="appVersion"
      isLoading={isLoading}
      isError={isError}
      labels={["Version", "Sessions"]}
      items={rows || []}
    />
  );
}
