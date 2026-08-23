import type { Metadata } from "next";
import { AlertTriangle, BadgeCheck, CheckCircle2, Clock3, Database, FileWarning, ShieldCheck } from "lucide-react";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { getCoverage } from "@/lib/coverage-api";

export const metadata: Metadata = {
  title: "Phạm vi dữ liệu thị trường",
  description: "Báo cáo công khai về phạm vi hãng, model, trim, độ đầy đủ và độ mới của dữ liệu xe Việt Nam.",
};

const percent = (value: number) => new Intl.NumberFormat("vi-VN", { style: "percent", maximumFractionDigits: 1 }).format(value);
const domainLabel = (domain: string) => ({ price: "Giá xe", promotion: "Khuyến mại", "dealer-offer": "Đại lý", energy: "Năng lượng", legal: "Pháp lý" })[domain] ?? domain;

export default async function CoveragePage() {
  const coverage = await getCoverage();
  const includedBrands = coverage.brands.filter(brand => brand.included);
  const excludedBrands = coverage.brands.filter(brand => !brand.included);

  return (
    <div className="coverage-shell">
      <SiteHeader />
      <main className="coverage-main">
        <header className="coverage-intro">
          <div>
            <p className="machine-label">FULL-MARKET COVERAGE · {coverage.scopeVersion ?? "UNVERSIONED"}</p>
            <h1>Phạm vi dữ liệu được đo, không được tuyên bố suông.</h1>
            <p>Mỗi hãng trong scope Việt Nam phải được review; mỗi model/trim tìm thấy phải được publish hoặc ghi rõ khoảng trống. Hệ thống không tự dựng trim để làm đẹp coverage.</p>
          </div>
          <div className={`coverage-gate ${coverage.fullMarketGatePassed ? "is-pass" : "is-blocked"}`}>
            {coverage.fullMarketGatePassed ? <BadgeCheck aria-hidden="true" size={30} /> : <AlertTriangle aria-hidden="true" size={30} />}
            <span>FULL-MARKET GATE</span>
            <strong>{coverage.fullMarketGatePassed ? "PASS" : "BLOCKED"}</strong>
            <small>Tính lúc {new Date(coverage.calculatedAt).toLocaleString("vi-VN")}</small>
          </div>
        </header>

        {coverage.gateFailures.length > 0 && (
          <section className="coverage-blockers" aria-label="Điều kiện chưa đạt">
            <AlertTriangle aria-hidden="true" size={20} />
            <div><strong>Chưa đủ điều kiện gắn nhãn toàn thị trường.</strong>{coverage.gateFailures.map(failure => <code key={failure}>{failure}</code>)}</div>
          </section>
        )}

        <section className="coverage-stat-grid" aria-label="Tổng quan phạm vi dữ liệu">
          <article><ShieldCheck aria-hidden="true" /><span>HÃNG ĐÃ REVIEW</span><strong>{coverage.reviewedBrandCount}/{coverage.brandScopeCount}</strong><small>{includedBrands.length} trong scope · {coverage.excludedBrandCount} loại trừ có chủ đích</small></article>
          <article><Database aria-hidden="true" /><span>CANDIDATE ĐÃ XỬ LÝ</span><strong>{coverage.resolvedCandidateCount}/{coverage.discoveredCandidateCount}</strong><small>{coverage.activeModelCount} model · {coverage.activeTrimCount} trim công khai</small></article>
          <article><CheckCircle2 aria-hidden="true" /><span>CORE COMPLETENESS</span><strong>{percent(coverage.coreCompleteness)}</strong><small>UNKNOWN minh bạch được giữ nguyên, không suy đoán</small></article>
          <article><Clock3 aria-hidden="true" /><span>FRESHNESS</span><strong>{percent(coverage.freshness)}</strong><small>{coverage.unresolvedDuplicates} duplicate chưa xử lý</small></article>
        </section>

        <section className="coverage-section">
          <header><div><p className="machine-label">FRESHNESS SLA</p><h2>Nguồn quan trọng theo từng miền dữ liệu.</h2></div><span>{coverage.freshnessDomains.filter(item => item.passed).length}/{coverage.freshnessDomains.length} miền đạt</span></header>
          <div className="coverage-domain-grid">
            {coverage.freshnessDomains.map(item => (
              <article key={item.domain} className={item.passed ? "is-pass" : "is-blocked"}>
                <div>{item.passed ? <CheckCircle2 aria-hidden="true" size={17} /> : <AlertTriangle aria-hidden="true" size={17} />}<strong>{domainLabel(item.domain)}</strong></div>
                <span>{item.sourceCount} nguồn · {item.staleCount} stale</span>
                <b>{percent(item.freshness)}</b>
              </article>
            ))}
          </div>
        </section>

        <section className="coverage-section">
          <header><div><p className="machine-label">BRAND SCOPE · VIỆT NAM</p><h2>{includedBrands.length} hãng đang được theo dõi.</h2></div><span>{coverage.activeModelCount} model · {coverage.activeTrimCount} trim</span></header>
          <div className="coverage-table-wrap"><table className="coverage-table"><thead><tr><th>Hãng</th><th>Model candidate</th><th>Trim candidate</th><th>Published</th><th>Khoảng trống trim</th><th>Đầy đủ</th><th>Độ mới</th></tr></thead><tbody>{includedBrands.map(brand => <tr key={brand.brandId}><td><strong>{brand.brandName}</strong><small>{brand.reviewed ? `reviewed ${brand.reviewedAt ? new Date(brand.reviewedAt).toLocaleDateString("vi-VN") : ""}` : "chưa review"}</small></td><td>{brand.modelCandidates}</td><td>{brand.trimCandidates}</td><td>{brand.published}</td><td>{brand.trimInventoryGaps}</td><td>{percent(brand.completeness)}<small>{brand.missingCoreCount} core field thiếu</small></td><td>{percent(brand.freshness)}<small>{brand.stale} stale</small></td></tr>)}</tbody></table></div>
        </section>

        <section className="coverage-section coverage-gap-section">
          <header><div><p className="machine-label">DOCUMENTED GAPS</p><h2>Biết thiếu gì, nói rõ thiếu gì.</h2></div><span>{coverage.documentedBlockedCount} khoảng trống · {coverage.trimInventoryGapCount} inventory gap</span></header>
          <p>Trang hãng có thể xác nhận model nhưng chưa công bố đầy đủ danh sách trim. Những trường hợp đó được ghi lại với nguồn và lý do; chúng không bị loại khỏi phép đo và không được thay bằng dữ liệu suy đoán.</p>
          <details>
            <summary><FileWarning aria-hidden="true" size={17} /> Xem {coverage.candidateGaps.length} khoảng trống đã ghi nhận</summary>
            <div className="coverage-gap-list">{coverage.candidateGaps.map(gap => <article key={gap.candidateId}><div><strong>{gap.brandName} · {gap.candidateName}</strong><code>{gap.code}</code></div><p>{gap.reason}</p><small>{gap.candidateKind} · thấy gần nhất {new Date(gap.lastSeenAt).toLocaleDateString("vi-VN")}</small></article>)}</div>
          </details>
        </section>

        <section className="coverage-exclusions">
          <div><p className="machine-label">EXPLICIT EXCLUSIONS</p><h2>Ngoài scope hiện tại.</h2></div>
          <p>{excludedBrands.map(brand => brand.brandName).join(" · ")}</p>
          <small>Nhóm supercar/ultra-luxury không thuộc phạm vi V2.8. Porsche vẫn nằm trong scope premium bắt buộc.</small>
        </section>

        <footer className="coverage-proof">
          <span>Scope hash</span><code>{coverage.manifestHash ?? "không có"}</code><span>Coverage được tính trực tiếp từ catalog, source snapshot và trạng thái review hiện hành.</span>
        </footer>
      </main>
      <SiteFooter />
    </div>
  );
}
