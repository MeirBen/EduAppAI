import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: { baseURL: 'http://localhost:5199', trace: 'retain-on-failure', serviceWorkers: 'block' },
  webServer: {
    command: 'node e2e/start-server.mjs',
    url: 'http://localhost:5199/health',
    reuseExistingServer: false,
    timeout: 60_000,
    gracefulShutdown: { signal: 'SIGTERM', timeout: 10_000 },
  },
});
