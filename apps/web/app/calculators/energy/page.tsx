import { AlertTriangle, ArrowRight, BadgeInfo, BatteryCharging, DatabaseZap, Gauge } from "lucide-react";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { SourceDetails } from "@/components/source-details";
import { formatDate, getCars, type CatalogSearchParams } from "@/lib/catalog-api";
import { calculateEnergy } from "@/lib/energy-api";

type Params = CatalogSearchParams & {
  trimId?: string;
  calculationDate?: string;
  monthlyKilometres?: string;
  fuelType?: string;
  evSharePercent?: string;
  homeSharePercent?: string;
  chargingEfficiencyPercent?: string;
  homeMode?: string;
  householdBaseKwh?: string;
  customHomeAmountPerKwh?: string;
  connectorType?: string;
  chargingPowerKw?: string;
  publicSessions?: string;
  sessionsUsedThisMonth?: string;
  postChargeMinutesPerSession?: string;
  customerType?: string;
  purchaseDate?: string;
  promotionEligibilityConfirmed?: string;
};

const componentLabels: Record<string, string> = {
  Fuel: "Nhiên liệu",
  HomeChargingTier: "Sạc nhà · bậc điện biên",
  HomeChargingCustom: "Sạc nhà · giá cố định",
  PublicCharging: "Sạc công cộng",
  PostChargeServiceFee: "Phí dịch vụ sau phiên sạc",
};

function numberParam(value: string | string[] | undefined, fallback: number) {
  const parsed = typeof value === "string" ? Number(value) : Number.NaN;
  return Number.isFinite(parsed) ? parsed : fallback;
}

function textParam(value: string | string[] | undefined, fallback: string) {
  return typeof value === "string" ? value : fallback;
}

function money(amount: number, currency = "VND") {
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency, maximumFractionDigits: 0 }).format(amount);
}

function quantity(amount: number, unit: string) {
  return `${new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(amount)} ${unit}`;
}

