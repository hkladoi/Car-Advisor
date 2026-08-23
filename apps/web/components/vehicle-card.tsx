/* eslint-disable @next/next/no-img-element */
import { ArrowRight, GitCompareArrows, ImageOff } from "lucide-react";
import Link from "next/link";

import { formatMoney, type CatalogCar } from "@/lib/catalog-api";

const powertrainLabels: Record<string, string> = {
  ICE: "Động cơ đốt trong",
  HEV: "Hybrid",
  PHEV: "Plug-in hybrid",
  EREV: "Điện mở rộng tầm",
  BEV: "Thuần điện",
};

function formatRange(minimum: number, maximum: number, currency: string): string {
  const format = new Intl.NumberFormat("vi-VN", { style: "currency", currency, maximumFractionDigits: 0 });
  return minimum === maximum ? format.format(minimum) : `${format.format(minimum)} – ${format.format(maximum)}`;
}

export function VehicleCard({ car }: { car: CatalogCar }) {
  const displayPrice = car.currentPrice ?? car.msrp;

  return (
    <article className="vehicle-card">
      <Link className="vehicle-card__media" href={`/cars/${car.trimId}`} aria-label={`Xem ${car.brandName} ${car.modelName} ${car.trimName}`}>
        {car.primaryImageUrl ? (
          <img src={car.primaryImageUrl} alt={`${car.brandName} ${car.modelName} ${car.trimName}`} loading="lazy" />
        ) : (
          <span className="vehicle-card__no-image"><ImageOff aria-hidden="true" size={26} /> Chưa có ảnh được cấp quyền</span>
        )}
      </Link>
      <div className="vehicle-card__body">
        <p className="vehicle-card__meta">{car.brandName} · MY{car.modelYear} · {powertrainLabels[car.powertrainType.toUpperCase()] ?? car.powertrainType}</p>
        <h2><Link href={`/cars/${car.trimId}`}>{car.modelName} <span>{car.trimName}</span></Link></h2>
        <dl className="vehicle-card__facts">
          <div>
            <dt>{car.currentPrice ? "Giá hiện hành" : car.msrp ? "MSRP" : "Giá"}</dt>
            <dd className={!displayPrice ? "data-state data-state--unknown" : ""}>{formatMoney(displayPrice)}</dd>
          </div>
          <div>
            <dt>Ra biển theo khu vực</dt>
            <dd className={!car.onRoadRange ? "data-state data-state--unknown" : ""}>
              {car.onRoadRange ? formatRange(car.onRoadRange.minimum, car.onRoadRange.maximum, car.onRoadRange.currency) : "Chưa được tính"}
            </dd>
          </div>
          <div>
            <dt>Chi phí tháng</dt>
            <dd className="data-state data-state--unknown">Chưa được tính</dd>
          </div>
        </dl>
        {car.featureCodes.length > 0 && (
          <ul className="feature-code-list" aria-label="Trang bị đã xác minh">
            {car.featureCodes.slice(0, 3).map((code) => <li key={code}>{code.replaceAll("_", " ")}</li>)}
          </ul>
        )}
        <div className="vehicle-card__actions">
          <Link className="vehicle-card__link" href={`/cars/${car.trimId}`}>Xem dữ liệu trim <ArrowRight aria-hidden="true" size={16} /></Link>
          <Link className="vehicle-card__compare" href={`/compare?trims=${car.trimId}`}><GitCompareArrows aria-hidden="true" size={15} /> So sánh</Link>
        </div>
      </div>
    </article>
  );
}
