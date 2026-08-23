import { ChevronLeft, ChevronRight, ListFilter, SearchX } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";

import { CatalogFilters } from "@/components/catalog-filters";
import { RegionSelector } from "@/components/region-selector";
import { SiteFooter, SiteHeader } from "@/components/site-header";
import { VehicleCard } from "@/components/vehicle-card";
import { catalogQuery, getBrands, getCars, type CatalogSearchParams } from "@/lib/catalog-api";

export const metadata: Metadata = {
  title: "Catalog xe mới theo phiên bản",
  description: "Tìm và lọc các phiên bản xe mới tại Việt Nam với giá, thông số, trạng thái dữ liệu và nguồn công khai.",
};

function pageLink(params: CatalogSearchParams, page: number): string {
  return `/cars?${catalogQuery({ ...params, Page: String(page) })}`;
}

export default async function CarsPage({ searchParams }: { searchParams: Promise<CatalogSearchParams> }) {
  const params = await searchParams;
  const [result, brands] = await Promise.all([getCars(params), getBrands()]);
  const { pagination } = result;

  return (
    <div className="catalog-shell">
      <SiteHeader />
      <main className="catalog-main">
        <header className="catalog-intro">
          <div>
            <p className="machine-label">CATALOG · ĐƠN VỊ TRIM + MODEL YEAR</p>
            <h1>Dữ liệu xe mới tại Việt Nam.</h1>
            <p>Giá, thông số và trang bị chỉ hiển thị theo trạng thái đã được nguồn xác nhận.</p>
          </div>
          <RegionSelector />
        </header>

        <details className="filter-drawer">
          <summary><ListFilter aria-hidden="true" size={18} /> Lọc {pagination.totalItems} phiên bản</summary>
          <CatalogFilters params={params} brands={brands.data} facets={result.facets} idPrefix="mobile" />
        </details>

        <div className="catalog-layout">
          <aside className="catalog-sidebar" aria-label="Bộ lọc catalog">
            <CatalogFilters params={params} brands={brands.data} facets={result.facets} idPrefix="desktop" />
          </aside>

          <section className="catalog-results" aria-labelledby="catalog-results-title">
            <header className="catalog-results__head">
              <div>
                <p className="machine-label">{pagination.totalItems} KẾT QUẢ</p>
                <h2 id="catalog-results-title">Phiên bản phù hợp</h2>
              </div>
              <p>Trang {pagination.page}/{Math.max(pagination.totalPages, 1)} · feature filter {result.featureFilterSemantics}</p>
            </header>

            {result.data.length > 0 ? (
              <div className="vehicle-grid">
                {result.data.map((car) => <VehicleCard car={car} key={car.trimId} />)}
              </div>
            ) : (
              <div className="catalog-empty">
                <SearchX aria-hidden="true" size={30} />
                <h2>Không có trim khớp bộ lọc.</h2>
                <p>Thử bỏ bớt điều kiện. Hệ thống không tự suy đoán dữ liệu còn thiếu để tạo kết quả.</p>
                <Link className="button-control button-outline" href="/cars">Xóa bộ lọc</Link>
              </div>
            )}

            {pagination.totalPages > 1 && (
              <nav className="pagination" aria-label="Phân trang catalog">
                {pagination.page > 1 ? <Link href={pageLink(params, pagination.page - 1)}><ChevronLeft aria-hidden="true" size={17} /> Trang trước</Link> : <span />}
                <span>{pagination.page} / {pagination.totalPages}</span>
                {pagination.page < pagination.totalPages ? <Link href={pageLink(params, pagination.page + 1)}>Trang sau <ChevronRight aria-hidden="true" size={17} /></Link> : <span />}
              </nav>
            )}
          </section>
        </div>
      </main>
      <SiteFooter />
    </div>
  );
}
