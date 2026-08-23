import type { Metadata } from "next";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { AffordabilityWorkbench } from "@/features/affordability/affordability-workbench";
import { defaultAffordabilityRequest, evaluateAffordability } from "@/lib/affordability-api";
import { getRegions } from "@/lib/registration-api";

export const metadata: Metadata = {
  title: "Chi phí sở hữu và lọc theo lương",
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

export default async function AffordabilityPage() {
  const initialRequest = defaultAffordabilityRequest(localToday());
  const [regions, outcome] = await Promise.all([getRegions(), evaluateAffordability(initialRequest)]);
  if (outcome.error) throw new Error(`${outcome.error.code}: ${outcome.error.message}`);

  return (
    <div className="calculator-shell affordability-shell">
      <SiteHeader />
      <main className="affordability-main">
        <header className="affordability-intro">
          <div><p className="machine-label">SALARY FILTER · OWNERSHIP ONLY</p><h1>Lương này, nuôi xe nào mà vẫn giữ được khoảng thở?</h1><p>So current, normalized và worst-reasonable trên cùng hồ sơ. Xe không pass phải nói rõ do thu nhập, phần còn lại, gửi xe hay năng lượng.</p></div>
          <div className="onroad-principle"><span aria-hidden="true" className="affordability-mark">₫</span><span>“Nuôi được” độc lập với “mua/vay được”. Khoản trả góp không nằm trong phép tính V1.7.</span></div>
        </header>
        <AffordabilityWorkbench regions={regions.data} initialRequest={initialRequest} initialResult={outcome.data} />
      </main>
      <SiteFooter />
    </div>
  );
}
