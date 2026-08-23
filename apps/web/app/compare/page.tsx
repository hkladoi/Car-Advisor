import type { Metadata } from "next";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { CompareWorkbench } from "@/features/compare/compare-workbench";
import { buildCompareRequest, calculateCompare, type CompareFinancingPreset, type CompareProfilePreset } from "@/lib/compare-api";
import { getCars } from "@/lib/catalog-api";
import { getRegions } from "@/lib/registration-api";

export const metadata: Metadata = {
  title: "So sánh phiên bản",
  robots: { index: false, follow: false },
};

const VF6 = "8b31de05-bd4c-5b70-9efd-47879f5e609c";
const SEALION6 = "13bb54aa-f730-5a7a-a12d-9050aa0e58fd";
const profiles = new Set<CompareProfilePreset>(["lean-city", "city-balanced", "high-mileage-public"]);
const financing = new Set<CompareFinancingPreset>(["cash-preset", "standard-loan", "short-reducing"]);

type SearchParams = Record<string, string | string[] | undefined>;

function one(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function localToday() {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Ho_Chi_Minh", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
}

export default async function ComparePage({ searchParams }: { searchParams: Promise<SearchParams> }) {
  const params = await searchParams;
  const explicitTrims = one(params.trims);
  const selected = (explicitTrims === undefined ? [VF6, SEALION6] : explicitTrims.split(","))
    .filter((value, index, values) => /^[0-9a-f-]{36}$/i.test(value) && values.indexOf(value) === index)
    .slice(0, 4);
  const profileValue = one(params.profile) as CompareProfilePreset | undefined;
  const financingValue = one(params.financing) as CompareFinancingPreset | undefined;
  const profile = profileValue && profiles.has(profileValue) ? profileValue : "city-balanced";
  const financingPreset = financingValue && financing.has(financingValue) ? financingValue : "standard-loan";
  const dateValue = one(params.date);
  const date = dateValue && /^\d{4}-\d{2}-\d{2}$/.test(dateValue) ? dateValue : localToday();
  const [cars, regions] = await Promise.all([getCars({ pageSize: "100" }), getRegions()]);
  const province = regions.data.some((region) => region.code === one(params.region)) ? one(params.region)! : "VN-01";
  const validSelected = selected.filter((id) => cars.data.some((car) => car.trimId === id));
  const request = buildCompareRequest(date, validSelected, province, profile, financingPreset);
  const outcome = validSelected.length >= 2 ? await calculateCompare(request) : null;

  return (
    <div className="compare-shell">
      <SiteHeader />
      <main className="compare-main">
        <header className="compare-intro">
          <div><p className="machine-label">COMPARE · 2–4 TRIMS · CANONICAL UNITS</p><h1>Đặt các phiên bản lên cùng một thước đo.</h1><p>Một khu vực, một ngày, một profile sở hữu và một kịch bản tài chính được áp dụng cho mọi xe. UNKNOWN không bao giờ bị đổi thành “không có”.</p></div>
          <div className="onroad-principle"><span aria-hidden="true" className="affordability-mark">≠</span><span>URL chỉ chứa trim, region và tên preset không nhạy cảm — không chứa lương, tiền mặt hay nợ thật.</span></div>
        </header>
        <CompareWorkbench
          cars={cars.data}
          regions={regions.data}
          selectedTrimIds={validSelected}
          profile={profile}
          financing={financingPreset}
          provinceCode={province}
          calculationDate={date}
          initialDifferencesOnly={one(params.differences) === "1"}
          result={outcome?.data ?? null}
          error={outcome?.error ?? null}
        />
      </main>
      <SiteFooter />
    </div>
  );
}
