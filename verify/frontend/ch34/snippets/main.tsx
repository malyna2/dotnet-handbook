// Build entry for the code-splitting test: Vite bundles this, and ReportsPage must land in its
// own chunk because it is only reachable through import().
import { createRoot } from "react-dom/client";
import { App } from "./lazy-route";

createRoot(document.getElementById("root")!).render(<App route={location.pathname} />);
