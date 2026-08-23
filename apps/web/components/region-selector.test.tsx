import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";

import { RegionSelector } from "./region-selector";

describe("RegionSelector", () => {
  beforeEach(() => window.localStorage.clear());

  it("persists the region and restores it in another selector", () => {
    const first = render(<RegionSelector />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "VN-79" } });
    expect(window.localStorage.getItem("vcp:region")).toBe("VN-79");
    first.unmount();
    render(<RegionSelector />);
    expect(screen.getByRole("combobox")).toHaveValue("VN-79");
  });
});
