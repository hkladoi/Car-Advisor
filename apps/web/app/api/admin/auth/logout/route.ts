import { NextResponse } from "next/server";

import { ADMIN_COOKIE } from "@/lib/admin-api";
import { isSameOrigin } from "@/lib/admin-csrf";

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ code: "ADMIN_CSRF_REJECTED" }, { status: 403 });
  const token = request.headers.get("cookie")?.split(";").map(value => value.trim()).find(value => value.startsWith(`${ADMIN_COOKIE}=`))?.slice(ADMIN_COOKIE.length + 1);
  if (token) {
    await fetch(`${apiBase()}/api/v1/admin/auth/logout`, {
      method: "POST",
      cache: "no-store",
      headers: { Authorization: `Bearer ${decodeURIComponent(token)}`, "Content-Type": "application/json" },
      body: JSON.stringify({ reason: "Administrator explicitly signed out from the web console." }),
    }).catch(() => undefined);
  }
  const response = NextResponse.json({ authenticated: false });
  response.cookies.delete(ADMIN_COOKIE);
  return response;
}
