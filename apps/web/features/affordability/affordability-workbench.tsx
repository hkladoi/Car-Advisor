"use client";

import { useState, type FormEvent } from "react";
import { AlertTriangle, ArrowRight, BadgeInfo, Banknote, ChevronDown, CircleCheck, DatabaseZap, Gauge } from "lucide-react";

import { SourceDetails } from "@/components/source-details";
import type { AffordabilityRequest, AffordabilityResponse } from "@/lib/affordability-api";
import type { RegionItem } from "@/lib/registration-api";

type Props = { regions: RegionItem[]; initialRequest: AffordabilityRequest; initialResult: AffordabilityResponse };

const componentLabels: Record<string, string> = {
  Energy: "Năng lượng",
  Parking: "Gửi xe",
  MaintenanceReserve: "Dự phòng bảo dưỡng",
  CompulsoryInsurance: "Bảo hiểm TNDS bắt buộc",
  BodyInsurance: "Bảo hiểm thân vỏ",
  RoadUsage: "Phí sử dụng đường bộ",
  Inspection: "Đăng kiểm",
  TyreReserve: "Dự phòng lốp",
  BatteryRental: "Thuê pin",
  RoadUsageFee: "Phí sử dụng đường bộ",
  InspectionFee: "Đăng kiểm",
};

const reasonLabels: Record<string, string> = {
  LOW_DISPOSABLE_INCOME: "Thu nhập khả dụng không còn dương",
  INCOME_RATIO_EXCEEDED: "Vượt tỷ lệ tối đa trên thu nhập",
  DISPOSABLE_RATIO_EXCEEDED: "Vượt tỷ lệ tối đa trên phần còn lại",
  MAX_MONTHLY_VEHICLE_SPEND_EXCEEDED: "Vượt trần ngân sách xe tự đặt",
  NORMALIZED_COST_FAILS: "Chỉ pass nhờ ưu đãi tạm thời",
  WORST_REASONABLE_COST_EXCEEDS_POLICY: "Kịch bản bảo thủ vượt ngưỡng",
  ENERGY_COST_HIGH: "Năng lượng chiếm tỷ trọng cao",
  PARKING_DOMINATES: "Chi phí gửi xe chi phối",
  CURRENT_ENERGY_PROMOTION_APPLIED: "Đang áp ưu đãi năng lượng",
};

function number(data: FormData, name: string) {
  const value = Number(data.get(name));
  return Number.isFinite(value) ? value : 0;
}

function nullableNumber(data: FormData, name: string) {
  const raw = String(data.get(name) ?? "").trim();
  if (!raw) return null;
  const value = Number(raw);
  return Number.isFinite(value) ? value : null;
}

function money(value: number) {
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(value);
}

function percent(value: number) {
  return new Intl.NumberFormat("vi-VN", { style: "percent", maximumFractionDigits: 1 }).format(value);
}

function CandidateRow({ candidate, eligible }: { candidate: AffordabilityResponse["eligibleCars"][number]; eligible: boolean }) {
  const result = candidate.ownership.result;
  return (
    <article className="affordability-candidate">
      <header>
        <div><p className="machine-label">{candidate.vehicle.brandName} · {candidate.vehicle.powertrain}</p><h3>{candidate.vehicle.modelName} · {candidate.vehicle.trimName}</h3></div>
        <span className={`affordability-rating ${eligible ? "affordability-rating--pass" : "affordability-rating--fail"}`}>
          {eligible ? <CircleCheck aria-hidden="true" size={16} /> : <AlertTriangle aria-hidden="true" size={16} />}{candidate.evaluation.rating}
        </span>
      </header>
      <div className="affordability-band-grid">
        <div><span>Hiện tại</span><strong>{money(result.currentMonthlyCost)}</strong></div>
        <div><span>Chuẩn hóa</span><strong>{money(result.normalizedMonthlyCost)}</strong></div>
        <div><span>Bảo thủ hợp lý</span><strong>{money(result.worstReasonableMonthlyCost)}</strong></div>
      </div>
      <dl className="affordability-ratios">
        <div><dt>Trên thu nhập</dt><dd>{percent(candidate.evaluation.normalized.incomeRatio)}</dd></div>
        <div><dt>Trên phần còn lại</dt><dd>{percent(candidate.evaluation.normalized.disposableRatio)}</dd></div>
      </dl>
      {candidate.evaluation.reasons.length > 0 && <div className="reason-strip">{candidate.evaluation.reasons.map((reason) => <span key={reason}>{reasonLabels[reason] ?? reason}</span>)}</div>}
      <details className="affordability-details">
        <summary>Breakdown và nguồn <ChevronDown aria-hidden="true" size={16} /></summary>
        <div className="ownership-breakdown">
          {result.breakdown.map((item) => (
            <div key={item.component}>
              <div><strong>{componentLabels[item.component] ?? item.component}</strong><span>{item.origin}</span></div>
              <div><span>{item.currentAmount !== item.normalizedAmount ? `Hiện tại ${money(item.currentAmount)}` : item.note}</span><strong>{money(item.normalizedAmount)}</strong></div>
            </div>
          ))}
        </div>
        <div className="affordability-sources">
          {candidate.ownership.appliedRecurringRules.map((rule) => rule.source && <div key={rule.ruleId}><span>{componentLabels[rule.component] ?? rule.component} · rule v{rule.version}</span><SourceDetails source={rule.source} compact /></div>)}
          {candidate.ownership.energy.appliedRates.map((rate) => rate.source && <div key={rate.rateId}><span>{rate.provider} · {rate.kind}</span><SourceDetails source={rate.source} compact /></div>)}
        </div>
      </details>
    </article>
  );
}

