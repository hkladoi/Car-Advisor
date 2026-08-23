import type { Metadata } from "next";
import { AlertTriangle, BatteryCharging, LocateFixed, MapPinned, PlugZap } from "lucide-react";

import { SiteFooter, SiteHeader } from "@/components/site-header";
import { geocodeAddress, getChargingStations } from "@/lib/charging-api";

export const metadata: Metadata = {
  title: "Trạm sạc tham khảo",
  description: "Vị trí trạm sạc đã cache từ Open Charge Map, có độ tin cậy và tách biệt biểu giá provider.",
};

type Params = { address?: string; connectorType?: string; minimumPowerKw?: string };
type Bounds = { minLatitude: number; minLongitude: number; maxLatitude: number; maxLongitude: number };

const vietnamBounds: Bounds = { minLatitude: 7.5, minLongitude: 101.5, maxLatitude: 24, maxLongitude: 110.5 };

function localBounds(latitude: number, longitude: number): Bounds {
  const radius = 0.25;
  return {
    minLatitude: Math.max(vietnamBounds.minLatitude, latitude - radius),
    minLongitude: Math.max(vietnamBounds.minLongitude, longitude - radius),
    maxLatitude: Math.min(vietnamBounds.maxLatitude, latitude + radius),
    maxLongitude: Math.min(vietnamBounds.maxLongitude, longitude + radius),
  };
}

function markerStyle(latitude: number, longitude: number, bounds: Bounds) {
  const left = ((longitude - bounds.minLongitude) / (bounds.maxLongitude - bounds.minLongitude)) * 100;
  const top = (1 - (latitude - bounds.minLatitude) / (bounds.maxLatitude - bounds.minLatitude)) * 100;
  return { left: `${Math.min(98, Math.max(2, left))}%`, top: `${Math.min(96, Math.max(4, top))}%` };
}

function addressLine(station: { addressLine1: string | null; addressLine2: string | null; town: string | null; stateOrProvince: string | null }) {
  return [station.addressLine1, station.addressLine2, station.town, station.stateOrProvince].filter(Boolean).join(", ") || "Địa chỉ chi tiết chưa có";
}

