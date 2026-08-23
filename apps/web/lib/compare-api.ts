import type { AffordabilityRequest } from "@/lib/affordability-api";
import type { SourceBadge } from "@/lib/catalog-api";
import type { FinancingRequest } from "@/lib/financing-api";

export type CompareProfilePreset = "lean-city" | "city-balanced" | "high-mileage-public";
export type CompareFinancingPreset = "cash-preset" | "standard-loan" | "short-reducing";

export type CompareRequest = Omit<AffordabilityRequest, "trimIds"> & {
  trimIds: string[];
  profilePreset: CompareProfilePreset;
  financingPreset: CompareFinancingPreset;
  purchase: FinancingRequest["purchase"];
};

export type CompareSource = SourceBadge & { sourceFactId: string };

export type CompareResponse = {
  vehicles: {
    trimId: string;
    brandName: string;
    modelName: string;
    trimName: string;
    modelYear: number;
    bodyType: string;
    segment: string;
    powertrain: string;
    dataUpdatedAt: string;
  }[];
  scenario: {
    provinceCode: string;
    calculationDate: string;
    profilePreset: string;
    financingPreset: string;
    policy: string;
    monthlyKilometres: number;
    parkingMonthly: number;
    fundingSource: string;
    purchaseMethod: string;
    repaymentMethod: string;
    annualInterestRate: number | null;
    termMonths: number;
    downPaymentPercent: number | null;
    currency: string;
  };
  rows: {
    code: string;
    label: string;
    section: string;
    canonicalUnit: string | null;
    format: "Money" | "Number" | "Boolean" | "Text";
    different: boolean;
    cells: {
      trimId: string;
      state: string;
      numericValue: number | null;
      textValue: string | null;
      booleanValue: boolean | null;
      sources: CompareSource[];
      note: string | null;
    }[];
  }[];
  warnings: string[];
  calculatedAt: string;
};

export type CompareOutcome =
  | { data: CompareResponse; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

const ownershipBase = {
  bodyInsuranceAnnual: 0,
  tyreReserveMonthly: 300_000,
  batteryRentalMonthly: 0,
  compulsoryInsuranceMonthlyOverride: null,
  roadUsageMonthlyOverride: null,
  inspectionMonthlyOverride: null,
  firstInspectionExempt: true,
};

const energyBase = {
  fuelType: "E10Ron95III",
  evShare: 0.5,
  chargingEfficiency: 0.9,
  homeMode: "EvnMarginalTiers",
  householdBaseKwh: 250,
  customHomeAmountPerKwh: null,
  chargingProviderSlug: "v-green",
  connectorType: "DC",
  chargingPowerKw: 60,
  publicSessions: 6,
  sessionsUsedThisMonth: 0,
  postChargeMinutesPerSession: 0,
  customerType: "Personal",
  purchaseDate: null,
  promotionEligibilityConfirmed: false,
};

export function buildCompareRequest(
  calculationDate: string,
  trimIds: string[],
  provinceCode: string,
  profilePreset: CompareProfilePreset,
  financingPreset: CompareFinancingPreset,
): CompareRequest {
  const profile = profilePreset === "lean-city"
    ? { netMonthlyIncome: 30_000_000, rentHousing: 3_000_000, essentialExpenses: 6_000_000, savingsTarget: 2_000_000, monthlyKilometres: 800, parkingMonthly: 500_000, maintenanceReserveMonthly: 800_000, homeChargingShare: 1 }
    : profilePreset === "high-mileage-public"
      ? { netMonthlyIncome: 50_000_000, rentHousing: 8_000_000, essentialExpenses: 8_000_000, savingsTarget: 3_000_000, monthlyKilometres: 2_500, parkingMonthly: 2_000_000, maintenanceReserveMonthly: 1_500_000, homeChargingShare: 0 }
      : { netMonthlyIncome: 50_000_000, rentHousing: 8_000_000, essentialExpenses: 8_000_000, savingsTarget: 3_000_000, monthlyKilometres: 1_000, parkingMonthly: 1_200_000, maintenanceReserveMonthly: 1_000_000, homeChargingShare: 0.7 };
  const purchase: FinancingRequest["purchase"] = financingPreset === "cash-preset"
    ? { fundingSource: "SelfFunded", purchaseMethod: "Cash", availableCash: 3_000_000_000, familyContribution: 0, tradeInNetValue: 0, downPaymentAmount: null, downPaymentPercent: null, annualInterestRate: null, interestRateSourceFactId: null, termMonths: 0, repaymentMethod: "Annuity", bankFees: 0, loanInsuranceUpfront: 0, selectedDealerOfferIds: [] }
    : financingPreset === "short-reducing"
      ? { fundingSource: "SelfFunded", purchaseMethod: "Loan", availableCash: 800_000_000, familyContribution: 0, tradeInNetValue: 0, downPaymentAmount: null, downPaymentPercent: 0.3, annualInterestRate: 0.1, interestRateSourceFactId: null, termMonths: 36, repaymentMethod: "ReducingBalance", bankFees: 0, loanInsuranceUpfront: 0, selectedDealerOfferIds: [] }
      : { fundingSource: "SelfFunded", purchaseMethod: "Loan", availableCash: 600_000_000, familyContribution: 0, tradeInNetValue: 0, downPaymentAmount: null, downPaymentPercent: 0.2, annualInterestRate: 0.12, interestRateSourceFactId: null, termMonths: 60, repaymentMethod: "Annuity", bankFees: 0, loanInsuranceUpfront: 0, selectedDealerOfferIds: [] };
  return {
    trimIds,
    provinceCode,
    calculationDate: `${calculationDate}T12:00:00+07:00`,
    profilePreset,
    financingPreset,
    policy: "Balanced",
    netMonthlyIncome: profile.netMonthlyIncome,
    rentHousing: profile.rentHousing,
    essentialExpenses: profile.essentialExpenses,
    otherFixedDebt: 0,
    savingsTarget: profile.savingsTarget,
    maximumMonthlyVehicleSpend: null,
    expenses: {
      ...ownershipBase,
      monthlyKilometres: profile.monthlyKilometres,
      parkingMonthly: profile.parkingMonthly,
      maintenanceReserveMonthly: profile.maintenanceReserveMonthly,
    },
    energy: { ...energyBase, homeChargingShare: profile.homeChargingShare },
    purchase,
  };
}

export async function calculateCompare(request: CompareRequest): Promise<CompareOutcome> {
  const response = await fetch(`${apiBase()}/api/v1/compare/calculate`, {
    method: "POST",
    cache: "no-store",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(request),
  });
  const payload = (await response.json()) as CompareResponse | { code?: string; message?: string };
  if (!response.ok) {
    const error = payload as { code?: string; message?: string };
    return { data: null, error: { code: error.code ?? "COMPARE_FAILED", message: error.message ?? "Không thể so sánh các phiên bản." } };
  }
  return { data: payload as CompareResponse, error: null };
}
