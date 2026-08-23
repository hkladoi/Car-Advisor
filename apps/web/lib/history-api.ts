export type HistorySourceReference = {
  sourceFactId: string;
  sourceId: string;
  name: string;
  url: string;
  authority: string;
  fetchedAt: string;
  contentHash: string;
  confidence: string;
};

export type PriceTimelineEvent = {
  id: string;
  series: string;
  valueKind: string;
  amount: number | null;
  currency: string;
  status: string;
  scope: string;
  label: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  isCurrent: boolean;
  isStale: boolean;
  provenance: string;
  source: HistorySourceReference | null;
};

export type CashPriceRangeInsight = {
  available: boolean;
  basis: string;
  policy: string;
  reasonCode: string;
  observationCount: number;
  distinctObservationDates: number;
  spanDays: number;
  currentAmount: number | null;
  twelveMonthMinimum: number | null;
  twelveMonthMaximum: number | null;
  currency: string;
  position: string | null;
};

export type VehiclePriceHistoryResponse = {
  vehicle: { trimId: string; brandName: string; modelName: string; trimName: string; modelYear: number };
  timeline: PriceTimelineEvent[];
  currentVsTwelveMonthRange: CashPriceRangeInsight;
  window: { from: string; to: string; months: number; truncated: boolean };
  generatedAt: string;
};

export type DealerOfferHistoryItem = {
  id: string;
  dealerName: string;
  branchName: string;
  provinceCode: string;
  headline: string;
  status: string;
  conditionsJson: string;
  combinabilityGroup: string | null;
  maximumEligibleCashReduction: number | null;
  currency: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  lastVerifiedAt: string;
  isCurrent: boolean;
  isStale: boolean;
  benefits: {
    type: string;
    cashValue: number | null;
    statedValue: number | null;
    currency: string;
    isCashEquivalent: boolean;
    exclusivityGroup: string | null;
    note: string | null;
  }[];
  provenance: string;
  source: HistorySourceReference | null;
};

export type DealerOfferHistoryResponse = {
  vehicle: VehiclePriceHistoryResponse["vehicle"];
  current: DealerOfferHistoryItem[];
  history: DealerOfferHistoryItem[];
  cashSemantics: string;
  window: VehiclePriceHistoryResponse["window"];
  generatedAt: string;
};

export type EnergyPriceSeries = {
  seriesKey: string;
  energyType: string;
  provider: string;
  regionCode: string;
  unit: string;
  currency: string;
  tierFromInclusive: number;
  tierToInclusive: number | null;
  observations: {
    id: string;
    amount: number;
    taxRate: number;
    taxIncluded: boolean;
    effectiveFrom: string;
    effectiveTo: string | null;
    isCurrent: boolean;
    provenance: string;
    source: HistorySourceReference | null;
  }[];
};

export type EnergyPriceHistoryResponse = {
  series: EnergyPriceSeries[];
  window: VehiclePriceHistoryResponse["window"];
  semantics: string;
  generatedAt: string;
};

export type PriceChartRow = { at: number; label: string } & Record<string, number | string>;

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase()}${path}`, { cache: "no-store", headers: { Accept: "application/json" } });
  if (!response.ok) throw new Error(`History API ${path} returned ${response.status}`);
  return (await response.json()) as T;
}

export async function getVehiclePriceHistory(trimId: string, regionScope = "VN", months = 12): Promise<VehiclePriceHistoryResponse> {
  const query = new URLSearchParams({ regionScope, months: String(months) });
  return apiGet(`/api/v1/cars/${encodeURIComponent(trimId)}/prices?${query}`);
}

export async function getDealerOfferHistory(trimId: string, provinceCode?: string, months = 12): Promise<DealerOfferHistoryResponse> {
  const query = new URLSearchParams({ months: String(months) });
  if (provinceCode) query.set("provinceCode", provinceCode);
  return apiGet(`/api/v1/cars/${encodeURIComponent(trimId)}/dealer-offers?${query}`);
}

export async function getEnergyPriceHistory(filters: { energyType?: string; provider?: string; regionCode?: string; months?: number } = {}): Promise<EnergyPriceHistoryResponse> {
  const query = new URLSearchParams({ regionCode: filters.regionCode ?? "VN", months: String(filters.months ?? 12) });
  if (filters.energyType) query.set("energyType", filters.energyType);
  if (filters.provider) query.set("provider", filters.provider);
  return apiGet(`/api/v1/energy/prices/history?${query}`);
}

export function buildPriceChartRows(timeline: PriceTimelineEvent[]): PriceChartRow[] {
  const rows = new Map<number, PriceChartRow>();
  for (const event of timeline) {
    if (event.valueKind !== "CashPrice" || event.amount === null || event.status !== "Official") continue;
    const at = Date.parse(event.effectiveFrom);
    const row = rows.get(at) ?? { at, label: event.effectiveFrom };
    row[event.series] = event.amount;
    rows.set(at, row);
  }
  return [...rows.values()].sort((left, right) => left.at - right.at);
}

export function formatEnergyRateUnit(series: Pick<EnergyPriceSeries, "currency" | "unit">): string {
  return series.unit.toLocaleUpperCase("en-US").startsWith(`${series.currency.toLocaleUpperCase("en-US")}/`)
    ? series.unit
    : `${series.currency}/${series.unit}`;
}
