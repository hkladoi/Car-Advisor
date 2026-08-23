import Link from "next/link";

import { CommandPalette } from "@/components/command-palette";

export function SiteHeader() {
  return (
    <header className="site-nav">
      <div className="site-nav__inner">
        <Link className="wordmark" href="/" aria-label="Vietnam Car Platform — trang chủ">
          <span aria-hidden="true">VCP</span>
          <strong>Vietnam Car Platform</strong>
        </Link>
        <CommandPalette />
        <nav className="site-nav__links" aria-label="Điều hướng chính">
          <Link href="/cars">Xe</Link>
          <Link href="/calculators/on-road">Ra biển</Link>
          <Link href="/calculators/energy">Năng lượng</Link>
          <Link href="/charging">Trạm sạc</Link>
          <Link href="/energy/history">Lịch sử giá</Link>
          <Link href="/affordability">Nuôi xe</Link>
          <Link href="/financing">Mua/vay</Link>
          <Link href="/compare">So sánh</Link>
          <Link href="/recommend">Gợi ý</Link>
          <Link href="/coverage">Phạm vi dữ liệu</Link>
        </nav>
      </div>
    </header>
  );
}

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <span>Vietnam Car Platform · dữ liệu xe mới theo trim</span>
      <span>Giá và trang bị có provenance</span>
    </footer>
  );
}
