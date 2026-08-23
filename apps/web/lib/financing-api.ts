import type { AffordabilityCandidate, AffordabilityRequest, OwnershipCostComponent } from "@/lib/affordability-api";
import { defaultAffordabilityRequest } from "@/lib/affordability-api";
import type { SourceBadge } from "@/lib/catalog-api";
import type { EnergyResponse } from "@/lib/energy-api";
import type { OnRoadResponse } from "@/lib/registration-api";

export type FinancingRequest = Omit<AffordabilityRequest, "trimIds"> & {
  trimId: string;
  purchase: {
    fundingSource: "SelfFunded" | "FamilyFunded" | "Mixed";
    purchaseMethod: "Cash" | "Loan";
    availableCash: number;
    familyContribution: number;
    tradeInNetValue: number;
    downPaymentAmount: number | null;
    downPaymentPercent: number | null;
    annualInterestRate: number | null;
    interestRateSourceFactId: string | null;
    termMonths: number;
    repaymentMethod: "Annuity" | "ReducingBalance";
    bankFees: number;
    loanInsuranceUpfront: number;
    selectedDealerOfferIds: string[];
  };
};

type RuleSource = SourceBadge & { sourceFactId: string };

export type FinancingResponse = {
  policy: string;
  purchaseRating: "Pass" | "Warn" | "Fail" | "ExternallyFunded";
  profile: {
    netMonthlyIncome: number;
    rentHousing: number;
    essentialExpenses: number;
    otherFixedDebt: number;
    savingsTarget: number;
    disposableIncomeBeforeVehicle: number;
    currency: string;
  };
  ownership: {
    result: {
      currentMonthlyCost: number;
      normalizedMonthlyCost: number;
      worstReasonableMonthlyCost: number;
      breakdown: OwnershipCostComponent[];
    };
    vehicle: { trimId: string; brandName: string; modelName: string; trimName: string; modelYear: number; powertrain: string };
    energy: EnergyResponse;
    appliedRecurringRules: { ruleId: string; component: string; version: number; source: RuleSource | null }[];
    assumptions: string[];
    warnings: string[];
    calculatedAt: string;
  };
  ownershipAffordability: AffordabilityCandidate["evaluation"];
  onRoad: OnRoadResponse;
  financing: {
    purchaseStatus: "Pass" | "Fail" | "ExternallyFunded";
    financingStatus: "Applicable" | "NotApplicable";
    acquisitionCost: number;
    externalContribution: number;
    tradeInNetValue: number;
    otherUpfrontCredits: number;
    financedBasis: number;
    downPayment: number;
    upfrontCashRequired: number;
    availableCash: number;
    cashShortfall: number;
    loanPrincipal: number;
    firstPayment: number;
    averagePayment: number;
    lastPayment: number;
    monthlyPaymentForCommitment: number;
    totalInterest: number;
    totalLoanRepayment: number;
    currency: string;
  };
  purchaseThresholds: {
    maximumVehicleDebtRatio: number;
    maximumTotalCommitmentRatio: number;
    warningUtilization: number;
  };
  purchaseCashflow: {
    vehicleDebtRatio: number;
    totalDebtRatio: number;
    totalMonthlyVehicleCommitment: number;
    totalCommitmentRatio: number;
    postPaymentDisposable: number;
    rating: "Pass" | "Warn" | "Fail";
    reasons: string[];
  };
  interestRate: {
    annualInterestRate: number;
    origin: "UserInput" | "VerifiedSource" | "NotApplicable";
    fieldPath: string | null;
    rawValue: string | null;
    source: RuleSource | null;
  };
  appliedDealerCredits: {
    offerId: string;
    benefitId: string;
    offerHeadline: string;
    type: string;
    amount: number;
    currency: string;
    note: string | null;
    source: RuleSource | null;
  }[];
  assumptions: string[];
  warnings: string[];
  calculatedAt: string;
};

export type FinancingOutcome =
  | { data: FinancingResponse; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export function defaultFinancingRequest(calculationDate: string, trimId: string): FinancingRequest {
  const ownership = defaultAffordabilityRequest(calculationDate);
  return {
    ...ownership,
    trimId,
    netMonthlyIncome: 50_000_000,
    rentHousing: 8_000_000,
    essentialExpenses: 8_000_000,
    savingsTarget: 3_000_000,
    purchase: {
      fundingSource: "SelfFunded",
      purchaseMethod: "Loan",
      availableCash: 150_000_000,
      familyContribution: 0,
      tradeInNetValue: 0,
      downPaymentAmount: 150_000_000,
      downPaymentPercent: null,
      annualInterestRate: 0.12,
      interestRateSourceFactId: null,
      termMonths: 60,
      repaymentMethod: "Annuity",
      bankFees: 0,
      loanInsuranceUpfront: 0,
      selectedDealerOfferIds: [],
    },
  };
}

export async function calculateFinancing(request: FinancingRequest): Promise<FinancingOutcome> {
  const response = await fetch(`${apiBase()}/api/v1/financing/calculate`, {
    method: "POST",
    cache: "no-store",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(request),
  });
  const payload = (await response.json()) as FinancingResponse | { code?: string; message?: string };
  if (!response.ok) {
    const error = payload as { code?: string; message?: string };
    return { data: null, error: { code: error.code ?? "FINANCING_CALCULATION_FAILED", message: error.message ?? "Không thể tính kịch bản mua xe." } };
  }
  return { data: payload as FinancingResponse, error: null };
}
