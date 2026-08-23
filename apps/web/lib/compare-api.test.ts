import { describe, expect, it } from "vitest";

import { buildCompareRequest } from "@/lib/compare-api";

describe("compare presets", () => {
  it("applies one canonical profile and percentage loan to every selected trim", () => {
    const result = buildCompareRequest(
      "2026-08-22",
      ["trim-a", "trim-b"],
      "VN-01",
      "city-balanced",
      "standard-loan",
    );

    expect(result.trimIds).toEqual(["trim-a", "trim-b"]);
    expect(result.expenses.monthlyKilometres).toBe(1_000);
    expect(result.purchase.downPaymentPercent).toBe(0.2);
    expect(result.purchase.downPaymentAmount).toBeNull();
    expect(result.purchase.annualInterestRate).toBe(0.12);
    expect(result.purchase.selectedDealerOfferIds).toEqual([]);
  });

  it("keeps the share preset non-sensitive and cash financing not applicable", () => {
    const result = buildCompareRequest("2026-08-22", ["a", "b"], "VN-79", "lean-city", "cash-preset");

    expect(result.profilePreset).toBe("lean-city");
    expect(result.financingPreset).toBe("cash-preset");
    expect(result.purchase.purchaseMethod).toBe("Cash");
    expect(result.purchase.annualInterestRate).toBeNull();
    expect(result.purchase.termMonths).toBe(0);
  });
});
