// @vitest-environment node
// Chapter 34, code splitting: a production build with the pinned Vite puts the page reached only
// through import() into its own chunk, and gives every chunk a content hash in its file name.
import { build } from "vite";
import { join } from "node:path";

test("vite build splits the lazily imported page into its own hashed chunk", async () => {
  const root = join(process.cwd(), "ch34", "snippets");
  const result = await build({
    root,
    logLevel: "silent",
    configFile: false,
    build: { write: false, rollupOptions: { input: join(root, "main.tsx") } },
  });
  const outputs = (Array.isArray(result) ? result : [result]).flatMap((r) => ("output" in r ? r.output : []));
  const chunks = outputs.filter((o) => o.type === "chunk");
  const reports = chunks.find((c) => c.facadeModuleId?.endsWith("ReportsPage.tsx"));
  expect(chunks.length).toBeGreaterThanOrEqual(2);
  expect(reports).toBeDefined();
  expect(reports!.isDynamicEntry).toBe(true);
  for (const c of chunks) expect(c.fileName).toMatch(/-[A-Za-z0-9_-]{6,}\.js$/);
}, 60_000);
