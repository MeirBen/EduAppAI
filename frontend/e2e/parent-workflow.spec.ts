import { expect, test, type Page } from '@playwright/test';

/** Verify native document scrolling at a narrow viewport with enlarged text. */
async function checkNarrowLayout(page: Page, name: string) {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `../artifacts/${name}-mobile.png`, fullPage: true });
  await page.locator('footer').scrollIntoViewIfNeeded();
  await expect(page.locator('footer')).toBeInViewport();
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.evaluate(() => (document.documentElement.style.fontSize = '100%'));
  await page.setViewportSize({ width: 1440, height: 1000 });
}

async function login(page: Page, email = 'browser@example.test') {
  await page.goto('/');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill(email);
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

test('a parent prompt becomes an editable reusable template and distinct frozen tasks', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('lang', 'he');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { name: 'טוב שחזרתם' })).toBeVisible();
  await checkNarrowLayout(page, 'login');
  await page.screenshot({ path: '../artifacts/login-desktop.png', fullPage: true });
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'דילוג לתוכן הראשי' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
  await login(page);
  await expect(page.getByRole('heading', { name: 'מתחילים עם רעיון אחד' })).toBeVisible();
  await checkNarrowLayout(page, 'empty-library');
  const proposalResponse = page.waitForResponse('/api/ai/template-drafts');
  await propose(page, 'קטעי קריאה לכיתה ג׳ עם נושא ורמה לבחירה ושאלות מעורבות');
  const { definition } = await (await proposalResponse).json();
  await expect(page.getByLabel('הנחיות ליצירת המשימות')).toHaveValue(
    definition.generation.instructions,
  );
  expect((await (await page.request.get('/api/templates')).json()).length).toBe(0);
  await page.getByLabel('שם התבנית', { exact: true }).fill('קוראים ומגלים');
  const parameter = page.locator('[data-parameter-editor]').first();
  await parameter.getByLabel('שם השדה להורה').fill('מה נחקור?');
  await checkNarrowLayout(page, 'ai-template-review');
  const createdResponse = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/templates') && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
  const template = await (await createdResponse).json();
  expect(template.definition.schemaVersion).toBe(2);
  expect(template.definition.generation.instructions).toBe(definition.generation.instructions);
  await expect(page.getByLabel('מה נחקור?', { exact: true })).toHaveValue('דינוזאורים');
  await checkNarrowLayout(page, 'create-instance');
  await page.screenshot({ path: '../artifacts/create-instance-desktop.png', fullPage: true });
  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
  const originalUrl = page.url();
  const originalQuestions = await page.locator('.question-prompt').allTextContents();
  const originalContent = await page.locator('section').innerText();
  await expect(page.locator('.question-prompt')).toHaveCount(2);
  await page.screenshot({ path: '../artifacts/reading-preview-desktop.png', fullPage: true });
  await page.reload();
  await expect(page.locator('section')).toHaveText(originalContent, { useInnerText: true });
  await page.goto(`/templates/${template.id}/create`);
  const secondTheme = 'חלל' + 'A'.repeat(80);
  await page.getByLabel('מה נחקור?', { exact: true }).fill(secondTheme);
  await page.getByLabel('רמה', { exact: true }).selectOption('מאתגרת');
  await page.getByLabel('מספר שאלות', { exact: true }).fill('3');
  await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: `לומדים על ${secondTheme}`, exact: true }),
  ).toBeVisible();
  await expect(page.locator('.question-prompt')).toHaveCount(3);
  await page.getByText('הצגת התשובה לשאלה 1', { exact: true }).click();
  await expect(page.locator('details').first()).toHaveAttribute('open', '');
  await page.keyboard.press('Space');
  await expect(page.locator('details').first()).not.toHaveAttribute('open');
  await page.keyboard.press('Enter');
  await expect(page.locator('details').first()).toHaveAttribute('open', '');
  await checkNarrowLayout(page, 'ai-preview');
  await page.screenshot({ path: '../artifacts/ai-preview-desktop.png', fullPage: true });
  await page.goto(originalUrl);
  await expect(page.locator('.question-prompt')).toHaveText(originalQuestions);
  await expect(
    page.getByRole('heading', { name: 'לומדים על דינוזאורים', exact: true }),
  ).toBeVisible();
  await page.goto('/templates');
  await expect(page.getByRole('link', { name: 'יצירת תרגול: קוראים ומגלים' })).toBeVisible();
  await checkNarrowLayout(page, 'library');
  await page.screenshot({ path: '../artifacts/library-desktop.png', fullPage: true });
  await page.getByRole('button', { name: 'יציאה מהחשבון', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'טוב שחזרתם' })).toBeVisible();
  expect(errors).toEqual([]);
});

