import type { Metadata } from "next";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { AccountAccess } from "@/features/account/account-access";
import { AccountDashboard } from "@/features/account/account-dashboard";
import {
  accountFetch,
  type AccountAlert,
  type AccountProfile,
  type AccountSession,
  type SavedComparison,
  type WatchlistItem,
} from "@/lib/account-api";
import { getRegions } from "@/lib/registration-api";

export const metadata: Metadata = {
  title: "Tài khoản và quyền riêng tư",
  robots: { index: false, follow: false },
};

export default async function AccountPage() {
  const session = await accountFetch<AccountSession>("me");
  if (!session) {
    return (
      <div className="account-shell">
        <SiteHeader />
        <main className="account-main account-main--access">
          <AccountAccess />
        </main>
        <SiteFooter />
      </div>
    );
  }
  const [profile, comparisons, watchlist, alerts, regions] = await Promise.all([
    accountFetch<AccountProfile>("profile"),
    accountFetch<SavedComparison[]>("comparisons"),
    accountFetch<WatchlistItem[]>("watchlist"),
    accountFetch<AccountAlert[]>("alerts"),
    getRegions(),
  ]);
  return (
    <div className="account-shell">
      <SiteHeader />
      <main className="account-main">
        <AccountDashboard
          session={session}
          profile={profile}
          comparisons={comparisons ?? []}
          watchlist={watchlist ?? []}
          alerts={alerts ?? []}
          regions={regions.data}
        />
      </main>
      <SiteFooter />
    </div>
  );
}
