import "server-only";

import { cookies } from "next/headers";

export const ADMIN_COOKIE = "vcp_admin_session";

export type AdminSession = { userId: string; email: string; displayName: string; role: string; expiresAt: string };
export type AdminCoverage = {
  brands: {
    brandId: string; brandName: string; included: boolean; discovered: number; mapped: number; published: number; blocked: number;
    stale: number; completeness: number; freshness: number; missingCoreCount: number; modelCandidates: number; trimCandidates: number;
    trimInventoryGaps: number; reviewed: boolean; reviewedAt: string | null;
  }[];
  brandScopeCount: number; activeModelCount: number; activeTrimCount: number; coreCompleteness: number; freshness: number;
  unresolvedDuplicates: number; fullMarketGatePassed: boolean; gateFailures: string[]; scopeVersion: string | null; manifestHash: string | null;
  reviewedBrandCount: number; excludedBrandCount: number; discoveredCandidateCount: number; resolvedCandidateCount: number;
  documentedBlockedCount: number; trimInventoryGapCount: number;
  candidateGaps: { candidateId: string; brandName: string; candidateKind: string; candidateName: string; code: string; reason: string; lastSeenAt: string }[];
  freshnessDomains: { domain: string; sourceCount: number; staleCount: number; freshness: number; passed: boolean }[];
  calculatedAt: string;
};
export type AdminQuality = {
  issues: { code: string; severity: string; entityType: string; entityId: string; fieldPath: string; message: string }[];
  impossibleValues: number; duplicates: number; staleSources: number; missingCoreFields: number; sourceConflicts: number; dealerOfferIssues: number; checkedAt: string;
};
export type AdminReviewItem = {
  id: string; entityType: string; entityId: string; fieldPath: string; oldValue: string | null; newValue: string | null;
  riskLevel: string; status: string; detectedAt: string; anomalyCode: string | null; detectionContext: string | null;
  source: {
    sourceFactId?: string; snapshotId?: string; name?: string; url?: string; authority?: string; fetchedAt?: string;
    contentHash?: string; objectKey?: string; parserVersion?: string; rawValue?: string; normalizedValue?: string;
    extractionContext?: string; factStatus?: string; confidence?: string;
  } | null; fieldLocked: boolean;
};
export type AdminPublication = {
  id: string; dataChangeId: string; entityType: string; entityId: string; fieldPath: string;
  beforeValue: string | null; afterValue: string | null; beforeSourceFactId: string | null; sourceFactId: string | null;
  status: string; publishedAt: string; publishedBy: string; rolledBackAt: string | null; rolledBackBy: string | null; rollbackReason: string | null;
};
export type AdminSource = {
  id: string; name: string; url: string; domain: string; authorityLevel: string; contentType: string; active: boolean; priority: number;
  refreshIntervalHours: number; lastFetchedAt: string | null; stale: boolean; snapshotCount: number; robotsNote: string | null; termsNote: string | null;
};
export type AdminTrim = {
  trimId: string; brandName: string; modelName: string; generationCode: string; modelYear: number; trimName: string; slug: string;
  marketStatus: string; bodyType: string; segment: string; updatedAt: string;
};
export type AdminImport = {
  id: string; fileName: string; format: string; status: string; contentHash: string; recordCount: number;
  issues: { row: number | null; field: string; code: string; severity: string; message: string }[]; submittedAt: string; stagedAt: string | null;
};
export type AdminFieldLock = { id: string; entityType: string; entityId: string; fieldPath: string; reason: string; actor: string; expiresAt: string | null; active: boolean };
export type AdminAudit = { id: string; actor: string; action: string; entityType: string; entityId: string; beforeJson: string | null; afterJson: string | null; reason: string; occurredAt: string; correlationId: string | null };
export type AdminMonitoring = {
  runsLast24Hours: number; succeededLast24Hours: number; failedLast24Hours: number; partialLast24Hours: number;
  contentChangesLast24Hours: number; openAlerts: number; highCriticalAlerts: number; generatedAt: string;
  monitorKinds: { monitorKind: string; runsLast24Hours: number; succeededLast24Hours: number; successRate: number; lastStartedAt: string | null; lastSucceededAt: string | null }[];
  recentRuns: { id: string; jobType: string; monitorKind: string; sourceKey: string | null; status: string; requestedAt: string; startedAt: string; completedAt: string | null; httpStatus: number | null; parseStatus: string | null; contentChanged: boolean | null; errorStage: string | null; errorCode: string | null; durationMilliseconds: number | null }[];
  alerts: { id: string; alertType: string; severity: string; status: string; sourceKey: string | null; jobRunId: string | null; message: string; occurrenceCount: number; firstTriggeredAt: string; lastTriggeredAt: string; acknowledgedAt: string | null; acknowledgedBy: string | null; resolvedAt: string | null }[];
};

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function adminFetch<T>(path: string): Promise<T | null> {
  const token = (await cookies()).get(ADMIN_COOKIE)?.value;
  if (!token) return null;
  const response = await fetch(`${apiBase()}/api/v1/admin/${path}`, {
    cache: "no-store",
    signal: AbortSignal.timeout(30_000),
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
  });
  if (response.status === 401 || response.status === 403) return null;
  if (!response.ok) throw new Error(`Admin API ${path} returned ${response.status}`);
  return response.json() as Promise<T>;
}
