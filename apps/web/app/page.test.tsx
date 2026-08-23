import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

import HomePage from "./page";

describe("HomePage", () => {
  it("states the trim-first and source-first product contract", () => {
    render(<HomePage />);
    expect(screen.getByRole("heading", { name: "Chọn đúng phiên bản xe." })).toBeInTheDocument();
    expect(screen.getByText("Source-first")).toBeInTheDocument();
    expect(screen.getByText(/UNKNOWN/)).toBeInTheDocument();
  });
});

