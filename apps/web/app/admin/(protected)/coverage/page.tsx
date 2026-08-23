import Link from "next/link";
import { AlertOctagon, CheckCircle2, Clock3, Database, FileWarning, ScanSearch, ShieldCheck } from "lucide-react";

import { adminFetch, type AdminCoverage, type AdminQuality } from "@/lib/admin-api";

const percent = (value: number) => new Intl.NumberFormat("vi-VN", { style: "percent", maximumFractionDigits: 1 }).format(value);

export default async function AdminCoveragePage() {
  const [coverage, quality] = await Promise.all([adminFetch<AdminCoverage>("coverage"), adminFetch<AdminQuality>("quality")]);
  if (!coverage || !quality) return null;
  return (
    <div className="admin-page">
      <header className="admin-page-head"><div><p className="machine-label">COVERAGE · FRESHNESS · COMPLETENESS</p><h1>Measure the claim.</h1></div><div className={`admin-gate-stamp ${coverage.fullMarketGatePassed ? "is-pass" : "is-blocked"}`}>{coverage.fullMarketGatePassed ? <CheckCircle2 size={20} /> : <AlertOctagon size={20} />}<span>FULL-MARKET</span><strong>{coverage.fullMarketGatePassed ? "PASS" : "BLOCKED"}</strong></div></header>
      <p className="admin-lede">Targets: brand scope reviewed 100%, active trim represented, ≥95% core facts hoặc explicit UNKNOWN, current price state 100%, freshness SLA và zero unresolved duplicate. Scope <code>{coverage.scopeVersion ?? "unversioned"}</code> · <Link href="/coverage">báo cáo công khai →</Link></p>

      <section className="admin-metric-grid">
        <article><ShieldCheck size={18} /><span>BRANDS REVIEWED</span><strong>{coverage.reviewedBrandCount}/{coverage.brandScopeCount}</strong><small>{coverage.excludedBrandCount} explicit exclusions</small></article>
        <article><ScanSearch size={18} /><span>CANDIDATES RESOLVED</span><strong>{coverage.resolvedCandidateCount}/{coverage.discoveredCandidateCount}</strong><small>model + trim candidates</small></article>
        <article><Database size={18} /><span>PUBLISHED CATALOG</span><strong>{coverage.activeModelCount}/{coverage.activeTrimCount}</strong><small>active models / explicit trims</small></article>
        <article><FileWarning size={18} /><span>DOCUMENTED GAPS</span><strong>{coverage.documentedBlockedCount}</strong><small>{coverage.trimInventoryGapCount} trim inventory gaps</small></article>
      </section>

      <section className="admin-panel">
        <div className="admin-panel-head"><div><p className="machine-label">FRESHNESS DOMAINS</p><h2>Price · promotion · dealer · energy · legal.</h2></div><span>{percent(coverage.freshness)} overall</span></div>
        <div className="coverage-domain-grid">{coverage.freshnessDomains.map(item => <article key={item.domain} className={item.passed ? "is-pass" : "is-blocked"}><div>{item.passed ? <CheckCircle2 size={17} /> : <AlertOctagon size={17} />}<strong>{item.domain}</strong></div><span>{item.sourceCount} sources · {item.staleCount} stale</span><b>{percent(item.freshness)}</b></article>)}</div>
      </section>

      <section className="admin-panel">
        <div className="admin-panel-head"><div><p className="machine-label">BRAND MATRIX</p><h2>Discovered → mapped → published.</h2></div><span>{percent(coverage.coreCompleteness)} core</span></div>
        <div className="admin-table-wrap"><table className="admin-table admin-coverage-table"><thead><tr><th>Brand</th><th>Scope/review</th><th>Model cand.</th><th>Trim cand.</th><th>Published</th><th>Inventory gaps</th><th>Blocked/stale</th><th>Completeness</th><th>Freshness</th></tr></thead><tbody>{coverage.brands.map(brand => <tr key={brand.brandId} className={!brand.included ? "is-muted" : undefined}><td><strong>{brand.brandName}</strong></td><td>{brand.included ? "IN" : "OUT"}<small>{brand.reviewed ? `reviewed ${brand.reviewedAt ? new Date(brand.reviewedAt).toLocaleDateString("vi-VN") : ""}` : "NOT REVIEWED"}</small></td><td>{brand.modelCandidates}</td><td>{brand.trimCandidates}</td><td>{brand.published}<small>{brand.mapped} mapped / {brand.discovered} found</small></td><td>{brand.trimInventoryGaps}</td><td>{brand.blocked} / {brand.stale}</td><td>{percent(brand.completeness)}<small>{brand.missingCoreCount} missing</small></td><td>{percent(brand.freshness)}</td></tr>)}</tbody></table></div>
      </section>

      <section className="admin-panel">
        <div className="admin-panel-head"><div><p className="machine-label">CANDIDATE GAPS</p><h2>Không có silent drop.</h2></div><span>{coverage.candidateGaps.length} records</span></div>
        <div className="coverage-gap-list">{coverage.candidateGaps.length ? coverage.candidateGaps.map(gap => <article key={gap.candidateId}><div><strong>{gap.brandName} · {gap.candidateName}</strong><code>{gap.code}</code></div><p>{gap.reason}</p><small>{gap.candidateKind} · {new Date(gap.lastSeenAt).toLocaleString("vi-VN")}</small></article>) : <p className="admin-empty">Không có gap.</p>}</div>
      </section>

      <section className="admin-panel" id="quality">
        <div className="admin-panel-head"><div><p className="machine-label">QUALITY CHECKS · LIVE</p><h2>Findings that need ownership.</h2></div><span><Clock3 size={15} /> {new Date(quality.checkedAt).toLocaleTimeString("vi-VN")}</span></div>
        <div className="admin-quality-list">{quality.issues.length ? quality.issues.map((issue, index) => <article key={`${issue.code}-${issue.entityId}-${issue.fieldPath}-${index}`} className={`admin-quality-item severity-${issue.severity.toLowerCase()}`}><div><span>{issue.severity}</span><code>{issue.code}</code></div><strong>{issue.entityType} · {issue.fieldPath}</strong><p>{issue.message}</p><small>{issue.entityId}</small></article>) : <p className="admin-empty">Không có finding ở thời điểm kiểm tra.</p>}</div>
      </section>
    </div>
  );
}
