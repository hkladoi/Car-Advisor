import { redirect } from "next/navigation";

import { AdminShell } from "@/features/admin/admin-shell";
import { adminFetch, type AdminSession } from "@/lib/admin-api";

export default async function ProtectedAdminLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const session = await adminFetch<AdminSession>("auth/session");
  if (!session) redirect("/admin/login");
  return <AdminShell session={session}>{children}</AdminShell>;
}
