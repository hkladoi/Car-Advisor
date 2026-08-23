import { cookies } from "next/headers";
import { NextResponse } from "next/server";

import { ADMIN_COOKIE } from "@/lib/admin-api";
import { isSameOrigin } from "@/lib/admin-csrf";

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";
const allowed = /^(catalog\/trims(?:\/[0-9a-f-]+)?|sources(?:\/[0-9a-f-]+)?|imports(?:\/validate|\/[0-9a-f-]+\/stage)?|review-queue|changes\/[0-9a-f-]+\/(?:approve|reject|edit-publish)|publications(?:\/[0-9a-f-]+\/rollback)?|monitoring(?:\/alerts\/[0-9a-f-]+\/acknowledge)?|overrides|field-locks(?:\/[0-9a-f-]+\/unlock)?|dealers(?:\/[0-9a-f-]+)?|dealer-branches(?:\/[0-9a-f-]+)?|dealer-offers(?:\/[0-9a-f-]+)?|coverage|quality|audit)$/i;

async function proxy(request: Request, context: { params: Promise<{ path: string[] }> }) {
  const path = (await context.params).path.join("/");
  if (!allowed.test(path)) return NextResponse.json({ code: "ADMIN_PROXY_PATH_REJECTED" }, { status: 404 });
  if (request.method !== "GET") {
    if (!isSameOrigin(request)) return NextResponse.json({ code: "ADMIN_CSRF_REJECTED" }, { status: 403 });
  }
  const token = (await cookies()).get(ADMIN_COOKIE)?.value;
  if (!token) return NextResponse.json({ code: "ADMIN_AUTH_REQUIRED" }, { status: 401 });
  const source = new URL(request.url);
  const target = `${apiBase()}/api/v1/admin/${path}${source.search}`;
  const response = await fetch(target, {
    method: request.method,
    cache: "no-store",
    signal: AbortSignal.timeout(30_000),
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json", ...(request.method === "GET" ? {} : { "Content-Type": "application/json" }) },
    body: request.method === "GET" ? undefined : await request.text(),
  });
  if (response.status === 204) return new NextResponse(null, { status: 204 });
  return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" } });
}

export const GET = proxy;
export const POST = proxy;
export const PUT = proxy;
export const DELETE = proxy;
