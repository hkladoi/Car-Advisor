"use client";

import { useState, type CSSProperties, type FormEvent } from "react";
import { AlertTriangle, ArrowRight, Check, Copy, ExternalLink, GitCompareArrows, SlidersHorizontal } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";

import type { CatalogCar } from "@/lib/catalog-api";
import type { CompareFinancingPreset, CompareProfilePreset, CompareResponse } from "@/lib/compare-api";
import type { RegionItem } from "@/lib/registration-api";

type Props = {
  cars: CatalogCar[];
  regions: RegionItem[];
  selectedTrimIds: string[];
  profile: CompareProfilePreset;
  financing: CompareFinancingPreset;
  provinceCode: string;
  calculationDate: string;
  initialDifferencesOnly: boolean;
  result: CompareResponse | null;
  error: { code: string; message: string } | null;
};

const profileLabels: Record<CompareProfilePreset, string> = {
  "lean-city": "Đô thị gọn · 800 km",
  "city-balanced": "Đô thị cân bằng · 1.000 km",
  "high-mileage-public": "Đi nhiều · sạc công cộng",
};

const financingLabels: Record<CompareFinancingPreset, string> = {
  "cash-preset": "Trả thẳng · preset 3 tỷ",
  "standard-loan": "Vay 80% · 12% · 60 tháng",
  "short-reducing": "Vay 70% · 10% · 36 tháng giảm dần",
};

function money(value: number) {
  return new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(value);
}

function number(value: number) {
  return new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 3 }).format(value);
}

function CellValue({ row, cell }: { row: CompareResponse["rows"][number]; cell: CompareResponse["rows"][number]["cells"][number] }) {
  let value: string;
  if (cell.state === "Unknown") value = "Chưa có dữ liệu xác minh";
  else if (cell.state === "NotAvailable") value = "Không được cung cấp";
  else if (cell.state === "NotApplicable") value = "Không áp dụng";
  else if (cell.booleanValue !== null) value = cell.booleanValue ? "Có" : "Không có";
  else if (cell.numericValue !== null) value = row.format === "Money" ? money(cell.numericValue) : `${number(cell.numericValue)}${row.canonicalUnit ? ` ${row.canonicalUnit}` : ""}`;
  else value = cell.textValue ?? "Chưa có dữ liệu xác minh";
  const stateClass = cell.state === "Unknown" ? "is-unknown" : cell.state === "NotAvailable" ? "is-unavailable" : cell.state === "Expected" ? "is-expected" : "";
  return (
    <div className={`compare-cell-value ${stateClass}`}>
      <strong>{value}</strong>
      {cell.state !== "Calculated" && cell.state !== "Official" && <span className="compare-state">{cell.state}</span>}
      {cell.note && <small>{cell.note}</small>}
      {cell.sources.length > 0 && <div className="compare-cell-sources">{cell.sources.slice(0, 2).map((source) => <a href={source.url} target="_blank" rel="noreferrer" key={source.sourceFactId} title={`${source.name} · SHA-256 ${source.contentHash}`}><ExternalLink aria-hidden="true" size={12} />{source.authority}</a>)}{cell.sources.length > 2 && <span>+{cell.sources.length - 2} nguồn</span>}</div>}
    </div>
  );
}

