import { BadgeCheck, ExternalLink } from "lucide-react";

import { formatDate, type SourceBadge } from "@/lib/catalog-api";

export function SourceDetails({ source, compact = false }: { source: SourceBadge | null; compact?: boolean }) {
  if (!source) return <span className="data-state data-state--unknown">Chưa gắn nguồn</span>;

  const authorityLabel: Record<string, string> = {
    Government: "Nguồn cơ quan nhà nước",
    CompetentAuthority: "Nguồn cơ quan nhà nước",
    TrustedSecondary: "Nguồn dữ liệu tham chiếu",
    BrandOfficial: "Nguồn hãng",
    DistributorOfficial: "Nguồn phân phối chính thức",
    DealerOfficial: "Nguồn đại lý chính thức",
  };

  return (
    <details className={`source-disclosure${compact ? " source-disclosure--compact" : ""}`}>
      <summary><BadgeCheck aria-hidden="true" size={15} /> {authorityLabel[source.authority] ?? `Nguồn ${source.authority}`}</summary>
      <div className="source-disclosure__panel">
        <strong>{source.name}</strong>
        <dl>
          <div><dt>Trạng thái fact</dt><dd>{source.factStatus}</dd></div>
          <div><dt>Độ tin cậy</dt><dd>{source.confidence}</dd></div>
          <div><dt>Snapshot</dt><dd>{formatDate(source.fetchedAt)}</dd></div>
          <div><dt>SHA-256</dt><dd><code>{source.contentHash.slice(0, 16)}…</code></dd></div>
        </dl>
        <a href={source.url} target="_blank" rel="noreferrer">Mở nguồn chính thức <ExternalLink aria-hidden="true" size={14} /></a>
      </div>
    </details>
  );
}
