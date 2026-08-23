export default function CarsLoading() {
  return (
    <main className="catalog-loading" aria-busy="true" aria-label="Đang tải catalog">
      <div className="loading-line loading-line--title" />
      <div className="loading-layout">
        <div className="loading-panel" />
        <div className="loading-grid">
          {Array.from({ length: 6 }, (_, index) => <div className="loading-card" key={index} />)}
        </div>
      </div>
    </main>
  );
}