test('AI template revisions preserve snapshots and concurrent edits', async ({ page, context }) => {
  await login(page);
  await propose(page, 'שאלות מדעים בנושאים משתנים');
  const createdResponse = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/templates') && response.request().method() === 'POST',
  );
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

test('AI failures preserve the prompt and explicit retry can recover', async ({ page }) => {
  await login(page, 'failures@example.test');
  const before = (await (await page.request.get('/api/templates')).json()).length;
  await page.goto('/templates/new');
  await page.getByLabel('הרעיון שלכם לתבנית').fill('בדיקת כשל');
  await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('לא נשמר דבר');
  await expect(page.getByLabel('הרעיון שלכם לתבנית')).toHaveValue('בדיקת כשל');
  await page.getByLabel('הרעיון שלכם לתבנית').fill('בדיקת מגבלת פלט');
  await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('מגבלת הפלט');
  await expect(page.getByRole('alert')).toContainText('לא נשמר דבר');
  await expect(page.getByLabel('הרעיון שלכם לתבנית')).toHaveValue('בדיקת מגבלת פלט');
  for (const prompt of ['בדיקת מכסה', 'בדיקת מכסה בגוף התשובה']) {
    await page.getByLabel('הרעיון שלכם לתבנית').fill(prompt);
    const limited = page.waitForResponse('/api/ai/template-drafts');
    await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
    expect((await limited).status()).toBe(429);
    await expect(page.getByRole('alert')).toContainText('מגבלת הבקשות');
    await expect(page.getByRole('alert')).not.toContainText('private provider');
    await expect(page.getByRole('alert')).not.toContainText('דקה');
    await expect(page.getByLabel('הרעיון שלכם לתבנית')).toHaveValue(prompt);
  }
  await page.getByLabel('הרעיון שלכם לתבנית').fill('מילים באנגלית לתלמידים מתחילים');
  await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'בדיקה ועריכת התבנית' })).toBeVisible();
  await page.getByRole('button', { name: 'ביטול ההצעה וחזרה לרעיון' }).click();
  await expect(page.getByRole('heading', { name: 'בדיקה ועריכת התבנית' })).toHaveCount(0);
  expect((await (await page.request.get('/api/templates')).json()).length).toBe(before);
});

