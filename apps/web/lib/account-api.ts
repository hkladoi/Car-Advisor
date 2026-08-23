import "server-only";

import { cookies } from "next/headers";

export const ACCOUNT_COOKIE = "vcp_account_session";

export type AccountSession = {
  userId: string;
  email: string;
  displayName: string;
  expiresAt: string;
  consentedAt: string;
  privacyPolicyVersion: string;
};

export type AccountProfile = {
  id: string;
  name: string;
  regionCode: string;
  netMonthlyIncome: number;
  rentHousing: number;
  essentialExpenses: number;
  otherFixedDebt: number;
  savingsTarget: number;
  monthlyKilometres: number;
  parkingMonthly: number;
  householdBaseKwh: number;
  policy: string;
  updatedAt: string;
};

export type SavedComparison = {
  id: string;
  name: string;
  trimIds: string[];
  regionCode: string;
  profilePreset: string;
  financingPreset: string;
  createdAt: string;
  updatedAt: string;
};

export type WatchlistItem = {
  id: string;
  trimId: string;
  brandName: string;
  modelName: string;
  trimName: string;
  regionCode: string;
  currentPrice: number | null;
  targetPrice: number | null;
  priceAlerts: boolean;
  promotionAlerts: boolean;
  dealerOfferAlerts: boolean;
  updatedAt: string;
};

export type AccountAlert = {
  id: string;
  kind: "Price" | "Promotion" | "DealerOffer";
  trimId: string;
  vehicle: string;
  title: string;
  message: string;
  amount: number | null;
  currency: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  source: {
    sourceFactId: string | null;
    name: string | null;
    url: string | null;
    authority: string | null;
    verifiedAt: string | null;
  };
};

const apiBase = () => process.env.API_INTERNAL_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export async function accountFetch<T>(path: string): Promise<T | null> {
  const token = (await cookies()).get(ACCOUNT_COOKIE)?.value;
  if (!token) return null;
  const response = await fetch(`${apiBase()}/api/v1/accounts/${path}`, {
    cache: "no-store",
    signal: AbortSignal.timeout(30_000),
    headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
  });
  if (response.status === 401 || response.status === 403 || response.status === 204) return null;
  if (!response.ok) throw new Error(`Account API ${path} returned ${response.status}`);
  return response.json() as Promise<T>;
}
