import { MonitoringWorkspace } from "@/features/admin/monitoring-workspace";
import { adminFetch, type AdminMonitoring } from "@/lib/admin-api";

export default async function AdminMonitoringPage() {
  const monitoring = await adminFetch<AdminMonitoring>("monitoring");
  if (!monitoring) return null;
  return (
    <div className="admin-page">
      <header className="admin-page-head"><div><p className="machine-label">V2.5 · RECURRING SOURCE OPERATIONS</p><h1>Freshness is measurable.</h1></div><div className={`admin-gate-stamp ${monitoring.highCriticalAlerts ? "is-blocked" : "is-pass"}`}><span>MONITORING</span><strong>{monitoring.highCriticalAlerts ? `${monitoring.highCriticalAlerts} ALERTS` : "HEALTHY"}</strong></div></header>
      <p className="admin-lede">Daily/weekly jobs, parser health, source staleness và snapshot change rate được đọc trực tiếp từ run ledger. Crawler failure không thay đổi published data.</p>
      <MonitoringWorkspace monitoring={monitoring} />
    </div>
  );
}
