"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Calculator, CarFront, GitCompareArrows, Landmark, Search, WalletCards } from "lucide-react";

import { cn } from "@/lib/utils";

const destinations = [
  { label: "Mở catalog xe", hint: "Lọc theo trim và nguồn", href: "/cars", icon: CarFront },
  { label: "Tính giá ra biển", hint: "Theo khu vực và ngày", href: "/calculators/on-road", icon: Calculator },
  { label: "Kiểm tra chi phí nuôi", hint: "Current và normalized", href: "/affordability", icon: WalletCards },
  { label: "Tính mua và vay", hint: "Tiền trước, khoản vay, cam kết tháng", href: "/financing", icon: Landmark },
  { label: "So sánh phiên bản", hint: "Từ 2 đến 4 trim", href: "/compare", icon: GitCompareArrows },
];

export function CommandPalette() {
  const router = useRouter();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);

  const filtered = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase("vi");
    if (!normalized) return destinations;
    return destinations.filter((item) => `${item.label} ${item.hint}`.toLocaleLowerCase("vi").includes(normalized));
  }, [query]);

  function open() {
    dialogRef.current?.showModal();
    requestAnimationFrame(() => inputRef.current?.focus());
  }

  function close() {
    dialogRef.current?.close();
    setQuery("");
    setActiveIndex(0);
  }

  function select(index: number) {
    const item = filtered[index];
    if (!item) return;
    close();
    router.push(item.href);
  }

  useEffect(() => {
    function handleShortcut(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLocaleLowerCase() === "k") {
        event.preventDefault();
        if (dialogRef.current?.open) close();
        else open();
      }
    }
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  });

  return (
    <>
      <button className="search-pill" type="button" onClick={open} aria-label="Mở tìm kiếm nhanh">
        <Search aria-hidden="true" size={17} strokeWidth={1.8} />
        <span className="search-pill__label">Tìm xe hoặc công cụ</span>
        <span className="search-pill__keys" aria-hidden="true"><kbd>Ctrl</kbd><kbd>K</kbd></span>
      </button>

      <dialog
        ref={dialogRef}
        className="command-dialog"
        aria-label="Tìm kiếm nhanh"
        onClick={(event) => {
          if (event.target === dialogRef.current) close();
        }}
        onClose={() => {
          setQuery("");
          setActiveIndex(0);
        }}
      >
        <div className="command-panel">
          <div className="command-field">
            <Search aria-hidden="true" size={19} />
            <label className="sr-only" htmlFor="command-query">Tìm route hoặc công cụ</label>
            <input
              id="command-query"
              ref={inputRef}
              value={query}
              onChange={(event) => {
                setQuery(event.target.value);
                setActiveIndex(0);
              }}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown") {
                  event.preventDefault();
                  setActiveIndex((index) => Math.min(index + 1, filtered.length - 1));
                } else if (event.key === "ArrowUp") {
                  event.preventDefault();
                  setActiveIndex((index) => Math.max(index - 1, 0));
                } else if (event.key === "Enter") {
                  event.preventDefault();
                  select(activeIndex);
                }
              }}
              placeholder="Ví dụ: giá ra biển"
              autoComplete="off"
            />
            <kbd className="command-field__escape">Esc</kbd>
          </div>

          <p className="command-group">Điểm đến</p>
          <div className="command-results" role="listbox" aria-label="Kết quả tìm kiếm">
            {filtered.length === 0 ? (
              <div className="command-empty" role="status">
                <strong>Không có điểm đến phù hợp.</strong>
                <span>Thử “catalog”, “ra biển” hoặc “so sánh”.</span>
              </div>
            ) : (
              filtered.map((item, index) => {
                const Icon = item.icon;
                return (
                  <button
                    className={cn("command-item", index === activeIndex && "is-active")}
                    key={item.href}
                    type="button"
                    role="option"
                    aria-selected={index === activeIndex}
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => select(index)}
                  >
                    <Icon aria-hidden="true" size={20} strokeWidth={1.7} />
                    <span><strong>{item.label}</strong><small>{item.hint}</small></span>
                  </button>
                );
              })
            )}
          </div>

          <div className="command-footer" aria-hidden="true">
            <span><kbd>↑</kbd><kbd>↓</kbd> di chuyển</span>
            <span><kbd>Enter</kbd> mở</span>
            <span><kbd>Esc</kbd> đóng</span>
          </div>
        </div>
      </dialog>
    </>
  );
}