function localToday() {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Ho_Chi_Minh",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export default async function EnergyPage({ searchParams }: { searchParams: Promise<Params> }) {
  const params = await searchParams;
  const cars = await getCars({ PageSize: "100", Sort: "name_asc" });
  const values = {
    trimId: textParam(params.trimId, ""),
    calculationDate: textParam(params.calculationDate, localToday()),
    monthlyKilometres: numberParam(params.monthlyKilometres, 1_000),
    fuelType: textParam(params.fuelType, "E10Ron95III"),
    evSharePercent: numberParam(params.evSharePercent, 50),
    homeSharePercent: numberParam(params.homeSharePercent, 100),
    chargingEfficiencyPercent: numberParam(params.chargingEfficiencyPercent, 90),
    homeMode: textParam(params.homeMode, "EvnMarginalTiers"),
    householdBaseKwh: numberParam(params.householdBaseKwh, 250),
    customHomeAmountPerKwh: numberParam(params.customHomeAmountPerKwh, 3_500),
    connectorType: textParam(params.connectorType, "DC"),
    chargingPowerKw: numberParam(params.chargingPowerKw, 60),
    publicSessions: numberParam(params.publicSessions, 6),
    sessionsUsedThisMonth: numberParam(params.sessionsUsedThisMonth, 0),
    postChargeMinutesPerSession: numberParam(params.postChargeMinutesPerSession, 0),
    customerType: textParam(params.customerType, "Personal"),
    purchaseDate: textParam(params.purchaseDate, ""),
    promotionEligibilityConfirmed: params.promotionEligibilityConfirmed === "true",
  };
  const outcome = values.trimId
    ? await calculateEnergy({
        trimId: values.trimId,
        calculationDate: `${values.calculationDate}T12:00:00+07:00`,
        monthlyKilometres: values.monthlyKilometres,
        fuelType: values.fuelType,
        evShare: values.evSharePercent / 100,
        homeChargingShare: values.homeSharePercent / 100,
        chargingEfficiency: values.chargingEfficiencyPercent / 100,
        homeMode: values.homeMode,
        householdBaseKwh: values.householdBaseKwh,
        customHomeAmountPerKwh: values.homeMode === "CustomFixedRate" ? values.customHomeAmountPerKwh : null,
        chargingProviderSlug: "v-green",
        connectorType: values.connectorType || null,
        chargingPowerKw: values.chargingPowerKw,
        publicSessions: values.publicSessions,
        sessionsUsedThisMonth: values.sessionsUsedThisMonth,
        postChargeMinutesPerSession: values.postChargeMinutesPerSession,
        customerType: values.customerType,
        purchaseDate: values.purchaseDate || null,
        promotionEligibilityConfirmed: values.promotionEligibilityConfirmed,
      })
    : null;

  return (
    <div className="calculator-shell">
      <SiteHeader />
      <main className="onroad-main energy-main">
        <header className="onroad-intro energy-intro">
          <div>
            <p className="machine-label">ENERGY ENGINE · EFFECTIVE-DATED</p>
            <h1>Chi phí chạy xe, theo đúng nơi bạn nạp năng lượng.</h1>
            <p>Xăng dầu theo kỳ điều hành, điện nhà tính biên trên sáu bậc EVN, sạc công cộng có phí sau phiên. PHEV tách km EV và km chạy xăng.</p>
          </div>
          <div className="onroad-principle"><DatabaseZap aria-hidden="true" /><span>Giá, biểu phí và ưu đãi đều lấy từ dữ liệu có ngày hiệu lực và snapshot nguồn.</span></div>
        </header>

        <div className="onroad-layout energy-layout">
          <aside className="onroad-form-panel">
            <form className="onroad-form energy-form" method="get">
              <div><Gauge aria-hidden="true" size={19} /><h2>Kịch bản sử dụng tháng</h2></div>

              <fieldset>
                <legend>Xe và quãng đường</legend>
                <label htmlFor="trimId">Phiên bản xe</label>
                <select id="trimId" name="trimId" required defaultValue={values.trimId}>
                  <option value="" disabled>Chọn trim có hồ sơ năng lượng</option>
                  {cars.data.map((car) => <option key={car.trimId} value={car.trimId}>{car.brandName} {car.modelName} · {car.trimName} · {car.powertrainType}</option>)}
                </select>
                <div className="energy-field-grid">
                  <div><label htmlFor="monthlyKilometres">Km/tháng</label><input id="monthlyKilometres" name="monthlyKilometres" type="number" min="0" max="100000" step="10" required defaultValue={values.monthlyKilometres} /></div>
                  <div><label htmlFor="calculationDate">Ngày tính</label><input id="calculationDate" name="calculationDate" type="date" min="2020-01-01" max="2100-12-31" required defaultValue={values.calculationDate} /></div>
                </div>
                <div className="energy-field-grid">
                  <div><label htmlFor="fuelType">Loại nhiên liệu</label><select id="fuelType" name="fuelType" defaultValue={values.fuelType}><option value="E10Ron95III">E10RON95-III</option><option value="Ron92E5">E5RON92-II</option><option value="Diesel">Diesel 0,05S</option></select></div>
                  <div><label htmlFor="evSharePercent">Tỷ lệ km chạy EV (%)</label><input id="evSharePercent" name="evSharePercent" type="number" min="0" max="100" step="1" defaultValue={values.evSharePercent} /></div>
                </div>
              </fieldset>

              <fieldset>
                <legend>Sạc tại nhà</legend>
                <div className="energy-field-grid">
                  <div><label htmlFor="homeSharePercent">Tỷ lệ điện sạc nhà (%)</label><input id="homeSharePercent" name="homeSharePercent" type="number" min="0" max="100" step="1" defaultValue={values.homeSharePercent} /></div>
                  <div><label htmlFor="chargingEfficiencyPercent">Hiệu suất sạc (%)</label><input id="chargingEfficiencyPercent" name="chargingEfficiencyPercent" type="number" min="1" max="100" step="1" defaultValue={values.chargingEfficiencyPercent} /></div>
                </div>
                <label htmlFor="homeMode">Cách tính giá điện nhà</label>
                <select id="homeMode" name="homeMode" defaultValue={values.homeMode}><option value="EvnMarginalTiers">EVN 6 bậc · tính phần tăng thêm</option><option value="CustomFixedRate">Giá cố định tự nhập / nhà thuê</option></select>
                <div className="energy-field-grid">
                  <div><label htmlFor="householdBaseKwh">Điện sinh hoạt nền (kWh)</label><input id="householdBaseKwh" name="householdBaseKwh" type="number" min="0" max="100000" step="1" defaultValue={values.householdBaseKwh} /></div>
                  <div><label htmlFor="customHomeAmountPerKwh">Giá cố định (VND/kWh)</label><input id="customHomeAmountPerKwh" name="customHomeAmountPerKwh" type="number" min="0" max="1000000" step="1" defaultValue={values.customHomeAmountPerKwh} /></div>
                </div>
              </fieldset>

              <fieldset>
                <legend>Sạc công cộng V-Green</legend>
                <div className="energy-field-grid">
                  <div><label htmlFor="connectorType">Loại trụ</label><select id="connectorType" name="connectorType" defaultValue={values.connectorType}><option value="DC">DC</option><option value="AC7">AC 7 kW</option><option value="AC11">AC 11 kW</option><option value="AC22">AC 22 kW</option></select></div>
                  <div><label htmlFor="chargingPowerKw">Công suất (kW)</label><input id="chargingPowerKw" name="chargingPowerKw" type="number" min="0" max="1000" step="0.1" defaultValue={values.chargingPowerKw} /></div>
                </div>
                <div className="energy-field-grid energy-field-grid--three">
                  <div><label htmlFor="publicSessions">Phiên tháng này</label><input id="publicSessions" name="publicSessions" type="number" min="0" max="1000" step="1" defaultValue={values.publicSessions} /></div>
                  <div><label htmlFor="sessionsUsedThisMonth">Phiên đã dùng</label><input id="sessionsUsedThisMonth" name="sessionsUsedThisMonth" type="number" min="0" max="1000" step="1" defaultValue={values.sessionsUsedThisMonth} /></div>
                  <div><label htmlFor="postChargeMinutesPerSession">Phút đỗ sau sạc/phiên</label><input id="postChargeMinutesPerSession" name="postChargeMinutesPerSession" type="number" min="0" max="10000" step="1" defaultValue={values.postChargeMinutesPerSession} /></div>
                </div>
              </fieldset>

              <fieldset>
                <legend>Điều kiện ưu đãi</legend>
                <div className="energy-field-grid">
                  <div><label htmlFor="customerType">Nhóm khách hàng</label><select id="customerType" name="customerType" defaultValue={values.customerType}><option value="Personal">Cá nhân</option><option value="Organization">Tổ chức</option><option value="TransportBusiness">Kinh doanh vận tải</option></select></div>
                  <div><label htmlFor="purchaseDate">Ngày mua xe</label><input id="purchaseDate" name="purchaseDate" type="date" min="2020-01-01" max="2100-12-31" defaultValue={values.purchaseDate} /></div>
                </div>
                <label className="energy-confirmation"><input name="promotionEligibilityConfirmed" type="checkbox" value="true" defaultChecked={values.promotionEligibilityConfirmed} /><span>Tôi xác nhận xe và chủ xe đáp ứng các điều kiện chi tiết của chương trình ưu đãi đang hiệu lực.</span></label>
              </fieldset>

              <p className="form-assumption"><BadgeInfo aria-hidden="true" size={16} /> currentCost áp ưu đãi tạm thời đủ điều kiện; normalizedCost bỏ ưu đãi để so sánh dài hạn.</p>
              <button className="button-control button-primary" type="submit">Tính chi phí tháng <ArrowRight aria-hidden="true" size={17} /></button>
            </form>
          </aside>

          <section className="onroad-result" aria-live="polite">
            {!outcome ? (
              <div className="onroad-empty"><BatteryCharging aria-hidden="true" size={30} /><h2>Chưa chạy phép tính.</h2><p>Chọn xe và mô tả cách bạn nạp năng lượng để nhận breakdown theo dữ liệu hiện hành.</p></div>
            ) : outcome.error ? (
              <div className="onroad-error"><AlertTriangle aria-hidden="true" /><div><p className="machine-label">{outcome.error.code}</p><h2>Chưa thể cho kết quả đáng tin cậy.</h2><p>{outcome.error.message}</p></div></div>
            ) : (
              <>
                <header className="result-hero energy-result-hero">
                  <p className="machine-label">{outcome.data.vehicle.brandName} · {outcome.data.vehicle.modelName} · {outcome.data.vehicle.trimName}</p>
                  <div className="energy-total-grid">
                    <div><span>Chi phí hiện tại</span><h2>{money(outcome.data.result.currentCost)}</h2></div>
                    <div><span>Chi phí chuẩn hóa</span><strong>{money(outcome.data.result.normalizedCost)}</strong></div>
                  </div>
                  <p>{outcome.data.vehicle.powertrain} · {values.monthlyKilometres.toLocaleString("vi-VN")} km/tháng · ngày {formatDate(outcome.data.calculationDate)}</p>
                  <dl>
                    <div><dt>Nhiên liệu</dt><dd>{quantity(outcome.data.result.fuelLitres, "lít")}</dd></div>
                    <div><dt>Điện vào pin</dt><dd>{quantity(outcome.data.result.batteryEnergyKwh, "kWh")}</dd></div>
                    <div><dt>Điện lấy từ lưới</dt><dd>{quantity(outcome.data.result.gridEnergyKwh, "kWh")}</dd></div>
                    <div><dt>Ưu đãi đã áp</dt><dd>− {money(outcome.data.result.promotionSavings)}</dd></div>
                  </dl>
                </header>

                <section className="breakdown-section">
                  <header><p className="machine-label">CURRENT / NORMALIZED</p><h2>Từng dòng chi phí năng lượng</h2></header>
                  <div className="onroad-breakdown energy-breakdown">
                    {outcome.data.breakdown.map((item, index) => (
                      <article key={`${item.component}-${item.appliedRate?.rateId ?? index}`}>
                        <div><h3>{componentLabels[item.component] ?? item.component}</h3><p>{quantity(item.quantity, item.unit)} · {item.detail}</p></div>
                        <div className="breakdown-values">
                          {item.currentAmount !== item.normalizedAmount && <span>Chuẩn hóa {money(item.normalizedAmount)}</span>}
                          <strong>{money(item.currentAmount)}</strong>
                        </div>
                        {item.appliedRate?.source ? <SourceDetails source={item.appliedRate.source} compact /> : <span className="data-state data-state--unknown">Đầu vào tùy chỉnh</span>}
                      </article>
                    ))}
                  </div>
                </section>

                <section className="input-provenance energy-profile-source">
                  <div><p className="machine-label">OFFICIAL ENERGY PROFILE</p><h2>Điều kiện thử không bị trộn</h2><p>{outcome.data.energyProfile.fuelConsumptionCondition ?? "Không dùng nhiên liệu"} · {outcome.data.energyProfile.electricConsumptionCondition ?? "Không dùng điện"}</p><p>{outcome.data.energyProfile.consumptionNotes}</p></div>
                  <SourceDetails source={outcome.data.energyProfile.source} />
                </section>

                {outcome.data.appliedPromotions.length > 0 && (
                  <section className="energy-promotions"><p className="machine-label">PROMOTION APPLIED</p><h2>Ưu đãi có phiên bản và thời hạn</h2>{outcome.data.appliedPromotions.map((promotion) => <article key={promotion.promotionId}><div><strong>{promotion.benefit}</strong><span>Hiệu lực {formatDate(promotion.effectiveFrom)} – {promotion.effectiveTo ? formatDate(promotion.effectiveTo) : "không xác định"}</span></div><SourceDetails source={promotion.source} compact /></article>)}</section>
                )}

                {outcome.data.warnings.length > 0 && <section className="calculation-warnings"><h2>Cảnh báo dữ liệu và điều kiện</h2>{outcome.data.warnings.map((warning) => <p key={warning}><AlertTriangle aria-hidden="true" size={16} /> {warning}</p>)}</section>}

                <footer className="calculation-meta"><span>Tính lúc {formatDate(outcome.data.calculatedAt)}</span><span>{outcome.data.appliedRates.length} biểu giá · {outcome.data.appliedPromotions.length} ưu đãi</span></footer>
              </>
            )}
          </section>
        </div>
      </main>
      <SiteFooter />
    </div>
  );
}
