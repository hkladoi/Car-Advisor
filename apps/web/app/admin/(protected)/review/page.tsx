import { ReviewWorkspace } from "@/features/admin/review-workspace";
import { adminFetch, type AdminReviewItem } from "@/lib/admin-api";

export default async function AdminReviewPage() {
  const items = await adminFetch<AdminReviewItem[]>("review-queue");
  if (!items) return null;
  return (
    <div className="admin-page">
      <header className="admin-page-head"><div><p className="machine-label">OLD / NEW / SOURCE / RISK</p><h1>Review before publish.</h1></div><div className="admin-counter"><strong>{items.length}</strong><span>pending</span></div></header>
      <p className="admin-lede">High-risk change không tự publish. Lý do quyết định, actor, timestamp và before/after được ghi vào audit log.</p>
      <ReviewWorkspace items={items} />
    </div>
  );
}
