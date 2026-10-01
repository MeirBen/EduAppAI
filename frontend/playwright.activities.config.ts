import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e/activities',
  workers: 1,
  reporter: 'list',
  use: { baseURL: 'http://localhost:5299', trace: 'retain-on-failure' },
  webServer: {
    command: 'ng serve --configuration activities-test --host localhost --port 5299',
    url: 'http://localhost:5299',
    reuseExistingServer: false,
    timeout: 60000,
  },
});
