"use client";

import { useState, type FormEvent } from "react";
import { BellRing, Download, ExternalLink, LogOut, ShieldCheck, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";

import type { AccountAlert, AccountProfile, AccountSession, SavedComparison, WatchlistItem } from "@/lib/account-api";
import type { RegionItem } from "@/lib/registration-api";

type Props = {
  session: AccountSession;
  profile: AccountProfile | null;
  comparisons: SavedComparison[];
  watchlist: WatchlistItem[];
  alerts: AccountAlert[];
  regions: RegionItem[];
};

const money = (value: number | null) => value === null ? "Chưa đặt" : new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(value);

export function AccountDashboard({ session, profile, comparisons, watchlist, alerts, regions }: Props) {
  const router = useRouter();
  const [pending, setPending] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function mutate(path: string, method: "POST" | "PUT" | "DELETE", body?: object) {
    setPending(`${method}:${path}`);
    setMessage(null);
    const response = await fetch(`/api/account/${path}`, {
      method,
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
    const payload = response.status === 204 ? null : await response.json().catch(() => null) as { message?: string } | null;
    setPending(null);
    if (!response.ok) {
      setMessage(payload?.message ?? "Thao tác chưa hoàn tất.");
      return false;
    }
    router.refresh();
    return true;
  }

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    await mutate("profile", "PUT", {
      name: data.get("name"), regionCode: data.get("regionCode"), policy: data.get("policy"),
      netMonthlyIncome: Number(data.get("netMonthlyIncome")), rentHousing: Number(data.get("rentHousing")),
      essentialExpenses: Number(data.get("essentialExpenses")), otherFixedDebt: Number(data.get("otherFixedDebt")),
      savingsTarget: Number(data.get("savingsTarget")), monthlyKilometres: Number(data.get("monthlyKilometres")),
      parkingMonthly: Number(data.get("parkingMonthly")), householdBaseKwh: Number(data.get("householdBaseKwh")),
    });
  }

  async function removeAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    if (await mutate("me", "DELETE", { password: data.get("password"), confirmation: data.get("confirmation") })) router.push("/");
  }

  return (
    <>
      <header className="account-dashboard-head">
        <div><p className="machine-label">PRIVATE SPACE · {session.privacyPolicyVersion}</p><h1>Chào {session.displayName}.</h1><p>{session.email} · consent {new Date(session.consentedAt).toLocaleDateString("vi-VN")}</p></div>
        <div className="account-head-actions"><Link className="button-control button-outline" href="/api/account/export" prefetch={false} download><Download aria-hidden="true" /> Xuất JSON</Link><button type="button" className="button-control button-ghost" onClick={() => mutate("logout", "POST")}><LogOut aria-hidden="true" /> Đăng xuất</button></div>
      </header>
      {message && <p className="account-form-message is-error" role="alert">{message}</p>}
      <div className="account-dashboard-grid">
        <form className="account-profile-panel" onSubmit={saveProfile}>
          <header><p className="machine-label">REGION + AFFORDABILITY PROFILE</p><h2>Profile riêng tư</h2><span>Chỉ lưu sau khi opt-in. Không xuất hiện trong URL chia sẻ.</span></header>
          <div className="account-profile-fields">
            <label>Tên profile<input name="name" defaultValue={profile?.name ?? "Profile chính"} minLength={2} maxLength={80} required /></label>
            <label>Khu vực<select name="regionCode" defaultValue={profile?.regionCode ?? "VN-01"}>{regions.map(region => <option value={region.code} key={region.code}>{region.name}</option>)}</select></label>
            <label>Chính sách<select name="policy" defaultValue={profile?.policy ?? "Balanced"}><option>Conservative</option><option>Balanced</option><option>Aggressive</option><option>Custom</option></select></label>
            <label>Thu nhập ròng<input name="netMonthlyIncome" type="number" min="0" step="100000" defaultValue={profile?.netMonthlyIncome ?? 0} required /></label>
            <label>Nhà ở<input name="rentHousing" type="number" min="0" step="100000" defaultValue={profile?.rentHousing ?? 0} required /></label>
            <label>Chi thiết yếu<input name="essentialExpenses" type="number" min="0" step="100000" defaultValue={profile?.essentialExpenses ?? 0} required /></label>
            <label>Nợ cố định<input name="otherFixedDebt" type="number" min="0" step="100000" defaultValue={profile?.otherFixedDebt ?? 0} required /></label>
            <label>Mục tiêu tiết kiệm<input name="savingsTarget" type="number" min="0" step="100000" defaultValue={profile?.savingsTarget ?? 0} required /></label>
            <label>Km / tháng<input name="monthlyKilometres" type="number" min="0" step="10" defaultValue={profile?.monthlyKilometres ?? 1000} required /></label>
            <label>Gửi xe / tháng<input name="parkingMonthly" type="number" min="0" step="50000" defaultValue={profile?.parkingMonthly ?? 0} required /></label>
            <label>Điện nền gia đình<input name="householdBaseKwh" type="number" min="0" step="1" defaultValue={profile?.householdBaseKwh ?? 250} required /></label>
          </div>
          <button className="button-control button-primary" type="submit" disabled={pending === "PUT:profile"}><ShieldCheck aria-hidden="true" /> {pending === "PUT:profile" ? "Đang lưu…" : "Lưu profile đã consent"}</button>
        </form>
        <section className="account-alert-panel">
          <header><p className="machine-label">CURRENT WATCH SIGNALS</p><h2><BellRing aria-hidden="true" /> {alerts.length} tín hiệu</h2></header>
          <div className="account-alert-list">{alerts.length === 0 ? <p className="account-empty">Chưa có tín hiệu phù hợp. Thêm xe vào watchlist từ trang chi tiết.</p> : alerts.map(alert => <article key={alert.id}><span>{alert.kind}</span><strong>{alert.vehicle}</strong><h3>{alert.title}</h3><p>{alert.message}</p>{alert.amount !== null && <b>{money(alert.amount)}</b>}{alert.source.url && <a href={alert.source.url} target="_blank" rel="noreferrer">{alert.source.name ?? "Nguồn"} <ExternalLink aria-hidden="true" /></a>}</article>)}</div>
        </section>
      </div>
      <div className="account-collections">
        <section><header><p className="machine-label">SAVED COMPARISONS</p><h2>{comparisons.length} bộ so sánh</h2></header>{comparisons.length === 0 ? <p className="account-empty">Lưu trực tiếp từ màn hình so sánh 2–4 trim.</p> : comparisons.map(item => <article key={item.id}><div><strong>{item.name}</strong><span>{item.trimIds.length} trim · {item.regionCode}</span></div><Link href={`/compare?trims=${item.trimIds.join(",")}&region=${item.regionCode}&profile=${item.profilePreset}&financing=${item.financingPreset}`}>Mở lại</Link><button aria-label={`Xóa ${item.name}`} type="button" disabled={pending === `DELETE:comparisons/${item.id}`} onClick={() => mutate(`comparisons/${item.id}`, "DELETE")}><Trash2 aria-hidden="true" /></button></article>)}</section>
        <section><header><p className="machine-label">WATCHLIST</p><h2>{watchlist.length} xe đang theo dõi</h2></header>{watchlist.length === 0 ? <p className="account-empty">Mở một trang chi tiết xe để thêm watchlist.</p> : watchlist.map(item => <article key={item.id}><div><strong>{item.brandName} {item.modelName}</strong><span>{item.trimName} · hiện tại {money(item.currentPrice)} · mục tiêu {money(item.targetPrice)}</span></div><Link href={`/cars/${item.trimId}`}>Chi tiết</Link><button aria-label={`Bỏ theo dõi ${item.modelName}`} type="button" disabled={pending === `DELETE:watchlist/${item.trimId}`} onClick={() => mutate(`watchlist/${item.trimId}`, "DELETE")}><Trash2 aria-hidden="true" /></button></article>)}</section>
      </div>
      <details className="account-danger-zone"><summary>Xóa toàn bộ dữ liệu tài khoản</summary><form onSubmit={removeAccount}><p>Hành động này xóa profile, session, comparison và watchlist. Không thể hoàn tác.</p><label>Mật khẩu hiện tại<input name="password" type="password" autoComplete="current-password" required /></label><label>Nhập DELETE<input name="confirmation" pattern="DELETE" required /></label><button className="button-control" type="submit" disabled={pending === "DELETE:me"}><Trash2 aria-hidden="true" /> {pending === "DELETE:me" ? "Đang xóa…" : "Xóa vĩnh viễn"}</button></form></details>
    </>
  );
}
