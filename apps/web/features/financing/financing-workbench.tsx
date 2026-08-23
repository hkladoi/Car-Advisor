"use client";

import { useMemo, useState, type FormEvent } from "react";
import { AlertTriangle, ArrowRight, BadgeInfo, Banknote, ChevronDown, CircleCheck, Landmark, ReceiptText, ShieldCheck } from "lucide-react";

import { SourceDetails } from "@/components/source-details";
import type { CatalogCar } from "@/lib/catalog-api";
import type { FinancingRequest, FinancingResponse } from "@/lib/financing-api";
import type { RegionItem } from "@/lib/registration-api";

export type FinancingOfferOption = {
  id: string;
  trimId: string;
  headline: string;
  dealerName: string;
  branchName: string;
  provinceCode: string;
  benefits: { type: string; amount: number | null; cashEquivalent: boolean }[];
  sourceName: string | null;
};

type Props = {
  cars: CatalogCar[];
  regions: RegionItem[];
  offers: FinancingOfferOption[];
  initialRequest: FinancingRequest;
  initialResult: FinancingResponse;
};

const reasonLabels: Record<string, string> = {
  VEHICLE_DEBT_RATIO_EXCEEDED: "Khoản trả xe vượt tỷ lệ nợ xe",
  TOTAL_COMMITMENT_RATIO_EXCEEDED: "Tổng cam kết xe vượt ngưỡng",
  POST_PAYMENT_DISPOSABLE_NEGATIVE: "Phần còn lại sau xe bị âm",
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
  return new Intl.NumberFormat("vi-VN", { style: "percent", maximumFractionDigits: 2 }).format(value);
}

function ratingLabel(value: FinancingResponse["purchaseRating"]) {
  if (value === "ExternallyFunded") return "Gia đình chi trả toàn bộ";
  if (value === "Pass") return "Trong ngưỡng";
  if (value === "Warn") return "Cần thận trọng";
  return "Không đạt";
}

