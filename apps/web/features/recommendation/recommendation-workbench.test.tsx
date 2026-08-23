import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { defaultRecommendationRequest, type RecommendationResponse } from "@/lib/recommendation-api";
import { RecommendationWorkbench } from "./recommendation-workbench";

const result: RecommendationResponse = {
  methodology: {
    version: "v3.1-deterministic-1",
    evaluationOrder: ["hard_filters", "component_completeness", "source_trust", "peer_normalization", "weighted_ranking", "explanation"],
    completenessThreshold: 0.8,
    normalizedWeights: {},
    overallFormula: "overall = weighted components",
    pricePerformanceFormula: "P/P = value + performance",
    assumptions: ["No missing value becomes zero."],
  },
  considered: 49,
  hardFilterMatched: 1,
  ranked: [],
  dataWithheld: [{
    vehicle: { trimId: "10000000-0000-0000-0000-000000000001", brandName: "VinFast", modelName: "VF 8", trimName: "Eco", modelYear: 2026, bodyType: "Suv", segment: "D", powertrain: "Bev", currentPrice: 1_019_000_000, currency: "VND" },
    rank: null,
    completeness: 0.5714,
    completenessPassed: false,
    trustPassed: true,
    overallScore: null,
    pricePerformanceScore: null,
    reasons: ["COMPLETENESS_BELOW_80_PERCENT", "MISSING_SAFETY_ADAS"],
    components: [
      { code: "value", label: "Giá / giá trị", weight: 0.2, rawMetrics: [{ code: "purchase_price", label: "Giá mua", value: 1_019_000_000, unit: "VND", direction: "LowerIsBetter" }], score: null, includedInOverall: false, trusted: true, sources: [], explanation: "Passed data gate." },
      ...["running_cost", "space", "safety_adas", "comfort", "performance", "technology"].map((code) => ({ code, label: code, weight: 0.1, rawMetrics: [], score: null, includedInOverall: false, trusted: false, sources: [], explanation: "Missing reviewed facts." })),
    ],
  }],
  hardFilterExcluded: [],
  evaluatedAt: "2026-08-23T08:00:00Z",
};

describe("RecommendationWorkbench", () => {
  it("withholds ranking and price/performance when completeness is below the gate", () => {
    render(<RecommendationWorkbench initialRequest={defaultRecommendationRequest()} initialResult={result} initialError={null} />);

    expect(screen.getByText("Chưa có trim đủ bằng chứng để xếp hạng.")).toBeInTheDocument();
    expect(screen.getByText("P/P chưa phát hành")).toBeInTheDocument();
    expect(screen.getByText("COMPLETENESS_BELOW_80_PERCENT")).toBeInTheDocument();
    expect(screen.queryByText("P/P 0")).not.toBeInTheDocument();
  });
});
