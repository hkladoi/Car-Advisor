import type { Metadata } from "next";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { FinancingWorkbench, type FinancingOfferOption } from "@/features/financing/financing-workbench";
import { getCar, getCars } from "@/lib/catalog-api";
import { calculateFinancing, defaultFinancingRequest } from "@/lib/financing-api";
import { getRegions } from "@/lib/registration-api";

export const metadata: Metadata = {
  title: "Khả năng mua và vay",
  robots: { index: false, follow: false },
};

function localToday() {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Ho_Chi_Minh",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export default async function FinancingPage() {
  const cars = await getCars({ pageSize: "100" });
  const preferred = cars.data.find((car) => car.brandSlug === "vinfast" && car.modelSlug === "vf-6") ?? cars.data[0];
  if (!preferred) throw new Error("Catalog has no published trim for the financing calculator.");
  const initialRequest = defaultFinancingRequest(localToday(), preferred.trimId);
  const [regions, outcome, details] = await Promise.all([
    getRegions(),
    calculateFinancing(initialRequest),
    Promise.all(cars.data.map((car) => getCar(car.trimId))),
  ]);
  if (outcome.error) throw new Error(`${outcome.error.code}: ${outcome.error.message}`);
  const offers: FinancingOfferOption[] = details.flatMap((detail) => detail?.dealerOffers.map((offer) => ({
    id: offer.id,
    trimId: detail.car.trimId,
    headline: offer.headline,
    dealerName: offer.dealerName,
    branchName: offer.branchName,
    provinceCode: offer.provinceCode,
    benefits: offer.benefits.map((benefit) => ({ type: benefit.type, amount: benefit.cashValue ?? benefit.statedValue, cashEquivalent: benefit.isCashEquivalent })),
    sourceName: offer.source?.name ?? null,
  })) ?? []);

  return (
    <div className="calculator-shell financing-shell">
      <SiteHeader />
      <main className="affordability-main">
        <header className="affordability-intro financing-intro">
          <div>
            <p className="machine-label">PURCHASE CASHFLOW · OWNERSHIP + FINANCING</p>
            <h1>Mua được, vay được và nuôi được là ba câu hỏi khác nhau.</h1>
            <p>Ghép giá ra biển có nguồn, tiền sẵn có, khoản vay và chi phí sở hữu chuẩn hóa — nhưng giữ riêng từng kết luận để không che mất rủi ro.</p>
          </div>
          <div className="onroad-principle"><span aria-hidden="true" className="affordability-mark">%</span><span>Lãi suất tự nhập là giả định. Chỉ gắn “đã kiểm chứng” khi có source fact chính thức.</span></div>
        </header>
        <FinancingWorkbench
          cars={cars.data}
          regions={regions.data}
          offers={offers}
          initialRequest={initialRequest}
          initialResult={outcome.data}
        />
      </main>
      <SiteFooter />
    </div>
  );
}
