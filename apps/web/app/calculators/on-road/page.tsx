import { AlertTriangle, ArrowRight, BadgeInfo, Calculator, DatabaseZap } from "lucide-react";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { SourceDetails } from "@/components/source-details";
import { formatDate, getCars, type CatalogSearchParams } from "@/lib/catalog-api";
import { calculateOnRoad, getRegions } from "@/lib/registration-api";

type Params = CatalogSearchParams & {
  trimId?: string;
  provinceCode?: string;
  calculationDate?: string;
  buyerType?: string;
};

const componentLabels: Record<string, string> = {
  FirstRegistrationTax: "Lệ phí trước bạ lần đầu",
  PlateAndRegistrationFee: "Đăng ký và cấp biển số",
  CompulsoryInsurance: "Bảo hiểm TNDS bắt buộc",
  InspectionFee: "Đăng kiểm lần đầu",
  RoadUsageFee: "Phí sử dụng đường bộ (12 tháng)",
  Other: "Khoản phí khác",
};

function money(amount: number, currency = "VND") {
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency, maximumFractionDigits: 0 }).format(amount);
}

function localToday() {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Ho_Chi_Minh",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export default async function OnRoadPage({ searchParams }: { searchParams: Promise<Params> }) {
  const params = await searchParams;
  const [regions, cars] = await Promise.all([getRegions(), getCars({ PageSize: "100", Sort: "name_asc" })]);
  const calculationDate = typeof params.calculationDate === "string" ? params.calculationDate : localToday();
  const provinceCode = typeof params.provinceCode === "string" ? params.provinceCode : "VN-01";
  const trimId = typeof params.trimId === "string" ? params.trimId : "";
  const buyerType = typeof params.buyerType === "string" ? params.buyerType : "Individual";
  const outcome = trimId
    ? await calculateOnRoad({
        trimId,
        provinceCode,
        calculationDate: `${calculationDate}T12:00:00+07:00`,
        buyerType,
        vehicleType: "PassengerCar",
        firstInspectionExempt: true,
        roadUsageMonths: 12,
        selectedOfferIds: [],
      })
    : null;

  return (
    <div className="calculator-shell">
      <SiteHeader />
      <main className="onroad-main">
        <header className="onroad-intro">
          <div>
            <p className="machine-label">ON-ROAD ENGINE · RULE-EFFECTIVE</p>
            <h1>Giá ra biển, không phải con số đoán.</h1>
            <p>Chọn đúng phiên bản, tỉnh đăng ký và ngày mua. Backend sẽ tra rule đang hiệu lực, tách ưu đãi tiền mặt khỏi quà tặng và trả breakdown có nguồn.</p>
          </div>
          <div className="onroad-principle"><DatabaseZap aria-hidden="true" /><span>Không có mức phí nào được hard-code trong giao diện.</span></div>
        </header>

        <div className="onroad-layout">
          <aside className="onroad-form-panel">
            <form className="onroad-form" method="get">
              <div><Calculator aria-hidden="true" size={19} /><h2>Đầu vào phép tính</h2></div>
              <label htmlFor="trimId">Phiên bản xe</label>
              <select id="trimId" name="trimId" required defaultValue={trimId}>
                <option value="" disabled>Chọn trim + model year</option>
                {cars.data.map((car) => <option key={car.trimId} value={car.trimId}>{car.brandName} {car.modelName} · {car.trimName} · {car.modelYear}</option>)}
              </select>

              <label htmlFor="provinceCode">Tỉnh/thành đăng ký</label>
              <select id="provinceCode" name="provinceCode" required defaultValue={provinceCode}>
                {regions.data.map((region) => <option key={region.code} value={region.code}>{region.name} · Khu vực {region.areaClass}</option>)}
              </select>

              <label htmlFor="calculationDate">Ngày tính</label>
              <input id="calculationDate" name="calculationDate" type="date" required defaultValue={calculationDate} min="2020-01-01" max="2100-12-31" />

              <label htmlFor="buyerType">Chủ đăng ký</label>
              <select id="buyerType" name="buyerType" defaultValue={buyerType}>
                <option value="Individual">Cá nhân</option>
                <option value="Household">Hộ gia đình</option>
                <option value="Organization">Tổ chức</option>
              </select>

              <p className="form-assumption"><BadgeInfo aria-hidden="true" size={16} /> Xe mới, đủ điều kiện miễn đăng kiểm lần đầu; phí đường bộ 12 tháng.</p>
              <button className="button-control button-primary" type="submit">Tính giá ra biển <ArrowRight aria-hidden="true" size={17} /></button>
            </form>
          </aside>

          <section className="onroad-result" aria-live="polite">
            {!outcome ? (
              <div className="onroad-empty">
                <Calculator aria-hidden="true" size={30} />
                <h2>Chưa chạy phép tính.</h2>
                <p>Chọn một phiên bản xe để nhận kết quả theo dữ liệu đang có trong hệ thống.</p>
              </div>
            ) : outcome.error ? (
              <div className="onroad-error"><AlertTriangle aria-hidden="true" /><div><p className="machine-label">{outcome.error.code}</p><h2>Chưa thể cho kết quả đáng tin cậy.</h2><p>{outcome.error.message}</p></div></div>
            ) : (
              <>
                <header className="result-hero">
                  <p className="machine-label">{outcome.data.vehicle.brandName} · {outcome.data.vehicle.modelName} · {outcome.data.vehicle.trimName}</p>
                  <h2>{money(outcome.data.result.onRoadPrice, outcome.data.result.currency)}</h2>
                  <p>{outcome.data.region.name} · Khu vực {outcome.data.region.areaClass} · ngày {formatDate(outcome.data.calculationDate)}</p>
                  <dl>
                    <div><dt>Giá đầu vào</dt><dd>{money(outcome.data.result.inputPrice)}</dd></div>
                    <div><dt>Giảm tiền mặt</dt><dd>− {money(outcome.data.result.cashPurchaseReduction)}</dd></div>
                    <div><dt>Giá mua tiền mặt hiệu lực</dt><dd>{money(outcome.data.result.effectiveCashPurchasePrice)}</dd></div>
                  </dl>
                </header>

                <section className="breakdown-section">
                  <header><p className="machine-label">BREAKDOWN</p><h2>Từng khoản phí đã áp dụng</h2></header>
                  <div className="onroad-breakdown">
                    {outcome.data.breakdown.map((item) => (
                      <article key={item.appliedRule.ruleId}>
                        <div><h3>{componentLabels[item.component] ?? item.component}</h3><p>Rule v{item.appliedRule.version} · ưu tiên {item.appliedRule.priority}</p></div>
                        <div className="breakdown-values">
                          {item.eligibleSupport > 0 && <span>{money(item.beforeSupport)} − hỗ trợ {money(item.eligibleSupport)}</span>}
                          <strong>{money(item.amount)}</strong>
                        </div>
                        <SourceDetails source={item.appliedRule.source} compact />
                      </article>
                    ))}
                  </div>
                </section>

                <section className="input-provenance">
                  <div><p className="machine-label">INPUT PRICE</p><h2>{outcome.data.inputPrice.priceType} · version {outcome.data.inputPrice.version}</h2><p>Hiệu lực từ {formatDate(outcome.data.inputPrice.effectiveFrom)} · phạm vi {outcome.data.inputPrice.regionScope}</p></div>
                  <SourceDetails source={outcome.data.inputPrice.source} />
                </section>

                {outcome.data.nonCashBenefits.length > 0 && (
                  <section className="noncash-benefits"><p className="machine-label">KHÔNG TRỪ VÀO GIÁ MUA</p><h2>Quà tặng và quyền lợi phi tiền mặt</h2>{outcome.data.nonCashBenefits.map((benefit) => <p key={`${benefit.originId}-${benefit.type}`}>{benefit.type}: {benefit.statedValue ? money(benefit.statedValue) : "không công bố giá trị"}</p>)}</section>
                )}

                {outcome.data.warnings.length > 0 && <section className="calculation-warnings"><h2>Cảnh báo dữ liệu</h2>{outcome.data.warnings.map((warning) => <p key={warning}><AlertTriangle aria-hidden="true" size={16} /> {warning}</p>)}</section>}

                <footer className="calculation-meta"><span>Tính lúc {formatDate(outcome.data.calculatedAt)}</span><span>{outcome.data.appliedRules.length} rule · mọi rule có effective date</span></footer>
              </>
            )}
          </section>
        </div>
      </main>
      <SiteFooter />
    </div>
  );
}
