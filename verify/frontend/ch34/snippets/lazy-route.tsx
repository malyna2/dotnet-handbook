import { lazy, Suspense } from "react";

const ReportsPage = lazy(() => import("./ReportsPage"));   // its own chunk, fetched on first render

function App({ route }: { route: string }) {
  return (
    <Suspense fallback={<p>Loading…</p>}>
      {route === "/reports" ? <ReportsPage /> : <p>Home</p>}
    </Suspense>
  );
}

export { App };