test.describe('publication recovery', () => {
  test.use({ serviceWorkers: 'block' });

  test('loading is accessible and customizable, and a failed save preserves edits for retry', async ({
    page,
  }) => {
    await login(page);
    const before = (await (await page.request.get('/api/templates')).json()).length;
    let finishGeneration!: () => void;
    const generationGate = new Promise<void>((resolve) => (finishGeneration = resolve));
    await page.route(
      '**/api/ai/template-drafts',
      async (route) => {
        await generationGate;
        await route.continue();
      },
      { times: 1 },
    );
    await page.goto('/templates/new');
    await page.getByLabel('הרעיון שלכם לתבנית').fill('הבנת הנקרא עם נושא ורמת קושי לבחירה');
    await page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }).click();
    try {
      const loading = page.getByRole('status').filter({ hasText: 'בונים את התבנית שלכם' });
      await expect(loading).toBeVisible();
      await expect(loading).toContainText('זה עשוי לקחת כמה דקות');
      await expect(page.getByLabel('הרעיון שלכם לתבנית')).toBeDisabled();
      expect(await loading.evaluate((element) => element.closest('[aria-busy="true"]'))).toBeNull();
      const mark = loading.locator('.loader-mark');
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      expect(
        await mark.evaluate((element) => getComputedStyle(element, '::before').animationName),
      ).not.toBe('none');
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: '../artifacts/loader-desktop.png', fullPage: true });
      await checkNarrowLayout(page, 'loader');
      await loading.evaluate((element) => {
        element.style.setProperty('--loader-size', '4rem');
        element.style.setProperty('--loader-color', 'rgb(100, 50, 150)');
        element.style.setProperty('--loader-duration', '3s');
      });
      await expect(mark).toHaveCSS('width', '64px');
      await expect(mark).toHaveCSS('color', 'rgb(100, 50, 150)');
      expect(
        await mark.evaluate((element) => getComputedStyle(element, '::before').animationDuration),
      ).toBe('3s');
      await page.emulateMedia({ reducedMotion: 'reduce' });
      expect(
        await mark.evaluate((element) => getComputedStyle(element, '::before').animationName),
      ).toBe('none');
      await expect(loading).toBeVisible();
    } finally {
      finishGeneration();
    }
    await expect(page.getByRole('heading', { name: 'בדיקה ועריכת התבנית' })).toBeVisible();
    await expect(page.locator('app-loading-indicator .loader-mark')).toHaveCount(0);
    await page.getByLabel('שם התבנית', { exact: true }).fill('העריכה נשמרת גם אחרי כשל');
    const parameter = page.locator('[data-parameter-editor]').first();
    await parameter.getByLabel('שם השדה להורה').fill('נושא הקריאה שלי');
    let finishSave!: () => void;
    const saveGate = new Promise<void>((resolve) => (finishSave = resolve));
    await page.route(
      '**/api/templates',
      async (route) => {
        await saveGate;
        await route.fulfill({
          status: 500,
          contentType: 'application/problem+json',
          body: JSON.stringify({ status: 500 }),
        });
      },
      { times: 1 },
    );

    await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
    try {
      await expect(page.getByRole('status').filter({ hasText: 'שומרים את התבנית' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'שומרים…', exact: true })).toBeDisabled();
    } finally {
      finishSave();
    }
    await expect(page.getByRole('alert')).toContainText('השרת לא הצליח להשלים');
    await expect(page.locator('app-loading-indicator .loader-mark')).toHaveCount(0);
    await expect(page.getByLabel('שם התבנית', { exact: true })).toHaveValue(
      'העריכה נשמרת גם אחרי כשל',
    );
    await expect(parameter.getByLabel('שם השדה להורה')).toHaveValue('נושא הקריאה שלי');
    expect((await (await page.request.get('/api/templates')).json()).length).toBe(before);

    await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
    await expect(page.getByLabel('נושא הקריאה שלי', { exact: true })).toHaveValue('דינוזאורים');
    expect((await (await page.request.get('/api/templates')).json()).length).toBe(before + 1);
  });
});

