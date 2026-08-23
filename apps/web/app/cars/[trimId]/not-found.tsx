import Link from "next/link";

export default function CarNotFound() {
  return <main className="catalog-error"><h1>Không tìm thấy phiên bản xe.</h1><p>Trim có thể chưa được publish hoặc đường dẫn không còn hợp lệ.</p><Link className="button-control button-outline" href="/cars">Trở lại catalog</Link></main>;
}
