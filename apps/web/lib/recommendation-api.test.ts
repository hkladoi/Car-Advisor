import { afterEach, describe, expect, it, vi } from "vitest";

import { defaultRecommendationRequest, evaluateRecommendation } from "./recommendation-api";

afterEach(() => vi.unstubAllGlobals());

describe("recommendation API client", () => {
  it("posts the seven configurable weights to the versioned endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ranked: [] }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }));
    vi.stubGlobal("fetch", fetchMock);
    const request = defaultRecommendationRequest();

    const outcome = await evaluateRecommendation(request);

    expect(outcome.error).toBeNull();
    expect(fetchMock).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls[0][0]).toBe("http://localhost:8080/api/v1/recommendations");
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(Object.keys(body.weights)).toEqual(["priceValue", "runningCost", "space", "safetyAdas", "comfort", "performance", "technology"]);
  });

  it("keeps upstream validation errors explicit", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: "RECOMMENDATION_WEIGHTS_INVALID", message: "Weights are invalid." }), {
      status: 400,
      headers: { "Content-Type": "application/json" },
    })));

    const outcome = await evaluateRecommendation(defaultRecommendationRequest());

    expect(outcome.data).toBeNull();
    expect(outcome.error).toEqual({ code: "RECOMMENDATION_WEIGHTS_INVALID", message: "Weights are invalid." });
  });
});
