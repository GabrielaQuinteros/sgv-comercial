import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { "/api": process.env.SGV_API_URL ?? "http://localhost:5080" },
  },
  test: {
    include: ["src/**/*.test.{ts,tsx,mjs}"],
    environment: "jsdom",
    coverage: {
      provider: "v8",
      allowExternal: true,
      reporter: ["text", "lcov"],
      include: [
        "src/lib/**/*.ts",
        "src/components/**/*.tsx",
        "src/App.tsx",
        fileURLToPath(new URL("../scripts/demo-config.mjs", import.meta.url)),
      ],
    },
  },
});
