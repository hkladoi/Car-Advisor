import { afterEach, describe, expect, it, vi } from "vitest";

import { geocodeAddress, getChargingStations } from "./charging-api";

describe("charging API boundary", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("queries only the cached station API with bounded filters", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ data: [], count: 0, dataset: { coverage: "ReferenceOnly" }, generatedAt: "2026-08-23T00:00:00Z" }),
    });
    vi.stubGlobal("fetch", fetchMock);

    const result = await getChargingStations({ minLatitude: 20.5, maxLatitude: 21.5, limit: 100 });

    expect(result.dataset.coverage).toBe("ReferenceOnly");
    expect(fetchMock).toHaveBeenCalledOnce();
    const url = String(fetchMock.mock.calls[0][0]);
    expect(url).toContain("/api/v1/charging/stations?");
    expect(url).toContain("minLatitude=20.5");
    expect(url).not.toContain("openchargemap.io");
  });

  it("keeps Goong absence as a degraded optional result", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({
      ok: false,
      json: async () => ({ code: "GOONG_NOT_CONFIGURED", message: "Optional geocoding is disabled." }),
    }));

    const result = await geocodeAddress("Hà Nội");

    expect(result.data).toBeNull();
    expect(result.error?.code).toBe("GOONG_NOT_CONFIGURED");
  });
});
