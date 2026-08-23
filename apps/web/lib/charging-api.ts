export type ChargingStation = {
  id: string;
  openChargeMapId: number;
  name: string;
  addressLine1: string | null;
  addressLine2: string | null;
  town: string | null;
  stateOrProvince: string | null;
  postcode: string | null;
  latitude: number;
  longitude: number;
  operatorName: string | null;
  usageType: string | null;
  operationalStatus: string | null;
  isOperational: boolean | null;
  numberOfPoints: number | null;
  coverage: string;
  confidence: string;
  confidenceBasis: string;
  externalUpdatedAt: string | null;
  lastSeenAt: string;
  connectors: {
    connectorType: string | null;
    chargingLevel: string | null;
    currentType: string | null;
    operationalStatus: string | null;
    powerKw: number | null;
    quantity: number | null;
  }[];
  tariff: {
    providerId: string;
    providerName: string;
    providerOfficialUrl: string;
    amountPerKwh: number | null;
    amountPerSession: number | null;
    overstayAmountPerMinute: number | null;
    currency: string;
    taxIncluded: boolean;
    effectiveFrom: string;
    effectiveTo: string | null;
    sourceUrl: string;
  } | null;
  tariffAuthority: string;
  source: {
    name: string;
    url: string;
    fetchedAt: string;
    contentHash: string;
    attribution: string;
    licenseUrl: string;
  };
};

export type ChargingResponse = {
  data: ChargingStation[];
  count: number;
  dataset: {
    provider: string;
    coverage: string;
    geographicCompleteness: string;
    attribution: string;
    licenseUrl: string;
    lastSyncedAt: string | null;
    isStale: boolean;
    tariffPolicy: string;
  };
  generatedAt: string;
};

export type GeocodeOutcome =
  | { data: { results: { formattedAddress: string; latitude: number; longitude: number; placeId: string | null }[]; provider: string; cached: boolean }; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function getChargingStations(params: Record<string, string | number | undefined> = {}): Promise<ChargingResponse> {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined) query.set(key, String(value));
  }
  const response = await fetch(`${apiBase()}/api/v1/charging/stations?${query}`, {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`Charging API returned ${response.status}`);
  return (await response.json()) as ChargingResponse;
}

export async function geocodeAddress(address: string): Promise<GeocodeOutcome> {
  const response = await fetch(`${apiBase()}/api/v1/maps/geocode?address=${encodeURIComponent(address)}`, {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  const payload = (await response.json()) as {
    results?: { formattedAddress: string; latitude: number; longitude: number; placeId: string | null }[];
    provider?: string;
    cached?: boolean;
    code?: string;
    message?: string;
  };
  if (!response.ok) {
    return {
      data: null,
      error: {
        code: payload.code ?? "GEOCODE_UNAVAILABLE",
        message: payload.message ?? "Không thể định vị địa chỉ lúc này.",
      },
    };
  }
  return {
    data: {
      results: payload.results ?? [],
      provider: payload.provider ?? "Goong",
      cached: payload.cached ?? false,
    },
    error: null,
  };
}