export function AffordabilityWorkbench({ regions, initialRequest, initialResult }: Props) {
  const [result, setResult] = useState(initialResult);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ code: string; message: string } | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    const data = new FormData(event.currentTarget);
    const request: AffordabilityRequest = {
      trimIds: [],
      provinceCode: String(data.get("provinceCode")),
      calculationDate: `${String(data.get("calculationDate"))}T12:00:00+07:00`,
      policy: String(data.get("policy")) as AffordabilityRequest["policy"],
      netMonthlyIncome: number(data, "netMonthlyIncome"),
      rentHousing: number(data, "rentHousing"),
      essentialExpenses: number(data, "essentialExpenses"),
      otherFixedDebt: number(data, "otherFixedDebt"),
      savingsTarget: number(data, "savingsTarget"),
      maximumMonthlyVehicleSpend: nullableNumber(data, "maximumMonthlyVehicleSpend"),
      expenses: {
        monthlyKilometres: number(data, "monthlyKilometres"),
        parkingMonthly: number(data, "parkingMonthly"),
        maintenanceReserveMonthly: number(data, "maintenanceReserveMonthly"),
        bodyInsuranceAnnual: number(data, "bodyInsuranceAnnual"),
        tyreReserveMonthly: number(data, "tyreReserveMonthly"),
        batteryRentalMonthly: number(data, "batteryRentalMonthly"),
        compulsoryInsuranceMonthlyOverride: nullableNumber(data, "compulsoryInsuranceMonthlyOverride"),
        roadUsageMonthlyOverride: nullableNumber(data, "roadUsageMonthlyOverride"),
        inspectionMonthlyOverride: nullableNumber(data, "inspectionMonthlyOverride"),
        firstInspectionExempt: data.get("firstInspectionExempt") === "true",
      },
      energy: {
        fuelType: String(data.get("fuelType")),
        evShare: number(data, "evSharePercent") / 100,
        homeChargingShare: number(data, "homeSharePercent") / 100,
        chargingEfficiency: number(data, "chargingEfficiencyPercent") / 100,
        homeMode: String(data.get("homeMode")),
        householdBaseKwh: number(data, "householdBaseKwh"),
        customHomeAmountPerKwh: nullableNumber(data, "customHomeAmountPerKwh"),
        chargingProviderSlug: "v-green",
        connectorType: String(data.get("connectorType")),
        chargingPowerKw: nullableNumber(data, "chargingPowerKw"),
        publicSessions: number(data, "publicSessions"),
        sessionsUsedThisMonth: number(data, "sessionsUsedThisMonth"),
        postChargeMinutesPerSession: number(data, "postChargeMinutesPerSession"),
        customerType: String(data.get("customerType")),
        purchaseDate: String(data.get("purchaseDate") ?? "") || null,
        promotionEligibilityConfirmed: data.get("promotionEligibilityConfirmed") === "true",
      },
    };
    try {
      const response = await fetch("/api/affordability", { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify(request) });
      const payload = (await response.json()) as AffordabilityResponse | { code?: string; message?: string };
      if (!response.ok) {
        const failure = payload as { code?: string; message?: string };
        setError({ code: failure.code ?? "AFFORDABILITY_FAILED", message: failure.message ?? "Không thể đánh giá kịch bản." });
      } else {
        setResult(payload as AffordabilityResponse);
      }
    } catch {
      setError({ code: "NETWORK_ERROR", message: "Không thể kết nối dịch vụ đánh giá." });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="affordability-layout">
      <aside className="affordability-profile-panel">
        <form className="affordability-form" onSubmit={submit}>
          <div><Gauge aria-hidden="true" size={19} /><h2>Hồ sơ dòng tiền</h2></div>
          <p className="machine-label">QUICK MODE</p>
          <label htmlFor="netMonthlyIncome">Lương thực nhận / tháng</label>
          <input id="netMonthlyIncome" name="netMonthlyIncome" type="number" min="100000" step="100000" required defaultValue={initialRequest.netMonthlyIncome} />
          <div className="affordability-field-grid">
            <div><label htmlFor="monthlyKilometres">Km / tháng</label><input id="monthlyKilometres" name="monthlyKilometres" type="number" min="0" max="100000" step="10" defaultValue={initialRequest.expenses.monthlyKilometres} /></div>
            <div><label htmlFor="policy">Mức thận trọng</label><select id="policy" name="policy" defaultValue={initialRequest.policy}><option value="Conservative">Thận trọng</option><option value="Balanced">Cân bằng</option><option value="Aggressive">Chấp nhận cao</option></select></div>
          </div>
          <label htmlFor="essentialExpenses">Chi thiết yếu mặc định / tháng</label>
          <input id="essentialExpenses" name="essentialExpenses" type="number" min="0" step="100000" defaultValue={initialRequest.essentialExpenses} />
          <p className="field-note">Giả định này luôn hiện và sửa được; backend không tự suy đoán mức sống của bạn.</p>

          <details className="affordability-advanced">
            <summary>Advanced · nhà ở, dự phòng, cách sạc <ChevronDown aria-hidden="true" size={16} /></summary>
            <div className="advanced-fields">
              <fieldset>
                <legend>Dòng tiền cố định</legend>
                <label htmlFor="provinceCode">Tỉnh/thành đăng ký</label><select id="provinceCode" name="provinceCode" defaultValue={initialRequest.provinceCode}>{regions.map((region) => <option value={region.code} key={region.code}>{region.name} · KV {region.areaClass}</option>)}</select>
                <label htmlFor="calculationDate">Ngày tính</label><input id="calculationDate" name="calculationDate" type="date" defaultValue={initialRequest.calculationDate.slice(0, 10)} />
                <div className="affordability-field-grid"><div><label htmlFor="rentHousing">Nhà ở / thuê</label><input id="rentHousing" name="rentHousing" type="number" min="0" step="100000" defaultValue={initialRequest.rentHousing} /></div><div><label htmlFor="otherFixedDebt">Nợ cố định khác</label><input id="otherFixedDebt" name="otherFixedDebt" type="number" min="0" step="100000" defaultValue={initialRequest.otherFixedDebt} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="savingsTarget">Mục tiêu tiết kiệm</label><input id="savingsTarget" name="savingsTarget" type="number" min="0" step="100000" defaultValue={initialRequest.savingsTarget} /></div><div><label htmlFor="maximumMonthlyVehicleSpend">Trần chi xe (trống = policy)</label><input id="maximumMonthlyVehicleSpend" name="maximumMonthlyVehicleSpend" type="number" min="0" step="100000" /></div></div>
              </fieldset>
              <fieldset>
                <legend>Chi phí sở hữu</legend>
                <div className="affordability-field-grid"><div><label htmlFor="parkingMonthly">Gửi xe / tháng</label><input id="parkingMonthly" name="parkingMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.parkingMonthly} /></div><div><label htmlFor="maintenanceReserveMonthly">Dự phòng bảo dưỡng</label><input id="maintenanceReserveMonthly" name="maintenanceReserveMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.maintenanceReserveMonthly} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="tyreReserveMonthly">Dự phòng lốp</label><input id="tyreReserveMonthly" name="tyreReserveMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.tyreReserveMonthly} /></div><div><label htmlFor="bodyInsuranceAnnual">Bảo hiểm thân vỏ / năm</label><input id="bodyInsuranceAnnual" name="bodyInsuranceAnnual" type="number" min="0" step="100000" defaultValue={initialRequest.expenses.bodyInsuranceAnnual} /></div></div>
                <label htmlFor="batteryRentalMonthly">Thuê pin / tháng nếu có</label><input id="batteryRentalMonthly" name="batteryRentalMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.batteryRentalMonthly} />
                <p className="field-note">TNDS, đường bộ và đăng kiểm mặc định lấy rule có nguồn. Chỉ nhập các ô override khi bạn chủ động thay thế.</p>
                <div className="affordability-field-grid affordability-field-grid--three"><div><label htmlFor="compulsoryInsuranceMonthlyOverride">TNDS override</label><input id="compulsoryInsuranceMonthlyOverride" name="compulsoryInsuranceMonthlyOverride" type="number" min="0" step="1000" /></div><div><label htmlFor="roadUsageMonthlyOverride">Đường bộ override</label><input id="roadUsageMonthlyOverride" name="roadUsageMonthlyOverride" type="number" min="0" step="1000" /></div><div><label htmlFor="inspectionMonthlyOverride">Đăng kiểm override</label><input id="inspectionMonthlyOverride" name="inspectionMonthlyOverride" type="number" min="0" step="1000" /></div></div>
                <label className="affordability-check"><input type="checkbox" name="firstInspectionExempt" value="true" defaultChecked={initialRequest.expenses.firstInspectionExempt} /><span>Xe mới đủ điều kiện miễn kiểm định lần đầu</span></label>
              </fieldset>
              <fieldset>
                <legend>Kịch bản năng lượng</legend>
                <div className="affordability-field-grid"><div><label htmlFor="homeSharePercent">Sạc nhà (%)</label><input id="homeSharePercent" name="homeSharePercent" type="number" min="0" max="100" defaultValue={initialRequest.energy.homeChargingShare * 100} /></div><div><label htmlFor="evSharePercent">PHEV chạy EV (%)</label><input id="evSharePercent" name="evSharePercent" type="number" min="0" max="100" defaultValue={initialRequest.energy.evShare * 100} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="chargingEfficiencyPercent">Hiệu suất sạc (%)</label><input id="chargingEfficiencyPercent" name="chargingEfficiencyPercent" type="number" min="1" max="100" defaultValue={initialRequest.energy.chargingEfficiency * 100} /></div><div><label htmlFor="householdBaseKwh">Điện nền gia đình (kWh)</label><input id="householdBaseKwh" name="householdBaseKwh" type="number" min="0" defaultValue={initialRequest.energy.householdBaseKwh} /></div></div>
                <label htmlFor="homeMode">Giá điện nhà</label><select id="homeMode" name="homeMode" defaultValue={initialRequest.energy.homeMode}><option value="EvnMarginalTiers">EVN 6 bậc · phần tăng thêm</option><option value="CustomFixedRate">Giá tự nhập / nhà trọ</option></select>
                <label htmlFor="customHomeAmountPerKwh">Giá điện custom (VND/kWh)</label><input id="customHomeAmountPerKwh" name="customHomeAmountPerKwh" type="number" min="0" step="1" />
                <div className="affordability-field-grid"><div><label htmlFor="publicSessions">Phiên sạc công cộng</label><input id="publicSessions" name="publicSessions" type="number" min="0" defaultValue={initialRequest.energy.publicSessions} /></div><div><label htmlFor="sessionsUsedThisMonth">Phiên ưu đãi đã dùng</label><input id="sessionsUsedThisMonth" name="sessionsUsedThisMonth" type="number" min="0" defaultValue={initialRequest.energy.sessionsUsedThisMonth} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="connectorType">Loại trụ</label><select id="connectorType" name="connectorType" defaultValue={initialRequest.energy.connectorType ?? "DC"}><option value="DC">DC</option><option value="AC7">AC 7 kW</option><option value="AC11">AC 11 kW</option></select></div><div><label htmlFor="chargingPowerKw">Công suất (kW)</label><input id="chargingPowerKw" name="chargingPowerKw" type="number" min="0" step="0.1" defaultValue={initialRequest.energy.chargingPowerKw ?? 60} /></div></div>
                <input type="hidden" name="postChargeMinutesPerSession" value={initialRequest.energy.postChargeMinutesPerSession} />
                <label htmlFor="fuelType">Nhiên liệu</label><select id="fuelType" name="fuelType" defaultValue={initialRequest.energy.fuelType}><option value="E10Ron95III">E10RON95-III</option><option value="Ron92E5">E5RON92-II</option><option value="Diesel">Diesel</option></select>
                <div className="affordability-field-grid"><div><label htmlFor="customerType">Nhóm khách hàng</label><select id="customerType" name="customerType" defaultValue={initialRequest.energy.customerType}><option value="Personal">Cá nhân</option><option value="Organization">Tổ chức</option><option value="TransportBusiness">Kinh doanh vận tải</option></select></div><div><label htmlFor="purchaseDate">Ngày mua xe</label><input id="purchaseDate" name="purchaseDate" type="date" /></div></div>
                <label className="affordability-check"><input type="checkbox" name="promotionEligibilityConfirmed" value="true" /><span>Tôi xác nhận đủ điều kiện ưu đãi đang hiệu lực</span></label>
              </fieldset>
            </div>
          </details>
          <button className="button-control button-primary" type="submit" disabled={loading}>{loading ? "Đang tính…" : "Đánh giá xe nuôi được"} <ArrowRight aria-hidden="true" size={17} /></button>
          <p className="form-assumption"><BadgeInfo aria-hidden="true" size={16} /> OperatingOwnershipCost không bao gồm khoản vay. Mua/vay được là gate riêng ở V1.8.</p>
        </form>
      </aside>

      <section className="affordability-results" aria-live="polite">
        {error && <div className="onroad-error"><AlertTriangle aria-hidden="true" /><div><p className="machine-label">{error.code}</p><h2>Chưa thể đánh giá.</h2><p>{error.message}</p></div></div>}
        <header className="affordability-summary">
          <div><p className="machine-label">OWNERSHIP ELIGIBILITY · {result.policy}</p><h2>{result.eligibleCars.length} xe trong ngưỡng</h2><p>{result.overBudgetCars.length} xe vượt ngân sách · {result.dataExcludedCars.length} xe thiếu dữ liệu để kết luận.</p></div>
          <div><span>Thu nhập còn lại trước xe</span><strong>{money(result.profile.disposableIncomeBeforeVehicle)}</strong></div>
        </header>
        <div className="estimate-notice"><DatabaseZap aria-hidden="true" size={19} /><div><strong>Ước tính kịch bản — không phải lời khuyên tài chính.</strong><span>Pass dùng chi phí chuẩn hóa, không dùng mức 0đ do ưu đãi tạm thời để che rủi ro dài hạn.</span></div></div>
        <section className="affordability-list"><header><p className="machine-label">ELIGIBLE · NORMALIZED PASS</p><h2>Có thể cân nhắc nuôi</h2></header>{result.eligibleCars.length === 0 ? <p className="affordability-empty">Không có xe đủ dữ liệu nào nằm trong ngưỡng hiện tại. Mỗi xe bị loại vẫn có lý do bên dưới.</p> : result.eligibleCars.map((candidate) => <CandidateRow key={candidate.vehicle.trimId} candidate={candidate} eligible />)}</section>
        {result.overBudgetCars.length > 0 && <section className="affordability-list affordability-list--excluded"><header><p className="machine-label">EXCLUDED · EXPLAINABLE</p><h2>Vượt chính sách đã chọn</h2></header>{result.overBudgetCars.map((candidate) => <CandidateRow key={candidate.vehicle.trimId} candidate={candidate} eligible={false} />)}</section>}
        <details className="data-excluded-list"><summary>{result.dataExcludedCars.length} xe chưa đủ dữ liệu để đánh giá <ChevronDown aria-hidden="true" size={16} /></summary><div>{result.dataExcludedCars.map((item) => <p key={item.vehicle.trimId}><strong>{item.vehicle.brandName} {item.vehicle.modelName}</strong><span>{item.reasons.join(", ")} · {item.explanation}</span></p>)}</div></details>
        <footer className="affordability-policy-note"><Banknote aria-hidden="true" size={18} /><span>Ngưỡng {result.policy}: tối đa {percent(result.thresholds.maximumIncomeRatio)} thu nhập và {percent(result.thresholds.maximumDisposableRatio)} phần còn lại. Đây là guardrail cấu hình, không phải một “điểm tài chính” bí ẩn.</span></footer>
      </section>
    </div>
  );
}
