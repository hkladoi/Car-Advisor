"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Activity, AlertTriangle, CheckCircle2, Clock3, RefreshCw } from "lucide-react";

import type { AdminMonitoring } from "@/lib/admin-api";

const percent = (value: number) => new Intl.NumberFormat("vi-VN", { style: "percent", maximumFractionDigits: 1 }).format(value);

export function MonitoringWorkspace({ monitoring }: { monitoring: AdminMonitoring }) {
  const router = useRouter();
  const [pending, setPending] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function acknowledge(id: string, form: HTMLFormElement) {
    const reason = String(new FormData(form).get("reason") ?? "");
    if (reason.trim().length < 10) return setMessage("Lý do acknowledge cần ít nhất 10 ký tự.");
    setPending(id);
    const response = await fetch(`/api/admin/monitoring/alerts/${id}/acknowledge`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason }),
    });
    setPending(null);
    if (!response.ok) {
      const payload = await response.json() as { message?: string };
      return setMessage(payload.message ?? "Không thể acknowledge alert.");
    }
    setMessage("Đã acknowledge; worker chỉ auto-resolve khi freshness/parser phục hồi.");
    router.refresh();
  }

  return (
    <div className="admin-monitoring-stack">
      {message ? <p className="admin-inline-message" role="status">{message}</p> : null}
      <section className="admin-metric-grid" aria-label="Monitoring 24 giờ">
        <article><Activity size={18} /><span>RUNS / 24H</span><strong>{monitoring.runsLast24Hours}</strong><small>{monitoring.succeededLast24Hours} succeeded</small></article>
        <article><AlertTriangle size={18} /><span>FAILED / PARTIAL</span><strong>{monitoring.failedLast24Hours + monitoring.partialLast24Hours}</strong><small>{monitoring.failedLast24Hours} failed · {monitoring.partialLast24Hours} partial</small></article>
        <article><RefreshCw size={18} /><span>CONTENT CHANGES</span><strong>{monitoring.contentChangesLast24Hours}</strong><small>immutable snapshot/parser deltas</small></article>
        <article><Clock3 size={18} /><span>OPEN ALERTS</span><strong>{monitoring.openAlerts}</strong><small>{monitoring.highCriticalAlerts} high/critical</small></article>
      </section>

      <section className="admin-panel"><div className="admin-panel-head"><div><p className="machine-label">CADENCE / SUCCESS</p><h2>Monitor kinds.</h2></div><span>24-hour window</span></div><div className="admin-table-wrap"><table className="admin-table"><thead><tr><th>Monitor</th><th>Runs</th><th>Success</th><th>Last run</th><th>Last success</th></tr></thead><tbody>{monitoring.monitorKinds.map(kind => <tr key={kind.monitorKind}><td><code>{kind.monitorKind}</code></td><td>{kind.runsLast24Hours}</td><td>{percent(kind.successRate)}</td><td>{kind.lastStartedAt ? new Date(kind.lastStartedAt).toLocaleString("vi-VN") : "—"}</td><td>{kind.lastSucceededAt ? new Date(kind.lastSucceededAt).toLocaleString("vi-VN") : "—"}</td></tr>)}</tbody></table></div></section>

      <section className="admin-panel"><div className="admin-panel-head"><div><p className="machine-label">ACTIONABLE ALERTS</p><h2>Stale & parser failure.</h2></div><span>{monitoring.openAlerts} active</span></div><div className="admin-monitoring-alerts">{!monitoring.alerts.length ? <p className="admin-empty"><CheckCircle2 size={18} /> Chưa có alert.</p> : monitoring.alerts.map(alert => <article key={alert.id} className={`severity-${alert.severity.toLowerCase()} is-${alert.status.toLowerCase()}`}><div><span>{alert.severity} · {alert.status}</span><code>{alert.alertType}</code><strong>{alert.sourceKey ?? "platform"}</strong><small>{alert.occurrenceCount} occurrences · {new Date(alert.lastTriggeredAt).toLocaleString("vi-VN")}</small></div><p>{alert.message}</p>{alert.status === "Open" ? <form onSubmit={event => event.preventDefault()}><input name="reason" minLength={10} required placeholder="Owner/action/runbook cụ thể" /><button className="button-secondary" type="button" disabled={pending === alert.id} onClick={event => acknowledge(alert.id, event.currentTarget.form!)}>{pending === alert.id ? "Đang ghi…" : "Acknowledge"}</button></form> : <small>{alert.acknowledgedBy ?? "auto-resolved"} · {alert.resolvedAt ? new Date(alert.resolvedAt).toLocaleString("vi-VN") : "awaiting recovery"}</small>}</article>)}</div></section>

      <section className="admin-panel"><div className="admin-panel-head"><div><p className="machine-label">RECENT RUN LEDGER</p><h2>Failure-safe execution.</h2></div><span>{monitoring.recentRuns.length} runs</span></div><div className="admin-table-wrap"><table className="admin-table admin-monitoring-runs"><thead><tr><th>Started</th><th>Monitor/source</th><th>Status</th><th>HTTP/parser</th><th>Change</th><th>Duration/error</th></tr></thead><tbody>{monitoring.recentRuns.map(run => <tr key={run.id}><td>{new Date(run.startedAt).toLocaleString("vi-VN")}</td><td><code>{run.monitorKind}</code><small>{run.sourceKey ?? "platform"}</small></td><td><span className={`admin-state state-${run.status.toLowerCase()}`}>{run.status}</span></td><td>{run.httpStatus ?? "—"} / {run.parseStatus ?? "—"}</td><td>{run.contentChanged == null ? "—" : run.contentChanged ? "changed" : "unchanged"}</td><td>{run.durationMilliseconds == null ? "—" : `${run.durationMilliseconds} ms`}<small>{run.errorStage ? `${run.errorStage}: ${run.errorCode}` : ""}</small></td></tr>)}</tbody></table></div></section>
    </div>
  );
}
