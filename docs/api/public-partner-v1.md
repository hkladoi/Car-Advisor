# Public/partner read API V1

Base path: `/api/v1/partner`. Contract version: `v1`. Data policy version:
`2026-08-24`.

The existing anonymous `/api/v1` product endpoints remain compatible. The
partner namespace provides a separately metered, read-only integration surface
over the same reviewed PostgreSQL read models; it never calls source sites or
external providers on a request path.

## Authentication and key handling

Protected calls require exactly one server-side header:

```http
X-VCP-API-Key: vcp_v1_<prefix>.<secret>
```

The full 256-bit key is shown once at issuance. PostgreSQL stores only its
non-secret prefix and SHA-256 hash. Admin list/audit responses never return the
hash or plaintext. Keep the key in a server secret manager, rotate by issuing a
replacement, and revoke the old key. Do not put it in frontend code, a query
string or logs.

Administrators issue and revoke credentials through authenticated admin routes:

- `GET /api/v1/admin/partner-api/keys` — Viewer or higher; metadata only.
- `POST /api/v1/admin/partner-api/keys` — Administrator; plaintext returned once.
- `POST /api/v1/admin/partner-api/keys/{id}/revoke` — Administrator.

Issuance requires an active plan, exact current policy acceptance, optional
future expiry and a human reason. Issue/revoke actions are audited.

## Read endpoints

- `GET /policy` — public policy, scope and active plan definitions.
- `GET /me` — credential metadata and assigned limits.
- `GET /brands` — active catalog brands.
- `GET /cars` — catalog search/filter/paging; filter semantics match
  `docs/api/catalog-v1.md` subject to the plan's maximum page size.
- `GET /cars/{trimId}` — full vehicle detail including fact-level provenance.

No write verb is part of the partner namespace. OpenAPI is available from
`/swagger/v1/swagger.json` and declares the `PartnerApiKey` header security
scheme on every protected operation.

## Plans and distributed rate limits

| Plan | Requests/minute | Requests/month | Max page size |
| --- | ---: | ---: | ---: |
| `sandbox` | 30 | 10,000 | 25 |
| `standard` | 300 | 500,000 | 100 |

Redis atomically enforces both fixed UTC-minute and UTC-month counters so limits
remain correct across API replicas. Enforcement fails closed if the counter is
unavailable. Rejected over-limit calls do not consume additional quota.

Successful authenticated responses and `429` responses include:

- `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`
- `X-RateLimit-Month-Limit`, `X-RateLimit-Month-Remaining`,
  `X-RateLimit-Month-Reset`
- `Retry-After` on `429`
- `X-VCP-Contract-Version`, `X-VCP-Data-Policy-Version` and a policy `Link`

## Responses and errors

Partner catalog payloads wrap the established catalog response as `data` and
add `meta` with contract version, policy version, source-specific licence
marker, attribution and policy path. Vehicle detail retains each source name,
URL, authority, content hash, fetch timestamp, fact status and confidence.

Errors use the common JSON contract:

```json
{
  "code": "PARTNER_API_KEY_INVALID",
  "message": "The partner API key is invalid, expired, revoked or bound to an older policy.",
  "fieldErrors": [],
  "traceId": "..."
}
```

Expected statuses are `400` for invalid catalog filters, `401` for a missing or
invalid key, `403` for plan-page-size violations, `404` for an unknown trim,
`429` for usage-plan exhaustion and `503` when distributed enforcement is
unavailable.

## Compatibility policy

V1 may receive additive optional fields and new read endpoints. Clients must
ignore unknown response fields. Removing a field, changing its type/meaning or
adding a required request field is breaking and requires `/api/v2`, a migration
guide and an announced overlap window. A data-policy update is independent of
the schema version and can require explicit re-acceptance and key re-issuance.

See `docs/api/data-attribution-policy.md` for reuse rules.
