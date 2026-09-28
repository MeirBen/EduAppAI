import { expect, test, type Page } from '@playwright/test';

/** Checks Ionic's actual scroll container as well as the document, which clips body overflow. */
async function checkNarrowLayout(page: Page, name: string) {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const content = page.locator('ion-content');
  expect(await content.evaluate(async (element) => {
    const scroll = await (element as HTMLElement & { getScrollElement(): Promise<HTMLElement> }).getScrollElement();
    return scroll.scrollWidth <= scroll.clientWidth;
  })).toBe(true);
  await page.screenshot({ path: `../artifacts/${name}-mobile.png`, fullPage: true });
  await content.evaluate(async (element) => {
    await (element as HTMLElement & { scrollToBottom(duration: number): Promise<void> }).scrollToBottom(0);
  });
  await page.screenshot({ path: `../artifacts/${name}-mobile-bottom.png`, fullPage: true });
  await content.evaluate(async (element) => {
    await (element as HTMLElement & { scrollToTop(duration: number): Promise<void> }).scrollToTop(0);
  });
  await page.evaluate(() => (document.documentElement.style.fontSize = '100%'));
  await page.setViewportSize({ width: 1440, height: 1000 });
}

test('Hebrew parent workflow preserves wire values and frozen content in RTL', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('lang', 'he');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page).toHaveTitle('כניסת הורים · לומדים ביחד');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'דילוג לתוכן הראשי' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
  await page.screenshot({ path: '../artifacts/login-desktop.png', fullPage: true });
  await checkNarrowLayout(page, 'login');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByText('יש להזין כתובת דוא״ל תקינה.')).toBeVisible();
  const email = page.getByLabel('כתובת דוא״ל', { exact: true });
  await expect(email).toHaveAttribute('dir', 'ltr');
  await expect(email).toHaveAttribute('aria-invalid', 'true');
  await email.fill('browser@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByRole('heading', { name: 'מתחילים עם רעיון אחד' })).toBeVisible();
  await checkNarrowLayout(page, 'empty-library');
  await page.getByRole('link', { name: 'יצירת התבנית הראשונה' }).click();
  await checkNarrowLayout(page, 'template-form');
  await page.getByLabel('שם התבנית').fill('לוח הכפל — כיתה 2 (Math)');
  const editorUrl = page.url();
  await page.getByRole('link', { name: 'דילוג לתוכן הראשי' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
  expect(page.url()).toBe(editorUrl);
  await expect(page.getByLabel('שם התבנית')).toHaveValue('לוח הכפל — כיתה 2 (Math)');
  await page.getByLabel('רמת קושי התחלתית').selectOption('easy');
  await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
  const difficulty = page.getByLabel('רמת קושי', { exact: true });
  await expect(difficulty).toHaveValue('easy');
  await expect(difficulty.locator('option:checked')).toHaveText('קלה');
  await checkNarrowLayout(page, 'task-form');
  await page.getByLabel('מספר שאלות', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'יצירת טיוטה' }).click();
  await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
  await expect(page.getByText('מהי המכפלה של שני המספרים?')).toBeVisible();
  const prompts = page.locator('.question-prompt');
  const questions = await prompts.allTextContents();
  expect(questions).toHaveLength(3);
  await expect(prompts.first()).toHaveCSS('direction', 'ltr');
  await page.reload();
  await expect(prompts).toHaveText(questions);
  await page.getByText('הצגת התשובה לשאלה 1', { exact: true }).click();
  await expect(page.locator('details').first()).toHaveAttribute('open', '');
  await page.screenshot({ path: '../artifacts/preview-desktop.png', fullPage: true });
  await checkNarrowLayout(page, 'preview');
  await page.getByRole('link', { name: 'חזרה לכל התרגולים', exact: true }).click();
  await expect(page.locator('.draft-list')).toContainText('לוח הכפל — כיתה 2 (Math)');
  const month = new Intl.DateTimeFormat('he-IL', { month: 'long' }).format(new Date());
  await expect(page.locator('.draft-list')).toContainText(month);
  await checkNarrowLayout(page, 'library');
  await page.screenshot({ path: '../artifacts/learning-space.png', fullPage: true });
  await page.setViewportSize({ width: 568, height: 320 });
  await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
  const create = page.getByRole('link', { name: 'תבנית חדשה', exact: false });
  await create.scrollIntoViewIfNeeded();
  await expect(create).toBeInViewport({ ratio: 1 });
  await page.evaluate(() => (document.documentElement.style.fontSize = '100%'));
  await page.setViewportSize({ width: 1440, height: 1000 });
  const unknown = await page.request.get('/api/does-not-exist');
  expect(unknown.status()).toBe(404);
  expect(unknown.headers()['content-type']).toContain('application/problem+json');
  await page.getByRole('button', { name: 'יציאה מהחשבון', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'טוב שחזרתם' })).toBeVisible();
  expect(errors).toEqual([]);
});
