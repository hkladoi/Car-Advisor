import { NextResponse } from "next/server";

import { ADMIN_COOKIE } from "@/lib/admin-api";
import { isSameOrigin } from "@/lib/admin-csrf";

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) {
    return NextResponse.json({ code: "ADMIN_CSRF_REJECTED", message: "Origin không hợp lệ." }, { status: 403 });
  }
  try {
    const response = await fetch(`${apiBase()}/api/v1/admin/auth/login`, {
      method: "POST",
      cache: "no-store",
      signal: AbortSignal.timeout(15_000),
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: await request.text(),
    });
    const payload = await response.json() as { token?: string; expiresAt?: string; code?: string; message?: string };
    if (!response.ok || !payload.token || !payload.expiresAt) return NextResponse.json(payload, { status: response.status });
    const result = NextResponse.json({ authenticated: true, expiresAt: payload.expiresAt });
    const forwardedProtocol = request.headers.get("x-forwarded-proto")?.split(",", 1)[0]?.trim().toLowerCase();
    const secureRequest = forwardedProtocol ? forwardedProtocol === "https" : new URL(request.url).protocol === "https:";
    result.cookies.set(ADMIN_COOKIE, payload.token, {
      httpOnly: true,
      secure: secureRequest,
      sameSite: "strict",
      path: "/",
      expires: new Date(payload.expiresAt),
    });
    return result;
  } catch {
    return NextResponse.json({ code: "ADMIN_UPSTREAM_UNAVAILABLE", message: "Dịch vụ quản trị tạm thời chưa phản hồi." }, { status: 502 });
  }
}
