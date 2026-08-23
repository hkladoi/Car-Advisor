import { NextResponse } from "next/server";

import { ACCOUNT_COOKIE } from "@/lib/account-api";
import { isSameOrigin } from "@/lib/admin-csrf";

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";
const id = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function allowed(method: string, segments: string[]) {
  const path = segments.join("/");
  if (method === "POST" && ["register", "login", "logout", "comparisons"].includes(path)) return true;
  if (method === "PUT" && ["profile", "watchlist"].includes(path)) return true;
  if (method === "GET" && ["me", "profile", "comparisons", "watchlist", "alerts", "export"].includes(path)) return true;
  if (method === "DELETE" && path === "me") return true;
  return method === "DELETE" && segments.length === 2
    && ["comparisons", "watchlist"].includes(segments[0]) && id.test(segments[1]);
}

async function forward(request: Request, context: { params: Promise<{ path: string[] }> }) {
  const { path } = await context.params;
  if (!allowed(request.method, path)) {
    return NextResponse.json({ code: "ACCOUNT_ROUTE_NOT_ALLOWED", message: "Account operation is not allowed." }, { status: 404 });
  }
  if (request.method !== "GET" && !isSameOrigin(request)) {
    return NextResponse.json({ code: "ACCOUNT_CSRF_REJECTED", message: "Origin không hợp lệ." }, { status: 403 });
  }
  const token = request.headers.get("cookie")?.match(/(?:^|;\s*)vcp_account_session=([^;]+)/)?.[1];
  const body = request.method === "GET" ? undefined : await request.text();
  try {
    const response = await fetch(`${apiBase()}/api/v1/accounts/${path.join("/")}`, {
      method: request.method,
      cache: "no-store",
      signal: AbortSignal.timeout(30_000),
      headers: {
        Accept: "application/json",
        ...(body ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: `Bearer ${decodeURIComponent(token)}` } : {}),
      },
      body,
    });
    const contentType = response.headers.get("content-type") ?? "application/json";
    const payload = response.status === 204 ? null : await response.arrayBuffer();
    let result = payload
      ? new NextResponse(payload, { status: response.status, headers: { "Content-Type": contentType } })
      : new NextResponse(null, { status: response.status });
    if (["register", "login"].includes(path[0]) && response.ok && payload) {
      const auth = JSON.parse(new TextDecoder().decode(payload)) as { token?: string; expiresAt?: string };
      if (auth.token && auth.expiresAt) {
        result = NextResponse.json({ authenticated: true, expiresAt: auth.expiresAt }, { status: response.status });
        const forwardedProtocol = request.headers.get("x-forwarded-proto")?.split(",", 1)[0]?.trim().toLowerCase();
        const secureRequest = forwardedProtocol ? forwardedProtocol === "https" : new URL(request.url).protocol === "https:";
        result.cookies.set(ACCOUNT_COOKIE, auth.token, {
          httpOnly: true,
          secure: secureRequest,
          sameSite: "strict",
          path: "/",
          expires: new Date(auth.expiresAt),
        });
      }
    }
    if ((path[0] === "logout" || (path[0] === "me" && request.method === "DELETE")) && response.ok) {
      result.cookies.set(ACCOUNT_COOKIE, "", { httpOnly: true, sameSite: "strict", path: "/", expires: new Date(0) });
    }
    if (path[0] === "export" && response.ok) {
      result.headers.set("Content-Disposition", `attachment; filename="vcp-account-export-${new Date().toISOString().slice(0, 10)}.json"`);
    }
    return result;
  } catch {
    return NextResponse.json({ code: "ACCOUNT_UPSTREAM_UNAVAILABLE", message: "Dịch vụ tài khoản tạm thời chưa phản hồi." }, { status: 502 });
  }
}

export const GET = forward;
export const POST = forward;
export const PUT = forward;
export const DELETE = forward;
