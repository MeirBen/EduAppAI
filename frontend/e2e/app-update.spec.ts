import { expect, test } from '@playwright/test';
import { startAppBuilds } from './app-builds';

test('child visits leave unused chunks undownloaded and cache them when a screen needs them', async ({
  page,
  context,
}) => {
  const builds = await startAppBuilds();
  try {
    const manifest = await (await page.request.get(`${builds.url}/ngsw.json`)).json();
    const chunks = Object.keys(manifest.hashTable).filter((path) => /^\/chunk-.*\.js$/.test(path));
    expect(chunks.length).toBeGreaterThan(0);
    await page.goto(`${builds.url}/child/activate`);
    await expect(page.getByLabel('הקוד מההורה', { exact: true })).toBeVisible();
    await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
    await page.reload();
    await expect
      .poll(() => page.evaluate(async () => (await fetch('/ngsw/state')).text()))
      .toMatch(/Latest manifest hash: [0-9a-f]{40}/);
    await expect(page.getByLabel('הקוד מההורה', { exact: true })).toBeVisible();
    expect(builds.requests.has('/api/auth/me')).toBe(false);
    const unused = chunks.filter((path) => !builds.requests.has(path));
    expect(unused.length).toBeGreaterThan(0);

    await page.goto(`${builds.url}/login`);
    await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
    const loaded = unused.filter((path) => builds.requests.has(path));
    expect(loaded.length).toBeGreaterThan(0);
    const unvisited = unused.find((path) => !builds.requests.has(path));
    expect(unvisited).toBeDefined();
    await context.setOffline(true);
    const status = await page.evaluate(
      async ({ loaded, unvisited }) => ({
        loaded: (await fetch(loaded)).status,
        unvisited: await fetch(unvisited).then(
          (response) => response.status,
          () => 0,
        ),
      }),
      { loaded: loaded[0], unvisited: unvisited! },
    );
    expect(status.loaded).toBe(200);
    expect(status.unvisited).not.toBe(200);
  } finally {
    await context.setOffline(false);
    builds.close();
  }
});

test('an open tab offers the next build after a deployment and opens it on reload', async ({
  page,
}) => {
  const builds = await startAppBuilds();
  try {
    await page.goto(`${builds.url}/login`);
    await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
    // The worker initializes on the first request it serves, then names the build it installed.
    await page.reload();
    await expect
      .poll(() => page.evaluate(async () => (await fetch('/ngsw/state')).text()))
      .toMatch(/Latest manifest hash: [0-9a-f]{40}/);
    await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
    await expect(page.locator('meta[name="build"]')).toHaveCount(0);

    builds.deploy();
    // Headless tabs never hide, so the page replays the return the app checks on.
    await page.evaluate(() => {
      for (const state of ['hidden', 'visible']) {
        Object.defineProperty(document, 'visibilityState', { value: state, configurable: true });
        document.dispatchEvent(new Event('visibilitychange'));
      }
    });
    const notice = page.getByRole('status').filter({ hasText: 'גרסה חדשה של האתר מוכנה.' });
    await expect(notice).toBeVisible();
    await expect(notice.locator(':focus')).toHaveCount(0);

    await notice.getByRole('button', { name: 'טעינה מחדש' }).click();
    await expect(page.locator('meta[name="build"]')).toHaveAttribute('content', 'next');
    await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
    await expect(notice).toHaveCount(0);
  } finally {
    builds.close();
  }
});
