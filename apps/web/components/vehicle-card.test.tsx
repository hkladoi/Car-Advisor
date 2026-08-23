import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { CatalogCar } from "@/lib/catalog-api";
import { VehicleCard } from "./vehicle-card";

const car: CatalogCar = {
  trimId: "a0a408cb-c0a4-5eac-bf10-7b6bca0f033f",
  brandName: "Geely",
  brandSlug: "geely",
  modelName: "EX5",
  modelSlug: "ex5",
  generationCode: "E245",
  modelYear: 2026,
  trimName: "Pro",
  trimSlug: "pro",
  marketStatus: "OnSale",
  bodyType: "Crossover",
  segment: "C",
  powertrainType: "BEV",
  msrp: null,
  currentPrice: null,
  onRoadRange: null,
  specifications: { seats: null, lengthMm: null, widthMm: null, heightMm: null, wheelbaseMm: null, officialRangeKm: null, usableBatteryKwh: null, fuelLitresPer100Km: null, electricKwhPer100Km: null },
  featureCodes: [],
  colorCodes: [],
  primaryImageUrl: null,
  dataUpdatedAt: "2026-08-22T10:00:00Z",
};

describe("VehicleCard", () => {
  it("keeps unknown values explicit and does not render them as false", () => {
    render(<VehicleCard car={car} />);
    expect(screen.getByText("Chưa có giá công khai")).toBeInTheDocument();
    expect(screen.getByText("Chưa có ảnh được cấp quyền")).toBeInTheDocument();
    expect(screen.getAllByText("Chưa được tính")).toHaveLength(2);
    expect(screen.queryByText("Không có")).not.toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("renders only the approved URL supplied by the API contract", () => {
    render(<VehicleCard car={{ ...car, primaryImageUrl: "https://official.example/ex5.jpg" }} />);
    expect(screen.getByRole("img", { name: "Geely EX5 Pro" })).toHaveAttribute("src", "https://official.example/ex5.jpg");
  });
});
