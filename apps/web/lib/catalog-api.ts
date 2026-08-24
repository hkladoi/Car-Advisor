export type MoneyValue = { amount: number; currency: string; type?: string | null };
export type MoneyRange = { minimum: number; maximum: number; currency: string };

export type CatalogCar = {
  trimId: string;
  brandName: string;
  brandSlug: string;
  modelName: string;
  modelSlug: string;
  generationCode: string;
  modelYear: number;
  trimName: string;
  trimSlug: string;
  marketStatus: string;
  bodyType: string;
  segment: string;
  powertrainType: string;
  msrp: MoneyValue | null;
  currentPrice: MoneyValue | null;
  onRoadRange: MoneyRange | null;
  specifications: {
    seats: number | null;
    lengthMm: number | null;
    widthMm: number | null;
    heightMm: number | null;
    wheelbaseMm: number | null;
    officialRangeKm: number | null;
    usableBatteryKwh: number | null;
    fuelLitresPer100Km: number | null;
    electricKwhPer100Km: number | null;
  };
  featureCodes: string[];
  colorCodes: string[];
  primaryImageUrl: string | null;
  dataUpdatedAt: string;
};

export type FacetValue = { value: string; count: number };
export type CatalogFacets = {
  brands: FacetValue[];
  models: FacetValue[];
  bodyTypes: FacetValue[];
  segments: FacetValue[];
  powertrains: FacetValue[];
  seats: FacetValue[];
  features: FacetValue[];
  colors: FacetValue[];
};

export type CarsResponse = {
  data: CatalogCar[];
  facets: CatalogFacets;
  pagination: { page: number; pageSize: number; totalItems: number; totalPages: number };
  featureFilterSemantics: string;
  generatedAt: string;
};

export type BrandsResponse = {
  data: { id: string; name: string; slug: string; currentTrimCount: number }[];
  generatedAt: string;
};

export type SourceBadge = {
  sourceId: string;
  name: string;
  url: string;
  authority: string;
  contentType: string;
  fetchedAt: string;
  contentHash: string;
  factStatus: string;
  confidence: string;
};

export type RealWorldConsumptionReference = {
  id: string;
  vehicleRegistrationYear: number;
  manufacturer: string;
  fuelType: string;
  sampleSize: number;
  realWorldFuelWeightedLitresPer100Km: number | null;
  officialWltpFuelWeightedLitresPer100Km: number | null;
  fuelWeightedAbsoluteGapLitresPer100Km: number | null;
  fuelWeightedPercentageGap: number | null;
  realWorldCo2WeightedGramsPerKm: number | null;
  officialWltpCo2WeightedGramsPerKm: number | null;
  geography: string;
  aggregationScope: string;
  isTrimSpecific: boolean;
  methodologyUrl: string;
  attribution: string;
  source: SourceBadge;
};

export type CarDetailResponse = {
  car: CatalogCar;
  trims: { trimId: string; name: string; slug: string; modelYear: number; currentPrice: MoneyValue | null; selected: boolean }[];
  prices: { id: string; type: string; status: string; amount: number | null; currency: string; regionScope: string; effectiveFrom: string; effectiveTo: string | null; source: SourceBadge | null }[];
  gallery: { id: string; type: string; url: string; rightsStatus: string; rightsNote: string | null }[];
  specifications: { code: string; label: string; group: string; status: string; numericValue: number | null; textValue: string | null; enumValue: string | null; unit: string | null; source: SourceBadge | null }[];
  features: { code: string; label: string; group: string; status: string; booleanValue: boolean | null; numericValue: number | null; textValue: string | null; enumValue: string | null; source: SourceBadge | null }[];
  colors: { code: string; name: string; hexHint: string | null; type: string; availability: string; extraPrice: number | null; currency: string; source: SourceBadge | null }[];
  warranty: { vehicleMonths: number | null; vehicleKilometres: number | null; batteryMonths: number | null; batteryKilometres: number | null; conditions: string | null; source: SourceBadge | null } | null;
  dealerOffers: { id: string; dealerName: string; branchName: string; provinceCode: string; headline: string; status: string; conditionsJson: string; effectiveFrom: string; effectiveTo: string | null; benefits: { type: string; cashValue: number | null; statedValue: number | null; currency: string; isCashEquivalent: boolean; exclusivityGroup: string | null; note: string | null }[]; source: SourceBadge | null }[];
  realWorldConsumption: RealWorldConsumptionReference[];
  primarySource: SourceBadge | null;
  generatedAt: string;
};

export type CatalogSearchParams = Record<string, string | string[] | undefined>;

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase()}${path}`, {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    throw new Error(`Catalog API ${path} returned ${response.status}`);
  }
  return (await response.json()) as T;
}

export function catalogQuery(searchParams: CatalogSearchParams): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(searchParams)) {
    if (Array.isArray(value)) value.forEach((item) => query.append(key, item));
    else if (value) query.set(key, value);
  }
  return query.toString();
}

export async function getCars(searchParams: CatalogSearchParams): Promise<CarsResponse> {
  const query = catalogQuery(searchParams);
  return apiGet<CarsResponse>(`/api/v1/cars${query ? `?${query}` : ""}`);
}

export async function getBrands(): Promise<BrandsResponse> {
  return apiGet<BrandsResponse>("/api/v1/brands");
}

export async function getCar(trimId: string): Promise<CarDetailResponse | null> {
  const response = await fetch(`${apiBase()}/api/v1/cars/${encodeURIComponent(trimId)}`, {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Catalog detail API returned ${response.status}`);
  return (await response.json()) as CarDetailResponse;
}

export function formatMoney(value: MoneyValue | null): string {
  if (!value) return "Chưa có giá công khai";
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency: value.currency, maximumFractionDigits: 0 }).format(value.amount);
}

export function formatNumber(value: number | null, unit?: string | null): string {
  if (value === null) return "Chưa có dữ liệu";
  const number = new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 2 }).format(value);
  return unit ? `${number} ${unit}` : number;
}

export function formatDate(value: string): string {
  return new Intl.DateTimeFormat("vi-VN", { dateStyle: "medium", timeZone: "Asia/Ho_Chi_Minh" }).format(new Date(value));
}
