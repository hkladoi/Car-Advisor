import Link from "next/link";
import { ArrowRight, BadgeCheck, Calculator, Database, GitCompareArrows, ShieldCheck } from "lucide-react";

import { CommandPalette } from "@/components/command-palette";
import { Button } from "@/components/ui/button";

const trustRows = [
  ["Đơn vị dữ liệu", "Trim + model year", "Không gộp nhầm các phiên bản"],
  ["Giá và luật", "Effective-dated", "Tái tính được theo ngày"],
  ["Giá trị thiếu", "UNKNOWN", "Không biến thành “không có”"],
  ["Ưu đãi", "Cash tách quà", "Không thổi phồng giá tiền mặt"],
];

export default function HomePage() {
  return (
    <main>
      <header className="site-nav">
        <div className="site-nav__inner">
          <Link className="wordmark" href="/" aria-label="Vietnam Car Platform — trang chủ">
            <span aria-hidden="true">VCP</span>
            <strong>Vietnam Car Platform</strong>
          </Link>
          <CommandPalette />
          <nav className="site-nav__links" aria-label="Điều hướng chính">
            <Link href="/cars">Xe</Link>
            <Link href="/affordability">Chi phí</Link>
            <Link href="/compare">So sánh</Link>
          </nav>
        </div>
      </header>

      <section className="hero-shell">
        <div className="hero-copy">
          <p className="machine-label">CATALOG THEO TRIM · VIỆT NAM</p>
          <h1>Chọn đúng phiên bản xe.</h1>
          <p>
            Tra cứu xe mới theo từng trim, kiểm nguồn của giá và trang bị, rồi tính ra biển,
            năng lượng, sở hữu và tài chính từ cùng một bộ giả định.
          </p>
          <div className="hero-actions">
            <Button asChild size="lg">
              <Link href="/cars">Mở catalog <ArrowRight aria-hidden="true" size={18} /></Link>
            </Button>
            <Link className="text-link" href="/calculators/on-road">Tính giá ra biển <ArrowRight aria-hidden="true" size={16} /></Link>
          </div>
        </div>

        <div className="workbench" aria-label="Luồng tìm xe theo trim">
          <div className="workbench__head">
            <span>Tìm theo nhu cầu</span>
            <span className="status-chip"><span aria-hidden="true" /> Dữ liệu có nguồn</span>
          </div>
          <div className="workbench__query">
            <span className="workbench__prompt" aria-hidden="true">›</span>
            <span>Tìm hãng, model hoặc trim…</span>
          </div>
          <dl className="workbench__filters">
            <div><dt>Giá</dt><dd>MSRP · tiền mặt · ra biển</dd></div>
            <div><dt>Vận hành</dt><dd>ICE · HEV · PHEV · EREV · BEV</dd></div>
            <div><dt>Trang bị</dt><dd>ADAS · tiện nghi · màu</dd></div>
            <div><dt>Khả năng chi trả</dt><dd>Nuôi được · mua/vay được</dd></div>
          </dl>
          <div className="workbench__result">
            <BadgeCheck aria-hidden="true" size={20} />
            <span><strong>Kết quả có thể giải thích.</strong> Mỗi phép tính trả giả định, rule và nguồn đã áp dụng.</span>
          </div>
        </div>
      </section>

      <section className="trust-section">
        <header className="section-heading">
          <h2>Dữ liệu trước, giao diện sau.</h2>
          <p>Catalog không gọi crawler hay search engine trong request của người dùng. Dữ liệu đã publish nằm trong PostgreSQL; Redis chỉ tăng tốc.</p>
        </header>
        <div className="trust-grid">
          <div className="trust-table" role="table" aria-label="Nguyên tắc dữ liệu">
            {trustRows.map(([label, value, note]) => (
              <div className="trust-row" role="row" key={label}>
                <span role="cell">{label}</span><strong role="cell">{value}</strong><small role="cell">{note}</small>
              </div>
            ))}
          </div>
          <aside className="source-note">
            <ShieldCheck aria-hidden="true" size={25} />
            <h3>Source-first</h3>
            <p>Giá, luật, tariff và field quan trọng phải truy về source snapshot hoặc manual override có lý do.</p>
          </aside>
        </div>
      </section>

      <section className="calculation-band">
        <div className="calculation-band__copy">
          <p className="machine-label">MỘT PROFILE · MỘT NGÀY · MỘT KHU VỰC</p>
          <h2>Từ catalog tới dòng tiền.</h2>
          <p>Frontend gửi input. Các engine authoritative ở API trả breakdown, cảnh báo và provenance.</p>
        </div>
        <ol className="engine-flow">
          <li><Database aria-hidden="true" /><span>Catalog</span><small>trim + source</small></li>
          <li><Calculator aria-hidden="true" /><span>On-road</span><small>rule theo ngày</small></li>
          <li><GitCompareArrows aria-hidden="true" /><span>Ownership</span><small>current + normalized</small></li>
          <li><BadgeCheck aria-hidden="true" /><span>Financing</span><small>cash + loan</small></li>
        </ol>
      </section>

      <section className="next-step">
        <h2>Bắt đầu bằng một chiếc xe cụ thể.</h2>
        <p>Chọn trim, khu vực và hồ sơ chi phí. Các giả định luôn hiển thị và có thể sửa.</p>
        <Button asChild variant="outline" size="lg">
          <Link href="/cars">Duyệt catalog <ArrowRight aria-hidden="true" size={18} /></Link>
        </Button>
      </section>

      <footer className="site-footer">
        <span>Vietnam Car Platform · dữ liệu xe mới theo trim</span>
        <span>V1 đang được triển khai theo gate</span>
      </footer>
    </main>
  );
}

