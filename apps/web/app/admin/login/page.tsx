import Link from "next/link";
import { redirect } from "next/navigation";

import { AdminLoginForm } from "@/features/admin/admin-login-form";
import { adminFetch, type AdminSession } from "@/lib/admin-api";

export default async function AdminLoginPage() {
  if (await adminFetch<AdminSession>("auth/session")) redirect("/admin");
  return (
    <main className="admin-login-page">
      <Link className="admin-back-link" href="/">← Vietnam Car Platform</Link>
      <AdminLoginForm />
    </main>
  );
}
