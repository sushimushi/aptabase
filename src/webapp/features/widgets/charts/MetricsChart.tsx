import { EmptyState, ErrorState, LoadingState } from "../../primitives";
import {
  BarController,
  LineController,
  BarElement,
  LineElement,
  PointElement,
  CategoryScale,
  Chart,
  LinearScale,
  Tooltip,
  ScriptableContext,
} from "chart.js";
import Annotation, { LineAnnotationOptions } from "chartjs-plugin-annotation";
import { useEffect, useRef, useState } from "react";
import colors from "./colors";

Chart.defaults.font.family = "'Inter var', 'Helvetica Neue', 'Helvetica', 'Arial', sans-serif";

Chart.register(
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Annotation
);

type Props = {
  hasPartialData: boolean;
  labels: string[];
  activeMetric: "users" | "sessions";
  users: number[];
  sessions: number[];
  events: number[];
  showEvents: boolean;
  showAllLabels: boolean;
  isEmpty?: boolean;
  isLoading?: boolean;
  isError?: boolean;
  formatLabel: (label: string | number) => string;
  renderTooltip: (dataPoint: TooltipDataPoint) => JSX.Element;
};

type TooltipDataPoint = {
  label: string;
  points: Array<{
    name: string;
    value: number;
  }>;
};

export function MetricsChart(props: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const [tooltipDataPoint, setTooltipDataPoint] = useState<TooltipDataPoint | null>(null);
  let chartInstance: Chart;

  useEffect(() => {
    if (!canvasRef.current) return;

    chartInstance = new Chart(canvasRef.current, {
      data: {
        labels: props.labels,
        datasets: [
          {
            order: 2,
            type: "bar",
            label: props.activeMetric == "users" ? "Users" : "Sessions",
            yAxisID: props.activeMetric,
            data: props[props.activeMetric],
            backgroundColor: props.hasPartialData
              ? (ctx: ScriptableContext<"bar">) => {
                  const total = ctx.chart.data.datasets[0].data.length;
                  return total - 1 === ctx.dataIndex ? colors.primaryStripped : colors.primary;
                }
              : colors.primary,
            borderRadius: 2,
          },
          {
            order: 1,
            type: "line",
            label: "Events",
            data: props.events,
            yAxisID: "events",
            hidden: !props.showEvents,
            borderColor: colors.secondary,
            tension: 0.2,
            borderWidth: 3,
            pointRadius: 0,
            pointHoverRadius: 0,
            segment: {
              borderDash: props.hasPartialData
                ? (ctx) => {
                    const total = chartInstance?.data.datasets[0].data.length || 0;
                    return total - 1 === ctx.p1DataIndex ? [8, 8] : [];
                  }
                : [],
            },
          },
        ],
      },
      options: {
        animation: false,
        interaction: {
          mode: "index",
          intersect: false,
        },
        maintainAspectRatio: false,
        layout: {
          padding: 0,
        },
        scales: {
          x: {
            grid: {
              display: false,
            },
            ticks: {
              autoSkip: true,
              maxRotation: props.showAllLabels ? 45 : 0,
              autoSkipPadding: props.showAllLabels ? 0 : 20,
              maxTicksLimit: props.showAllLabels ? 0 : 8,
              callback: function (value) {
                const label = typeof value === "number" ? this.getLabelForValue(value) : value;
                return props.formatLabel(label);
              },
            },
            border: {
              display: false,
            },
          },
          [props.activeMetric]: {
            grid: {
              tickWidth: 0,
            },
            beginAtZero: true,
            ticks: {
              maxTicksLimit: 4,
            },
            border: {
              display: false,
              dash: [4, 4],
            },
          },
          events: {
            display: props.showEvents,
            position: "right",
            grid: {
              tickWidth: 0,
            },
            beginAtZero: true,
            ticks: {
              maxTicksLimit: 4,
            },
            border: {
              display: false,
              dash: [4, 4],
            },
          },
        },
        plugins: {
          annotation: {
            annotations: [
              {
                type: "line",
                display: false,
                borderColor: colors.highlight,
                borderWidth: (ctx) => {
                  const columns = ctx.chart.data.datasets[0].data.length;
                  const chartWidth = ctx.chart.width + 100;
                  return chartWidth / columns;
                },
              },
            ],
          },
          tooltip: {
            enabled: false,
            position: "nearest",

            external: function ({ tooltip, chart }) {
              if (!tooltipRef.current) return;

              const annotations = chartInstance.options.plugins?.annotation
                ?.annotations as LineAnnotationOptions[];
              const highlight = annotations[0];

              if (tooltip.opacity === 0) {
                tooltipRef.current.style.opacity = "0";
                highlight.display = false;
                chart.update("none");
                return;
              }

              const dataPoint = tooltip.dataPoints[0];
              const label = dataPoint.label;
              const points = tooltip.dataPoints.map((dataPoint) => ({
                name: dataPoint.dataset.label ?? "",
                value: dataPoint.parsed.y ?? 0,
              }));

              setTooltipDataPoint({ label, points });

              if (highlight && highlight.xMin !== dataPoint.dataIndex) {
                highlight.display = true;
                highlight.xMin = dataPoint.dataIndex;
                highlight.xMax = dataPoint.dataIndex;
                chart.update("none");
              }

              tooltipRef.current.style.opacity = "1";

              var offsetY = Math.min(
                ...tooltip.dataPoints.map((x) => x.element.tooltipPosition(true).y)
              );

              const isMobile = chart.height < 300;
              const topOffset = isMobile ? 320 : 160;

              const { offsetLeft: positionX } = chart.canvas;
              tooltipRef.current.style.left = positionX + tooltip.caretX + "px";
              tooltipRef.current.style.top = offsetY + topOffset + "px";
              tooltipRef.current.style.padding =
                tooltip.options.padding + "px " + tooltip.options.padding + "px";
            },
          },
        },
      },
    });

    return () => chartInstance.destroy();
  }, [canvasRef, props]);

  return (
    <div className="h-60 md:h-80 w-full">
      {props.isError ? (
        <ErrorState />
      ) : props.isLoading ? (
        <LoadingState />
      ) : props.isEmpty ? (
        <EmptyState />
      ) : (
        <>
          <canvas ref={canvasRef}></canvas>
          <div
            ref={tooltipRef}
            style={{ opacity: 0 }}
            className="absolute pointer-events-none shadow border bg-background p-2 rounded transform -translate-x-1/2"
          >
            {tooltipDataPoint ? props.renderTooltip(tooltipDataPoint) : null}
          </div>
        </>
      )}
    </div>
  );
}