export default async function ChargingPage({ searchParams }: { searchParams: Promise<Params> }) {
  const params = await searchParams;
  const address = typeof params.address === "string" ? params.address.trim() : "";
  const geocode = address ? await geocodeAddress(address) : null;
  const center = geocode?.data?.results[0];
  const bounds = center ? localBounds(center.latitude, center.longitude) : vietnamBounds;
  const minimumPowerKw = Number(params.minimumPowerKw);
  let stations = null;
  let stationError: string | null = null;
  try {
    stations = await getChargingStations({
      ...bounds,
      limit: 200,
      connectorType: params.connectorType || undefined,
      minimumPowerKw: Number.isFinite(minimumPowerKw) && minimumPowerKw >= 0 ? minimumPowerKw : undefined,
    });
  } catch {
    stationError = "Kho trạm sạc đã cache tạm thời không truy cập được.";
  }

  return (
    <div className="charging-shell">
      <SiteHeader />
      <main className="charging-main">
        <header className="charging-intro">
          <div>
            <p className="machine-label">OCM CACHED · GOONG OPTIONAL · PROVIDER TARIFF ONLY</p>
            <h1>Tìm trạm sạc mà không biến dữ liệu cộng đồng thành biểu giá.</h1>
            <p>Vị trí và đầu nối là dữ liệu tham khảo đã cache. Giá chỉ xuất hiện khi trạm đã được review mapping với nguồn chính thức của nhà cung cấp.</p>
          </div>
          <div className="charging-policy"><BatteryCharging aria-hidden="true" /><span>Catalog và danh sách đã cache vẫn hoạt động khi OCM hoặc Goong gián đoạn.</span></div>
        </header>

        <form className="charging-search" method="get">
          <div><label htmlFor="address">Địa chỉ tại Việt Nam</label><input id="address" name="address" defaultValue={address} placeholder="Ví dụ: Hoàn Kiếm, Hà Nội" /></div>
          <div><label htmlFor="connectorType">Chuẩn đầu nối</label><input id="connectorType" name="connectorType" defaultValue={params.connectorType} placeholder="CCS, Type 2…" /></div>
          <div><label htmlFor="minimumPowerKw">Công suất tối thiểu</label><input id="minimumPowerKw" name="minimumPowerKw" type="number" min="0" max="1000" step="1" defaultValue={params.minimumPowerKw} placeholder="60 kW" /></div>
          <button className="button-control button-primary" type="submit"><LocateFixed aria-hidden="true" size={17} /> Tìm quanh địa chỉ</button>
        </form>

        {geocode?.error && <div className="charging-notice charging-notice--warn"><AlertTriangle aria-hidden="true" /><div><strong>{geocode.error.code}</strong><span>{geocode.error.message} Danh sách trạm đã cache trên toàn Việt Nam vẫn được hiển thị.</span></div></div>}
        {center && <div className="charging-notice"><MapPinned aria-hidden="true" /><div><strong>{center.formattedAddress}</strong><span>Định vị bởi {geocode?.data?.provider}{geocode?.data?.cached ? " · kết quả cache" : " · theo yêu cầu này"}; bán kính hiển thị xấp xỉ 25 km.</span></div></div>}
        {stationError && <div className="charging-notice charging-notice--warn"><AlertTriangle aria-hidden="true" /><span>{stationError}</span></div>}

        {stations && (
          <>
            <section className="charging-dataset">
              <div><p className="machine-label">{stations.dataset.coverage}</p><h2>{stations.count} trạm trong khung hiện tại</h2><p>{stations.dataset.geographicCompleteness}</p></div>
              <div><span>Cập nhật gần nhất</span><strong>{stations.dataset.lastSyncedAt ? new Date(stations.dataset.lastSyncedAt).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" }) : "Chưa đồng bộ — cần OCM API key"}</strong><a href={stations.dataset.licenseUrl} target="_blank" rel="noreferrer">{stations.dataset.attribution} · CC BY 4.0</a></div>
            </section>

            <div className="charging-layout">
              <section className="charging-map" aria-label="Sơ đồ tọa độ trạm sạc tham khảo">
                <div className="charging-map__grid" aria-hidden="true" />
                {stations.data.slice(0, 100).map((station) => <a key={station.id} href={`#station-${station.id}`} className={`charging-marker charging-marker--${station.confidence.toLowerCase()}`} style={markerStyle(station.latitude, station.longitude, bounds)} title={station.name}><PlugZap aria-hidden="true" size={14} /><span className="sr-only">{station.name}</span></a>)}
                {stations.data.length === 0 && <div className="charging-map__empty"><MapPinned aria-hidden="true" /><strong>Chưa có điểm đã cache trong vùng này.</strong><span>Cấu hình OCM key ở server để chạy đồng bộ định kỳ; không có dữ liệu giả thay thế.</span></div>}
                <footer><span>{bounds.minLatitude.toFixed(2)}, {bounds.minLongitude.toFixed(2)}</span><strong>Tọa độ tham khảo · không phải bản đồ dẫn đường</strong><span>{bounds.maxLatitude.toFixed(2)}, {bounds.maxLongitude.toFixed(2)}</span></footer>
              </section>

              <section className="charging-list" aria-label="Danh sách trạm sạc">
                {stations.data.map((station) => (
                  <article id={`station-${station.id}`} key={station.id}>
                    <header><div><p className="machine-label">OCM {station.openChargeMapId} · {station.coverage}</p><h2>{station.name}</h2><p>{addressLine(station)}</p></div><span className={`charging-confidence charging-confidence--${station.confidence.toLowerCase()}`}>{station.confidence}</span></header>
                    <dl><div><dt>Nhà vận hành ghi nhận</dt><dd>{station.operatorName ?? "Chưa rõ"}</dd></div><div><dt>Trạng thái</dt><dd>{station.operationalStatus ?? "Chưa rõ"}</dd></div><div><dt>Số điểm</dt><dd>{station.numberOfPoints ?? "Chưa rõ"}</dd></div><div><dt>Tọa độ</dt><dd>{station.latitude.toFixed(5)}, {station.longitude.toFixed(5)}</dd></div></dl>
                    <div className="charging-connectors">{station.connectors.length ? station.connectors.map((connector, index) => <span key={`${connector.connectorType}-${connector.powerKw}-${index}`}>{connector.connectorType ?? "Đầu nối chưa rõ"}{connector.powerKw ? ` · ${connector.powerKw} kW` : ""}{connector.quantity ? ` · ×${connector.quantity}` : ""}</span>) : <span>Chi tiết đầu nối chưa có</span>}</div>
                    <p className="charging-confidence-note">{station.confidenceBasis}</p>
                    {station.tariff ? <div className="charging-tariff"><strong>{station.tariff.providerName} · nguồn provider</strong><span>{station.tariff.amountPerKwh?.toLocaleString("vi-VN")} {station.tariff.currency}/kWh</span><a href={station.tariff.sourceUrl} target="_blank" rel="noreferrer">Xem nguồn biểu giá</a></div> : <div className="charging-tariff charging-tariff--unknown"><strong>Không hiển thị giá</strong><span>Chưa có mapping provider đã review; nội dung chi phí từ OCM bị bỏ qua.</span></div>}
                  </article>
                ))}
              </section>
            </div>
          </>
        )}
      </main>
      <SiteFooter />
    </div>
  );
}
