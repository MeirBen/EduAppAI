import { expect, test } from '@playwright/test';
import { startAppBuilds } from './app-builds';

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
