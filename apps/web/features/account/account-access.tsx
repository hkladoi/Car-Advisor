"use client";

import { useState, type FormEvent } from "react";
import { ArrowRight, LockKeyhole, ShieldCheck } from "lucide-react";
import { useRouter } from "next/navigation";

export function AccountAccess() {
  const router = useRouter();
  const [mode, setMode] = useState<"login" | "register">("login");
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPending(true);
    setMessage(null);
    const data = new FormData(event.currentTarget);
    const response = await fetch(`/api/account/${mode}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(mode === "register" ? {
        email: data.get("email"),
        password: data.get("password"),
        displayName: data.get("displayName"),
        privacyConsent: data.get("privacyConsent") === "on",
      } : {
        email: data.get("email"),
        password: data.get("password"),
      }),
    });
    const payload = await response.json().catch(() => ({})) as { message?: string };
    setPending(false);
    if (!response.ok) {
      setMessage(payload.message ?? "Không thể xác thực tài khoản.");
      return;
    }
    router.refresh();
  }

  return (
    <div className="account-access-grid">
      <section className="account-access-copy">
        <p className="machine-label">OPT-IN ACCOUNT · PRIVATE BY DEFAULT</p>
        <h1>Lưu shortlist, không đánh đổi quyền kiểm soát.</h1>
        <p>Calculator vẫn dùng được hoàn toàn ẩn danh. Tài khoản chỉ được tạo khi bạn đồng ý rõ ràng; token phiên nằm trong cookie HttpOnly và dữ liệu có thể xuất hoặc xóa.</p>
        <div className="account-privacy-points">
          <span><ShieldCheck aria-hidden="true" /> Consent trước khi persist</span>
          <span><LockKeyhole aria-hidden="true" /> Không đưa profile nhạy cảm vào URL</span>
        </div>
      </section>
      <section className="account-access-panel" aria-labelledby="account-access-title">
        <div className="account-mode-switch" role="tablist" aria-label="Chế độ tài khoản">
          <button type="button" role="tab" aria-selected={mode === "login"} onClick={() => { setMode("login"); setMessage(null); }}>Đăng nhập</button>
          <button type="button" role="tab" aria-selected={mode === "register"} onClick={() => { setMode("register"); setMessage(null); }}>Tạo tài khoản</button>
        </div>
        <form onSubmit={submit}>
          <header><p className="machine-label">{mode === "login" ? "RETURNING MEMBER" : "EXPLICIT CONSENT"}</p><h2 id="account-access-title">{mode === "login" ? "Mở không gian đã lưu." : "Tạo không gian riêng."}</h2></header>
          {mode === "register" && <label>Tên hiển thị<input name="displayName" minLength={2} maxLength={80} autoComplete="name" required /></label>}
          <label>Email<input name="email" type="email" maxLength={320} autoComplete="email" required /></label>
          <label>Mật khẩu<input name="password" type="password" minLength={12} maxLength={256} autoComplete={mode === "login" ? "current-password" : "new-password"} required /><small>Tối thiểu 12 ký tự, có chữ và số.</small></label>
          {mode === "register" && <label className="account-consent"><input name="privacyConsent" type="checkbox" required /><span>Tôi đồng ý lưu profile, shortlist và thiết lập cảnh báo theo chính sách 2026-08-v1. Tôi có thể xuất hoặc xóa toàn bộ dữ liệu.</span></label>}
          {message && <p className="account-form-message is-error" role="alert">{message}</p>}
          <button className="button-control button-primary account-submit" type="submit" disabled={pending}>{pending ? "Đang xử lý…" : mode === "login" ? "Đăng nhập" : "Đồng ý và tạo tài khoản"}<ArrowRight aria-hidden="true" /></button>
        </form>
      </section>
    </div>
  );
}
