import { afterEach, describe, expect, it, vi } from "vitest";

import { getCoverage } from "./coverage-api";

describe("getCoverage", () => {
  afterEach(() => vi.restoreAllMocks());

  it("loads the public full-market gate without caching", async () => {
    const payload = { scopeVersion: "v2.8", fullMarketGatePassed: true, brands: [] };
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue({ ok: true, json: async () => payload } as Response);

    await expect(getCoverage()).resolves.toEqual(payload);
    expect(fetchMock).toHaveBeenCalledWith("http://localhost:8080/api/v1/coverage", expect.objectContaining({ cache: "no-store" }));
  });

  it("rejects a failed coverage response", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({ ok: false, status: 503 } as Response);
    await expect(getCoverage()).rejects.toThrow("Coverage API returned 503");
  });
});
