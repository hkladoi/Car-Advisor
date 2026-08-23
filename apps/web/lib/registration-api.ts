import type { CatalogCar, SourceBadge } from "@/lib/catalog-api";

export type RegionItem = {
  code: string;
  name: string;
  areaClass: string;
  type: string;
  source: SourceBadge | null;
};

export type RegionsResponse = { data: RegionItem[]; generatedAt: string };

export type OnRoadRequest = {
  trimId: string;
  provinceCode: string;
  calculationDate: string;
  buyerType: string;
  vehicleType: "PassengerCar";
  firstInspectionExempt: boolean;
  roadUsageMonths: number;
  selectedOfferIds: string[];
};

type AppliedRule = {
  ruleId: string;
  component: string;
  version: number;
  priority: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  source: SourceBadge | null;
};

type Benefit = {
  type: string;
  cashValue: number | null;
  statedValue: number | null;
  isCashEquivalent: boolean;
  origin: string;
  originId: string;
  note: string | null;
};

export type OnRoadResponse = {
  result: { onRoadPrice: number; effectiveCashPurchasePrice: number; inputPrice: number; cashPurchaseReduction: number; eligibleFeeSupportBenefits: number; currency: string };
  vehicle: Pick<CatalogCar, "trimId" | "brandName" | "modelName" | "trimName" | "modelYear"> & { powertrain: string; seats: number | null };
  region: RegionItem;
  calculationDate: string;
  inputPrice: { priceId: string; priceType: string; version: number; amount: number; currency: string; regionScope: string; effectiveFrom: string; effectiveTo: string | null; source: SourceBadge | null };
  breakdown: { component: string; beforeSupport: number; eligibleSupport: number; amount: number; appliedRule: AppliedRule }[];
  assumptions: string[];
  appliedRules: AppliedRule[];
  appliedBenefits: Benefit[];
  nonCashBenefits: Benefit[];
  warnings: string[];
  calculatedAt: string;
};

export type CalculationOutcome =
  | { data: OnRoadResponse; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function getRegions(): Promise<RegionsResponse> {
  const response = await fetch(`${apiBase()}/api/v1/regions`, { cache: "no-store" });
  if (!response.ok) throw new Error(`Regions API returned ${response.status}`);
  return (await response.json()) as RegionsResponse;
}

export async function calculateOnRoad(request: OnRoadRequest): Promise<CalculationOutcome> {
  const response = await fetch(`${apiBase()}/api/v1/calculators/on-road`, {
    method: "POST",
    cache: "no-store",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(request),
  });
  const payload = (await response.json()) as OnRoadResponse | { code?: string; message?: string };
  if (!response.ok) {
    const error = payload as { code?: string; message?: string };
    return { data: null, error: { code: error.code ?? "CALCULATION_FAILED", message: error.message ?? "Không thể tính giá ra biển." } };
  }
  return { data: payload as OnRoadResponse, error: null };
}
