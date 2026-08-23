"use client";

import { MapPin } from "lucide-react";
import { useSyncExternalStore } from "react";

const regions = [
  { value: "VN-01", label: "Hà Nội · Khu vực I" },
  { value: "VN-79", label: "TP.HCM · Khu vực I" },
  { value: "VN-48", label: "Đà Nẵng · Khu vực II" },
];

export function RegionSelector() {
  const region = useSyncExternalStore(
    (onStoreChange) => {
      window.addEventListener("storage", onStoreChange);
      window.addEventListener("vcp-region-change", onStoreChange);
      return () => {
        window.removeEventListener("storage", onStoreChange);
        window.removeEventListener("vcp-region-change", onStoreChange);
      };
    },
    () => {
      const stored = window.localStorage.getItem("vcp:region");
      return stored && regions.some((item) => item.value === stored) ? stored : "VN-01";
    },
    () => "VN-01",
  );

  return (
    <label className="region-control">
      <MapPin aria-hidden="true" size={16} />
      <span className="sr-only">Khu vực tính giá ra biển</span>
      <select
        value={region}
        onChange={(event) => {
          window.localStorage.setItem("vcp:region", event.target.value);
          window.dispatchEvent(new Event("vcp-region-change"));
        }}
      >
        {regions.map((item) => <option value={item.value} key={item.value}>{item.label}</option>)}
      </select>
    </label>
  );
}
