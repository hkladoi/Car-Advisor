import { describe, expect, it } from "vitest";

import { buildPriceChartRows, formatEnergyRateUnit, type PriceTimelineEvent } from "./history-api";

const event = (series: string, valueKind: string, amount: number, at: string): PriceTimelineEvent => ({
  id: crypto.randomUUID(), series, valueKind, amount, currency: "VND", status: "Official", scope: "VN",
  label: series, effectiveFrom: at, effectiveTo: null, isCurrent: false, isStale: false,
  provenance: "SourceFact", source: null,
});

describe("history chart policy", () => {
  it("plots observed cash prices but never cash benefits", () => {
    const rows = buildPriceChartRows([
      event("Msrp", "CashPrice", 700_000_000, "2026-01-01T00:00:00Z"),
      event("DealerCashOffer", "CashBenefit", 100_000_000, "2026-02-01T00:00:00Z"),
      event("ManufacturerPromotionPrice", "CashPrice", 680_000_000, "2026-03-01T00:00:00Z"),
    ]);

    expect(rows).toHaveLength(2);
    expect(rows[0].Msrp).toBe(700_000_000);
    expect(rows[1].ManufacturerPromotionPrice).toBe(680_000_000);
    expect(rows.some((row) => "DealerCashOffer" in row)).toBe(false);
  });

  it("does not duplicate currency when the canonical unit already contains it", () => {
    expect(formatEnergyRateUnit({ currency: "VND", unit: "VND/litre" })).toBe("VND/litre");
    expect(formatEnergyRateUnit({ currency: "VND", unit: "kWh" })).toBe("VND/kWh");
  });
});
