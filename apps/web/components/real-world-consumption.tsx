import { ExternalLink, Gauge, UsersRound } from "lucide-react";

import { formatNumber, type RealWorldConsumptionReference } from "@/lib/catalog-api";

export function RealWorldConsumptionPanel({
  officialTrimFuelLitresPer100Km,
  references,
}: {
  officialTrimFuelLitresPer100Km: number | null;
  references: RealWorldConsumptionReference[];
}) {
  return (
    <section className="detail-section real-world-section" aria-labelledby="real-world-title">
      <header>
        <p className="machine-label">OFFICIAL TRIM ≠ REAL-WORLD COHORT</p>
        <h2 id="real-world-title">Mức tiêu thụ: hai lớp dữ liệu tách biệt</h2>
      </header>

      <div className="real-world-trim-fact">
        <Gauge aria-hidden="true" size={22} />
        <div>
          <span>Thông số công bố của trim Việt Nam</span>
          <strong>{officialTrimFuelLitresPer100Km === null
            ? "Chưa có dữ liệu chính thức"
            : formatNumber(officialTrimFuelLitresPer100Km, "l/100 km")}</strong>
        </div>
      </div>

      <p className="real-world-warning">
        Dữ liệu bên dưới là cohort xe đăng ký tại EU/EEA theo hãng sản xuất × loại nhiên liệu,
        không phải phép đo của trim này tại Việt Nam và không được dùng để thay thế thông số chính thức ở trên.
      </p>

      {references.length === 0 ? (
        <p className="empty-fact">Chưa có cohort đủ tin cậy và được ánh xạ chính xác cho hãng này.</p>
      ) : (
        <div className="real-world-grid">
          {references.map((reference) => (
            <article key={reference.id}>
              <header>
                <div>
                  <p>{reference.manufacturer} · {reference.fuelType}</p>
                  <h3>Xe đăng ký năm {reference.vehicleRegistrationYear}</h3>
                </div>
                <span>COHORT — KHÔNG PHẢI TRIM</span>
              </header>
              <dl>
                <div>
                  <dt>Thực tế OBFCM có trọng số</dt>
                  <dd>{formatNumber(reference.realWorldFuelWeightedLitresPer100Km, "l/100 km")}</dd>
                </div>
                <div>
                  <dt>WLTP của cùng cohort</dt>
                  <dd>{formatNumber(reference.officialWltpFuelWeightedLitresPer100Km, "l/100 km")}</dd>
                </div>
                <div>
                  <dt>Chênh lệch cohort</dt>
                  <dd>{formatNumber(reference.fuelWeightedPercentageGap, "%")}</dd>
                </div>
              </dl>
              <p className="real-world-sample"><UsersRound aria-hidden="true" size={15} /> Cỡ mẫu {new Intl.NumberFormat("vi-VN").format(reference.sampleSize)} xe · {reference.geography}</p>
              <div className="real-world-links">
                <a href={reference.methodologyUrl} target="_blank" rel="noreferrer">Phương pháp thống kê <ExternalLink aria-hidden="true" size={12} /></a>
                <a href={reference.source.url} target="_blank" rel="noreferrer">Dữ liệu gốc EEA <ExternalLink aria-hidden="true" size={12} /></a>
              </div>
              <small>{reference.attribution}</small>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
