/* eslint-disable @next/next/no-img-element */
import { ArrowLeft, CalendarClock, ExternalLink, Gift, GitCompareArrows, ImageOff, MapPin, ShieldCheck, TrendingDown, WalletCards } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { RegionSelector } from "@/components/region-selector";
import { PriceHistoryChart } from "@/components/history-charts";
import { RealWorldConsumptionPanel } from "@/components/real-world-consumption";
import { SiteFooter, SiteHeader } from "@/components/site-header";
import { SourceDetails } from "@/components/source-details";
import { WatchlistButton } from "@/features/account/watchlist-button";
import { formatDate, formatMoney, formatNumber, getCar, type CarDetailResponse } from "@/lib/catalog-api";
import { getDealerOfferHistory, getVehiclePriceHistory, type DealerOfferHistoryItem } from "@/lib/history-api";

function factValue(fact: CarDetailResponse["specifications"][number]): string {
  if (fact.status !== "Official") return "Chưa có dữ liệu chính thức";
  if (fact.numericValue !== null) return formatNumber(fact.numericValue, fact.unit);
  return fact.textValue ?? fact.enumValue ?? "Chưa có dữ liệu";
}

function featureValue(feature: CarDetailResponse["features"][number]): string {
  if (feature.status !== "Official") return "Chưa rõ";
  if (feature.booleanValue === true) return "Có";
  if (feature.booleanValue === false) return "Không có";
  if (feature.numericValue !== null) return formatNumber(feature.numericValue);
  return feature.textValue ?? feature.enumValue ?? "Chưa rõ";
}

function priceLabel(type: string, status: string, amount: number | null, currency: string): string {
  if (amount !== null) return formatMoney({ amount, currency });
  if (type === "Unannounced") return "Chưa công bố";
  if (type === "ExpectedPrice" || status === "Expected") return "Giá dự kiến — chưa xác nhận";
  return "Chưa xác định";
}

const priceTypeLabels: Record<string, string> = {
  Msrp: "Giá niêm yết (MSRP)",
  PromotionPrice: "Giá khuyến mại",
  ExpectedPrice: "Giá dự kiến",
  Unannounced: "Giá chưa công bố",
  DealerCashPrice: "Giá tiền mặt đại lý",
  DealerQuote: "Báo giá đại lý",
};

const powertrainLabels: Record<string, string> = {
  ICE: "Động cơ đốt trong",
  HEV: "Hybrid",
  PHEV: "Plug-in hybrid",
  EREV: "Điện mở rộng tầm",
  BEV: "Thuần điện",
};

const marketStatusLabels: Record<string, string> = {
  Active: "đang bán",
  Announced: "đã công bố",
  Discontinued: "ngừng bán",
  Unknown: "chưa rõ",
};

const priceSeriesLabels: Record<string, string> = {
  Msrp: "MSRP",
  ManufacturerPromotionPrice: "Giá khuyến mại hãng",
  ManufacturerPromotion: "Quyền lợi khuyến mại hãng",
  DealerCashPrice: "Giá tiền mặt đại lý",
  DealerCashOffer: "Quyền lợi tiền mặt đại lý",
  DealerQuote: "Báo giá tham khảo",
  ExpectedPrice: "Giá dự kiến",
  Unannounced: "Chưa công bố",
};

const rangePositionLabels: Record<string, string> = {
  At12MonthLow: "đang ở đáy 12 tháng",
  Near12MonthLow: "đang gần vùng thấp 12 tháng",
  MidRange: "đang ở giữa biên độ 12 tháng",
  Near12MonthHigh: "đang gần vùng cao 12 tháng",
  At12MonthHigh: "đang ở đỉnh 12 tháng",
  Flat: "không đổi trong các mốc đủ điều kiện",
};

