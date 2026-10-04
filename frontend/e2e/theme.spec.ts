import { expect, test, type Page } from '@playwright/test';

// Requests must reach page.route rather than a cached service worker.
test.use({ serviceWorkers: 'block' });

const colorScheme = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);

test('the color theme follows the device, remembers a choice and applies it before the app starts', async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  const picker = page.getByRole('group', { name: 'מצב תצוגה' });
  const option = (name: string) => picker.getByRole('radio', { name });
  await expect(option('לפי המכשיר')).toBeChecked();
  expect(await colorScheme(page)).toBe('dark');

  await picker.locator('label', { hasText: 'בהיר' }).click();
  await expect(option('בהיר')).toBeChecked();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  expect(await colorScheme(page)).toBe('light');

  await page.keyboard.press('ArrowDown');
  await expect(option('כהה')).toBeChecked();
  await page.emulateMedia({ colorScheme: 'light' });
  await page.reload();
  await expect(option('כהה')).toBeChecked();
  expect(await colorScheme(page)).toBe('dark');
  await page.screenshot({
    animations: 'disabled',
    path: '../artifacts/login-dark-desktop.png',
    fullPage: true,
  });

  await page.route(/\/main-[^/]+\.js$/, (route) => route.abort());
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await colorScheme(page)).toBe('dark');
});
