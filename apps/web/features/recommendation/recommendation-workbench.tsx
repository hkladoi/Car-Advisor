"use client";

import { useState, type FormEvent } from "react";
import { AlertTriangle, ArrowRight, CheckCircle2, ExternalLink, Filter, Gauge, ShieldAlert } from "lucide-react";
import Link from "next/link";

import {
  evaluateRecommendation,
  formatRecommendationMoney,
  type RecommendationCandidate,
  type RecommendationOutcome,
  type RecommendationRequest,
  type RecommendationResponse,
} from "@/lib/recommendation-api";

type Props = {
  initialRequest: RecommendationRequest;
  initialResult: RecommendationResponse | null;
  initialError: RecommendationOutcome["error"];
};

const weightFields = [
  ["priceValue", "Giá / giá trị"],
  ["runningCost", "Chi phí vận hành"],
  ["space", "Không gian"],
  ["safetyAdas", "An toàn / ADAS"],
  ["comfort", "Tiện nghi"],
  ["performance", "Hiệu năng"],
  ["technology", "Công nghệ"],
] as const;

function numeric(form: FormData, key: string): number | null {
  const value = String(form.get(key) ?? "").trim();
  return value === "" ? null : Number(value);
}

function single(value: FormDataEntryValue | null) {
  const text = String(value ?? "").trim();
  return text ? [text] : [];
}

export function RecommendationWorkbench({ initialRequest, initialResult, initialError }: Props) {
  const [result, setResult] = useState(initialResult);
  const [error, setError] = useState(initialError);
  const [loading, setLoading] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const request: RecommendationRequest = {
      hardFilters: {
        maximumPrice: numeric(form, "maximumPriceMillions") === null ? null : numeric(form, "maximumPriceMillions")! * 1_000_000,
        bodyTypes: single(form.get("bodyType")),
        segments: single(form.get("segment")),
        powertrains: single(form.get("powertrain")),
        minimumSeats: numeric(form, "minimumSeats"),
        requiredFeatureCodes: String(form.get("requiredFeatures") ?? "").split(",").map((value) => value.trim().toUpperCase()).filter(Boolean),
      },
      weights: Object.fromEntries(weightFields.map(([key]) => [key, numeric(form, key) ?? 0])) as unknown as RecommendationRequest["weights"],
      regionCode: "VN-01",
      asOfDate: null,
      maximumResults: 10,
    };
    setLoading(true);
    setError(null);
    const outcome = await evaluateRecommendation(request, true);
    setLoading(false);
    setResult(outcome.data);
    setError(outcome.error);
  }

  return (
    <div className="recommend-studio">
      <form className="recommend-form" onSubmit={submit} aria-label="Thiết lập gợi ý xe">
        <header><Filter aria-hidden="true" size={20} /><div><h2>Điều kiện bắt buộc</h2><p>Giá trị trống không tham gia lọc.</p></div></header>
        <div className="recommend-field-grid">
          <label><span>Giá tối đa · triệu đồng</span><input name="maximumPriceMillions" type="number" min="0" step="50" defaultValue={(initialRequest.hardFilters.maximumPrice ?? 0) / 1_000_000} /></label>
          <label><span>Số chỗ tối thiểu</span><input name="minimumSeats" type="number" min="1" max="100" defaultValue={initialRequest.hardFilters.minimumSeats ?? ""} /></label>
          <label><span>Kiểu thân xe</span><select name="bodyType" defaultValue=""><option value="">Tất cả</option><option value="Sedan">Sedan</option><option value="Hatchback">Hatchback</option><option value="Suv">SUV</option><option value="Crossover">Crossover</option><option value="Mpv">MPV</option><option value="Pickup">Pickup</option></select></label>
          <label><span>Phân khúc</span><select name="segment" defaultValue=""><option value="">Tất cả</option>{["A", "B", "C", "D", "E", "F", "Luxury", "Sports", "Utility"].map((value) => <option value={value} key={value}>{value}</option>)}</select></label>
          <label><span>Hệ truyền động</span><select name="powertrain" defaultValue=""><option value="">Tất cả</option>{["Ice", "Hev", "Phev", "Erev", "Bev"].map((value) => <option value={value} key={value}>{value.toUpperCase()}</option>)}</select></label>
          <label><span>Feature bắt buộc · mã, cách nhau dấu phẩy</span><input name="requiredFeatures" type="text" placeholder="AEB, CAMERA_360" /></label>
        </div>

        <fieldset className="recommend-weights">
          <legend>Trọng số tùy chỉnh</legend>
          <p>Engine tự chuẩn hóa tổng về 100%. Trọng số 0 loại component khỏi tổng điểm nhưng không hạ completeness gate.</p>
          <div>{weightFields.map(([key, label]) => <label key={key}><span>{label}</span><input name={key} type="number" min="0" max="100" step="1" defaultValue={initialRequest.weights[key]} /></label>)}</div>
        </fieldset>
        <button className={`recommend-submit${loading ? " is-loading" : ""}${error ? " is-error" : result ? " is-success" : ""}`} type="submit" disabled={loading} aria-busy={loading}>
          {loading ? "Đang kiểm dữ liệu…" : "Lọc và giải thích"}<ArrowRight aria-hidden="true" size={17} />
        </button>
      </form>

      <section className="recommend-proof" aria-live="polite" aria-busy={loading}>
        {error && <div className="recommend-error" role="alert"><AlertTriangle aria-hidden="true" /><div><strong>{error.code}</strong><p>{error.message}</p></div></div>}
        {!error && result && <RecommendationResults result={result} />}
      </section>
    </div>
  );
}