export function CompareWorkbench({ cars, regions, selectedTrimIds, profile, financing, provinceCode, calculationDate, initialDifferencesOnly, result, error }: Props) {
  const router = useRouter();
  const [differencesOnly, setDifferencesOnly] = useState(initialDifferencesOnly);
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const trims = data.getAll("trimId").map(String).filter(Boolean).filter((value, index, values) => values.indexOf(value) === index);
    if (trims.length < 2 || trims.length > 4) {
      setSelectionError("Chọn từ 2 đến 4 trim khác nhau.");
      return;
    }
    setSelectionError(null);
    const query = new URLSearchParams({
      trims: trims.join(","),
      region: String(data.get("region")),
      date: String(data.get("date")),
      profile: String(data.get("profile")),
      financing: String(data.get("financing")),
    });
    if (differencesOnly) query.set("differences", "1");
    router.push(`/compare?${query.toString()}`);
  }

  function toggleDifferences(checked: boolean) {
    setDifferencesOnly(checked);
    const url = new URL(window.location.href);
    if (checked) url.searchParams.set("differences", "1");
    else url.searchParams.delete("differences");
    window.history.replaceState(null, "", url);
  }

  async function share() {
    const url = new URL(window.location.href);
    if (!url.searchParams.has("trims")) url.searchParams.set("trims", selectedTrimIds.join(","));
    url.searchParams.set("region", provinceCode);
    url.searchParams.set("date", calculationDate);
    url.searchParams.set("profile", profile);
    url.searchParams.set("financing", financing);
    if (differencesOnly) url.searchParams.set("differences", "1");
    await navigator.clipboard.writeText(url.toString());
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  }

  const visibleRows = result?.rows.filter((row) => !differencesOnly || row.different) ?? [];
  const sections = [...new Set(visibleRows.map((row) => row.section))];

  return (
    <>
      <form className="compare-controls" onSubmit={submit}>
        <header><SlidersHorizontal aria-hidden="true" size={19} /><div><p className="machine-label">SHARED SCENARIO</p><h2>Chọn xe và preset</h2></div></header>
        <div className="compare-trim-selectors">
          {[0, 1, 2, 3].map((index) => <div key={index}><label htmlFor={`compare-trim-${index}`}>Trim {index + 1}{index > 1 ? " · tùy chọn" : ""}</label><select id={`compare-trim-${index}`} name="trimId" defaultValue={selectedTrimIds[index] ?? ""}><option value="">— Chưa chọn —</option>{cars.map((car) => <option value={car.trimId} key={car.trimId}>{car.brandName} {car.modelName} · {car.trimName}</option>)}</select></div>)}
        </div>
        <div className="compare-scenario-selectors">
          <div><label htmlFor="compare-region">Khu vực</label><select id="compare-region" name="region" defaultValue={provinceCode}>{regions.map((region) => <option value={region.code} key={region.code}>{region.name} · KV {region.areaClass}</option>)}</select></div>
          <div><label htmlFor="compare-date">Ngày tính</label><input id="compare-date" name="date" type="date" defaultValue={calculationDate} /></div>
          <div><label htmlFor="compare-profile">Profile sở hữu</label><select id="compare-profile" name="profile" defaultValue={profile}>{Object.entries(profileLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></div>
          <div><label htmlFor="compare-financing">Kịch bản tài chính</label><select id="compare-financing" name="financing" defaultValue={financing}>{Object.entries(financingLabels).map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select></div>
        </div>
        <div className="compare-control-actions">
          <button className="button-control button-primary" type="submit">Tính lại tất cả xe <ArrowRight aria-hidden="true" size={17} /></button>
          {result && <label className="compare-difference-toggle"><input type="checkbox" checked={differencesOnly} onChange={(event) => toggleDifferences(event.target.checked)} /><span>Chỉ hiện khác biệt</span></label>}
          {result && <button className="button-control button-outline" type="button" onClick={share}>{copied ? <Check aria-hidden="true" size={16} /> : <Copy aria-hidden="true" size={16} />}{copied ? "Đã sao chép" : "Chia sẻ URL"}</button>}
        </div>
        {selectionError && <p className="compare-selection-error">{selectionError}</p>}
        <p className="compare-preset-note">Preset là giả định công khai, không phải hồ sơ tài chính thật. Thay preset hoặc khu vực sẽ gọi lại toàn bộ engine cho mọi trim.</p>
      </form>

      {error && <div className="onroad-error compare-error"><AlertTriangle aria-hidden="true" /><div><p className="machine-label">{error.code}</p><h2>Chưa thể so sánh.</h2><p>{error.message}</p></div></div>}
      {!result && !error && <section className="compare-empty"><GitCompareArrows aria-hidden="true" size={30} /><h2>Chọn thêm ít nhất một trim.</h2><p>Xe vừa mang từ catalog đã được giữ lại. Chọn thêm 1–3 phiên bản rồi bấm “Tính lại tất cả xe”.</p></section>}
      {result && <>
        <section className="compare-scenario-band">
          <div><p className="machine-label">ONE PROFILE · ALL TRIMS</p><h2>{profileLabels[profile]}</h2><span>{result.scenario.monthlyKilometres.toLocaleString("vi-VN")} km/tháng · gửi xe {money(result.scenario.parkingMonthly)}</span></div>
          <div><p className="machine-label">FINANCING PRESET</p><strong>{financingLabels[financing]}</strong><span>{result.scenario.purchaseMethod} · {result.scenario.repaymentMethod}</span></div>
        </section>
        <div className="compare-table-wrap" data-count={result.vehicles.length} style={{ "--compare-count": result.vehicles.length } as CSSProperties}>
          <table className="compare-table">
            <thead><tr><th scope="col"><span>{differencesOnly ? `${visibleRows.length} khác biệt` : `${visibleRows.length} tiêu chí`}</span></th>{result.vehicles.map((vehicle) => <th scope="col" key={vehicle.trimId}><p>{vehicle.brandName} · MY{vehicle.modelYear}</p><strong>{vehicle.modelName}</strong><span>{vehicle.trimName}</span><small>{vehicle.powertrain} · {vehicle.segment === "Unknown" ? "phân khúc chưa rõ" : vehicle.segment}</small><Link href={`/cars/${vehicle.trimId}`}>Mở detail <ArrowRight aria-hidden="true" size={13} /></Link></th>)}</tr></thead>
            <tbody>{sections.map((section) => <SectionRows section={section} rows={visibleRows.filter((row) => row.section === section)} vehicles={result.vehicles} key={section} />)}</tbody>
          </table>
        </div>
        {result.warnings.length > 0 && <details className="compare-warnings"><summary>{result.warnings.length} cảnh báo dữ liệu / rule</summary><div>{result.warnings.map((warning) => <p key={warning}>{warning}</p>)}</div></details>}
        <footer className="compare-method-note"><GitCompareArrows aria-hidden="true" size={18} /><span>Canonical units đến từ taxonomy backend. Difference-only so sánh cả state lẫn value, nên UNKNOWN và NOT_AVAILABLE luôn được phân biệt.</span></footer>
      </>}
    </>
  );
}

function SectionRows({ section, rows, vehicles }: { section: string; rows: CompareResponse["rows"]; vehicles: CompareResponse["vehicles"] }) {
  return (
    <>
      <tr className="compare-section-row"><th colSpan={vehicles.length + 1} scope="rowgroup">{section}</th></tr>
      {rows.map((row) => <tr className={row.different ? "is-different" : ""} key={`${section}-${row.code}`}><th scope="row"><strong>{row.label}</strong>{row.canonicalUnit && <span>{row.canonicalUnit}</span>}</th>{vehicles.map((vehicle) => { const cell = row.cells.find((value) => value.trimId === vehicle.trimId)!; return <td key={vehicle.trimId}><CellValue row={row} cell={cell} /></td>; })}</tr>)}
    </>
  );
}
