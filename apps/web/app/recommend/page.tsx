import type { Metadata } from "next";

import { SiteHeader } from "@/components/site-header";
import { RecommendationWorkbench } from "@/features/recommendation/recommendation-workbench";
import { defaultRecommendationRequest, evaluateRecommendation } from "@/lib/recommendation-api";

export const metadata: Metadata = {
  title: "Gợi ý xe có thể giải thích",
  description: "Lọc nhu cầu trước, kiểm độ đầy đủ dữ liệu, rồi mới xếp hạng từng trim bằng các thành phần điểm công khai.",
};

export default async function RecommendationPage() {
  const request = defaultRecommendationRequest();
  const outcome = await evaluateRecommendation(request);

  return (
    <div className="recommend-shell">
      <SiteHeader />
      <main className="recommend-main">
        <header className="recommend-intro">
          <div>
            <h1>Lọc trước. Chấm sau.</h1>
            <p>Điều kiện bắt buộc loại xe trước khi tính điểm. Mỗi kết quả công khai raw facts, trọng số, nguồn và lý do; xe thiếu dữ liệu chỉ nằm trong danh sách chờ, không nhận điểm 0 giả.</p>
          </div>
          <dl className="recommend-order" aria-label="Thứ tự đánh giá">
            <div><dt>1</dt><dd>Hard filters</dd></div>
            <div><dt>2</dt><dd>Completeness + nguồn</dd></div>
            <div><dt>3</dt><dd>Normalize + xếp hạng</dd></div>
          </dl>
        </header>
        <RecommendationWorkbench initialRequest={request} initialResult={outcome.data} initialError={outcome.error} />
      </main>
      <footer className="recommend-footer">
        <p>Không đủ bằng chứng thì chưa có điểm.</p>
        <div><span>Vietnam Car Platform</span><span>Recommendation methodology v3.1</span></div>
      </footer>
    </div>
  );
}
