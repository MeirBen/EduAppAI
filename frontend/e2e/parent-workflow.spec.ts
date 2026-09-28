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
  await content.evaluate(async (element) => {
    await (element as HTMLElement & { scrollToTop(duration: number): Promise<void> }).scrollToTop(0);
  });
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

async function login(page: Page) {
  await page.goto('/');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('browser@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByRole('heading', { name: 'מה נלמד היום?' })).toBeVisible();
}

async function propose(page: Page, prompt: string) {
  await page.goto('/templates/new');
  await page.getByLabel('הרעיון שלכם לתבנית').fill(prompt);
  await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'בדיקה ועריכת התבנית' })).toBeVisible();
}

test('a parent prompt becomes an editable reusable template and distinct frozen tasks', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('lang', 'he');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'דילוג לתוכן הראשי' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
  await login(page);
  await expect(page.getByRole('heading', { name: 'מתחילים עם רעיון אחד' })).toBeVisible();
  await propose(page, 'קטעי קריאה לכיתה ג׳ עם נושא ורמה לבחירה ושאלות מעורבות');
  expect((await (await page.request.get('/api/templates')).json()).length).toBe(0);
  await expect(page.getByLabel('סוג התבנית')).toHaveCount(0);
  await page.getByLabel('שם התבנית', { exact: true }).fill('קוראים ומגלים');
  const parameter = page.locator('[data-parameter-editor]').first();
  await parameter.getByLabel('שם השדה להורה').fill('מה נחקור?');
  await checkNarrowLayout(page, 'ai-template-review');
  const createdResponse = page.waitForResponse(response => response.url().endsWith('/api/templates') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
  const template = await (await createdResponse).json();
  expect(template.definition.schemaVersion).toBe(2);
  expect(template.definition.generation.mode).toBeUndefined();
  await expect(page.getByLabel('מה נחקור?', { exact: true })).toHaveValue('דינוזאורים');
  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
  const originalUrl = page.url();
  const originalQuestions = await page.locator('.question-prompt').allTextContents();
  const originalContent = await page.locator('section').innerText();
  await expect(page.locator('.question-prompt')).toHaveCount(2);
  await page.reload();
  await expect(page.locator('section')).toHaveText(originalContent, { useInnerText: true });
  await page.goto(`/templates/${template.id}/create`);
  await page.getByLabel('מה נחקור?', { exact: true }).fill('חלל');
  await page.getByLabel('רמה', { exact: true }).selectOption('מאתגרת');
  await page.getByLabel('מספר שאלות', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'לומדים על חלל', exact: true })).toBeVisible();
  await expect(page.locator('.question-prompt')).toHaveCount(3);
  await page.getByText('הצגת התשובה לשאלה 1', { exact: true }).click();
  await expect(page.locator('details').first()).toHaveAttribute('open', '');
  await checkNarrowLayout(page, 'ai-preview');
  await page.screenshot({ path: '../artifacts/ai-preview-desktop.png', fullPage: true });
  await page.goto(originalUrl);
  await expect(page.locator('.question-prompt')).toHaveText(originalQuestions);
  await expect(page.getByRole('heading', { name: 'לומדים על דינוזאורים', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'יציאה מהחשבון', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'טוב שחזרתם' })).toBeVisible();
  expect(errors).toEqual([]);
});

test('AI template revisions preserve snapshots and concurrent edits', async ({ page, context }) => {
  await login(page);
  await propose(page, 'שאלות מדעים בנושאים משתנים');
  const createdResponse = page.waitForResponse(response => response.url().endsWith('/api/templates') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
  const template = await (await createdResponse).json();
  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(page.locator('.question-prompt')).toHaveCount(2);
  const frozenUrl = page.url();
  await page.goto(`/templates/${template.id}/edit`);
  const other = await context.newPage();
  await other.goto(`/templates/${template.id}/edit`);
  await expect(other.getByLabel('שם התבנית', { exact: true })).toHaveValue('חוקרים וקוראים');
  await page.getByLabel('שם התבנית', { exact: true }).fill('מדעים — גרסה חדשה');
  await page.getByRole('button', { name: 'פרסום גרסה חדשה', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'מדעים — גרסה חדשה', exact: true })).toBeVisible();
  await other.getByLabel('שם התבנית', { exact: true }).fill('העריכה המקומית שלי');
  await other.getByRole('button', { name: 'פרסום גרסה חדשה', exact: true }).click();
  await expect(other.getByRole('alert')).toContainText('התבנית השתנתה');
  await expect(other.getByLabel('שם התבנית', { exact: true })).toHaveValue('העריכה המקומית שלי');
  await other.getByRole('button', { name: 'טעינת הגרסה העדכנית והחלפת העריכה המקומית' }).click();
  await expect(other.getByLabel('שם התבנית', { exact: true })).toHaveValue('מדעים — גרסה חדשה');
  await page.goto(frozenUrl);
  await expect(page.getByText('גרסת תבנית 1', { exact: true })).toBeVisible();
  await other.close();
});

test('invalid AI output preserves the prompt and explicit retry can recover', async ({ page }) => {
  await login(page);
  const before = (await (await page.request.get('/api/templates')).json()).length;
  await page.goto('/templates/new');
  await page.getByLabel('הרעיון שלכם לתבנית').fill('בדיקת כשל');
  await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('לא נשמר דבר');
  await expect(page.getByLabel('הרעיון שלכם לתבנית')).toHaveValue('בדיקת כשל');
  await page.getByLabel('הרעיון שלכם לתבנית').fill('מילים באנגלית לתלמידים מתחילים');
  await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'בדיקה ועריכת התבנית' })).toBeVisible();
  await page.getByRole('button', { name: 'ביטול ההצעה וחזרה לרעיון' }).click();
  await expect(page.getByRole('heading', { name: 'בדיקה ועריכת התבנית' })).toHaveCount(0);
  expect((await (await page.request.get('/api/templates')).json()).length).toBe(before);
});
