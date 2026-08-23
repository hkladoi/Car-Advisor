import type { SourceBadge } from "@/lib/catalog-api";
import type { EnergyResponse } from "@/lib/energy-api";

export type AffordabilityRequest = {
  trimIds: string[];
  provinceCode: string;
  calculationDate: string;
  policy: "Conservative" | "Balanced" | "Aggressive";
  netMonthlyIncome: number;
  rentHousing: number;
  essentialExpenses: number;
  otherFixedDebt: number;
  savingsTarget: number;
  maximumMonthlyVehicleSpend: number | null;
  expenses: {
    monthlyKilometres: number;
    parkingMonthly: number;
    maintenanceReserveMonthly: number;
    bodyInsuranceAnnual: number;
    tyreReserveMonthly: number;
    batteryRentalMonthly: number;
    compulsoryInsuranceMonthlyOverride: number | null;
    roadUsageMonthlyOverride: number | null;
    inspectionMonthlyOverride: number | null;
    firstInspectionExempt: boolean;
  };
  energy: {
    fuelType: string;
    evShare: number;
    homeChargingShare: number;
    chargingEfficiency: number;
    homeMode: string;
    householdBaseKwh: number;
    customHomeAmountPerKwh: number | null;
    chargingProviderSlug: string;
    connectorType: string | null;
    chargingPowerKw: number | null;
    publicSessions: number;
    sessionsUsedThisMonth: number;
    postChargeMinutesPerSession: number;
    customerType: string;
    purchaseDate: string | null;
    promotionEligibilityConfirmed: boolean;
  };
};

export type OwnershipCostComponent = {
  component: string;
  currentAmount: number;
  normalizedAmount: number;
  worstReasonableAmount: number;
  origin: string;
  note: string;
};

type AffordabilityBand = {
  band: string;
  monthlyVehicleCashflow: number;
  incomeRatio: number;
  disposableRatio: number;
  eligible: boolean;
  rating: string;
  reasons: string[];
};

export type AffordabilityCandidate = {
  vehicle: {
    trimId: string;
    brandName: string;
    modelName: string;
    trimName: string;
    modelYear: number;
    powertrain: string;
  };
  evaluation: {
    eligible: boolean;
    rating: string;
    disposableIncome: number;
    current: AffordabilityBand;
    normalized: AffordabilityBand;
    worstReasonable: AffordabilityBand;
    reasons: string[];
  };
  ownership: {
    result: {
      currentMonthlyCost: number;
      normalizedMonthlyCost: number;
      worstReasonableMonthlyCost: number;
      breakdown: OwnershipCostComponent[];
    };
    energy: EnergyResponse;
    appliedRecurringRules: {
      ruleId: string;
      component: string;
      version: number;
      priority: number;
      effectiveFrom: string;
      effectiveTo: string | null;
      source: SourceBadge | null;
    }[];
    assumptions: string[];
    warnings: string[];
    calculatedAt: string;
  };
};

export type AffordabilityResponse = {
  policy: string;
  thresholds: {
    maximumIncomeRatio: number;
    maximumDisposableRatio: number;
    warningUtilization: number;
  };
  profile: {
    netMonthlyIncome: number;
    rentHousing: number;
    essentialExpenses: number;
    otherFixedDebt: number;
    savingsTarget: number;
    maximumMonthlyVehicleSpend: number | null;
    disposableIncomeBeforeVehicle: number;
    currency: string;
  };
  eligibleCars: AffordabilityCandidate[];
  overBudgetCars: AffordabilityCandidate[];
  dataExcludedCars: {
    vehicle: AffordabilityCandidate["vehicle"];
    reasons: string[];
    explanation: string;
  }[];
  assumptions: string[];
  evaluatedAt: string;
};

export type AffordabilityOutcome =
  | { data: AffordabilityResponse; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export function defaultAffordabilityRequest(calculationDate: string): AffordabilityRequest {
  return {
    trimIds: [],
    provinceCode: "VN-01",
    calculationDate: `${calculationDate}T12:00:00+07:00`,
    policy: "Balanced",
    netMonthlyIncome: 30_000_000,
    rentHousing: 5_000_000,
    essentialExpenses: 6_000_000,
    otherFixedDebt: 0,
    savingsTarget: 2_000_000,
    maximumMonthlyVehicleSpend: null,
    expenses: {
      monthlyKilometres: 1_000,
      parkingMonthly: 1_200_000,
      maintenanceReserveMonthly: 1_000_000,
      bodyInsuranceAnnual: 0,
      tyreReserveMonthly: 300_000,
      batteryRentalMonthly: 0,
      compulsoryInsuranceMonthlyOverride: null,
      roadUsageMonthlyOverride: null,
      inspectionMonthlyOverride: null,
      firstInspectionExempt: true,
    },
    energy: {
      fuelType: "E10Ron95III",
      evShare: 0.5,
      homeChargingShare: 0.7,
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
    },
  };
}

export async function evaluateAffordability(request: AffordabilityRequest): Promise<AffordabilityOutcome> {
  const response = await fetch(`${apiBase()}/api/v1/affordability/evaluate`, {
    method: "POST",
    cache: "no-store",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(request),
  });
  const payload = (await response.json()) as AffordabilityResponse | { code?: string; message?: string };
  if (!response.ok) {
    const error = payload as { code?: string; message?: string };
    return {
      data: null,
      error: {
        code: error.code ?? "AFFORDABILITY_EVALUATION_FAILED",
        message: error.message ?? "Không thể đánh giá chi phí sở hữu.",
      },
    };
  }
  return { data: payload as AffordabilityResponse, error: null };
}
