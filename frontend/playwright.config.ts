import { defineConfig, devices } from '@playwright/test';

/**
 * E2E against the dockerized stack (nginx + WAF + API): `scripts/dev-up` first.
 * BASE_URL defaults to http://localhost:8080. Screenshots for the user manuals are written to docs/images/.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { outputFolder: '../docs/test-reports/e2e', open: 'never' }], ['junit', { outputFile: '../docs/test-reports/e2e-junit.xml' }]],
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
    locale: 'vi-VN',
    timezoneId: 'Asia/Ho_Chi_Minh',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [
    { name: 'desktop-chrome', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, grep: /@mobile/ },
  ],
});
