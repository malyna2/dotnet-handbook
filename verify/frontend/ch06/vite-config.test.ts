// @vitest-environment node
// Chapter 6, "Vite: Dev Server and Build": the printed config loads with the pinned Vite and
// resolves to a dev-server proxy for /api. (Forwarding itself needs a running Kestrel; not run.)
import { resolveConfig } from "vite";
import { join } from "node:path";

test("the printed vite.config.js is a valid config with an /api proxy", async () => {
  const configFile = join(process.cwd(), "ch06", "snippets", "vite.config.js");
  const config = await resolveConfig({ configFile, logLevel: "silent" }, "serve");
  expect(config.server.proxy).toEqual({ "/api": "http://localhost:5080" });
});
