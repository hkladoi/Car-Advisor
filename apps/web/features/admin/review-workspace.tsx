"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Check, ExternalLink, LockKeyhole, X } from "lucide-react";

import type { AdminReviewItem } from "@/lib/admin-api";

export function ReviewWorkspace({ items }: { items: AdminReviewItem[] }) {
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
    setMessage(action === "approve" ? "Đã approve và ghi audit." : "Đã reject và ghi audit.");
    router.refresh();
  }

  if (!items.length) return <div className="admin-empty admin-empty--large"><Check size={22} /><strong>Review queue trống.</strong><span>Không có candidate change đang chờ quyết định.</span></div>;
  return (
    <div className="admin-review-list">
      {message ? <p className="admin-inline-message" role="status">{message}</p> : null}
      {items.map(item => (
        <article className="admin-review-card" key={item.id}>
          <header><div><span className={`admin-risk risk-${item.riskLevel.toLowerCase()}`}>{item.riskLevel}</span><code>{item.entityType}.{item.fieldPath}</code>{item.fieldLocked ? <span className="admin-lock-badge"><LockKeyhole size={13} /> LOCKED</span> : null}</div><time>{new Date(item.detectedAt).toLocaleString("vi-VN")}</time></header>
          <div className="admin-diff-grid"><div><span>BEFORE</span><pre>{item.oldValue ?? "∅"}</pre></div><div><span>CANDIDATE</span><pre>{item.newValue ?? "∅"}</pre></div></div>
          {item.source ? <div className="admin-source-strip"><div><strong>{item.source.name ?? "Source"}</strong><span>{item.source.authority} · {item.source.fetchedAt ? new Date(item.source.fetchedAt).toLocaleString("vi-VN") : "unknown fetch"}</span></div>{item.source.url ? <a href={item.source.url} target="_blank" rel="noreferrer">Mở source <ExternalLink size={14} /></a> : null}<code>{item.source.contentHash?.slice(0, 16)}…</code></div> : <div className="admin-source-strip is-manual"><strong>Manual candidate</strong><span>Không có SourceFact; approval chỉ chuyển state cho bước publish có kiểm soát.</span></div>}
          <form className="admin-review-actions" onSubmit={event => event.preventDefault()}>
            <label>Lý do quyết định<input name="reason" required minLength={10} placeholder="Nguồn, rule hoặc lý do nghiệp vụ cụ thể" /></label>
            <label>Edit-and-publish · tùy chọn<input name="editedValue" placeholder="Chỉ dùng với field override được whitelist" /></label>
            <div><button type="button" className="button-secondary" disabled={pending === item.id} onClick={event => decide(item, "reject", event.currentTarget.form!)}><X size={16} /> Reject</button><button type="button" className="button-primary" disabled={pending === item.id} onClick={event => decide(item, "approve", event.currentTarget.form!)}><Check size={16} /> {pending === item.id ? "Đang ghi…" : "Approve"}</button></div>
          </form>
        </article>
      ))}
    </div>
  );
}
