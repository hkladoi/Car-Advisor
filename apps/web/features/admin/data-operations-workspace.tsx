"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { FileCheck2, LockKeyhole, Plus, Save, Trash2, UnlockKeyhole, Upload } from "lucide-react";

import type { AdminFieldLock, AdminImport, AdminSource, AdminTrim } from "@/lib/admin-api";

type ApiError = { message?: string };

export function DataOperationsWorkspace({ sources, trims, imports, locks }: { sources: AdminSource[]; trims: AdminTrim[]; imports: AdminImport[]; locks: AdminFieldLock[] }) {
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function mutate(url: string, method: string, body?: unknown) {
    setPending(true);
    setMessage(null);
    const response = await fetch(url, { method, headers: body === undefined ? undefined : { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
    const payload = response.status === 204 ? null : await response.json() as ApiError;
    setPending(false);
    if (!response.ok) {
      setMessage(payload?.message ?? `Operation failed (${response.status}).`);
      return false;
    }
    setMessage("Đã ghi thay đổi và audit event.");
    router.refresh();
    return true;
  }

  async function createSource(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const okay = await mutate("/api/admin/sources", "POST", {
      name: data.get("name"), url: data.get("url"), authorityLevel: data.get("authorityLevel"), contentType: data.get("contentType"),
      robotsNote: data.get("robotsNote") || null, termsNote: data.get("termsNote") || null, active: true,
      priority: Number(data.get("priority")), refreshIntervalHours: Number(data.get("refreshIntervalHours")), reason: data.get("reason"),
    });
    if (okay) event.currentTarget.reset();
  }

  async function updateSource(event: React.FormEvent<HTMLFormElement>, source: AdminSource) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    await mutate(`/api/admin/sources/${source.id}`, "PUT", {
      name: data.get("name"), url: data.get("url"), authorityLevel: data.get("authorityLevel"), contentType: data.get("contentType"),
      robotsNote: data.get("robotsNote") || null, termsNote: data.get("termsNote") || null, active: data.get("active") === "on",
      priority: Number(data.get("priority")), refreshIntervalHours: Number(data.get("refreshIntervalHours")), reason: data.get("reason"),
    });
  }

  async function createTrim(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const okay = await mutate("/api/admin/catalog/trims", "POST", {
      brandName: data.get("brandName"), brandSlug: data.get("brandSlug"), brandCountryCode: data.get("brandCountryCode") || null,
      brandOfficialUrl: data.get("brandOfficialUrl") || null, modelName: data.get("modelName"), modelSlug: data.get("modelSlug"),
      bodyType: data.get("bodyType"), segment: data.get("segment"), generationCode: data.get("generationCode"),
      generationStartYear: Number(data.get("generationStartYear")), modelYear: Number(data.get("modelYear")), trimName: data.get("trimName"),
      trimSlug: data.get("trimSlug"), marketStatus: data.get("marketStatus"), reason: data.get("reason"),
    });
    if (okay) event.currentTarget.reset();
  }

  async function updateTrim(event: React.FormEvent<HTMLFormElement>, trim: AdminTrim) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    await mutate(`/api/admin/catalog/trims/${trim.trimId}`, "PUT", {
      name: data.get("name"), slug: data.get("slug"), marketStatus: data.get("marketStatus"),
      launchedAt: data.get("launchedAt") || null, discontinuedAt: data.get("discontinuedAt") || null, reason: data.get("reason"),
    });
  }

  async function validateImport(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const file = data.get("file");
    if (!(file instanceof File) || !file.size) {
      setMessage("Chọn file CSV hoặc JSON thật để validation.");
      return;
    }
    const okay = await mutate("/api/admin/imports/validate", "POST", { fileName: file.name, content: await file.text(), reason: data.get("reason") });
    if (okay) event.currentTarget.reset();
  }

  async function overrideField(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const okay = await mutate("/api/admin/overrides", "POST", {
      entityType: data.get("entityType"), entityId: data.get("entityId"), fieldPath: data.get("fieldPath"), newValue: data.get("newValue"),
      reason: data.get("reason"), lockField: data.get("lockField") === "on", lockExpiresAt: data.get("lockExpiresAt") || null,
    });
    if (okay) event.currentTarget.reset();
  }

  return (
    <div className="admin-ops-stack">
      {message ? <p className="admin-inline-message" role="status">{message}</p> : null}

      <section className="admin-panel">
        <div className="admin-panel-head"><div><p className="machine-label">SOURCE REGISTRY</p><h2>Known URL first.</h2></div><span>{sources.length} sources</span></div>
        <form className="admin-form-grid admin-create-form" onSubmit={createSource}>
          <label>Tên nguồn<input name="name" required /></label><label>HTTPS URL<input name="url" type="url" pattern="https://.*" required /></label>
          <label>Authority<select name="authorityLevel" defaultValue="BrandOfficial"><option>CompetentAuthority</option><option>BrandOfficial</option><option>DistributorOfficial</option><option>DealerOfficial</option><option>TrustedSecondary</option><option>DiscoveryOnly</option></select></label>
          <label>Content<select name="contentType" defaultValue="Html"><option>Html</option><option>Pdf</option><option>Json</option><option>Xml</option><option>Image</option><option>ManualDocument</option></select></label>
          <label>Priority<input name="priority" type="number" min="0" defaultValue="100" required /></label><label>Refresh (hours)<input name="refreshIntervalHours" type="number" min="1" max="8760" defaultValue="168" required /></label>
          <label className="admin-field-span">Robots / terms note<input name="robotsNote" /></label><label className="admin-field-span">Lý do thêm nguồn<input name="reason" minLength={10} required /></label>
          <button className="button-primary" disabled={pending}><Plus size={16} /> Thêm source</button>
        </form>
        <div className="admin-record-list">{sources.map(source => <details key={source.id}><summary><div><strong>{source.name}</strong><span>{source.authorityLevel} · {source.snapshotCount} snapshots · {source.stale ? "STALE" : "CURRENT"}</span></div><code>{source.domain}</code></summary><form className="admin-form-grid" onSubmit={event => updateSource(event, source)}><label>Tên<input name="name" defaultValue={source.name} required /></label><label>URL<input name="url" defaultValue={source.url} required /></label><label>Authority<select name="authorityLevel" defaultValue={source.authorityLevel}>{["CompetentAuthority","BrandOfficial","DistributorOfficial","DealerOfficial","TrustedSecondary","DiscoveryOnly","Unknown"].map(value => <option key={value}>{value}</option>)}</select></label><label>Content<select name="contentType" defaultValue={source.contentType}>{["Html","Pdf","Json","Xml","Image","ManualDocument"].map(value => <option key={value}>{value}</option>)}</select></label><label>Priority<input name="priority" type="number" min="0" defaultValue={source.priority} /></label><label>Refresh hours<input name="refreshIntervalHours" type="number" min="1" max="8760" defaultValue={source.refreshIntervalHours} /></label><label className="admin-checkbox"><input name="active" type="checkbox" defaultChecked={source.active} /> Active</label><label className="admin-field-span">Lý do sửa<input name="reason" minLength={10} required /></label><button className="button-secondary" disabled={pending}><Save size={15} /> Lưu source</button><button className="button-danger" type="button" disabled={pending || !source.active} onClick={() => { const reason = window.prompt("Lý do deactivate (ít nhất 10 ký tự)"); if (reason) void mutate(`/api/admin/sources/${source.id}?reason=${encodeURIComponent(reason)}`, "DELETE"); }}><Trash2 size={15} /> Deactivate</button></form></details>)}</div>
      </section>

      <section className="admin-panel">
        <div className="admin-panel-head"><div><p className="machine-label">CORE ENTITY CRUD</p><h2>Trim aggregate editor.</h2></div><span>{trims.length} trims</span></div>
        <form className="admin-form-grid admin-create-form" onSubmit={createTrim}>
          <label>Brand<input name="brandName" required /></label><label>Brand slug<input name="brandSlug" pattern="[a-z0-9]+(?:-[a-z0-9]+)*" required /></label><label>Country code<input name="brandCountryCode" maxLength={3} /></label><label>Official URL<input name="brandOfficialUrl" type="url" /></label>
          <label>Model<input name="modelName" required /></label><label>Model slug<input name="modelSlug" pattern="[a-z0-9]+(?:-[a-z0-9]+)*" required /></label><label>Body type<select name="bodyType"><option>Suv</option><option>Sedan</option><option>Hatchback</option><option>Pickup</option><option>Mpv</option><option>Coupe</option><option>Wagon</option><option>Van</option></select></label><label>Segment<select name="segment"><option>A</option><option>B</option><option>C</option><option>D</option><option>E</option><option>Luxury</option><option>Unknown</option></select></label>
          <label>Generation code<input name="generationCode" required /></label><label>Generation start<input name="generationStartYear" type="number" min="1950" max="2100" required /></label><label>Model year<input name="modelYear" type="number" min="1990" max="2100" required /></label><label>Market status<select name="marketStatus"><option>Active</option><option>Upcoming</option><option>Announced</option><option>Discontinued</option></select></label>
          <label>Trim name<input name="trimName" required /></label><label>Trim slug<input name="trimSlug" pattern="[a-z0-9]+(?:-[a-z0-9]+)*" required /></label><label className="admin-field-span">Lý do tạo draft<input name="reason" minLength={10} required /></label><button className="button-primary" disabled={pending}><Plus size={16} /> Tạo trim draft</button>
        </form>
        <div className="admin-record-list admin-record-list--trims">{trims.map(trim => <details key={trim.trimId}><summary><div><strong>{trim.brandName} {trim.modelName} · {trim.trimName}</strong><span>{trim.generationCode} · MY{trim.modelYear} · {trim.marketStatus}</span></div><code>{trim.trimId.slice(0, 8)}</code></summary><form className="admin-form-grid" onSubmit={event => updateTrim(event, trim)}><label>Tên trim<input name="name" defaultValue={trim.trimName} required /></label><label>Slug<input name="slug" defaultValue={trim.slug} required /></label><label>Status<select name="marketStatus" defaultValue={trim.marketStatus}>{["Active","Upcoming","Announced","Discontinued","Unknown"].map(value => <option key={value}>{value}</option>)}</select></label><label>Launched<input name="launchedAt" type="date" /></label><label>Discontinued<input name="discontinuedAt" type="date" /></label><label className="admin-field-span">Lý do sửa<input name="reason" minLength={10} required /></label><button className="button-secondary" disabled={pending}><Save size={15} /> Lưu trim</button><button className="button-danger" type="button" disabled={pending} onClick={() => { const reason = window.prompt("Chỉ draft không dependency mới xóa được. Nhập lý do:"); if (reason) void mutate(`/api/admin/catalog/trims/${trim.trimId}?reason=${encodeURIComponent(reason)}`, "DELETE"); }}><Trash2 size={15} /> Xóa draft</button></form></details>)}</div>
      </section>

      <section className="admin-dashboard-grid">
        <div className="admin-panel">
          <div className="admin-panel-head"><div><p className="machine-label">MANUAL IMPORT</p><h2>Validate, then review.</h2></div><Upload size={19} /></div>
          <form className="admin-stack-form" onSubmit={validateImport}><label>CSV/JSON reviewed file<input name="file" type="file" accept=".csv,.json,text/csv,application/json" required /></label><label>Lý do import<input name="reason" minLength={10} required /></label><button className="button-primary" disabled={pending}><FileCheck2 size={16} /> Chạy validation</button></form>
          <div className="admin-import-list">{imports.map(item => <article key={item.id}><div><strong>{item.fileName}</strong><span className={`admin-state state-${item.status.toLowerCase()}`}>{item.status}</span></div><small>{item.recordCount} rows · {item.issues.length} issues · sha256:{item.contentHash.slice(0, 12)}…</small>{item.issues.slice(0, 4).map((issue, index) => <p key={`${issue.code}-${index}`}>{issue.severity} · row {issue.row ?? "—"} · {issue.code}</p>)}{item.status === "Validated" ? <button className="button-secondary" disabled={pending} onClick={() => { const reason = window.prompt("Lý do stage review:"); if (reason) void mutate(`/api/admin/imports/${item.id}/stage`, "POST", { reason }); }}>Stage review →</button> : null}</article>)}</div>
        </div>
        <div className="admin-panel">
          <div className="admin-panel-head"><div><p className="machine-label">MANUAL OVERRIDE</p><h2>Reason + optional lock.</h2></div><LockKeyhole size={19} /></div>
          <form className="admin-stack-form" onSubmit={overrideField}><label>Entity type<select name="entityType"><option>Trim</option><option>Price</option><option>Source</option></select></label><label>Entity ID<input name="entityId" required /></label><label>Field path<input name="fieldPath" placeholder="marketStatus / amount / priority" required /></label><label>Giá trị mới<input name="newValue" required /></label><label>Lý do override<input name="reason" minLength={10} required /></label><label className="admin-checkbox"><input name="lockField" type="checkbox" /> Lock field khỏi crawler overwrite</label><label>Lock expiry · optional<input name="lockExpiresAt" type="datetime-local" /></label><button className="button-primary" disabled={pending}><LockKeyhole size={16} /> Apply override</button></form>
          <div className="admin-lock-list">{locks.map(lock => <article key={lock.id}><div><LockKeyhole size={15} /><strong>{lock.entityType}.{lock.fieldPath}</strong></div><code>{lock.entityId}</code><p>{lock.reason}</p><small>{lock.actor} · {lock.expiresAt ? new Date(lock.expiresAt).toLocaleString("vi-VN") : "no expiry"}</small><button className="button-secondary" disabled={pending} onClick={() => { const reason = window.prompt("Lý do unlock:"); if (reason) void mutate(`/api/admin/field-locks/${lock.id}/unlock`, "POST", { reason }); }}><UnlockKeyhole size={14} /> Unlock</button></article>)}</div>
        </div>
      </section>
    </div>
  );
}
