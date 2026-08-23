import { ArrowLeft, CalendarRange, Droplets, ExternalLink, Zap } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { EnergySeriesChart } from "@/components/history-charts";
import { SiteFooter, SiteHeader } from "@/components/site-header";
import { formatDate } from "@/lib/catalog-api";
import { formatEnergyRateUnit, getEnergyPriceHistory } from "@/lib/history-api";

export const metadata: Metadata = {
  title: "Lịch sử giá năng lượng",
  description: "Lịch sử giá xăng, dầu và điện có hiệu lực, tách theo nguồn, provider, khu vực và bậc điện.",
};

type Search = Record<string, string | string[] | undefined>;

const one = (value: string | string[] | undefined) => Array.isArray(value) ? value[0] : value;

const energyTypeLabels: Record<string, string> = {
  Ron92E5: "Xăng E5 RON92",
  E10Ron95III: "Xăng E10 RON95-III",
  Diesel: "Dầu diesel",
  Electricity: "Điện sinh hoạt",
};

export default async function EnergyHistoryPage({ searchParams }: { searchParams: Promise<Search> }) {
  const params = await searchParams;
  const energyType = one(params.energyType) ?? "";
  const provider = one(params.provider)?.trim() ?? "";
  const regionCode = one(params.regionCode)?.trim().toUpperCase() || "VN";
  const parsedMonths = Number(one(params.months) ?? "12");
  const months = [6, 12, 24, 36, 60].includes(parsedMonths) ? parsedMonths : 12;
  const data = await getEnergyPriceHistory({ energyType: energyType || undefined, provider: provider || undefined, regionCode, months });

  return (
    <div className="energy-history-shell">
      <SiteHeader />
      <main className="energy-history-main">
        <Link className="detail-back" href="/calculators/energy"><ArrowLeft aria-hidden="true" size={17} /> Máy tính năng lượng</Link>
        <header className="energy-history-hero">
          <div>
            <p className="machine-label">FR-018 · EFFECTIVE-DATED · SOURCE FIRST</p>
            <h1>Lịch sử giá xăng, dầu và điện</h1>
            <p>Mỗi đường dữ liệu giữ nguyên loại năng lượng, nhà cung cấp, khu vực, đơn vị và bậc giá. Hệ thống không nối các đại lượng khác nhau thành một xu hướng giả.</p>
          </div>
          <CalendarRange aria-hidden="true" size={54} />
        </header>

        <form className="energy-history-filters" method="get">
          <label>Loại năng lượng<select name="energyType" defaultValue={energyType}><option value="">Tất cả</option><option value="Ron92E5">Xăng E5 RON92</option><option value="E10Ron95III">Xăng E10 RON95-III</option><option value="Diesel">Dầu diesel</option><option value="Electricity">Điện sinh hoạt</option></select></label>
          <label>Provider<input name="provider" defaultValue={provider} maxLength={200} placeholder="Để trống = tất cả" /></label>
          <label>Khu vực<input name="regionCode" defaultValue={regionCode} maxLength={20} /></label>
          <label>Cửa sổ<select name="months" defaultValue={String(months)}><option value="6">6 tháng</option><option value="12">12 tháng</option><option value="24">24 tháng</option><option value="36">36 tháng</option><option value="60">60 tháng</option></select></label>
          <button type="submit">Áp dụng</button>
        </form>

        <section className="energy-history-results" aria-labelledby="energy-series-title">
          <header><p className="machine-label">{data.series.length} SERIES · {data.window.months} MONTH WINDOW</p><h2 id="energy-series-title">Các chuỗi giá có nguồn</h2></header>
          {data.series.length === 0 ? <p className="history-empty-state">Không có bản ghi nguồn phù hợp bộ lọc. Hệ thống không tạo dữ liệu thay thế.</p> : (
            <div className="energy-series-grid">
              {data.series.map((series) => {
                const current = series.observations.find((item) => item.isCurrent) ?? series.observations.at(-1);
                const tier = series.energyType === "Electricity" ? `${series.tierFromInclusive}–${series.tierToInclusive ?? "∞"} kWh` : "Giá bán lẻ công bố";
                return (
                  <article className="energy-series-card" key={series.seriesKey}>
                    <header><span>{series.energyType === "Electricity" ? <Zap aria-hidden="true" size={18} /> : <Droplets aria-hidden="true" size={18} />}{energyTypeLabels[series.energyType] ?? series.energyType}</span><strong>{tier}</strong></header>
                    <p>{series.provider} · {series.regionCode}</p>
                    {current && <div className="energy-current-value"><span>{current.isCurrent ? "Hiện hành" : "Mốc gần nhất"}</span><strong>{new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(current.amount)} {formatEnergyRateUnit(series)}</strong></div>}
                    <EnergySeriesChart series={series} />
                    <div className="energy-observation-list">
                      {series.observations.map((item) => <div key={item.id}><span>{formatDate(item.effectiveFrom)}{item.effectiveTo ? ` – ${formatDate(item.effectiveTo)}` : " – nay"}</span><strong>{new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(item.amount)}</strong>{item.source ? <a href={item.source.url} target="_blank" rel="noreferrer">{item.source.name}<ExternalLink aria-hidden="true" size={12} /></a> : <span>Manual override có audit</span>}</div>)}
                    </div>
                  </article>
                );
              })}
            </div>
          )}
        </section>
        <p className="history-policy-note">Mỗi chuỗi giữ nguyên loại năng lượng, provider, khu vực, đơn vị và bậc điện; các chuỗi khác đại lượng không bị gộp thành một xu hướng. Cập nhật lúc {formatDate(data.generatedAt)}.</p>
      </main>
      <SiteFooter />
    </div>
  );
}
