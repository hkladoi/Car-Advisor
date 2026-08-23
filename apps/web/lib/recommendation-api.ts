export type RecommendationWeights = {
  priceValue: number;
  runningCost: number;
  space: number;
  safetyAdas: number;
  comfort: number;
  performance: number;
  technology: number;
};

export type RecommendationRequest = {
  hardFilters: {
    maximumPrice: number | null;
    bodyTypes: string[];
    segments: string[];
    powertrains: string[];
    minimumSeats: number | null;
    requiredFeatureCodes: string[];
  };
  weights: RecommendationWeights;
  regionCode: string;
  asOfDate: string | null;
  maximumResults: number;
};

export type RecommendationSource = {
  sourceFactId: string;
  sourceId: string;
  name: string;
  url: string;
  authority: string;
  contentType: string;
  fetchedAt: string;
  contentHash: string;
  factStatus: string;
  confidence: string;
  stale: boolean;
};

export type RecommendationComponent = {
  code: string;
  label: string;
  weight: number;
  rawMetrics: { code: string; label: string; value: number; unit: string; direction: string }[];
  score: number | null;
  includedInOverall: boolean;
  trusted: boolean;
  sources: RecommendationSource[];
  explanation: string;
};

export type RecommendationCandidate = {
  vehicle: {
    trimId: string;
    brandName: string;
    modelName: string;
    trimName: string;
    modelYear: number;
    bodyType: string;
    segment: string;
    powertrain: string;
    currentPrice: number | null;
    currency: string;
  };
  rank: number | null;
  completeness: number;
  completenessPassed: boolean;
  trustPassed: boolean;
  overallScore: number | null;
  pricePerformanceScore: number | null;
  components: RecommendationComponent[];
  reasons: string[];
};

export type RecommendationResponse = {
  methodology: {
    version: string;
    evaluationOrder: string[];
    completenessThreshold: number;
    normalizedWeights: Record<string, number>;
    overallFormula: string;
    pricePerformanceFormula: string;
    assumptions: string[];
  };
  considered: number;
  hardFilterMatched: number;
  ranked: RecommendationCandidate[];
  dataWithheld: RecommendationCandidate[];
  hardFilterExcluded: RecommendationCandidate[];
  evaluatedAt: string;
};

export type RecommendationOutcome =
  | { data: RecommendationResponse; error: null }
  | { data: null; error: { code: string; message: string } };

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export function defaultRecommendationRequest(): RecommendationRequest {
  return {
    hardFilters: {
      maximumPrice: 1_500_000_000,
      bodyTypes: [],
      segments: [],
      powertrains: [],
      minimumSeats: 5,
      requiredFeatureCodes: [],
    },
    weights: {
      priceValue: 20,
      runningCost: 15,
      space: 15,
      safetyAdas: 20,
      comfort: 10,
      performance: 10,
      technology: 10,
    },
    regionCode: "VN-01",
    asOfDate: null,
    maximumResults: 10,
  };
}

export async function evaluateRecommendation(
  request: RecommendationRequest,
  browser = false,
): Promise<RecommendationOutcome> {
  try {
    const response = await fetch(browser ? "/api/recommendations" : `${apiBase()}/api/v1/recommendations`, {
      method: "POST",
      cache: "no-store",
      signal: AbortSignal.timeout(30_000),
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(request),
    });
    const payload = (await response.json()) as RecommendationResponse | { code?: string; message?: string };
    if (!response.ok) {
      return { data: null, error: { code: "code" in payload && payload.code ? payload.code : "RECOMMENDATION_FAILED", message: "message" in payload && payload.message ? payload.message : "Không thể đánh giá danh sách xe." } };
    }
    return { data: payload as RecommendationResponse, error: null };
  } catch {
    return { data: null, error: { code: "RECOMMENDATION_UNAVAILABLE", message: "Dịch vụ gợi ý tạm thời chưa phản hồi. Hãy thử lại sau." } };
  }
}

export function formatRecommendationMoney(value: number | null, currency = "VND") {
  if (value === null) return "Chưa có giá xác minh";
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}
