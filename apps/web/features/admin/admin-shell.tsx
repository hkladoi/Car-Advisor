"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Activity, BellRing, ClipboardCheck, Database, Gauge, LogOut, ShieldCheck } from "lucide-react";

import type { AdminSession } from "@/lib/admin-api";

const links = [
  { href: "/admin", label: "Tổng quan", icon: Gauge },
  { href: "/admin/coverage", label: "Coverage & QA", icon: Activity },
  { href: "/admin/review", label: "Review queue", icon: ClipboardCheck },
  { href: "/admin/monitoring", label: "Monitoring", icon: BellRing },
  { href: "/admin/data", label: "Data operations", icon: Database },
];

export function AdminShell({ session, children }: { session: AdminSession; children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  async function logout() {
    await fetch("/api/admin/auth/logout", { method: "POST" });
    router.replace("/admin/login");
    router.refresh();
  }
  return (
    <div className="admin-shell">
      <header className="admin-topbar">
        <Link className="admin-wordmark" href="/admin"><ShieldCheck size={20} /><strong>VCP / CONTROL</strong></Link>
        <div className="admin-session-chip"><span>{session.email}</span><b>{session.role}</b></div>
        <button className="admin-icon-button" type="button" onClick={logout} aria-label="Đăng xuất"><LogOut size={18} /></button>
      </header>
      <aside className="admin-sidebar" aria-label="Điều hướng quản trị">
        <p className="machine-label">DATA OPERATIONS</p>
        <nav>{links.map(({ href, label, icon: Icon }) => <Link key={href} href={href} aria-current={pathname === href ? "page" : undefined}><Icon size={17} />{label}</Link>)}</nav>
        <Link className="admin-public-link" href="/cars">↗ Mở public catalog</Link>
      </aside>
      <main className="admin-main">{children}</main>
    </div>
  );
}
