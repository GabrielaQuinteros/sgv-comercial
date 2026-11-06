import { defineConfig } from "@playwright/test";
import { fileURLToPath } from "node:url";

export default defineConfig({
  testDir: ".",
  testMatch: "*.spec.ts",
  testIgnore: process.env.SGV_E2E_FULLSTACK ? [] : ["fiscal.spec.ts"],
  timeout: 60_000,
  use: {
    baseURL: `http://127.0.0.1:${process.env.SGV_WEB_PORT ?? "5173"}`,
    trace: "retain-on-failure",
    channel: process.env.CI ? undefined : "chrome",
  },
  webServer: {
    command: `npm run dev -- --port ${process.env.SGV_WEB_PORT ?? "5173"}`,
    cwd: fileURLToPath(new URL("../../frontend", import.meta.url)),
    url: `http://127.0.0.1:${process.env.SGV_WEB_PORT ?? "5173"}`,
    reuseExistingServer: !process.env.CI,
  },
  reporter: [
    ["list"],
    [
      "html",
      {
        open: "never",
        outputFolder: fileURLToPath(
          new URL("../../frontend/playwright-report", import.meta.url),
        ),
      },
    ],
  ],
});
