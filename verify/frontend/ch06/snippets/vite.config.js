// vite.config.js
import { defineConfig } from "vite";

export default defineConfig({
  server: {
    proxy: { "/api": "http://localhost:5080" },   // dev server forwards /api to Kestrel
  },
});
