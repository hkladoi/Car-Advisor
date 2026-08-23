"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { LockKeyhole } from "lucide-react";

export function AdminLoginForm() {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPending(true);
    setError(null);
    const data = new FormData(event.currentTarget);
    const response = await fetch("/api/admin/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: data.get("email"), password: data.get("password") }),
    });
    const payload = await response.json() as { message?: string };
    setPending(false);
    if (!response.ok) {
      setError(payload.message ?? "Không thể đăng nhập.");
      return;
    }
    router.replace("/admin");
    router.refresh();
  }

  return (
    <form className="admin-login-card" onSubmit={submit}>
      <div className="admin-login-card__mark" aria-hidden="true"><LockKeyhole size={22} /></div>
      <p className="machine-label">RESTRICTED · AUDITED SESSION</p>
      <h1>Data control room.</h1>
      <p>Mọi publish, override và reject đều cần actor, lý do và before/after. Session hết hạn sau 8 giờ.</p>
      <label htmlFor="admin-email">Email quản trị</label>
      <input id="admin-email" name="email" type="email" autoComplete="username" required />
      <label htmlFor="admin-password">Mật khẩu</label>
      <input id="admin-password" name="password" type="password" autoComplete="current-password" minLength={14} required />
      {error ? <p className="admin-form-error" role="alert">{error}</p> : null}
      <button className="button-primary" type="submit" disabled={pending}>{pending ? "Đang xác thực…" : "Mở phiên quản trị"}</button>
      <small>Thông tin xác thực chỉ đi qua server route và được lưu trong cookie HttpOnly, SameSite=Strict.</small>
    </form>
  );
}