test.describe('library cleanup', () => {
  // Route interception must see API requests instead of the published PWA's service worker.
  test.use({ serviceWorkers: 'block' });

  test('confirms deletion, handles failures and resets saved learning data', async ({ page }) => {
    await login(page, 'cleanup@example.test');
    await propose(page, 'תרגול מילים עם נושא לבחירה');
    await page.getByLabel('שם התבנית', { exact: true }).fill('תבנית למחיקה');
    await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
    await expect(page.getByRole('button', { name: 'יצירת טיוטה', exact: true })).toBeVisible();
    const creationUrl = page.url();
    await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
    await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
    const draftUrl = page.url();
    await page.goto(creationUrl);
    await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
    await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
    await page.goto('/templates');
    const drafts = page.getByRole('region', { name: 'טיוטות שמורות' });
    const removeDraft = drafts
      .getByRole('button', { name: 'מחיקת טיוטה: לומדים על דינוזאורים' })
      .first();
    const before = (await (await page.request.get('/api/instances')).json()).length;
    await removeDraft.click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('button', { name: 'ביטול', exact: true })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(dialog).not.toBeVisible();
    await expect(removeDraft).toBeFocused();
    expect((await (await page.request.get('/api/instances')).json()).length).toBe(before);
    await removeDraft.click();
    await page.route('**/api/instances/*', (route) =>
      route.request().method() === 'DELETE' ? route.abort() : route.continue(),
    );
    await dialog.getByRole('button', { name: 'מחיקה לצמיתות', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText('לא ניתן להתחבר לשרת');
    expect((await (await page.request.get('/api/instances')).json()).length).toBe(before);
    await page.unroute('**/api/instances/*');
    let finishDeletion!: () => void;
    const deletionGate = new Promise<void>((resolve) => (finishDeletion = resolve));
    await page.route('**/api/instances/*', async (route) => {
      if (route.request().method() === 'DELETE') {
        await deletionGate;
      }
      await route.continue();
    });
    await dialog.getByRole('button', { name: 'מחיקה לצמיתות', exact: true }).click();
    await expect(dialog.getByRole('button', { name: 'מוחקים…', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'ביטול', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('status')).toContainText('מוחקים');
    expect(
      await dialog.getByRole('status').evaluate((element) => element.closest('[aria-busy="true"]')),
    ).toBeNull();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();
    finishDeletion();
    await expect(dialog).not.toBeVisible();
    await page.unroute('**/api/instances/*');
    await expect(page.getByRole('status').filter({ hasText: 'הטיוטה נמחקה' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'מה נלמד היום?' })).toBeFocused();
    expect((await (await page.request.get('/api/instances')).json()).length).toBe(before - 1);

    await page.getByRole('button', { name: 'מחיקת תבנית: תבנית למחיקה', exact: true }).click();
    await expect(dialog).toContainText('כל הגרסאות שלה והטיוטות שנוצרו ממנה');
    await dialog.getByRole('button', { name: 'מחיקה לצמיתות', exact: true }).click();
    await expect(
      page.getByRole('status').filter({ hasText: 'התבנית והטיוטות שלה נמחקו' }),
    ).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'מחיקת תבנית: תבנית למחיקה', exact: true }),
    ).toHaveCount(0);
    expect(
      (await page.request.get(draftUrl.replace('/instances/', '/api/instances/'))).status(),
    ).toBe(404);

    // Keep reset coverage independent of the earlier tests' saved content.
    await propose(page, 'תרגול קריאה חדש');
    await page.getByRole('button', { name: 'שמירת התבנית', exact: true }).click();
    await page.getByRole('button', { name: 'יצירת טיוטה', exact: true }).click();
    await expect(page.getByText('הטיוטה נשמרה', { exact: false })).toBeVisible();
    await page.goto('/templates');
    await page.getByRole('button', { name: 'איפוס נתוני הלמידה', exact: true }).click();
    await expect(dialog).toContainText('החשבון והגדרות ה־AI יישארו');
    await page.setViewportSize({ width: 360, height: 800 });
    await page.evaluate(() => (document.documentElement.style.fontSize = '200%'));
    expect(await dialog.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(
      true,
    );
    await page.screenshot({ path: '../artifacts/library-reset-mobile.png', fullPage: true });
    await dialog.getByRole('button', { name: 'ביטול', exact: true }).click();
    await expect(page.locator('article')).not.toHaveCount(0);
    await page.getByRole('button', { name: 'איפוס נתוני הלמידה', exact: true }).click();
    await dialog.getByRole('button', { name: 'מחיקה לצמיתות', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'מתחילים עם רעיון אחד' })).toBeVisible();
    expect((await (await page.request.get('/api/templates')).json()).length).toBe(0);
    expect((await (await page.request.get('/api/instances')).json()).length).toBe(0);
    await page.reload();
    await expect(page.getByRole('heading', { name: 'מתחילים עם רעיון אחד' })).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'איפוס נתוני הלמידה', exact: true }),
    ).toBeDisabled();
    await page.goto('/templates/new');
    await expect(
      page.getByRole('button', { name: 'יצירת תבנית בעזרת AI', exact: true }),
    ).toBeEnabled();
  });
});
