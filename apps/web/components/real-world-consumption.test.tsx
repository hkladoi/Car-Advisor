import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { RealWorldConsumptionReference } from "@/lib/catalog-api";
import { RealWorldConsumptionPanel } from "./real-world-consumption";

const cohort: RealWorldConsumptionReference = {
  id: "10000000-0000-0000-0000-000000000001",
  vehicleRegistrationYear: 2023,
  manufacturer: "TOYOTA",
  fuelType: "PETROL/ELECTRIC",
  sampleSize: 1702,
  realWorldFuelWeightedLitresPer100Km: 4.62,
  officialWltpFuelWeightedLitresPer100Km: 0.97,
  fuelWeightedAbsoluteGapLitresPer100Km: 3.65,
  fuelWeightedPercentageGap: 377.97,
  realWorldCo2WeightedGramsPerKm: 105.28,
  officialWltpCo2WeightedGramsPerKm: 22.03,
  geography: "EU/EEA reporting Member States",
  aggregationScope: "ManufacturerFuelRegistrationYear",
  isTrimSpecific: false,
  methodologyUrl: "https://sdi.eea.europa.eu/methodology.pdf",
  attribution: "European Environment Agency (EEA)",
  source: {
    sourceId: "20000000-0000-0000-0000-000000000001",
    name: "EEA aggregate",
    url: "https://sdi.eea.europa.eu/data.csv",
    authority: "CompetentAuthority",
    contentType: "Csv",
    fetchedAt: "2026-08-24T00:00:00Z",
    contentHash: "abc",
    factStatus: "Official",
    confidence: "VerifiedOfficial",
  },
};

describe("RealWorldConsumptionPanel", () => {
  it("keeps the official trim figure separate from the real-world cohort", () => {
    render(<RealWorldConsumptionPanel officialTrimFuelLitresPer100Km={5.95} references={[cohort]} />);

    expect(screen.getByText("Thông số công bố của trim Việt Nam")).toBeInTheDocument();
    expect(screen.getByText(/COHORT — KHÔNG PHẢI TRIM/)).toBeInTheDocument();
    expect(screen.getByText(/không phải phép đo của trim này tại Việt Nam/)).toBeInTheDocument();
    expect(screen.getByText(/Cỡ mẫu 1.702 xe/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Dữ liệu gốc EEA/ })).toHaveAttribute("href", cohort.source.url);
  });

  it("shows a truthful empty state when no trusted cohort exists", () => {
    render(<RealWorldConsumptionPanel officialTrimFuelLitresPer100Km={null} references={[]} />);

    expect(screen.getByText("Chưa có dữ liệu chính thức")).toBeInTheDocument();
    expect(screen.getByText(/Chưa có cohort đủ tin cậy/)).toBeInTheDocument();
    expect(screen.queryByText(/0 l\/100 km/)).not.toBeInTheDocument();
  });
});
