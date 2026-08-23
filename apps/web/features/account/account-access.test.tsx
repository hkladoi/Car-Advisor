import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { AccountAccess } from "./account-access";

vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

afterEach(() => cleanup());

describe("AccountAccess", () => {
  it("keeps registration opt-in and exposes privacy controls before persistence", () => {
    render(<AccountAccess />);
    expect(screen.getByRole("heading", { name: "Mở không gian đã lưu." })).toBeDefined();

    fireEvent.click(screen.getByRole("tab", { name: "Tạo tài khoản" }));

    expect(screen.getByRole("heading", { name: "Tạo không gian riêng." })).toBeDefined();
    expect(screen.getByRole("checkbox")).toBeRequired();
    expect(screen.getByText(/xuất hoặc xóa toàn bộ dữ liệu/i)).toBeDefined();
  });
});