export function FinancingWorkbench({ cars, regions, offers, initialRequest, initialResult }: Props) {
  const [result, setResult] = useState(initialResult);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ code: string; message: string } | null>(null);
  const [trimId, setTrimId] = useState(initialRequest.trimId);
  const [provinceCode, setProvinceCode] = useState(initialRequest.provinceCode);
  const [purchaseMethod, setPurchaseMethod] = useState<"Cash" | "Loan">(initialRequest.purchase.purchaseMethod);
  const [fundingSource, setFundingSource] = useState<"SelfFunded" | "FamilyFunded" | "Mixed">(initialRequest.purchase.fundingSource);
  const [downMode, setDownMode] = useState<"amount" | "percent">("amount");
  const visibleOffers = useMemo(() => offers.filter((offer) => offer.trimId === trimId && offer.provinceCode === provinceCode), [offers, trimId, provinceCode]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    const data = new FormData(event.currentTarget);
    const method = String(data.get("purchaseMethod")) as FinancingRequest["purchase"]["purchaseMethod"];
    const request: FinancingRequest = {
      trimId: String(data.get("trimId")),
      provinceCode: String(data.get("provinceCode")),
      calculationDate: `${String(data.get("calculationDate"))}T12:00:00+07:00`,
      policy: String(data.get("policy")) as FinancingRequest["policy"],
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
        compulsoryInsuranceMonthlyOverride: null,
        roadUsageMonthlyOverride: null,
        inspectionMonthlyOverride: null,
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
        sessionsUsedThisMonth: 0,
        postChargeMinutesPerSession: 0,
        customerType: "Personal",
        purchaseDate: null,
        promotionEligibilityConfirmed: data.get("promotionEligibilityConfirmed") === "true",
      },
      purchase: {
        fundingSource: String(data.get("fundingSource")) as FinancingRequest["purchase"]["fundingSource"],
        purchaseMethod: method,
        availableCash: number(data, "availableCash"),
        familyContribution: fundingSource === "SelfFunded" ? 0 : number(data, "familyContribution"),
        tradeInNetValue: number(data, "tradeInNetValue"),
        downPaymentAmount: method === "Loan" && downMode === "amount" ? number(data, "downPaymentAmount") : null,
        downPaymentPercent: method === "Loan" && downMode === "percent" ? number(data, "downPaymentPercent") / 100 : null,
        annualInterestRate: method === "Loan" ? number(data, "annualInterestRatePercent") / 100 : null,
        interestRateSourceFactId: null,
        termMonths: method === "Loan" ? number(data, "termMonths") : 0,
        repaymentMethod: method === "Loan" ? String(data.get("repaymentMethod")) as FinancingRequest["purchase"]["repaymentMethod"] : "Annuity",
        bankFees: method === "Loan" ? number(data, "bankFees") : 0,
        loanInsuranceUpfront: method === "Loan" ? number(data, "loanInsuranceUpfront") : 0,
        selectedDealerOfferIds: data.getAll("selectedDealerOfferIds").map(String),
      },
    };
    try {
      const response = await fetch("/api/financing", { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify(request) });
      const payload = (await response.json()) as FinancingResponse | { code?: string; message?: string };
      if (!response.ok) {
        const failure = payload as { code?: string; message?: string };
        setError({ code: failure.code ?? "FINANCING_FAILED", message: failure.message ?? "Không thể tính kịch bản." });
      } else {
        setResult(payload as FinancingResponse);
      }
    } catch {
      setError({ code: "NETWORK_ERROR", message: "Không thể kết nối dịch vụ tính mua/vay." });
    } finally {
      setLoading(false);
    }
  }

  const financing = result.financing;
  const cashflow = result.purchaseCashflow;
  const hasLoan = financing.financingStatus === "Applicable";

  return (
    <div className="affordability-layout financing-layout">
      <aside className="affordability-profile-panel">
        <form className="affordability-form financing-form" onSubmit={submit}>
          <div><Landmark aria-hidden="true" size={19} /><h2>Kịch bản mua xe</h2></div>
          <p className="machine-label">ACQUISITION FIRST</p>
          <label htmlFor="trimId">Phiên bản xe</label>
          <select id="trimId" name="trimId" value={trimId} onChange={(event) => setTrimId(event.target.value)}>
            {cars.map((car) => <option key={car.trimId} value={car.trimId}>{car.brandName} {car.modelName} · {car.trimName}</option>)}
          </select>
          <div className="affordability-field-grid">
            <div><label htmlFor="purchaseMethod">Cách mua</label><select id="purchaseMethod" name="purchaseMethod" value={purchaseMethod} onChange={(event) => setPurchaseMethod(event.target.value as "Cash" | "Loan")}><option value="Loan">Vay mua</option><option value="Cash">Trả thẳng</option></select></div>
            <div><label htmlFor="fundingSource">Nguồn tiền</label><select id="fundingSource" name="fundingSource" value={fundingSource} onChange={(event) => setFundingSource(event.target.value as typeof fundingSource)}><option value="SelfFunded">Tự chi trả</option><option value="Mixed">Kết hợp gia đình</option><option value="FamilyFunded">Gia đình chi toàn bộ</option></select></div>
          </div>
          <label htmlFor="availableCash">Tiền mặt sẵn có của bạn</label>
          <input id="availableCash" name="availableCash" type="number" min="0" step="1000000" defaultValue={initialRequest.purchase.availableCash} />
          {fundingSource !== "SelfFunded" && <><label htmlFor="familyContribution">Gia đình / bên ngoài đóng góp</label><input id="familyContribution" name="familyContribution" type="number" min="0" step="1000000" defaultValue={initialRequest.purchase.familyContribution} /></>}
          <label htmlFor="tradeInNetValue">Giá trị thuần xe đổi cũ</label>
          <input id="tradeInNetValue" name="tradeInNetValue" type="number" min="0" step="1000000" defaultValue={initialRequest.purchase.tradeInNetValue} />

          {purchaseMethod === "Loan" && <fieldset className="financing-loan-fields">
            <legend>Khoản vay</legend>
            <label htmlFor="downMode">Nhập trả trước theo</label><select id="downMode" name="downMode" value={downMode} onChange={(event) => setDownMode(event.target.value as typeof downMode)}><option value="amount">Số tiền</option><option value="percent">Tỷ lệ</option></select>
            {downMode === "amount" ? <><label htmlFor="downPaymentAmount">Tiền trả trước</label><input id="downPaymentAmount" name="downPaymentAmount" type="number" min="0" step="1000000" defaultValue={initialRequest.purchase.downPaymentAmount ?? 0} /></> : <><label htmlFor="downPaymentPercent">Tỷ lệ trả trước (%)</label><input id="downPaymentPercent" name="downPaymentPercent" type="number" min="0" max="100" step="0.1" defaultValue={(initialRequest.purchase.downPaymentPercent ?? 0.2) * 100} /></>}
            <div className="affordability-field-grid">
              <div><label htmlFor="annualInterestRatePercent">Lãi suất / năm (%)</label><input id="annualInterestRatePercent" name="annualInterestRatePercent" type="number" min="0" max="100" step="0.01" defaultValue={(initialRequest.purchase.annualInterestRate ?? 0) * 100} /></div>
              <div><label htmlFor="termMonths">Thời hạn (tháng)</label><input id="termMonths" name="termMonths" type="number" min="1" max="480" defaultValue={initialRequest.purchase.termMonths} /></div>
            </div>
            <label htmlFor="repaymentMethod">Cách trả gốc/lãi</label><select id="repaymentMethod" name="repaymentMethod" defaultValue={initialRequest.purchase.repaymentMethod}><option value="Annuity">Niên kim · trả đều</option><option value="ReducingBalance">Dư nợ giảm dần</option></select>
            <p className="field-note">Chưa có snapshot lãi suất ngân hàng/đại lý đã kiểm duyệt trong dữ liệu hiện tại; mức trên được ghi rõ là giả định người dùng.</p>
          </fieldset>}

          <details className="affordability-advanced">
            <summary>Advanced · dòng tiền, chi phí nuôi, nguồn ưu đãi <ChevronDown aria-hidden="true" size={16} /></summary>
            <div className="advanced-fields">
              <fieldset>
                <legend>Hồ sơ dòng tiền</legend>
                <label htmlFor="netMonthlyIncome">Thu nhập thực nhận / tháng</label><input id="netMonthlyIncome" name="netMonthlyIncome" type="number" min="100000" step="100000" required defaultValue={initialRequest.netMonthlyIncome} />
                <div className="affordability-field-grid"><div><label htmlFor="rentHousing">Nhà ở / thuê</label><input id="rentHousing" name="rentHousing" type="number" min="0" step="100000" defaultValue={initialRequest.rentHousing} /></div><div><label htmlFor="essentialExpenses">Chi thiết yếu</label><input id="essentialExpenses" name="essentialExpenses" type="number" min="0" step="100000" defaultValue={initialRequest.essentialExpenses} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="otherFixedDebt">Nợ cố định khác</label><input id="otherFixedDebt" name="otherFixedDebt" type="number" min="0" step="100000" defaultValue={initialRequest.otherFixedDebt} /></div><div><label htmlFor="savingsTarget">Mục tiêu tiết kiệm</label><input id="savingsTarget" name="savingsTarget" type="number" min="0" step="100000" defaultValue={initialRequest.savingsTarget} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="policy">Mức thận trọng</label><select id="policy" name="policy" defaultValue={initialRequest.policy}><option value="Conservative">Thận trọng</option><option value="Balanced">Cân bằng</option><option value="Aggressive">Chấp nhận cao</option></select></div><div><label htmlFor="maximumMonthlyVehicleSpend">Trần chi xe tự đặt</label><input id="maximumMonthlyVehicleSpend" name="maximumMonthlyVehicleSpend" type="number" min="0" step="100000" /></div></div>
              </fieldset>
              <fieldset>
                <legend>Đăng ký & chi phí nuôi</legend>
                <label htmlFor="provinceCode">Tỉnh/thành đăng ký</label><select id="provinceCode" name="provinceCode" value={provinceCode} onChange={(event) => setProvinceCode(event.target.value)}>{regions.map((region) => <option value={region.code} key={region.code}>{region.name} · KV {region.areaClass}</option>)}</select>
                <label htmlFor="calculationDate">Ngày tính</label><input id="calculationDate" name="calculationDate" type="date" defaultValue={initialRequest.calculationDate.slice(0, 10)} />
                <div className="affordability-field-grid"><div><label htmlFor="monthlyKilometres">Km / tháng</label><input id="monthlyKilometres" name="monthlyKilometres" type="number" min="0" defaultValue={initialRequest.expenses.monthlyKilometres} /></div><div><label htmlFor="parkingMonthly">Gửi xe / tháng</label><input id="parkingMonthly" name="parkingMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.parkingMonthly} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="maintenanceReserveMonthly">Dự phòng bảo dưỡng</label><input id="maintenanceReserveMonthly" name="maintenanceReserveMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.maintenanceReserveMonthly} /></div><div><label htmlFor="tyreReserveMonthly">Dự phòng lốp</label><input id="tyreReserveMonthly" name="tyreReserveMonthly" type="number" min="0" step="50000" defaultValue={initialRequest.expenses.tyreReserveMonthly} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="bodyInsuranceAnnual">Bảo hiểm thân vỏ / năm</label><input id="bodyInsuranceAnnual" name="bodyInsuranceAnnual" type="number" min="0" defaultValue={initialRequest.expenses.bodyInsuranceAnnual} /></div><div><label htmlFor="batteryRentalMonthly">Thuê pin / tháng</label><input id="batteryRentalMonthly" name="batteryRentalMonthly" type="number" min="0" defaultValue={initialRequest.expenses.batteryRentalMonthly} /></div></div>
                <label className="affordability-check"><input type="checkbox" name="firstInspectionExempt" value="true" defaultChecked={initialRequest.expenses.firstInspectionExempt} /><span>Xe mới đủ điều kiện miễn kiểm định lần đầu</span></label>
              </fieldset>
              <fieldset>
                <legend>Năng lượng</legend>
                <div className="affordability-field-grid"><div><label htmlFor="homeSharePercent">Sạc nhà (%)</label><input id="homeSharePercent" name="homeSharePercent" type="number" min="0" max="100" defaultValue={initialRequest.energy.homeChargingShare * 100} /></div><div><label htmlFor="evSharePercent">PHEV chạy EV (%)</label><input id="evSharePercent" name="evSharePercent" type="number" min="0" max="100" defaultValue={initialRequest.energy.evShare * 100} /></div></div>
                <div className="affordability-field-grid"><div><label htmlFor="chargingEfficiencyPercent">Hiệu suất sạc (%)</label><input id="chargingEfficiencyPercent" name="chargingEfficiencyPercent" type="number" min="1" max="100" defaultValue={initialRequest.energy.chargingEfficiency * 100} /></div><div><label htmlFor="householdBaseKwh">Điện nền gia đình</label><input id="householdBaseKwh" name="householdBaseKwh" type="number" min="0" defaultValue={initialRequest.energy.householdBaseKwh} /></div></div>
                <label htmlFor="homeMode">Giá điện nhà</label><select id="homeMode" name="homeMode" defaultValue={initialRequest.energy.homeMode}><option value="EvnMarginalTiers">EVN 6 bậc · phần tăng thêm</option><option value="CustomFixedRate">Giá tự nhập</option></select>
                <label htmlFor="customHomeAmountPerKwh">Giá điện custom</label><input id="customHomeAmountPerKwh" name="customHomeAmountPerKwh" type="number" min="0" />
                <div className="affordability-field-grid"><div><label htmlFor="publicSessions">Phiên sạc công cộng</label><input id="publicSessions" name="publicSessions" type="number" min="0" defaultValue={initialRequest.energy.publicSessions} /></div><div><label htmlFor="chargingPowerKw">Công suất trụ (kW)</label><input id="chargingPowerKw" name="chargingPowerKw" type="number" min="0" step="0.1" defaultValue={initialRequest.energy.chargingPowerKw ?? 60} /></div></div>
                <label htmlFor="connectorType">Loại trụ</label><select id="connectorType" name="connectorType" defaultValue={initialRequest.energy.connectorType ?? "DC"}><option value="DC">DC</option><option value="AC7">AC 7 kW</option><option value="AC11">AC 11 kW</option></select>
                <label htmlFor="fuelType">Nhiên liệu</label><select id="fuelType" name="fuelType" defaultValue={initialRequest.energy.fuelType}><option value="E10Ron95III">E10RON95-III</option><option value="Ron92E5">E5RON92-II</option><option value="Diesel">Diesel</option></select>
                <label className="affordability-check"><input type="checkbox" name="promotionEligibilityConfirmed" value="true" /><span>Tôi xác nhận đủ điều kiện ưu đãi năng lượng</span></label>
              </fieldset>
              {purchaseMethod === "Loan" && <fieldset>
                <legend>Phí vay & ưu đãi đại lý</legend>
                <div className="affordability-field-grid"><div><label htmlFor="bankFees">Phí ngân hàng trả trước</label><input id="bankFees" name="bankFees" type="number" min="0" defaultValue={initialRequest.purchase.bankFees} /></div><div><label htmlFor="loanInsuranceUpfront">Bảo hiểm khoản vay trả trước</label><input id="loanInsuranceUpfront" name="loanInsuranceUpfront" type="number" min="0" defaultValue={initialRequest.purchase.loanInsuranceUpfront} /></div></div>
                {visibleOffers.length === 0 ? <p className="financing-no-offer">Không có ưu đãi tài chính/đổi xe đã xuất bản và có nguồn cho phiên bản, khu vực này. Hệ thống không tự tạo bonus.</p> : visibleOffers.map((offer) => <label className="financing-offer" key={offer.id}><input type="checkbox" name="selectedDealerOfferIds" value={offer.id} /><span><strong>{offer.headline}</strong><small>{offer.dealerName} · {offer.branchName} · {offer.sourceName ?? "chưa gắn nguồn"}</small></span></label>)}
              </fieldset>}
            </div>
          </details>
          <button className="button-control button-primary" type="submit" disabled={loading}>{loading ? "Đang tính…" : "Tính toàn bộ dòng tiền"} <ArrowRight aria-hidden="true" size={17} /></button>
          <p className="form-assumption"><BadgeInfo aria-hidden="true" size={16} /> Không lưu hồ sơ thu nhập trong URL hay database; request chỉ dùng cho phép tính hiện tại.</p>
        </form>
      </aside>

      <section className="affordability-results financing-results" aria-live="polite">
        {error && <div className="onroad-error"><AlertTriangle aria-hidden="true" /><div><p className="machine-label">{error.code}</p><h2>Chưa thể tính kịch bản.</h2><p>{error.message}</p></div></div>}
        <header className={`financing-hero financing-hero--${result.purchaseRating.toLocaleLowerCase()}`}>
          <div><p className="machine-label">PURCHASE RATING · {result.policy}</p><h2>{ratingLabel(result.purchaseRating)}</h2><p>{result.onRoad.vehicle.brandName} {result.onRoad.vehicle.modelName} · {result.onRoad.vehicle.trimName}</p></div>
          <div><span>Giá cần sở hữu ban đầu</span><strong>{money(financing.acquisitionCost)}</strong></div>
        </header>
        <div className="estimate-notice"><ShieldCheck aria-hidden="true" size={19} /><div><strong>Ước tính kịch bản — không phải phê duyệt tín dụng.</strong><span>Giá ra biển và chi phí pháp lý có nguồn; lãi suất {result.interestRate.origin === "VerifiedSource" ? "đến từ snapshot đã kiểm chứng" : result.interestRate.origin === "UserInput" ? "là giả định bạn nhập" : "không áp dụng"}.</span></div></div>

        <section className="financing-section">
          <header><p className="machine-label">01 · ACQUISITION CASH</p><h2>Tiền vào ngày mua</h2></header>
          <div className="financing-stat-grid">
            <div><span>Cần trả trước</span><strong>{money(financing.upfrontCashRequired)}</strong></div>
            <div><span>Tiền bạn có</span><strong>{money(financing.availableCash)}</strong></div>
            <div className={financing.cashShortfall > 0 ? "is-danger" : ""}><span>Thiếu hụt</span><strong>{money(financing.cashShortfall)}</strong></div>
            <div><span>Gia đình / bên ngoài</span><strong>{money(financing.externalContribution)}</strong></div>
            <div><span>Xe đổi cũ</span><strong>{money(financing.tradeInNetValue)}</strong></div>
            <div><span>Credit đủ điều kiện</span><strong>{money(financing.otherUpfrontCredits)}</strong></div>
          </div>
          <div className="financing-source-row"><div><strong>Giá ra biển {money(result.onRoad.result.onRoadPrice)}</strong><span>{result.onRoad.region.name} · hiệu lực {result.onRoad.calculationDate.slice(0, 10)}</span></div>{result.onRoad.inputPrice.source && <SourceDetails source={result.onRoad.inputPrice.source} compact />}</div>
        </section>

        <section className="financing-section">
          <header><p className="machine-label">02 · FINANCING</p><h2>{hasLoan ? "Lịch trả vay" : "Không có khoản vay"}</h2></header>
          {hasLoan ? <>
            <div className="financing-stat-grid financing-stat-grid--loan">
              <div><span>Gốc vay</span><strong>{money(financing.loanPrincipal)}</strong></div>
              <div><span>Kỳ đầu</span><strong>{money(financing.firstPayment)}</strong></div>
              <div><span>Kỳ bình quân</span><strong>{money(financing.averagePayment)}</strong></div>
              <div><span>Kỳ cuối</span><strong>{money(financing.lastPayment)}</strong></div>
              <div><span>Tổng lãi</span><strong>{money(financing.totalInterest)}</strong></div>
              <div><span>Tổng trả ngân hàng</span><strong>{money(financing.totalLoanRepayment)}</strong></div>
            </div>
            <div className="financing-source-row"><div><strong>Lãi suất {percent(result.interestRate.annualInterestRate)} / năm</strong><span>{result.interestRate.origin === "VerifiedSource" ? "Verified source" : "User input · chưa phải báo giá"}</span></div>{result.interestRate.source && <SourceDetails source={result.interestRate.source} compact />}</div>
          </> : <p className="financing-empty-state">{financing.purchaseStatus === "ExternallyFunded" ? "Nguồn bên ngoài đã bao phủ toàn bộ chi phí mua; khoản vay và tiền mặt của người dùng là NotApplicable." : "Trả thẳng nên principal, payment và total interest đều bằng 0."}</p>}
        </section>

        <section className="financing-section">
          <header><p className="machine-label">03 · MONTHLY COMMITMENT</p><h2>Nuôi xe + trả vay mỗi tháng</h2></header>
          <div className="commitment-equation" aria-label="Công thức tổng cam kết tháng">
            <div><span>Chi phí nuôi chuẩn hóa</span><strong>{money(result.ownership.result.normalizedMonthlyCost)}</strong></div><b aria-hidden="true">+</b><div><span>Khoản vay dùng cho gate</span><strong>{money(financing.monthlyPaymentForCommitment)}</strong></div><b aria-hidden="true">=</b><div><span>Tổng cam kết xe</span><strong>{money(cashflow.totalMonthlyVehicleCommitment)}</strong></div>
          </div>
          <dl className="financing-ratios">
            <div><dt>VehicleDebtRatio</dt><dd>{percent(cashflow.vehicleDebtRatio)}</dd></div>
            <div><dt>TotalDebtRatio</dt><dd>{percent(cashflow.totalDebtRatio)}</dd></div>
            <div><dt>TotalCommitmentRatio</dt><dd>{percent(cashflow.totalCommitmentRatio)}</dd></div>
            <div><dt>Còn lại sau xe</dt><dd>{money(cashflow.postPaymentDisposable)}</dd></div>
          </dl>
          {cashflow.reasons.length > 0 && <div className="reason-strip">{cashflow.reasons.map((reason) => <span key={reason}>{reasonLabels[reason] ?? reason}</span>)}</div>}
          <div className="financing-dual-status"><div><span>Gate mua/vay</span><strong>{cashflow.rating}</strong></div><div><span>Gate chỉ chi phí nuôi</span><strong>{result.ownershipAffordability.rating}</strong></div></div>
        </section>

        <details className="affordability-details financing-details">
          <summary>Nguồn, credit và giả định chi tiết <ChevronDown aria-hidden="true" size={16} /></summary>
          <div className="financing-detail-body">
            <section><h3>Credit đại lý đã áp dụng</h3>{result.appliedDealerCredits.length === 0 ? <p>Không có. Điều kiện false hoặc không có offer đã chọn sẽ không bao giờ tạo giảm trừ.</p> : result.appliedDealerCredits.map((credit) => <div className="financing-credit" key={credit.benefitId}><div><strong>{credit.offerHeadline}</strong><span>{credit.type} · {money(credit.amount)}</span></div>{credit.source && <SourceDetails source={credit.source} compact />}</div>)}</section>
            <section><h3>Giả định công khai</h3>{result.assumptions.map((assumption) => <p key={assumption}>{assumption}</p>)}</section>
            {result.warnings.length > 0 && <section><h3>Cảnh báo dữ liệu</h3>{result.warnings.map((warning) => <p key={warning}><AlertTriangle aria-hidden="true" size={15} />{warning}</p>)}</section>}
          </div>
        </details>
        <footer className="affordability-policy-note"><Banknote aria-hidden="true" size={18} /><span>Ngưỡng {result.policy}: tiền vay xe tối đa {percent(result.purchaseThresholds.maximumVehicleDebtRatio)}, tổng cam kết xe tối đa {percent(result.purchaseThresholds.maximumTotalCommitmentRatio)} thu nhập. Dư nợ giảm dần dùng kỳ đầu — không dùng bình quân để làm đẹp kết quả.</span></footer>
        <div className="financing-meta"><ReceiptText aria-hidden="true" size={16} /><span>Tính lúc {new Intl.DateTimeFormat("vi-VN", { dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Ho_Chi_Minh" }).format(new Date(result.calculatedAt))}</span><CircleCheck aria-hidden="true" size={16} /><span>Ownership và acquisition được báo riêng</span></div>
      </section>
    </div>
  );
}
