// The lazily loaded page for Chapter 34's code-splitting sample. Evaluating this module records
// that it was loaded, so the test can tell when the chunk was fetched.
(globalThis as { reportsPageLoaded?: boolean }).reportsPageLoaded = true;

export default function ReportsPage() {
  return <h1>Reports</h1>;
}
