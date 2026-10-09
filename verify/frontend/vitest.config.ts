import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    globals: true,
    environment: "jsdom",
    include: ["ch06/**/*.test.{ts,tsx}", "ch34/**/*.test.{ts,tsx}"],
  },
});
