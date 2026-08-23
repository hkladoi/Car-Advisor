"use client";

import { BellPlus, Check } from "lucide-react";
import { useState } from "react";

export function WatchlistButton({ trimId }: { trimId: string }) {
  const [state, setState] = useState<"idle" | "pending" | "saved" | "auth" | "error">("idle");

  async function save() {
    setState("pending");
    const regionCode = window.localStorage.getItem("vcp:region") ?? "VN-01";
    const response = await fetch("/api/account/watchlist", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ trimId, regionCode, targetPrice: null, priceAlerts: true, promotionAlerts: true, dealerOfferAlerts: true }),
    });
    if (response.ok) setState("saved");
    else if (response.status === 401) setState("auth");
    else setState("error");
  }

  return (
    <div className="watchlist-action">
      <button className="detail-compare-link" type="button" onClick={save} disabled={state === "pending" || state === "saved"}>
        {state === "saved" ? <Check aria-hidden="true" size={15} /> : <BellPlus aria-hidden="true" size={15} />}
        {state === "pending" ? "Đang lưu…" : state === "saved" ? "Đã theo dõi" : "Theo dõi giá"}
      </button>
      {state === "auth" && <a href="/account">Đăng nhập để lưu</a>}
      {state === "error" && <span role="alert">Chưa thể lưu.</span>}
    </div>
  );
}
