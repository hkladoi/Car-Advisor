import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Bộ tính chi phí xe",
  robots: { index: false, follow: false },
};

export default function CalculatorsLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return children;
}
