import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: { baseURL: 'http://localhost:5199', trace: 'retain-on-failure', serviceWorkers: 'block' },
  projects: [
    {
      name: 'workflows',
      testIgnore: ['**/auth-navigation.spec.ts', '**/parent-assignments.spec.ts'],
    },
    {
      name: 'auth',
      testMatch: '**/auth-navigation.spec.ts',
      use: { baseURL: 'http://localhost:5200' },
    },
    {
      name: 'parent-assignments',
      testMatch: '**/parent-assignments.spec.ts',
      use: { baseURL: 'http://localhost:5201' },
    },
  ],
  // Real sign-in tests get their own disposable host and login rate-limit budget.
  webServer: [5199, 5200, 5201].map((port) => ({
    command: `node e2e/start-server.mjs ${port}`,
    url: `http://localhost:${port}/health`,
    reuseExistingServer: false,
    timeout: 60_000,
    gracefulShutdown: { signal: 'SIGTERM' as const, timeout: 10_000 },
  })),
});
