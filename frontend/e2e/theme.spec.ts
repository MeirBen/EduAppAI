import { expect, test, type Page } from '@playwright/test';

// Requests must reach page.route rather than a cached service worker.
test.use({ serviceWorkers: 'block' });

const colorScheme = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);

for (const theme of ['light', 'dark'] as const) {
  test(`${theme} keeps text, controls and focus distinct on every themed surface`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: theme });
    await page.goto('/');
    await expect(page.getByLabel('כתובת דוא״ל', { exact: true })).toBeVisible();
    const contrasts = await page.evaluate(() => {
      const tokens = getComputedStyle(document.documentElement);
      const canvas = document.createElement('canvas').getContext('2d')!;
      const luminance = (name: string) => {
        canvas.fillStyle = tokens.getPropertyValue('--color-' + name);
        canvas.fillRect(0, 0, 1, 1);
        const [r, g, b] = [...canvas.getImageData(0, 0, 1, 1).data].slice(0, 3).map((v) => {
          v /= 255;
          return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
        });
        return r * 0.2126 + g * 0.7152 + b * 0.0722;
      };
      const ratio = (a: string, b: string) => {
        const first = luminance(a),
          second = luminance(b);
        return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
      };
      const pairs = ['canvas', 'surface', 'soft', 'accent', 'danger-soft'].flatMap((surface) =>
        ['ink', 'muted', 'brand', 'danger', 'control', 'focus'].map((role) => ({
          name: `${role} on ${surface}`,
          actual: ratio(role, surface),
          minimum: role === 'ink' ? 10 : role === 'control' || role === 'focus' ? 3.5 : 5.5,
        })),
      );
      return [
        ...pairs,
        ...['brand', 'brand-hover'].map((surface) => ({
          name: `on-brand on ${surface}`,
          actual: ratio('on-brand', surface),
          minimum: 5.5,
        })),
      ];
    });
    for (const { name, actual, minimum } of contrasts)
      expect(actual, `${theme}: ${name}`).toBeGreaterThanOrEqual(minimum);
  });
}

test('an explicit theme owns its palette, corners and elevation regardless of the device', async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  const panel = page.locator('section.panel');
  await expect(panel).toBeVisible();
  await page.evaluate(() => {
    const root = document.documentElement;
    root.dataset['theme'] = 'custom';
    root.style.setProperty('--color-surface', 'rgb(255, 246, 232)');
    root.style.setProperty('--radius-card', '4px');
    root.style.setProperty('--shadow-panel', '0 0 0 3px rgb(120, 40, 190)');
  });
  expect(await colorScheme(page)).toBe('light');
  await expect(panel).toHaveCSS('background-color', 'rgb(255, 246, 232)');
  await expect(panel).toHaveCSS('border-radius', '4px');
  expect(await panel.evaluate((element) => getComputedStyle(element).boxShadow)).toContain(
    'rgb(120, 40, 190)',
  );
});

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

  await page.route(/\/main-[^/]+\.js$/, (route) => route.abort());
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await colorScheme(page)).toBe('dark');
});
