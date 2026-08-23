import Link from "next/link";
import { ArrowLeft, CircleDotDashed } from "lucide-react";

import { CommandPalette } from "@/components/command-palette";

type MilestonePageProps = {
  eyebrow: string;
  title: string;
  description: string;
  gate: string;
};

export function MilestonePage({ eyebrow, title, description, gate }: MilestonePageProps) {
  return (
    <main className="milestone-shell">
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

      <section className="milestone-page">
        <div>
          <p className="machine-label">{eyebrow}</p>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
        <aside className="milestone-status" aria-label="Trạng thái triển khai">
          <CircleDotDashed aria-hidden="true" size={24} />
          <div>
            <strong>Chưa mở dữ liệu công khai</strong>
            <p>{gate} đang được triển khai và chỉ mở khi gate dữ liệu, API và giao diện đã pass.</p>
          </div>
        </aside>
        <Link className="text-link" href="/">
          <ArrowLeft aria-hidden="true" size={16} /> Về trang chủ
        </Link>
      </section>

      <footer className="site-footer">
        <span>Vietnam Car Platform · dữ liệu xe mới theo trim</span>
        <span>Không hiển thị dữ liệu mẫu</span>
      </footer>
    </main>
  );
}
