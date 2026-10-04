import { expect, test, type Page } from '@playwright/test';
import { textSize } from './text-size';

async function signIn(page: Page) {
  await page.goto('/login');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('browser@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page).toHaveURL('/activities/new');
}

test('saved login error parameters cannot create or restore a login error on refresh', async ({
  page,
}) => {
  const staleUrl = '/login?connection=unavailable&error=session-expired';
  await page.goto(staleUrl);
  await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.reload();
  await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);

  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('unknown@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('Invalid!Password123');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByRole('alert')).toContainText('הכניסה לא הצליחה');
  await expect(page).toHaveURL(staleUrl);
  await page.reload();
  await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);

  await signIn(page);
  await page.goto(staleUrl);
  await expect(page).toHaveURL('/activities/new');
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('sign-in replaces the login history entry and authenticated visits skip the form', async ({
  page,
}) => {
  await page.goto('/health');
  await signIn(page);
  await page.goBack();
  await expect(page).toHaveURL('/health');
  await page.goForward();
  await expect(page).toHaveURL('/activities/new');
  await page.goto('/login');
  await expect(page).toHaveURL('/activities/new');
  await page.reload();
  await expect(page.getByRole('heading', { name: 'פעילות חדשה', exact: true })).toBeVisible();
  await expect(page.getByLabel('סיסמה', { exact: true })).toHaveCount(0);
});

test('Back skips an old login entry after signing in from another tab', async ({
  page,
  context,
}) => {
  await page.goto('/login');
  await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
  await page.goto('/health');
  const otherTab = await context.newPage();
  await signIn(otherTab);
  await otherTab.close();
  await page.goBack();
  await expect(page).toHaveURL('/activities/new');
  await expect(page.getByLabel('סיסמה', { exact: true })).toHaveCount(0);
});

test('expired sessions and explicit sign-out show login and cannot reopen a private page', async ({
  page,
  context,
}) => {
  await signIn(page);
  await context.clearCookies();
  await page.reload();
  await expect(page).toHaveURL('/login');
  await expect(page.getByLabel('סיסמה', { exact: true })).toBeVisible();
  await signIn(page);
  await page.getByRole('button', { name: 'יציאה', exact: true }).click();
  await expect(page).toHaveURL('/login');
  expect((await page.request.get('/api/auth/me')).status()).toBe(401);
  await page.goBack();
  await expect(page).toHaveURL('/login');
  await expect(page.getByRole('button', { name: 'יציאה', exact: true })).toHaveCount(0);
});

for (const endpoint of ['auth/me', 'auth/csrf', 'limits']) {
  test(`${endpoint} outages offer retry without credentials or redirect loops`, async ({
    page,
  }) => {
    await signIn(page);
    let attempts = 0;
    await page.route(`**/api/${endpoint}`, (route) => {
      attempts++;
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto('/login');
    const retry = page.getByRole('button', { name: 'ניסיון נוסף', exact: true });
    await expect(retry).toBeVisible();
    await expect(page.getByRole('alert')).toContainText('לא הצלחנו לפתוח את המרחב');
    await expect(page.getByLabel('סיסמה', { exact: true })).toHaveCount(0);
    expect(attempts).toBe(1);
    await retry.focus();
    const retried = page.waitForResponse(
      (response) => new URL(response.url()).pathname === `/api/${endpoint}`,
    );
    await page.keyboard.press('Enter');
    await retried;
    await expect(retry).not.toHaveAttribute('aria-disabled', 'true');
    await expect(retry).toBeFocused();
    expect(attempts).toBe(2);
    await page.reload();
    await expect(retry).toBeVisible();
    expect(attempts).toBe(2);
    if (endpoint === 'auth/me') {
      await page.setViewportSize({ width: 360, height: 800 });
      await textSize(page, 32);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      );
      await page.screenshot({
        path: test.info().outputPath('auth-retry-mobile.png'),
        fullPage: true,
      });
    }
    await page.unroute(`**/api/${endpoint}`);
    await retry.click();
    await expect(page).toHaveURL('/activities/new');
    expect((await page.request.get('/api/auth/me')).status()).toBe(200);
  });
}
