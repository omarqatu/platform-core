import { defineConfig, devices } from '@playwright/test';

// E2E [B] against a real Api serving the built interface (the CI image, or a local Api with Web:Root = web/dist),
// on the seed contract. One worker: the fixtures change the database (each one restores what it changed).
export default defineConfig({
  testDir: 'e2e',
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5080',
    locale: 'en-US',
    trace: 'retain-on-failure',
    launchOptions: process.env.PW_CHROMIUM_PATH ? { executablePath: process.env.PW_CHROMIUM_PATH } : {},
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
