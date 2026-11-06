import { defineConfig } from '@playwright/test';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  testDir: '.', testMatch: '*.spec.ts',
  use: { baseURL: 'http://127.0.0.1:5173', trace: 'retain-on-failure', channel: process.env.CI ? undefined : 'chrome' },
  webServer: { command: 'npm run dev -- --port 5173', cwd: fileURLToPath(new URL('../../frontend', import.meta.url)), url: 'http://127.0.0.1:5173', reuseExistingServer: !process.env.CI },
  reporter: [['list'], ['html', { open: 'never', outputFolder: '../../frontend/playwright-report' }]],
});