function RecommendationResults({ result }: { result: RecommendationResponse }) {
  return (
    <>
      <header className="recommend-summary">
        <div><Gauge aria-hidden="true" size={21} /><span>Đã xét</span><strong>{result.considered}</strong></div>
        <div><Filter aria-hidden="true" size={21} /><span>Qua hard filter</span><strong>{result.hardFilterMatched}</strong></div>
        <div><CheckCircle2 aria-hidden="true" size={21} /><span>Được xếp hạng</span><strong>{result.ranked.length}</strong></div>
        <div><ShieldAlert aria-hidden="true" size={21} /><span>Chờ dữ liệu</span><strong>{result.dataWithheld.length}</strong></div>
      </header>

      {result.ranked.length > 0 ? <section className="recommend-ranked"><h2>Xếp hạng đã qua gate</h2>{result.ranked.map((candidate) => <Candidate candidate={candidate} ranked key={candidate.vehicle.trimId} />)}</section> : <section className="recommend-honest-empty"><ShieldAlert aria-hidden="true" size={28} /><div><h2>Chưa có trim đủ bằng chứng để xếp hạng.</h2><p>{result.hardFilterMatched} xe qua điều kiện bắt buộc, nhưng chưa xe nào đồng thời đạt {(result.methodology.completenessThreshold * 100).toLocaleString("vi-VN")}% completeness và source trust. Danh sách chờ bên dưới chỉ ra chính xác phần thiếu.</p></div></section>}

      <details className="recommend-method"><summary>Đọc công thức và giả định</summary><div><code>{result.methodology.overallFormula}</code><code>{result.methodology.pricePerformanceFormula}</code><ol>{result.methodology.evaluationOrder.map((step) => <li key={step}>{step}</li>)}</ol>{result.methodology.assumptions.map((assumption) => <p key={assumption}>{assumption}</p>)}</div></details>

      {result.dataWithheld.length > 0 && <section className="recommend-withheld"><h2>Qua bộ lọc, chưa qua gate dữ liệu</h2>{result.dataWithheld.map((candidate) => <Candidate candidate={candidate} key={candidate.vehicle.trimId} />)}</section>}
      {result.hardFilterExcluded.length > 0 && <details className="recommend-filtered"><summary>{result.hardFilterExcluded.length} xe bị loại bởi hard filter</summary><div>{result.hardFilterExcluded.map((candidate) => <p key={candidate.vehicle.trimId}><strong>{candidate.vehicle.brandName} {candidate.vehicle.modelName} · {candidate.vehicle.trimName}</strong><span>{candidate.reasons.join(" · ")}</span></p>)}</div></details>}
    </>
  );
}

function Candidate({ candidate, ranked = false }: { candidate: RecommendationCandidate; ranked?: boolean }) {
  return (
    <article className={`recommend-candidate${ranked ? " is-ranked" : ""}`}>
      <header>
        <div><span>{ranked ? `#${candidate.rank}` : `${Math.round(candidate.completeness * 100)}% complete`}</span><h3>{candidate.vehicle.brandName} {candidate.vehicle.modelName}</h3><p>{candidate.vehicle.trimName} · MY{candidate.vehicle.modelYear} · {candidate.vehicle.powertrain}</p></div>
        <div className="recommend-score"><span>Điểm tổng</span><strong>{candidate.overallScore?.toLocaleString("vi-VN", { maximumFractionDigits: 2 }) ?? "—"}</strong><small>P/P {candidate.pricePerformanceScore?.toLocaleString("vi-VN", { maximumFractionDigits: 2 }) ?? "chưa phát hành"}</small></div>
      </header>
      <div className="recommend-vehicle-meta"><span>{formatRecommendationMoney(candidate.vehicle.currentPrice, candidate.vehicle.currency)}</span><span>{candidate.vehicle.bodyType} · {candidate.vehicle.segment}</span><Link href={`/cars/${candidate.vehicle.trimId}`}>Mở trim <ArrowRight aria-hidden="true" size={13} /></Link></div>
      {candidate.reasons.length > 0 && <div className="recommend-reasons">{candidate.reasons.map((reason) => <code key={reason}>{reason}</code>)}</div>}
      <details className="recommend-components"><summary>7 thành phần điểm và raw facts</summary><div>{candidate.components.map((component) => <div className="recommend-component" key={component.code}><div><strong>{component.label}</strong><span>weight {(component.weight * 100).toLocaleString("vi-VN", { maximumFractionDigits: 1 })}%</span></div><div><b>{component.score?.toLocaleString("vi-VN", { maximumFractionDigits: 2 }) ?? "—"}</b>{component.rawMetrics.map((metric) => <small key={metric.code}>{metric.label}: {metric.value.toLocaleString("vi-VN", { maximumFractionDigits: 2 })} {metric.unit}</small>)}<p>{component.explanation}</p>{component.sources.map((source) => <a href={source.url} target="_blank" rel="noreferrer" key={source.sourceFactId}><ExternalLink aria-hidden="true" size={12} />{source.name} · {source.confidence}</a>)}</div></div>)}</div></details>
    </article>
  );
}
