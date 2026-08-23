import Link from "next/link";
import { Activity, AlertTriangle, CheckCircle2, Database, FileClock, ShieldAlert } from "lucide-react";

import { adminFetch, type AdminAudit, type AdminCoverage, type AdminQuality, type AdminReviewItem } from "@/lib/admin-api";

const percent = (value: number) => new Intl.NumberFormat("vi-VN", { style: "percent", maximumFractionDigits: 1 }).format(value);

export default async function AdminDashboardPage() {
  const [coverage, quality, queue, audit] = await Promise.all([
    adminFetch<AdminCoverage>("coverage"),
    adminFetch<AdminQuality>("quality"),
    adminFetch<AdminReviewItem[]>("review-queue"),
    adminFetch<AdminAudit[]>("audit?take=8"),
  ]);
  if (!coverage || !quality || !queue || !audit) return null;
  return (
    <div className="admin-page">
      <header className="admin-page-head">
        <div><p className="machine-label">V1.10 · ADMIN / QA / AUDIT</p><h1>Trust is an operating system.</h1></div>
        <div className={`admin-gate-stamp ${coverage.fullMarketGatePassed ? "is-pass" : "is-blocked"}`}>
          {coverage.fullMarketGatePassed ? <CheckCircle2 size={20} /> : <ShieldAlert size={20} />}
          <span>FULL-MARKET GATE</span><strong>{coverage.fullMarketGatePassed ? "PASS" : "BLOCKED"}</strong>
        </div>
      </header>
      <p className="admin-lede">Không có badge “toàn thị trường” khi coverage chưa đạt. Bảng này lấy trực tiếp từ published data, source snapshots và review state hiện tại.</p>

      <section className="admin-metric-grid" aria-label="Chỉ số vận hành">
        <article><Database size={18} /><span>ACTIVE TRIMS</span><strong>{coverage.activeTrimCount}</strong><small>{coverage.activeModelCount} model · {coverage.brandScopeCount} brand scope</small></article>
        <article><Activity size={18} /><span>CORE COMPLETENESS</span><strong>{percent(coverage.coreCompleteness)}</strong><small>explicit UNKNOWN được tính là fact minh bạch</small></article>
        <article><FileClock size={18} /><span>REVIEW QUEUE</span><strong>{queue.length}</strong><small>{queue.filter(item => item.riskLevel === "Critical" || item.riskLevel === "High").length} high/critical</small></article>
        <article><AlertTriangle size={18} /><span>QUALITY ISSUES</span><strong>{quality.issues.length}</strong><small>{quality.missingCoreFields} missing core · {quality.staleSources} stale</small></article>
      </section>

      <div className="admin-dashboard-grid">
        <section className="admin-panel">
          <div className="admin-panel-head"><div><p className="machine-label">GATE BLOCKERS</p><h2>Không được bỏ qua.</h2></div><Link href="/admin/coverage">Mở coverage →</Link></div>
          {coverage.gateFailures.length ? <ul className="admin-blocker-list">{coverage.gateFailures.map(value => <li key={value}><AlertTriangle size={16} /><code>{value}</code></li>)}</ul> : <p className="admin-empty">Không có blocker.</p>}
        </section>
        <section className="admin-panel">
          <div className="admin-panel-head"><div><p className="machine-label">DATA QA</p><h2>Signal, not vanity.</h2></div><Link href="/admin/coverage#quality">Xem issues →</Link></div>
          <dl className="admin-signal-list">
            <div><dt>Impossible values</dt><dd>{quality.impossibleValues}</dd></div>
            <div><dt>Duplicate identities</dt><dd>{quality.duplicates}</dd></div>
            <div><dt>Source conflicts</dt><dd>{quality.sourceConflicts}</dd></div>
            <div><dt>Dealer-offer QA</dt><dd>{quality.dealerOfferIssues}</dd></div>
          </dl>
        </section>
      </div>

      <section className="admin-panel admin-audit-panel">
        <div className="admin-panel-head"><div><p className="machine-label">IMMUTABLE INTENT</p><h2>Audit gần nhất.</h2></div><span>{audit.length} events</span></div>
        <div className="admin-table-wrap"><table className="admin-table"><thead><tr><th>Thời điểm</th><th>Actor</th><th>Action</th><th>Entity</th><th>Lý do</th></tr></thead><tbody>{audit.map(event => <tr key={event.id}><td>{new Date(event.occurredAt).toLocaleString("vi-VN")}</td><td>{event.actor}</td><td><code>{event.action}</code></td><td>{event.entityType}<small>{event.entityId}</small></td><td>{event.reason}</td></tr>)}</tbody></table></div>
      </section>
    </div>
  );
}
