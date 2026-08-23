import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { WatchlistButton } from "./watchlist-button";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe("WatchlistButton", () => {
  it("sends the locally selected region without exposing profile data", async () => {
    window.localStorage.setItem("vcp:region", "VN-79");
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    vi.stubGlobal("fetch", fetchMock);
    render(<WatchlistButton trimId="8b31de05-bd4c-5b70-9efd-47879f5e609c" />);

    fireEvent.click(screen.getByRole("button", { name: "Theo dõi giá" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Đã theo dõi" })).toBeDefined());
    expect(fetchMock).toHaveBeenCalledWith("/api/account/watchlist", expect.objectContaining({
      method: "PUT",
      body: expect.stringContaining('"regionCode":"VN-79"'),
    }));
  });
});
