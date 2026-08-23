import { NextResponse } from "next/server";

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function POST(request: Request) {
  try {
    const body = await request.text();
    const response = await fetch(`${apiBase()}/api/v1/affordability/evaluate`, {
      method: "POST",
      cache: "no-store",
      signal: AbortSignal.timeout(30_000),
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body,
    });
    const payload = await response.json();
    return NextResponse.json(payload, { status: response.status });
  } catch {
    return NextResponse.json(
      { code: "AFFORDABILITY_UPSTREAM_UNAVAILABLE", message: "Dịch vụ đánh giá chi phí sở hữu tạm thời chưa phản hồi." },
      { status: 502 },
    );
  }
}
