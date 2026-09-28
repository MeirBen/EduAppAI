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

test('parents author mixed content, publish revisions and retain stale edits on conflict', async ({ page, context }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('browser@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await page.getByRole('link', { name: 'תבנית חדשה', exact: false }).click();
  await page.getByLabel('סוג התבנית').selectOption('static');
  await page.getByLabel('שם התבנית').fill('קוראים וחושבים — Reading');
  await page.getByLabel('כותרת התרגול (לא חובה)', { exact: true }).fill('נועה והספרים');
  await page.getByLabel('הנחיות לתרגול (לא חובה)', { exact: true }).fill('קוראים ואז עונים.');
  await page.getByRole('button', { name: 'הוספת קטע קריאה', exact: true }).click();
  await page.getByLabel('קטע קריאה 1', { exact: true }).fill('נועה מצאה 3 ספרים.\nShe loves reading.');

  const questions = page.locator('[data-question]');
  await questions.nth(0).getByLabel('נוסח השאלה').fill('כמה ספרים מצאה נועה?');
  await questions.nth(0).getByLabel('סוג התשובה').selectOption('numeric-input');
  await questions.nth(0).getByLabel('התשובה הנכונה').fill('3');
  await questions.nth(0).getByLabel('נקודות לשאלה').fill('2');
  await page.getByRole('button', { name: 'הוספת שאלה', exact: true }).click();
  await questions.nth(1).getByLabel('נוסח השאלה').fill('מי מצאה את הספרים?');
  await questions.nth(1).getByLabel('התשובה הנכונה').fill('נועה');
  await page.getByRole('button', { name: 'הוספת שאלה', exact: true }).click();
  await questions.nth(2).getByLabel('נוסח השאלה').fill('מה נועה מצאה?');
  await questions.nth(2).getByLabel('סוג התשובה').selectOption('single-choice');
  await questions.nth(2).getByLabel('אפשרויות תשובה — אפשרות בכל שורה').fill('ספרים\nפרחים');
  await questions.nth(2).getByLabel('התשובה הנכונה').selectOption('ספרים');
  await questions.nth(2).getByLabel('נקודות לשאלה').fill('3');
  await page.getByRole('button', { name: 'העברת שאלה 3 למעלה', exact: true }).click();
  await expect(questions.nth(1).getByLabel('נוסח השאלה')).toHaveValue('מה נועה מצאה?');
  await expect(questions.nth(1).getByLabel('נוסח השאלה')).toBeFocused();
  await checkNarrowLayout(page, 'authored-template');

  const creation = page.waitForResponse(response => response.url().endsWith('/api/templates') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
  const created = await (await creation).json();
  const templateId = created.id;
  const originalQuestionIds = created.definition.generation.content.questions.map((question: { id: string }) => question.id);
  await expect(page.getByLabel('רמת קושי', { exact: true })).toHaveCount(0);
  await expect(page.getByText('עותק שאפשר לחזור אליו', { exact: true })).toBeVisible();
  await checkNarrowLayout(page, 'static-task-form');
  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
  const originalUrl = page.url();
  await expect(page.getByRole('heading', { name: 'נועה והספרים', exact: true })).toBeVisible();
  const prompts = page.locator('.question-prompt');
  await expect(prompts).toHaveText(['כמה ספרים מצאה נועה?', 'מה נועה מצאה?', 'מי מצאה את הספרים?']);
  await expect(prompts.first()).toHaveCSS('direction', 'rtl');
  await expect(page.getByRole('list', { name: 'אפשרויות תשובה' })).toContainText('פרחים');
  await page.getByText('הצגת התשובה לשאלה 2', { exact: true }).click();
  await expect(page.locator('details').nth(1)).toContainText('ספרים');
  await page.reload();
  await expect(prompts).toHaveText(['כמה ספרים מצאה נועה?', 'מה נועה מצאה?', 'מי מצאה את הספרים?']);
  await checkNarrowLayout(page, 'authored-preview');

  await page.goto(`/templates/${templateId}/edit`);
  await expect(page.getByLabel('סוג התבנית')).toBeDisabled();
  await expect(questions.nth(1).getByLabel('התשובה הנכונה')).toHaveValue('ספרים');
  await expect(page.getByLabel('כותרת התרגול (לא חובה)', { exact: true })).toHaveValue('נועה והספרים');
  const stale = await context.newPage();
  await stale.goto(`/templates/${templateId}/edit`);
  await stale.getByLabel('שם התבנית').fill('העריכה המקומית שלי');
  await page.getByLabel('קטע קריאה 1', { exact: true }).fill('קטע קריאה מתוקן');
  const publication = page.waitForResponse(response => response.url().endsWith(`/api/templates/${templateId}/versions`));
  await page.getByRole('button', { name: 'שמירת גרסה חדשה', exact: true }).click();
  const updated = await (await publication).json();
  expect(updated.currentVersion).toBe(2);
  expect(updated.definition.generation.content.questions.map((question: { id: string }) => question.id)).toEqual(originalQuestionIds);

  await stale.getByRole('button', { name: 'שמירת גרסה חדשה', exact: true }).click();
  await expect(stale.getByRole('alert')).toContainText('העריכה שלכם נשארה כאן');
  await expect(stale.getByLabel('שם התבנית')).toHaveValue('העריכה המקומית שלי');
  await stale.getByRole('button', { name: 'טעינת הגרסה העדכנית וביטול העריכה המקומית', exact: true }).click();
  await expect(stale.getByLabel('שם התבנית')).toHaveValue('קוראים וחושבים — Reading');
  await expect(stale.getByLabel('קטע קריאה 1', { exact: true })).toHaveValue('קטע קריאה מתוקן');
  await stale.close();

  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(page.getByText('קטע קריאה מתוקן', { exact: true })).toBeVisible();
  await expect(page.getByText('גרסת תבנית 2', { exact: true })).toBeVisible();
  await page.goto(originalUrl);
  await expect(page.getByText('נועה מצאה 3 ספרים.\nShe loves reading.', { exact: true })).toBeVisible();
  await expect(page.getByText('גרסת תבנית 1', { exact: true })).toBeVisible();
  expect(errors).toEqual([]);
});

test('parents can generate arithmetic operations through the UI', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('browser@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page.getByRole('heading', { name: 'מה נלמד היום?', exact: true })).toBeVisible();
  for (const [operation, symbol] of [['addition', '+'], ['subtraction', '−'], ['division', '÷']]) {
    await page.goto('/templates/new');
    await page.getByLabel('שם התבנית').fill(`תרגול ${operation}`);
    await page.getByLabel('פעולת החשבון').selectOption(operation);
    await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
    await page.getByLabel('מספר שאלות', { exact: true }).fill('2');
    await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
    await expect(page.locator('.question-prompt')).toHaveCount(2);
    await expect(page.locator('.question-prompt').first()).toContainText(symbol);
    await expect(page.locator('.question-prompt').first()).toHaveCSS('direction', 'ltr');
  }
});
