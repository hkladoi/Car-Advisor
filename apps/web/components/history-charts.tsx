"use client";

import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";

import { buildPriceChartRows, formatEnergyRateUnit, type EnergyPriceSeries, type PriceTimelineEvent } from "@/lib/history-api";

const colors: Record<string, string> = {
  Msrp: "#0b5d4b",
  ManufacturerPromotionPrice: "#b45309",
  DealerCashPrice: "#9f1239",
};

const money = (value: number) => new Intl.NumberFormat("vi-VN", { notation: "compact", maximumFractionDigits: 1 }).format(value);
const day = (value: number) => new Intl.DateTimeFormat("vi-VN", { month: "short", year: "2-digit", timeZone: "Asia/Ho_Chi_Minh" }).format(new Date(value));

export function PriceHistoryChart({ timeline }: { timeline: PriceTimelineEvent[] }) {
  const data = buildPriceChartRows(timeline);
  const series = [...new Set(timeline.filter((item) => item.valueKind === "CashPrice" && item.amount !== null && item.status === "Official").map((item) => item.series))];
  if (data.length === 0) return <p className="history-chart-empty">Chưa có mốc giá tiền mặt chính thức để vẽ biểu đồ.</p>;
  if (data.length === 1) return <p className="history-chart-empty">Hiện chỉ có một mốc giá chính thức. Mốc này vẫn có trong timeline nhưng chưa được vẽ thành đường xu hướng.</p>;
  return (
    <div className="history-chart" role="img" aria-label="Biểu đồ lịch sử giá tiền mặt chính thức">
      <ResponsiveContainer width="100%" height={280}>
        <LineChart data={data} margin={{ top: 12, right: 12, bottom: 8, left: 8 }}>
          <CartesianGrid strokeDasharray="3 5" vertical={false} />
          <XAxis dataKey="at" type="number" scale="time" domain={["dataMin", "dataMax"]} tickFormatter={day} />
          <YAxis width={70} tickFormatter={money} />
          <Tooltip labelFormatter={(value) => day(Number(value))} formatter={(value) => `${money(Number(value))} ₫`} />
          {series.map((key) => <Line key={key} type="stepAfter" dataKey={key} name={key} stroke={colors[key] ?? "#475569"} strokeWidth={2.5} connectNulls={false} dot={{ r: 4 }} />)}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

export function EnergySeriesChart({ series }: { series: EnergyPriceSeries }) {
  const data = series.observations.map((item) => ({ at: Date.parse(item.effectiveFrom), amount: item.amount }));
  if (data.length === 0) return null;
  if (data.length === 1) return <div className="energy-single-observation"><strong>{new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(data[0].amount)} {formatEnergyRateUnit(series)}</strong><span>Một mốc có nguồn · chưa vẽ đường xu hướng</span></div>;
  return (
    <div className="energy-history-chart" role="img" aria-label={`Lịch sử ${series.energyType} từ ${series.provider}`}>
      <ResponsiveContainer width="100%" height={170}>
        <LineChart data={data} margin={{ top: 14, right: 12, bottom: 4, left: 4 }}>
          <CartesianGrid strokeDasharray="3 5" vertical={false} />
          <XAxis dataKey="at" type="number" scale="time" domain={data.length === 1 ? [data[0].at - 86_400_000, data[0].at + 86_400_000] : ["dataMin", "dataMax"]} tickFormatter={day} />
          <YAxis width={64} tickFormatter={money} domain={["auto", "auto"]} />
          <Tooltip labelFormatter={(value) => day(Number(value))} formatter={(value) => `${new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(Number(value))} ${formatEnergyRateUnit(series)}`} />
          <Line type="stepAfter" dataKey="amount" name={series.energyType} stroke="#0b5d4b" strokeWidth={2.5} dot={{ r: 4 }} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
