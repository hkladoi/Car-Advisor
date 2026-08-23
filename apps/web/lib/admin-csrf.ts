export function isSameOrigin(request: Request): boolean {
  const origin = request.headers.get("origin");
  if (!origin) return true;
  const originHost = new URL(origin).host.toLowerCase();
  const requestHosts = [request.headers.get("host"), request.headers.get("x-forwarded-host")]
    .filter((value): value is string => Boolean(value))
    .map(value => value.toLowerCase());
  return requestHosts.includes(originHost);
}
