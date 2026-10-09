import { expect, test, type Page } from '@playwright/test';
import { createReviewSnapshot } from './parent-review';
import { textSize } from './text-size';

test.use({ actionTimeout: 10_000 });

async function narrow(page: Page, name: string) {
  await page.setViewportSize({ width: 360, height: 800 });
  await textSize(page, 32);
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.documentElement).fontSize))
    .toBe('32px');
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    .toBe(true);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: test.info().outputPath(`${name}.png`) });
  await page.locator('footer').scrollIntoViewIfNeeded();
  await expect(page.getByRole('banner')).not.toBeInViewport();
  await page.screenshot({ path: test.info().outputPath(`${name}-end.png`) });
  await textSize(page, 16);
  await page.setViewportSize({ width: 1280, height: 900 });
}

test('parent manages access, assigns frozen work, recovers final grades and resets the family', async ({
  page,
  browser,
}) => {
  test.setTimeout(120_000);
  await page.goto('/children');
  await page.getByLabel('כתובת דוא״ל', { exact: true }).fill('browser@example.test');
  await page.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await page.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(page).toHaveURL('/activities/new');
  await page.getByRole('link', { name: 'הילדים', exact: true }).click();
  await page.getByLabel('שם הילד או הילדה').fill('פרופיל ללא פעילויות');
  await page.getByRole('button', { name: 'יצירת פרופיל', exact: true }).click();
  await expect(page.getByText('הפרופיל נשמר.', { exact: true })).toBeVisible();
  page.once('dialog', (dialog) => dialog.accept());
  await page.getByRole('button', { name: 'מחיקת הפרופיל', exact: true }).click();
  await expect(page.getByText('הפרופיל נמחק.', { exact: true })).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'ניהול הפרופיל: פרופיל ללא פעילויות', exact: true }),
  ).toHaveCount(0);
  await page.getByLabel('שם הילד או הילדה').fill('נועה');
  await page.getByLabel('כיתה (רשות)', { exact: true }).fill('כיתה ד׳');
  await page.getByLabel('גיל (רשות)', { exact: true }).fill('9');
  await page.getByRole('button', { name: 'יצירת פרופיל', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('הפרופיל נשמר.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'שמירת הפרופיל', exact: true })).toBeFocused();
  const headers = {
    'X-XSRF-TOKEN': (await (await page.request.get('/api/auth/csrf')).json()).token,
  };
  const child = (await (await page.request.get('/api/children')).json()).items[0];
  expect(child).toMatchObject({ grade: 'כיתה ד׳', age: 9 });
  expect(child.ageConfirmedAtUtc).toMatch(/Z$/);
  await expect(page.locator('#profile-dates')).not.toHaveAttribute('open');
  await page.locator('#profile-dates summary').click();
  await expect(page.getByText('הגיל עודכן ב־', { exact: false })).toBeVisible();
  await page.locator('#profile-dates summary').click();
  const saveProfile = async () => {
    const saved = page.waitForResponse(
      (response) =>
        response.url().endsWith(`/api/children/${child.id}`) &&
        response.request().method() === 'PUT',
    );
    await page.getByRole('button', { name: 'שמירת הפרופיל', exact: true }).click();
    const response = await saved;
    expect(response.status()).toBe(200);
    await expect(page.getByRole('button', { name: 'שמירת הפרופיל', exact: true })).toBeEnabled();
    return response.json();
  };
  await page.getByLabel('כיתה (רשות)', { exact: true }).fill('');
  expect(await saveProfile()).toMatchObject({
    grade: null,
    age: 9,
    ageConfirmedAtUtc: child.ageConfirmedAtUtc,
  });
  await page.getByLabel('כיתה (רשות)', { exact: true }).fill('כיתה ד׳');
  await page.getByLabel('גיל (רשות)', { exact: true }).fill('');
  expect(await saveProfile()).toMatchObject({
    grade: 'כיתה ד׳',
    age: null,
    ageConfirmedAtUtc: null,
  });
  await page.getByLabel('גיל (רשות)', { exact: true }).fill('9');
  await saveProfile();
  await page.reload();
  await page.getByRole('button', { name: 'ניהול הפרופיל: נועה', exact: true }).click();
  await expect(page.getByLabel('כיתה (רשות)', { exact: true })).toHaveValue('כיתה ד׳');
  await expect(page.getByLabel('גיל (רשות)', { exact: true })).toHaveValue('9');
  await page.getByLabel('שם המכשיר', { exact: true }).fill('הטאבלט של נועה');
  await page.getByRole('button', { name: 'יצירת קוד הפעלה', exact: true }).click();
  const codeBox = page.locator('[data-activation-code]');
  await expect(codeBox).toHaveText(/^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$/);
  const code = (await codeBox.textContent())!;
  expect(await page.evaluate(() => JSON.stringify(localStorage))).not.toContain(code);
  expect(await page.evaluate(() => JSON.stringify(sessionStorage))).not.toContain(code);
  const childContext = await browser.newContext();
  try {
    const childApi = childContext.request,
      base = new URL(page.url()).origin;
    const anonymous = {
      'X-XSRF-TOKEN': (await (await childApi.get(base + '/api/child/auth/csrf')).json()).token,
    };
    expect(
      (
        await childApi.post(base + '/api/child/auth/activate', {
          headers: anonymous,
          data: { code },
        })
      ).status(),
    ).toBe(204);
    const childHeaders = {
      'X-XSRF-TOKEN': (await (await childApi.get(base + '/api/child/auth/csrf')).json()).token,
    };
    await page.getByRole('button', { name: 'רענון המכשירים', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'הטאבלט של נועה', exact: true })).toBeVisible();
    await narrow(page, 'profiles-mobile');
    const snapshot = await createReviewSnapshot(page.request, headers);
    const extra = await createReviewSnapshot(page.request, headers, 'עבודה לביטול');
    const unused = await createReviewSnapshot(page.request, headers, 'פעילות ללא הקצאה');
    await page.goto(`/instances/${snapshot.id}`);
    await expect(codeBox).toHaveCount(0);
    await page.getByRole('combobox', { name: 'ילד או ילדה', exact: true }).selectOption(child.id);
    const created = page.waitForResponse(
      (response) =>
        response.url().endsWith('/api/assignments') && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'הקצאת הפעילות', exact: true }).click();
    const assignmentResponse = await created;
    expect(assignmentResponse.status()).toBe(201);
    const assignment = await assignmentResponse.json();
    await page.getByRole('link', { name: 'פתיחת ההקצאה', exact: true }).click();
    await expect(page.getByText('העבודה עדיין לא הוגשה.', { exact: false })).toBeVisible();
    await page.getByText('פרטי זמנים', { exact: true }).click();
    await expect(page.getByText('עוד לא נפתחה', { exact: true })).toBeVisible();
    await expect(page.locator('[data-elapsed-time]')).toHaveCount(0);
    await page.goto(`/instances/${snapshot.id}`);
    await page.getByRole('combobox', { name: 'ילד או ילדה', exact: true }).selectOption(child.id);
    await page.getByRole('button', { name: 'הקצאת הפעילות', exact: true }).click();
    await expect(page.getByRole('link', { name: 'פתיחת ההקצאה הקיימת' })).toHaveAttribute(
      'href',
      `/assignments/${assignment.id}`,
    );
    await narrow(page, 'snapshot-assignment-mobile');
    await page.goto(`/instances/${extra.id}`);
    await page.getByRole('combobox', { name: 'ילד או ילדה', exact: true }).selectOption(child.id);
    await page.getByRole('button', { name: 'הקצאת הפעילות', exact: true }).click();
    await expect(page.getByRole('link', { name: 'פתיחת ההקצאה', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'פעילויות לילדים', exact: true }).first().click();
    page.once('dialog', (dialog) => dialog.accept());
    await page
      .getByRole('button', { name: 'ביטול ההקצאה: עבודה לביטול — נועה', exact: true })
      .click();
    await expect(
      page.getByText('ההקצאה בוטלה. היא נשמרת בבחירה "ההקצאה בוטלה".', { exact: true }),
    ).toBeVisible();
    await page
      .getByRole('combobox', { name: 'מצב הפעילות', exact: true })
      .selectOption('withdrawn');
    await expect(
      page.getByRole('link', { name: 'עבודה לביטול — נועה', exact: true }),
    ).toBeVisible();
    await expect(page.locator('[data-withdraw]')).toHaveCount(0);
    await narrow(page, 'assignments-mobile');
    // Assigning the withdrawn pair again finds it; only the explicit restore gives the work back.
    await page.goto(`/instances/${extra.id}`);
    await page.getByRole('combobox', { name: 'ילד או ילדה', exact: true }).selectOption(child.id);
    await page.getByRole('button', { name: 'הקצאת הפעילות', exact: true }).click();
    await expect(page.getByText('בוטלה קודם', { exact: false })).toBeVisible();
    await page.getByRole('button', { name: 'החזרת ההקצאה', exact: true }).click();
    await expect(page.getByText('ההקצאה הוחזרה', { exact: false })).toBeVisible();
    const sessionPath = `${base}/api/child/assignments/${assignment.id}/session`;
    const learner = await (
      await childApi.get(`${base}/api/child/assignments/${assignment.id}`)
    ).json();
    expect(JSON.stringify(learner)).not.toContain('private-parent-key');
    expect(Object.keys(learner.document.questions[0]).sort()).toEqual([
      'id',
      'interaction',
      'points',
      'prompt',
    ]);
    expect((await childApi.post(sessionPath, { headers: childHeaders })).status()).toBe(201);
    await page.goto(`/assignments/${assignment.id}`);
    await page.getByText('פרטי זמנים', { exact: true }).click();
    await expect(page.getByText('נפתחה ב־', { exact: false })).toBeVisible();
    await expect(page.getByText('עוד לא הוגשה', { exact: true })).toBeVisible();
    await expect(page.locator('[data-elapsed-time]')).toHaveCount(0);
    const answers = [
      { questionId: snapshot.document.questions[0].id, value: '+02.00' },
      { questionId: snapshot.document.questions[1].id, value: '  תְּשׁוּבָה בעברית\n  ' },
    ];
    expect(
      (
        await childApi.post(sessionPath + '/submit', {
          headers: childHeaders,
          data: { expectedRevision: 1, answers },
        })
      ).status(),
    ).toBe(200);
    expect((await childApi.get(`${base}/api/assignments/${assignment.id}/result`)).status()).toBe(
      401,
    );
    await page.goto('/activities');
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'הסרת הפעילות: פעילות ללא הקצאה', exact: true }).click();
    await expect(
      page.getByRole('button', { name: 'הסרת הפעילות: פעילות ללא הקצאה', exact: true }),
    ).toHaveCount(0);
    expect((await page.request.get(`/api/instances/${unused.id}`)).status()).toBe(404);
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'הסרת הפעילות: בדיקת תשובות', exact: true }).click();
    await expect(page.getByText('הפעילות הוסרה.', { exact: true })).toBeVisible();
    await page.goto(`/assignments/${assignment.id}`);
    await expect(page.getByText('הפעילות בארכיון.', { exact: false })).toBeVisible();
    await expect(page.getByText('ניקוד אוטומטי עד כה:', { exact: false })).toBeVisible();
    await expect(page.locator('[data-elapsed-time]')).toHaveText('פחות מדקה');
    const elapsed = await page.locator('[data-elapsed-time]').textContent();
    const grade = page.getByLabel('נקודות לשאלה 2 — בין 0 ל־3', { exact: true });
    await expect(grade).toHaveValue('');
    await expect(page.locator('[data-child-answer]').last()).toHaveText(answers[1].value);
    await page.getByText('תשובה להורים — שאלה 2', { exact: true }).click();
    await expect(page.getByText('private-parent-key', { exact: true })).toBeVisible();
    await grade.fill('1e0');
    await page.getByRole('button', { name: 'שמירת ציונים וסיום הבדיקה', exact: true }).click();
    await expect(page.getByText('יש להזין מספר שלם בין 0 ל־3.', { exact: true })).toBeVisible();
    await grade.fill('2');
    page.once('dialog', (dialog) => dialog.dismiss());
    await page.getByRole('link', { name: 'המרחב שלנו', exact: true }).first().click();
    await expect(page).toHaveURL(`/assignments/${assignment.id}`);
    await narrow(page, 'review-mobile-light');
    await page
      .getByRole('group', { name: 'מצב תצוגה' })
      .locator('label', { hasText: 'כהה' })
      .click();
    await expect(page.getByRole('radio', { name: 'כהה', exact: true })).toBeChecked();
    await narrow(page, 'review-mobile-dark');
    await page.route(`**/api/assignments/${assignment.id}/review`, async (route) => {
      expect((await route.fetch()).status()).toBe(200);
      await route.abort('failed');
    });
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'שמירת ציונים וסיום הבדיקה', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('ייתכן שהציונים נשמרו');
    await expect(grade).toHaveValue('2');
    await page.getByRole('button', { name: 'קריאת התוצאה השמורה', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'התוצאה השמורה', exact: true })).toBeVisible();
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'הצגת התוצאה השמורה', exact: true }).click();
    await expect(grade).toHaveCount(0);
    await expect(page.getByText('ציון סופי: 7 מתוך 8', { exact: false })).toBeVisible();
    await expect(page.locator('[data-elapsed-time]')).toHaveText(elapsed!);
    const result = await (
      await page.request.get(`/api/assignments/${assignment.id}/result`)
    ).json();
    expect(result.document).toEqual(snapshot.document);
    expect(result.answers).toEqual(answers);
    expect((await (await childApi.get(sessionPath)).json()).finalTotal).toBe(7);
    await page.unroute(`**/api/assignments/${assignment.id}/review`);
    await page.getByRole('link', { name: 'הילדים', exact: true }).click();
    await expect(codeBox).toHaveCount(0);
    await page.getByRole('button', { name: 'ניהול הפרופיל: נועה', exact: true }).click();
    await expect(page.getByRole('button', { name: 'מחיקת הפרופיל', exact: true })).toHaveCount(0);
    await page.getByLabel('כיתה (רשות)', { exact: true }).fill('כיתה ה׳');
    await page.getByLabel('גיל (רשות)', { exact: true }).fill('10');
    await saveProfile();
    expect(
      await (await page.request.get(`/api/assignments/${assignment.id}/result`)).json(),
    ).toEqual(result);
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'ביטול הגישה: הטאבלט של נועה', exact: true }).click();
    await expect(page.getByText('הגישה מהמכשיר בוטלה.', { exact: true })).toBeVisible();
    expect((await childApi.get(base + '/api/child/auth/me')).status()).toBe(401);
    page.once('dialog', (dialog) => dialog.accept());
    await page
      .getByRole('button', { name: 'הסרת המכשיר מהרשימה: הטאבלט של נועה', exact: true })
      .click();
    await expect(page.getByRole('heading', { name: 'הטאבלט של נועה', exact: true })).toHaveCount(0);
    expect(
      await (await page.request.get(`/api/assignments/${assignment.id}/result`)).json(),
    ).toEqual(result);
    await page.getByLabel('פרופיל פעיל', { exact: true }).uncheck();
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'שמירת הפרופיל', exact: true }).click();
    await expect(page.getByText('הפרופיל מושבת.', { exact: false })).toBeVisible();
    await expect(page.getByRole('button', { name: 'יצירת קוד הפעלה', exact: true })).toHaveCount(0);
    await page.goto('/activities');
    await page.getByText('ניהול נתונים', { exact: true }).click();
    page.once('dialog', async (dialog) => {
      for (const scope of ['פרופילי הילדים', 'המכשירים', 'ההקצאות', 'התשובות', 'הציונים'])
        expect(dialog.message()).toContain(scope);
      await dialog.accept();
    });
    await page.getByRole('button', { name: 'איפוס נתוני הלמידה', exact: true }).click();
    await expect(page.getByText('נתוני הלמידה נמחקו.', { exact: true })).toBeVisible();
    expect((await (await page.request.get('/api/children')).json()).items).toEqual([]);
    expect((await (await page.request.get('/api/assignments')).json()).items).toEqual([]);
    expect((await page.request.get(`/api/instances/${snapshot.id}`)).status()).toBe(404);
  } finally {
    await childContext.close();
  }
});
