import { DataOperationsWorkspace } from "@/features/admin/data-operations-workspace";
import { adminFetch, type AdminFieldLock, type AdminImport, type AdminSource, type AdminTrim } from "@/lib/admin-api";

export default async function AdminDataPage() {
  const [sources, trims, imports, locks] = await Promise.all([
    adminFetch<AdminSource[]>("sources"), adminFetch<AdminTrim[]>("catalog/trims"), adminFetch<AdminImport[]>("imports"), adminFetch<AdminFieldLock[]>("field-locks"),
  ]);
  if (!sources || !trims || !imports || !locks) return null;
  return (
    <div className="admin-page">
      <header className="admin-page-head"><div><p className="machine-label">CURATE · VALIDATE · LOCK · AUDIT</p><h1>Operate the data.</h1></div><div className="admin-counter"><strong>{sources.filter(source => source.active).length}</strong><span>active sources</span></div></header>
      <p className="admin-lede">Public record không được publish nếu thiếu SourceFact hoặc manual reason. Import luôn đi qua validation và review; crawler không overwrite field đang lock.</p>
      <DataOperationsWorkspace sources={sources} trims={trims} imports={imports} locks={locks} />
    </div>
  );
}
