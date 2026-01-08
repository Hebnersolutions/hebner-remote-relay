import { defineConfig } from '@playwright/test';

export default defineConfig({
  timeout: 120000,
  testDir: 'tests',
  use: {
    headless: true,
    viewport: { width: 1280, height: 800 },
    ignoreHTTPSErrors: true,
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } }
  ]
});