export async function generateMetadata({ params }: { params: Promise<{ trimId: string }> }): Promise<Metadata> {
  const { trimId } = await params;
  const detail = await getCar(trimId);
  if (!detail) return { title: "Không tìm thấy phiên bản", robots: { index: false, follow: false } };
  const { car } = detail;
  return {
    title: `${car.brandName} ${car.modelName} ${car.trimName} MY${car.modelYear}`,
    description: `Giá, thông số, trang bị, ưu đãi còn hiệu lực và nguồn của ${car.brandName} ${car.modelName} ${car.trimName} tại Việt Nam.`,
  };
}

export default async function CarDetailPage({ params }: { params: Promise<{ trimId: string }> }) {
  const { trimId } = await params;
  const detail = await getCar(trimId);
  if (!detail) notFound();
  const [priceHistory, offerHistory] = await Promise.all([
    getVehiclePriceHistory(trimId),
    getDealerOfferHistory(trimId),
  ]);
  const { car } = detail;
  const cashBenefits = (offer: DealerOfferHistoryItem) => offer.benefits.filter((benefit) => benefit.isCashEquivalent);
  const giftBenefits = (offer: DealerOfferHistoryItem) => offer.benefits.filter((benefit) => !benefit.isCashEquivalent);
  const range = priceHistory.currentVsTwelveMonthRange;
  const structuredData = {
    "@context": "https://schema.org",
    "@type": "Vehicle",
    name: `${car.brandName} ${car.modelName} ${car.trimName}`,
    brand: { "@type": "Brand", name: car.brandName },
    model: car.modelName,
    vehicleModelDate: String(car.modelYear),
    vehicleConfiguration: car.trimName,
    vehicleTransmission: car.powertrainType,
    offers: car.currentPrice ? {
      "@type": "Offer",
      price: car.currentPrice.amount,
      priceCurrency: car.currentPrice.currency,
      availability: car.marketStatus === "Active" ? "https://schema.org/InStock" : "https://schema.org/PreOrder",
    } : undefined,
  };

  return (
    <div className="detail-shell">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData).replace(/</g, "\\u003c") }} />
      <SiteHeader />
      <main className="vehicle-detail">
        <Link className="detail-back" href="/cars"><ArrowLeft aria-hidden="true" size={17} /> Trở lại catalog</Link>

        <header className="detail-hero">
          <div className="detail-gallery">
            {detail.gallery.length > 0 ? (
              <img src={detail.gallery[0].url} alt={`${car.brandName} ${car.modelName} ${car.trimName}`} />
            ) : (
              <div className="detail-no-image"><ImageOff aria-hidden="true" size={38} /><strong>Chưa có ảnh được cấp quyền</strong><span>Catalog không sử dụng ảnh không rõ quyền.</span></div>
            )}
          </div>
          <div className="detail-summary">
            <p className="machine-label">{car.brandName.toUpperCase()} · MY{car.modelYear} · {powertrainLabels[car.powertrainType.toUpperCase()] ?? car.powertrainType}</p>
            <h1>{car.modelName} <span>{car.trimName}</span></h1>
            <p>{car.bodyType} · phân khúc {car.segment === "Unknown" ? "chưa rõ" : car.segment} · {marketStatusLabels[car.marketStatus] ?? car.marketStatus}</p>
            <div className="detail-controls"><RegionSelector /><SourceDetails source={detail.primarySource} /><Link className="detail-compare-link" href={`/compare?trims=${car.trimId}`}><GitCompareArrows aria-hidden="true" size={15} /> So sánh trim</Link><WatchlistButton trimId={car.trimId} /></div>
            <dl className="detail-price-summary">
              <div><dt>Giá hiện hành</dt><dd>{formatMoney(car.currentPrice)}</dd></div>
              <div><dt>MSRP</dt><dd>{formatMoney(car.msrp)}</dd></div>
            </dl>
          </div>
        </header>

        <section className="trim-switch" aria-labelledby="trim-switch-title">
          <div><p className="machine-label">TRIM SWITCH</p><h2 id="trim-switch-title">Các phiên bản cùng model</h2></div>
          <nav aria-label="Chọn phiên bản">
            {detail.trims.map((trim) => (
              <Link className={trim.selected ? "is-selected" : ""} aria-current={trim.selected ? "page" : undefined} href={`/cars/${trim.trimId}`} key={trim.trimId}>
                <strong>{trim.name}</strong><span>{formatMoney(trim.currentPrice)}</span>
              </Link>
            ))}
          </nav>
        </section>

        <div className="detail-columns">
          <div className="detail-primary">
            <section className="detail-section" aria-labelledby="prices-title">
              <header><p className="machine-label">PRICE FACTS</p><h2 id="prices-title">Giá và hiệu lực</h2></header>
              {detail.prices.length > 0 ? (
                <div className="fact-table">
                  {detail.prices.map((price) => (
                    <div className="fact-row" key={price.id}>
                      <div><strong>{priceTypeLabels[price.type] ?? price.type}</strong><span>{price.regionScope}</span></div>
                      <div className={price.amount === null ? "data-state data-state--unknown" : ""}>{priceLabel(price.type, price.status, price.amount, price.currency)}</div>
                      <div><span>Từ {formatDate(price.effectiveFrom)}</span>{price.effectiveTo && <span>đến {formatDate(price.effectiveTo)}</span>}</div>
                      <SourceDetails source={price.source} compact />
                    </div>
                  ))}
                </div>
              ) : <p className="empty-fact">Chưa có giá công khai cho trim này.</p>}

              <div className="price-history-block">
                <header><div><p className="machine-label">12-MONTH OBSERVED HISTORY</p><h3>Lịch sử giá và ưu đãi</h3></div><TrendingDown aria-hidden="true" size={24} /></header>
                {range.available && range.currentAmount !== null && range.twelveMonthMinimum !== null && range.twelveMonthMaximum !== null ? (
                  <div className="price-range-insight">
                    <div><span>Giá tiền mặt hiện tại</span><strong>{formatMoney({ amount: range.currentAmount, currency: range.currency })}</strong></div>
                    <div><span>Biên độ 12 tháng</span><strong>{formatMoney({ amount: range.twelveMonthMinimum, currency: range.currency })} – {formatMoney({ amount: range.twelveMonthMaximum, currency: range.currency })}</strong></div>
                    <p>{range.position ? rangePositionLabels[range.position] ?? range.position : ""} · {range.observationCount} quan sát / {range.distinctObservationDates} ngày khác nhau.</p>
                  </div>
                ) : (
                  <div className="price-range-insight price-range-insight--insufficient"><strong>Chưa đủ dữ liệu để kết luận giá đang thấp hay cao.</strong><span>{range.observationCount}/3 quan sát, trải {range.spanDays}/90 ngày tối thiểu. Không suy diễn từ một mốc giá.</span></div>
                )}
                <PriceHistoryChart timeline={priceHistory.timeline} />
                <div className="price-timeline" aria-label="Các mốc giá và quyền lợi">
                  {priceHistory.timeline.length === 0 ? <p className="empty-fact">Chưa có mốc lịch sử có nguồn.</p> : priceHistory.timeline.map((event) => (
                    <article key={`${event.id}-${event.effectiveFrom}-${event.series}`}>
                      <time dateTime={event.effectiveFrom}>{formatDate(event.effectiveFrom)}</time>
                      <div><strong>{priceSeriesLabels[event.series] ?? event.series}</strong><span>{event.valueKind === "CashBenefit" ? "Quyền lợi tiền mặt — không phải giá xe" : event.valueKind === "BenefitValue" ? "Giá trị quyền lợi — không tự quy đổi thành tiền mặt" : event.status}</span></div>
                      <b>{event.amount === null ? "Chưa công bố" : formatMoney({ amount: event.amount, currency: event.currency })}</b>
                      {event.source ? <a href={event.source.url} target="_blank" rel="noreferrer">Nguồn <ExternalLink aria-hidden="true" size={12} /></a> : <span className="history-manual">Manual override có audit</span>}
                    </article>
                  ))}
                </div>
                <p className="history-policy-note">Chỉ MSRP/giá khuyến mại/giá tiền mặt chính thức đi vào biên độ. Cash benefit và dealer quote bị loại khỏi phép so sánh.</p>
              </div>
            </section>

            <section className="detail-section" aria-labelledby="specs-title">
              <header><p className="machine-label">SPECIFICATIONS</p><h2 id="specs-title">Thông số kỹ thuật</h2></header>
              {detail.specifications.length > 0 ? (
                <div className="fact-table">
                  {detail.specifications.map((spec) => (
                    <div className="fact-row fact-row--compact" key={spec.code}>
                      <div><strong>{spec.label}</strong><span>{spec.group}</span></div>
                      <div className={spec.status !== "Official" ? "data-state data-state--unknown" : ""}>{factValue(spec)}</div>
                      <SourceDetails source={spec.source} compact />
                    </div>
                  ))}
                </div>
              ) : <p className="empty-fact">Chưa có thông số đã chuẩn hóa.</p>}
            </section>

            <RealWorldConsumptionPanel
              officialTrimFuelLitresPer100Km={car.specifications.fuelLitresPer100Km}
              references={detail.realWorldConsumption}
            />

            <section className="detail-section" aria-labelledby="features-title">
              <header><p className="machine-label">FEATURES</p><h2 id="features-title">Trang bị</h2></header>
              {detail.features.length > 0 ? (
                <div className="feature-facts">
                  {detail.features.map((feature) => (
                    <article key={feature.code}>
                      <div><span>{feature.group}</span><h3>{feature.label}</h3></div>
                      <strong className={feature.status !== "Official" ? "data-state data-state--unknown" : ""}>{featureValue(feature)}</strong>
                      <SourceDetails source={feature.source} compact />
                    </article>
                  ))}
                </div>
              ) : <p className="empty-fact">Chưa có trang bị được nguồn xác nhận.</p>}
            </section>

            <section className="detail-section" aria-labelledby="colors-title">
              <header><p className="machine-label">COLORS</p><h2 id="colors-title">Màu sắc</h2></header>
              {detail.colors.length > 0 ? (
                <div className="color-list">
                  {detail.colors.map((color) => (
                    <article key={color.code}><span style={color.hexHint ? { backgroundColor: color.hexHint } : undefined} aria-hidden="true" /><div><h3>{color.name}</h3><p>{color.availability}{color.extraPrice !== null ? ` · cộng ${formatMoney({ amount: color.extraPrice, currency: color.currency })}` : ""}</p></div></article>
                  ))}
                </div>
              ) : <p className="empty-fact">Chưa có bảng màu được nguồn xác nhận.</p>}
            </section>
          </div>

          <aside className="detail-aside">
            <section className="detail-panel" aria-labelledby="offers-title">
              <WalletCards aria-hidden="true" size={22} />
              <h2 id="offers-title">Ưu đãi đại lý</h2>
              {offerHistory.current.length > 0 ? offerHistory.current.map((offer) => (
                <article className="dealer-offer" key={offer.id}>
                  <h3>{offer.headline}</h3>
                  <p><MapPin aria-hidden="true" size={14} /> {offer.dealerName} · {offer.branchName} · {offer.provinceCode}</p>
                  <div className="offer-benefits"><strong>Giá trị tiền mặt</strong>{cashBenefits(offer).length > 0 ? cashBenefits(offer).map((benefit, index) => <span key={`${benefit.type}-${index}`}>{benefit.type}: {benefit.cashValue !== null ? formatMoney({ amount: benefit.cashValue, currency: benefit.currency }) : "Chưa xác định"}</span>) : <span>Không có khoản tiền mặt được công bố</span>}</div>
                  <div className="offer-benefits"><strong>Quà / quyền lợi phi tiền mặt</strong>{giftBenefits(offer).length > 0 ? giftBenefits(offer).map((benefit, index) => <span key={`${benefit.type}-${index}`}>{benefit.type}{benefit.statedValue !== null ? ` · giá trị công bố ${formatMoney({ amount: benefit.statedValue, currency: benefit.currency })}` : ""}{benefit.note ? ` · ${benefit.note}` : ""}</span>) : <span>Không có quà được công bố</span>}</div>
                  <p><CalendarClock aria-hidden="true" size={14} /> {formatDate(offer.effectiveFrom)}{offer.effectiveTo ? ` – ${formatDate(offer.effectiveTo)}` : " · chưa công bố ngày kết thúc"}</p>
                  <details><summary>Điều kiện áp dụng</summary><pre>{offer.conditionsJson || "Chưa công bố điều kiện chi tiết"}</pre></details>
                  {offer.source ? <a className="history-source-link" href={offer.source.url} target="_blank" rel="noreferrer">{offer.source.name}<ExternalLink aria-hidden="true" size={12} /></a> : <span className="history-manual">Manual override có audit</span>}
                </article>
              )) : <p className="empty-fact">Chưa có ưu đãi đại lý còn hiệu lực được publish.</p>}
              {offerHistory.history.length > 0 && <details className="offer-history-disclosure"><summary>{offerHistory.history.length} ưu đãi đã hết hiệu lực / không còn current</summary><div>{offerHistory.history.map((offer) => <article key={offer.id}><strong>{offer.headline}</strong><span>{offer.dealerName} · {offer.branchName} · {offer.provinceCode}</span><span>{formatDate(offer.effectiveFrom)}{offer.effectiveTo ? ` – ${formatDate(offer.effectiveTo)}` : ""} · {offer.status}{offer.isStale ? " · stale" : ""}</span>{offer.maximumEligibleCashReduction !== null && <b>Giảm tiền mặt tối đa đủ điều kiện: {formatMoney({ amount: offer.maximumEligibleCashReduction, currency: offer.currency })}</b>}</article>)}</div></details>}
              <p className="history-policy-note">Chỉ quyền lợi có cấu trúc và được đánh dấu tương đương tiền mặt mới làm giảm tiền mua xe; giá trị quà tặng không được cộng vào “tiết kiệm tiền mặt”. Trong cùng nhóm loại trừ chỉ lấy quyền lợi tiền mặt lớn nhất đủ điều kiện.</p>
            </section>

            <section className="detail-panel" aria-labelledby="warranty-title">
              <ShieldCheck aria-hidden="true" size={22} />
              <h2 id="warranty-title">Bảo hành</h2>
              {detail.warranty ? (
                <dl className="warranty-facts">
                  <div><dt>Xe</dt><dd>{detail.warranty.vehicleMonths !== null ? `${detail.warranty.vehicleMonths} tháng` : "Chưa có dữ liệu"}{detail.warranty.vehicleKilometres !== null ? ` / ${formatNumber(detail.warranty.vehicleKilometres, "km")}` : ""}</dd></div>
                  <div><dt>Pin</dt><dd>{detail.warranty.batteryMonths !== null ? `${detail.warranty.batteryMonths} tháng` : "Chưa có dữ liệu"}{detail.warranty.batteryKilometres !== null ? ` / ${formatNumber(detail.warranty.batteryKilometres, "km")}` : ""}</dd></div>
                  <div><dt>Điều kiện</dt><dd>{detail.warranty.conditions ?? "Chưa có dữ liệu"}</dd></div>
                </dl>
              ) : <p className="empty-fact">Chưa có chính sách bảo hành được chuẩn hóa.</p>}
              <SourceDetails source={detail.warranty?.source ?? null} compact />
            </section>

            <section className="detail-panel detail-panel--note">
              <Gift aria-hidden="true" size={22} />
              <h2>Nguyên tắc hiển thị</h2>
              <p>Tiền mặt được tách khỏi quà. “Chưa rõ” không bị biến thành “không có”. Ảnh chỉ hiện khi quyền sử dụng đã được duyệt.</p>
            </section>
          </aside>
        </div>
      </main>
      <SiteFooter />
    </div>
  );
}
