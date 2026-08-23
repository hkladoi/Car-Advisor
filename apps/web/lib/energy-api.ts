import type { SourceBadge } from "@/lib/catalog-api";

export type EnergySource = SourceBadge & {
  sourceFactId: string;
  freshUntil: string;
  isStale: boolean;
};

export type EnergyRequest = {
  trimId: string;
  calculationDate: string;
  monthlyKilometres: number;
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

export type AppliedEnergyRate = {
  rateId: string;
  kind: string;
  provider: string;
  amount: number | null;
  unit: string;
  currency: string;
  taxRate: number | null;
  taxIncluded: boolean;
  effectiveFrom: string;
  effectiveTo: string | null;
  source: EnergySource | null;
};

export type EnergyResponse = {
  result: {
    currentCost: number;
    normalizedCost: number;
    promotionSavings: number;
    fuelLitres: number;
    batteryEnergyKwh: number;
    gridEnergyKwh: number;
    currency: string;
  };
  vehicle: {
    trimId: string;
    brandId: string;
    modelId: string;
    brandName: string;
    modelName: string;
    trimName: string;
    modelYear: number;
    powertrain: string;
  };
  energyProfile: {
    profileId: string;
    officialFuelLitresPer100Km: number | null;
    officialElectricKwhPer100Km: number | null;
    fuelConsumptionCondition: string | null;
    electricConsumptionCondition: string | null;
    testCycle: string | null;
    consumptionNotes: string | null;
    source: EnergySource | null;
  };
  calculationDate: string;
  breakdown: {
    component: string;
    quantity: number;
    unit: string;
    normalizedAmount: number;
    currentAmount: number;
    detail: string;
    appliedRate: AppliedEnergyRate | null;
  }[];
  assumptions: string[];
  appliedRates: AppliedEnergyRate[];
  appliedPromotions: {
    promotionId: string;
    benefit: string;
    benefitValue: number | null;
    effectiveFrom: string;
    effectiveTo: string | null;
    source: EnergySource | null;
  }[];
  warnings: string[];
  calculatedAt: string;
};

export type EnergyOutcome =
  | { data: EnergyResponse; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function calculateEnergy(request: EnergyRequest): Promise<EnergyOutcome> {
  const response = await fetch(`${apiBase()}/api/v1/calculators/energy`, {
    method: "POST",
    cache: "no-store",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(request),
  });
  const payload = (await response.json()) as EnergyResponse | { code?: string; message?: string };
  if (!response.ok) {
    const error = payload as { code?: string; message?: string };
    return {
      data: null,
      error: {
        code: error.code ?? "ENERGY_CALCULATION_FAILED",
        message: error.message ?? "Không thể tính chi phí năng lượng.",
      },
    };
  }
  return { data: payload as EnergyResponse, error: null };
}
