export type CoverageBrand = {
  brandId: string;
  brandName: string;
  included: boolean;
  discovered: number;
  mapped: number;
  published: number;
  blocked: number;
  stale: number;
  completeness: number;
  freshness: number;
  missingCoreCount: number;
  modelCandidates: number;
  trimCandidates: number;
  trimInventoryGaps: number;
  reviewed: boolean;
  reviewedAt: string | null;
};

export type CoverageResponse = {
  brands: CoverageBrand[];
  brandScopeCount: number;
  activeModelCount: number;
  activeTrimCount: number;
  coreCompleteness: number;
  freshness: number;
  unresolvedDuplicates: number;
  fullMarketGatePassed: boolean;
  gateFailures: string[];
  scopeVersion: string | null;
  manifestHash: string | null;
  reviewedBrandCount: number;
  excludedBrandCount: number;
  discoveredCandidateCount: number;
  resolvedCandidateCount: number;
  documentedBlockedCount: number;
  trimInventoryGapCount: number;
  candidateGaps: {
    candidateId: string;
    brandName: string;
    candidateKind: string;
    candidateName: string;
    code: string;
    reason: string;
    lastSeenAt: string;
  }[];
  freshnessDomains: {
    domain: string;
    sourceCount: number;
    staleCount: number;
    freshness: number;
    passed: boolean;
  }[];
  calculatedAt: string;
};

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function getCoverage(): Promise<CoverageResponse> {
  const response = await fetch(`${apiBase()}/api/v1/coverage`, {
    cache: "no-store",
    signal: AbortSignal.timeout(30_000),
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`Coverage API returned ${response.status}`);
  return response.json() as Promise<CoverageResponse>;
}
