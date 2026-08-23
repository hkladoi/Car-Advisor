"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Check, ExternalLink, LockKeyhole, RotateCcw, X } from "lucide-react";

import type { AdminPublication, AdminReviewItem } from "@/lib/admin-api";

export function ReviewWorkspace({ items, publications }: { items: AdminReviewItem[]; publications: AdminPublication[] }) {
  const router = useRouter();
  const [pending, setPending] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function decide(item: AdminReviewItem, action: "approve" | "reject", form: HTMLFormElement) {
    const data = new FormData(form);
    const reason = String(data.get("reason") ?? "");
    const edited = String(data.get("editedValue") ?? "").trim();
    if (reason.trim().length < 10) {
      setMessage("Lý do cần ít nhất 10 ký tự.");
      return;
    }
    setPending(item.id);
    setMessage(null);
    const response = await fetch(`/api/admin/changes/${item.id}/${action}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason, editedValue: edited || null }),
    });
    const payload = response.status === 204 ? null : await response.json() as { message?: string };
    setPending(null);
    if (!response.ok) {
      setMessage(payload?.message ?? "Không thể ghi quyết định.");
      return;
    }
    setMessage(action === "approve" ? "Đã publish canonical value và ghi phiên bản rollback." : "Đã reject và ghi audit.");
    router.refresh();
  }

  async function rollback(publication: AdminPublication, form: HTMLFormElement) {
    const reason = String(new FormData(form).get("rollbackReason") ?? "");
    if (reason.trim().length < 10) {
      setMessage("Lý do rollback cần ít nhất 10 ký tự.");
      return;
    }
    setPending(publication.id);
    setMessage(null);
    const response = await fetch(`/api/admin/publications/${publication.id}/rollback`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason }),
    });
    const payload = response.status === 204 ? null : await response.json() as { message?: string };
    setPending(null);
    if (!response.ok) {
      setMessage(payload?.message ?? "Không thể rollback phiên bản.");
      return;
    }
    setMessage("Đã khôi phục canonical value trước đó và ghi audit.");
    router.refresh();
  }

  return (
    <div className="admin-review-workspace">
      {message ? <p className="admin-inline-message" role="status">{message}</p> : null}
      <div className="admin-review-list">
        {!items.length ? <div className="admin-empty admin-empty--large"><Check size={22} /><strong>Review queue trống.</strong><span>Không có candidate change đang chờ quyết định.</span></div> : null}
        {items.map(item => (
          <article className="admin-review-card" key={item.id}>
            <header><div><span className={`admin-risk risk-${item.riskLevel.toLowerCase()}`}>{item.riskLevel}</span><code>{item.entityType}.{item.fieldPath}</code>{item.fieldLocked ? <span className="admin-lock-badge"><LockKeyhole size={13} /> LOCKED</span> : null}{item.anomalyCode ? <span className="admin-anomaly">{item.anomalyCode}</span> : null}</div><time>{new Date(item.detectedAt).toLocaleString("vi-VN")}</time></header>
            <div className="admin-diff-grid">
              <div><span>BEFORE · PUBLISHED</span><pre>{item.oldValue ?? "∅"}</pre></div>
              <div><span>CANDIDATE · NORMALIZED</span><pre>{item.newValue ?? "∅"}</pre></div>
              <div><span>SNAPSHOT EVIDENCE</span><pre>{item.source ? [item.source.rawValue, item.source.extractionContext, item.source.parserVersion ? `parser: ${item.source.parserVersion}` : null, item.source.objectKey ? `object: ${item.source.objectKey}` : null].filter(Boolean).join("\n\n") : "No immutable snapshot evidence"}</pre></div>
            </div>
            {item.source ? <div className="admin-source-strip"><div><strong>{item.source.name ?? "Source"}</strong><span>{item.source.authority} · {item.source.confidence} · {item.source.fetchedAt ? new Date(item.source.fetchedAt).toLocaleString("vi-VN") : "unknown fetch"}</span></div>{item.source.url ? <a href={item.source.url} target="_blank" rel="noreferrer">Mở source <ExternalLink size={14} /></a> : null}<code>{item.source.contentHash?.slice(0, 16)}…</code></div> : <div className="admin-source-strip is-manual"><strong>Manual candidate</strong><span>Không có SourceFact; approval chỉ chuyển state, không ghi vào canonical field nếu thiếu typed mapping.</span></div>}
            {item.detectionContext ? <details className="admin-detection-context"><summary>Risk policy context</summary><pre>{item.detectionContext}</pre></details> : null}
            <form className="admin-review-actions" onSubmit={event => event.preventDefault()}>
              <label>Lý do quyết định<input name="reason" required minLength={10} placeholder="Nguồn, rule hoặc lý do nghiệp vụ cụ thể" /></label>
              <label>Edit-and-publish · tùy chọn<input name="editedValue" placeholder="Giá trị canonical đã hiệu chỉnh" /></label>
              <div><button type="button" className="button-secondary" disabled={pending === item.id} onClick={event => decide(item, "reject", event.currentTarget.form!)}><X size={16} /> Reject</button><button type="button" className="button-primary" disabled={pending === item.id || item.fieldLocked} onClick={event => decide(item, "approve", event.currentTarget.form!)}><Check size={16} /> {pending === item.id ? "Đang ghi…" : "Approve & publish"}</button></div>
            </form>
          </article>
        ))}
      </div>

      <section className="admin-publication-history" aria-labelledby="publication-history-title">
        <header><div><p className="machine-label">IMMUTABLE VERSION LOG</p><h2 id="publication-history-title">Publication & rollback history.</h2></div><span>{publications.length} versions</span></header>
        {!publications.length ? <div className="admin-empty"><span>Chưa có canonical publication nào.</span></div> : publications.map(publication => (
          <article key={publication.id} className="admin-publication-row">
            <div><span className={`admin-publication-status is-${publication.status.toLowerCase()}`}>{publication.status}</span><code>{publication.entityType}.{publication.fieldPath}</code><small>{new Date(publication.publishedAt).toLocaleString("vi-VN")} · {publication.publishedBy}</small></div>
            <div className="admin-publication-values"><code>{publication.beforeValue ?? "∅"}</code><span>→</span><code>{publication.afterValue ?? "∅"}</code></div>
            {publication.status === "Published" ? <form onSubmit={event => event.preventDefault()}><input name="rollbackReason" minLength={10} required placeholder="Lý do rollback cụ thể" /><button type="button" className="button-secondary" disabled={pending === publication.id} onClick={event => rollback(publication, event.currentTarget.form!)}><RotateCcw size={15} /> {pending === publication.id ? "Đang khôi phục…" : "Rollback"}</button></form> : <p className="admin-rollback-note">{publication.rollbackReason} · {publication.rolledBackBy}</p>}
          </article>
        ))}
      </section>
    </div>
  );
}
