"use client";

import { AlertTriangle } from "lucide-react";

export default function CarsError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <main className="catalog-error">
      <AlertTriangle aria-hidden="true" size={30} />
      <h1>Catalog tạm thời chưa tải được.</h1>
      <p>Dữ liệu không được thay bằng bản giả. Hãy thử lại khi API sẵn sàng.</p>
      <button className="button-control button-primary" type="button" onClick={reset}>Thử lại</button>
    </main>
  );
}
