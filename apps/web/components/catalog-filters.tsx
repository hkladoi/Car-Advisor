import { SlidersHorizontal } from "lucide-react";
import Link from "next/link";

import type { BrandsResponse, CatalogFacets, CatalogSearchParams } from "@/lib/catalog-api";

const powertrainLabels: Record<string, string> = {
  ICE: "Xăng / dầu (ICE)",
  HEV: "Hybrid (HEV)",
  PHEV: "Plug-in hybrid (PHEV)",
  EREV: "Điện mở rộng tầm (EREV)",
  BEV: "Thuần điện (BEV)",
};

const bodyLabels: Record<string, string> = {
  Sedan: "Sedan",
  SUV: "SUV",
  Crossover: "Crossover",
  MPV: "MPV",
  Hatchback: "Hatchback",
  Pickup: "Bán tải",
};

function currentValue(params: CatalogSearchParams, key: string): string {
  const value = params[key];
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

export function CatalogFilters({
  params,
  brands,
  facets,
  idPrefix,
}: {
  params: CatalogSearchParams;
  brands: BrandsResponse["data"];
  facets: CatalogFacets;
  idPrefix: string;
}) {
  const fieldId = (name: string) => `${idPrefix}-${name}`;

  return (
    <form className="catalog-filters" action="/cars" method="get">
      <div className="filter-heading">
        <SlidersHorizontal aria-hidden="true" size={18} />
        <strong>Bộ lọc</strong>
      </div>

      <label htmlFor={fieldId("q")}>Tìm hãng, model hoặc phiên bản</label>
      <input id={fieldId("q")} name="q" type="search" defaultValue={currentValue(params, "q")} placeholder="Ví dụ: EX5, VF 6…" />

      <label htmlFor={fieldId("brand")}>Hãng</label>
      <select id={fieldId("brand")} name="Brand" defaultValue={currentValue(params, "Brand") || currentValue(params, "brand")}>
        <option value="">Tất cả hãng</option>
        {brands.map((brand) => <option value={brand.slug} key={brand.id}>{brand.name} ({brand.currentTrimCount})</option>)}
      </select>

      <label htmlFor={fieldId("powertrain")}>Hệ truyền động</label>
      <select id={fieldId("powertrain")} name="Powertrain" defaultValue={currentValue(params, "Powertrain") || currentValue(params, "powertrain")}>
        <option value="">Tất cả hệ truyền động</option>
        {facets.powertrains.map((item) => <option value={item.value} key={item.value}>{powertrainLabels[item.value.toUpperCase()] ?? item.value} ({item.count})</option>)}
      </select>

      <label htmlFor={fieldId("body")}>Kiểu thân xe</label>
      <select id={fieldId("body")} name="Body" defaultValue={currentValue(params, "Body") || currentValue(params, "body")}>
        <option value="">Tất cả kiểu xe</option>
        {facets.bodyTypes.map((item) => <option value={item.value} key={item.value}>{bodyLabels[item.value[0]?.toUpperCase() + item.value.slice(1).toLowerCase()] ?? item.value} ({item.count})</option>)}
      </select>

      <label htmlFor={fieldId("seats")}>Số chỗ</label>
      <select id={fieldId("seats")} name="Seats" defaultValue={currentValue(params, "Seats") || currentValue(params, "seats")}>
        <option value="">Tất cả</option>
        {facets.seats.map((item) => <option value={item.value} key={item.value}>{item.value} chỗ ({item.count})</option>)}
      </select>

      <fieldset>
        <legend>Khoảng giá công khai (VND)</legend>
        <div className="filter-range">
          <label htmlFor={fieldId("price-min")}>
            <span>Từ</span>
            <input id={fieldId("price-min")} name="CurrentPriceMin" type="number" min="0" step="10000000" placeholder="500000000" defaultValue={currentValue(params, "CurrentPriceMin")} />
          </label>
          <label htmlFor={fieldId("price-max")}>
            <span>Đến</span>
            <input id={fieldId("price-max")} name="CurrentPriceMax" type="number" min="0" step="10000000" placeholder="1000000000" defaultValue={currentValue(params, "CurrentPriceMax")} />
          </label>
        </div>
        <small>Nhập giá trị VND đầy đủ, ví dụ 500000000.</small>
      </fieldset>

      <label htmlFor={fieldId("features")}>Mã trang bị</label>
      <input id={fieldId("features")} name="Features" defaultValue={currentValue(params, "Features") || currentValue(params, "features")} placeholder="CAMERA_360,PANORAMIC_ROOF" />

      <label htmlFor={fieldId("feature-mode")}>Cách ghép trang bị</label>
      <select id={fieldId("feature-mode")} name="FeatureMode" defaultValue={currentValue(params, "FeatureMode") || "and"}>
        <option value="and">Phải có tất cả (AND)</option>
        <option value="or">Có ít nhất một (OR)</option>
      </select>

      <label htmlFor={fieldId("sort")}>Sắp xếp</label>
      <select id={fieldId("sort")} name="Sort" defaultValue={currentValue(params, "Sort") || currentValue(params, "sort") || "relevance"}>
        <option value="relevance">Liên quan nhất</option>
        <option value="price_asc">Giá tăng dần</option>
        <option value="price_desc">Giá giảm dần</option>
        <option value="name_asc">Tên A–Z</option>
        <option value="newest">Model year mới nhất</option>
      </select>

      <input type="hidden" name="PageSize" value={currentValue(params, "PageSize") || "24"} />
      <div className="filter-actions">
        <button className="button-control button-primary" type="submit">Áp dụng bộ lọc</button>
        <Link className="button-control button-outline" href="/cars">Xóa lọc</Link>
      </div>
    </form>
  );
}
