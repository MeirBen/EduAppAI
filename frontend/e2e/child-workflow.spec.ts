import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { suppliedPlan } from '../src/app/features/activities/learning-plan.fixture';
import { textSize } from './text-size';
import { expectChildResponse, inspectChildResponses } from './child-responses';

const story = 'פסקה ראשונה בעברית.\n\nSecond paragraph — keep 2 + 3 in order.';
const poem = 'שורה ראשונה\nשורה שנייה\nשורה שלישית';
const exactAnswer = '  תשובה מְנֻקֶּדֶת — exact text\n  ';

test.use({ actionTimeout: 10_000 });

/** Three real interaction types with immutable, manually reviewed test content; no generation calls. */
async function createSnapshot(parent: APIRequestContext, headers: Record<string, string>) {
  const { schemaVersion } = await (await parent.get('/api/ai/status')).json();
  const plan = {
    ...suppliedPlan,
    schemaVersion,
    defaults: { ...suppliedPlan.defaults, questionCount: 3 },
    materials: [
      { ...suppliedPlan.materials[0], text: story },
      {
        ...suppliedPlan.materials[0],
        id: '22222222222222222222222222222222',
        label: 'שיר',
        text: poem,
      },
    ],
    questions: {
      ...suppliedPlan.questions,
      formats: ['numeric-input', 'text-input', 'single-choice'],
      choiceCount: { value: 2, adjustable: false },
    },
  };
  const created = await parent.post('/api/activity-drafts', {
    headers,
    data: { plan, input: { settings: plan.defaults } },
  });
  expect(created.status()).toBe(201);
  const draft = await created.json();
  const path = `/api/activity-drafts/${draft.id}`;
  const saved = await parent.put(path, {
    headers,
    data: {
      expectedRevision: draft.revision,
      plan,
      input: draft.input,
      document: {
        title: 'מסע הקריאה שלי',
        instructions: 'קראו וענו בקצב שלכם.',
        materials: [
          { id: plan.materials[0].id, title: null, body: story },
          { id: plan.materials[1].id, title: null, body: poem },
        ],
        questions: [
          {
            id: null,
            prompt: '2 + 3 = ?',
            interaction: { type: 'numeric-input' },
            answer: { value: '5' },
            points: 2,
          },
          {
            id: null,
            prompt: 'מה למדתם מהטקסט?',
            interaction: { type: 'text-input' },
            answer: { value: 'private-child-workflow-key' },
            points: 3,
          },
          {
            id: null,
            prompt: 'איזו אפשרות בחרתם?',
            // A long option with an unbreakable number must wrap inside its card at any text size.
            interaction: {
              type: 'single-choice',
              options: [
                'אדום כמו תפוח בשל, או בעצם 12345678901234567890123456789012345678901234567890',
                'blue',
              ],
            },
            answer: { value: 'blue' },
            points: 1,
          },
        ],
      },
    },
  });
  expect(saved.status(), await saved.text()).toBe(200);
  const released = await parent.post(path + '/release', {
    headers,
    data: { expectedRevision: (await saved.json()).revision },
  });
  expect(released.status()).toBe(201);
  return released.json();
}

async function assignToNewChild(
  parent: APIRequestContext,
  headers: Record<string, string>,
  options: { name: string; deviceLabel: string; snapshotId: string },
) {
  const created = await parent.post('/api/children', { headers, data: { name: options.name } });
  expect(created.status()).toBe(201);
  const profile = await created.json();
  const issued = await parent.post(`/api/children/${profile.id}/activation`, {
    headers,
    data: { deviceLabel: options.deviceLabel },
  });
  expect(issued.status()).toBe(200);
  const assigned = await parent.post('/api/assignments', {
    headers,
    data: { childId: profile.id, snapshotId: options.snapshotId },
  });
  expect(assigned.status()).toBe(201);
  return { profile, assignment: await assigned.json(), code: (await issued.json()).code };
}

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
  await page.screenshot({ animations: 'disabled', path: test.info().outputPath(name + '.png') });
  if (await page.locator('#answer-form').count()) {
    await page.locator('#answer-form input[inputmode="decimal"]').scrollIntoViewIfNeeded();
    await page.screenshot({
      animations: 'disabled',
      path: test.info().outputPath(name + '-numeric.png'),
    });
    await page.locator('#answer-form fieldset').scrollIntoViewIfNeeded();
    await page.screenshot({
      animations: 'disabled',
      path: test.info().outputPath(name + '-choice.png'),
    });
  }
  await page.locator('footer').scrollIntoViewIfNeeded();
  await expect(page.getByRole('banner')).not.toBeInViewport();
  await page.screenshot({
    animations: 'disabled',
    path: test.info().outputPath(name + '-end.png'),
  });
  await textSize(page, 16);
  await page.setViewportSize({ width: 1280, height: 900 });
}

test('two families and siblings keep separate work through resume, lost submission, grading and reset', async ({
  page: parent,
  browser,
  request,
}) => {
  test.setTimeout(120_000);
  const callsBefore = await (await request.get('http://127.0.0.1:5203/__stats')).json();
  await parent.goto('/login');
  await parent.getByLabel('כתובת דוא״ל', { exact: true }).fill('source@example.test');
  await parent.getByLabel('סיסמה', { exact: true }).fill('TestOnly!Parent12345');
  await parent.getByRole('button', { name: 'כניסה למרחב שלנו' }).click();
  await expect(parent).toHaveURL('/activities/new');
  const headers = {
    'X-XSRF-TOKEN': (await (await parent.request.get('/api/auth/csrf')).json()).token,
  };
  const snapshot = await createSnapshot(parent.request, headers);
  const { profile, assignment, code } = await assignToNewChild(parent.request, headers, {
    name: 'נועה',
    deviceLabel: 'מכשיר הילדה',
    snapshotId: snapshot.id,
  });
  const baseURL = new URL(parent.url()).origin;
  let context = await browser.newContext({ baseURL, serviceWorkers: 'block' });
  let child = await context.newPage();
  const otherParent = await browser.newContext({ baseURL, serviceWorkers: 'block' });
  const siblingContext = await browser.newContext({ baseURL, serviceWorkers: 'block' });
  const foreignContext = await browser.newContext({ baseURL, serviceWorkers: 'block' });
  await inspectChildResponses(child);
  const childRequests: string[] = [];
  let submissions = 0;
  const trackRequests = (page: Page) =>
    page.on('request', (req) => {
      const path = new URL(req.url()).pathname;
      if (path.startsWith('/api/')) childRequests.push(path);
      if (req.method() === 'POST' && req.url().endsWith('/session/submit')) submissions++;
    });
  trackRequests(child);
  try {
    const anonymous = await (await otherParent.request.get('/api/auth/csrf')).json();
    expect(
      (
        await otherParent.request.post('/api/auth/login', {
          headers: { 'X-XSRF-TOKEN': anonymous.token },
          data: { email: 'blockers@example.test', password: 'TestOnly!Parent12345' },
        })
      ).status(),
    ).toBe(204);
    const foreignHeaders = {
      'X-XSRF-TOKEN': (await (await otherParent.request.get('/api/auth/csrf')).json()).token,
    };
    const foreignSnapshot = await createSnapshot(otherParent.request, foreignHeaders);
    const peers = [];
    for (const { owner, ownerHeaders, peerContext, content, name } of [
      {
        owner: parent.request,
        ownerHeaders: headers,
        peerContext: siblingContext,
        content: snapshot,
        name: 'אח',
      },
      {
        owner: otherParent.request,
        ownerHeaders: foreignHeaders,
        peerContext: foreignContext,
        content: foreignSnapshot,
        name: 'משפחה אחרת',
      },
    ]) {
      const { assignment: peerAssignment, code: peerCode } = await assignToNewChild(
        owner,
        ownerHeaders,
        {
          name,
          deviceLabel: name,
          snapshotId: content.id,
        },
      );
      const peerId = peerAssignment.id;
      const peerPage = await peerContext.newPage();
      await inspectChildResponses(peerPage);
      trackRequests(peerPage);
      await peerPage.goto('/child/activate');
      await peerPage.getByLabel('הקוד מההורה', { exact: true }).fill(peerCode);
      await peerPage.getByRole('button', { name: 'כניסה', exact: true }).click();
      await expect(peerPage).toHaveURL('/child');
      await peerPage.getByRole('link', { name: content.document.title, exact: true }).click();
      await peerPage.getByRole('textbox', { name: '2 + 3 = ?', exact: true }).fill('1');
      await peerPage.getByRole('textbox', { name: 'מה למדתם מהטקסט?', exact: true }).fill(name);
      await peerPage.getByRole('button', { name: 'שמירה', exact: true }).click();
      await expect(peerPage.getByText('נשמר', { exact: true })).toBeVisible();
      const peerPath = `/api/child/assignments/${peerId}/session`;
      const saved = await peerContext.request.get(peerPath);
      await expectChildResponse(saved);
      peers.push({
        context: peerContext,
        page: peerPage,
        id: peerId,
        path: peerPath,
        saved: await saved.json(),
      });
    }
    await child.goto('/child');
    await expect(child).toHaveURL('/child/activate');
    await expect(child.getByText('המכשיר הזה יזכור אתכם', { exact: false })).toBeVisible();
    await narrow(child, 'activation-mobile');
    await child.getByLabel('הקוד מההורה', { exact: true }).fill(code);
    await child.getByRole('button', { name: 'כניסה', exact: true }).click();
    await expect(child).toHaveURL('/child');
    await expect(
      child.getByRole('link', { name: snapshot.document.title, exact: true }),
    ).toBeVisible();
    await expect(child.getByRole('navigation', { name: 'ניהול המשפחה' })).toHaveCount(0);
    expect(
      await child.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage)),
    ).not.toContain(code);
    // Reopen a fresh browser context with only the persistent grant, without reactivating.
    const cookie = (await context.cookies()).find(
      (cookie) => cookie.name === 'FamilyLearning.Child',
    )!;
    expect(cookie.httpOnly).toBe(true);
    expect(cookie.expires).toBeGreaterThan(Date.now() / 1000);
    await context.close();
    context = await browser.newContext({
      baseURL,
      serviceWorkers: 'block',
      storageState: { cookies: [cookie], origins: [] },
    });
    child = await context.newPage();
    await inspectChildResponses(child);
    trackRequests(child);
    await child.goto('/child');
    await expect(child).toHaveURL('/child');
    expect((await context.cookies()).find((c) => c.name === cookie.name)?.expires).toBe(
      cookie.expires,
    );
    // Let the reopened app finish identity/CSRF bootstrap before making independent API writes.
    await expect(
      child.getByRole('link', { name: snapshot.document.title, exact: true }),
    ).toBeVisible();
    const learners = [{ context, id: assignment.id }, ...peers];
    for (const learner of learners) {
      const csrf = await learner.context.request.get('/api/child/auth/csrf');
      await expectChildResponse(csrf);
      const childHeaders = { 'X-XSRF-TOKEN': (await csrf.json()).token };
      const inbox = await learner.context.request.get('/api/child/assignments');
      await expectChildResponse(inbox);
      expect((await inbox.json()).items.map((item: { id: string }) => item.id)).toEqual([
        learner.id,
      ]);
      const parentDenied = await learner.context.request.get(
        `/api/assignments/${assignment.id}/result`,
      );
      expect(parentDenied.status()).toBe(401);
      await expectChildResponse(parentDenied);
      for (const target of learners.filter((target) => target.id !== learner.id)) {
        for (const suffix of ['', '/session']) {
          const denied = await learner.context.request.get(
            `/api/child/assignments/${target.id}${suffix}`,
          );
          expect(denied.status()).toBe(404);
          await expectChildResponse(denied);
        }
        const denied = await learner.context.request.put(
          `/api/child/assignments/${target.id}/session`,
          {
            headers: childHeaders,
            data: { expectedRevision: 1, answers: [] },
          },
        );
        expect(denied.status()).toBe(404);
        await expectChildResponse(denied);
      }
    }
    await narrow(child, 'inbox-mobile');
    const detailResponse = child.waitForResponse((r) =>
      r.url().endsWith(`/api/child/assignments/${assignment.id}`),
    );
    await child.getByRole('link', { name: snapshot.document.title, exact: true }).click();
    const detail = await (await detailResponse).json();
    expect(JSON.stringify(detail)).not.toContain('private-child-workflow-key');
    await expect(child.locator('[data-material]').first()).toHaveText(story);
    expect(await child.locator('[data-material]').first().textContent()).toBe(story);
    expect(await child.locator('[data-material]').last().textContent()).toBe(poem);
    const numeric = child.getByRole('textbox', { name: '2 + 3 = ?', exact: true });
    const text = child.getByRole('textbox', { name: 'מה למדתם מהטקסט?', exact: true });
    const choice = child.getByRole('group', { name: 'איזו אפשרות בחרתם?', exact: true });
    await numeric.fill('-');
    await text.fill(exactAnswer);
    await choice.getByRole('radio', { name: 'blue', exact: true }).check();
    child.once('dialog', (dialog) => dialog.dismiss());
    await child.getByRole('link', { name: 'לפעילויות שלי', exact: true }).click();
    await expect(child).toHaveURL(`/child/assignments/${assignment.id}`);
    await child.getByRole('button', { name: 'שמירה', exact: true }).click();
    await expect(child.getByText('נשמר', { exact: true })).toBeVisible();
    await child.reload();
    await expect(numeric).toHaveValue('-');
    await expect(text).toHaveValue(exactAnswer);
    await expect(choice.getByRole('radio', { name: 'blue', exact: true })).toBeChecked();
    await child.getByRole('button', { name: 'הגשה להורה', exact: true }).click();
    await expect(numeric).toBeFocused();
    expect(submissions).toBe(0);
    await numeric.fill('+05.00');
    await narrow(child, 'player-mobile-light');
    await child
      .getByRole('group', { name: 'מצב תצוגה' })
      .locator('label', { hasText: 'כהה' })
      .click();
    await narrow(child, 'player-mobile-dark');
    const sessionPath = `/api/child/assignments/${assignment.id}/session`;
    await child.route(
      '**' + sessionPath + '/submit',
      async (route) => {
        const response = await route.fetch();
        expect(response.status()).toBe(200);
        await expectChildResponse(response);
        await route.abort('failed');
      },
      { times: 1 },
    );
    child.once('dialog', async (dialog) => {
      expect(dialog.message()).toContain('אחרי ההגשה אי אפשר לשנות');
      await dialog.accept();
    });
    await child.getByRole('button', { name: 'הגשה להורה', exact: true }).click();
    await expect(child.getByRole('alert')).toContainText('אולי זה כבר נשמר');
    await expect(text).toHaveValue(exactAnswer);
    await child.getByRole('button', { name: 'בדיקת עדכונים', exact: true }).click();
    await expect(child.getByRole('heading', { name: 'העבודה הוגשה', exact: true })).toBeVisible();
    await expect(text).toBeDisabled();
    await expect(child.getByText('הציון:', { exact: false })).toHaveCount(0);
    expect(submissions).toBe(1);
    child.once('dialog', (dialog) => dialog.accept());
    await child.getByRole('button', { name: 'טעינת התשובות השמורות', exact: true }).click();
    expect(
      await child
        .locator(`[data-saved-answer="${snapshot.document.questions[1].id}"]`)
        .textContent(),
    ).toBe(exactAnswer);
    const receiptResponse = await context.request.get(sessionPath);
    await expectChildResponse(receiptResponse);
    const receipt = await receiptResponse.json();
    expect(receipt.finalTotal).toBeNull();
    await narrow(child, 'receipt-mobile');
    await parent.goto(`/assignments/${assignment.id}`);
    expect(
      await parent
        .locator(`[data-child-answer="${snapshot.document.questions[1].id}"]`)
        .textContent(),
    ).toBe(exactAnswer);
    await parent.getByLabel('נקודות לשאלה 2 — בין 0 ל־3', { exact: true }).fill('2');
    parent.once('dialog', (dialog) => dialog.accept());
    await parent.getByRole('button', { name: 'שמירת ציונים וסיום הבדיקה', exact: true }).click();
    await expect(parent.getByText('ציון סופי:', { exact: false })).toBeVisible();
    await child.getByRole('button', { name: 'בדיקת עדכונים', exact: true }).click();
    await expect(child.getByText('הציון:', { exact: false })).toContainText('5 מתוך 6');
    await child.getByRole('button', { name: 'טעינת התשובות השמורות', exact: true }).click();
    const finalResponse = await context.request.get(sessionPath);
    await expectChildResponse(finalResponse);
    const finalResult = await finalResponse.json();
    expect(finalResult).toMatchObject({
      status: 'completed',
      finalTotal: 5,
      possibleTotal: 6,
      answers: receipt.answers,
    });
    await child.reload();
    await expect(child.getByText('הציון:', { exact: false })).toContainText('5 מתוך 6');
    const reread = await context.request.get(sessionPath);
    await expectChildResponse(reread);
    expect(await reread.json()).toEqual(finalResult);
    await child.getByRole('link', { name: 'לפעילויות שלי', exact: true }).click();
    await child
      .getByRole('group', { name: 'אילו פעילויות להציג?' })
      .locator('label', { hasText: 'הגשתי' })
      .click();
    await expect(child.getByRole('radio', { name: 'הגשתי', exact: true })).toBeChecked();
    await expect(
      child.getByRole('link', { name: snapshot.document.title, exact: true }),
    ).toBeVisible();
    await expect(child.getByText('הבדיקה הושלמה.', { exact: true })).toHaveCount(0);
    expect(
      await child.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage)),
    ).not.toContain(exactAnswer);
    child.once('dialog', (dialog) => dialog.accept());
    await child.getByRole('button', { name: 'יציאה', exact: true }).click();
    await expect(child).toHaveURL('/child/activate');
    const disconnected = await context.request.get('/api/child/auth/me');
    expect(disconnected.status()).toBe(401);
    await expectChildResponse(disconnected);
    const devices = await (await parent.request.get(`/api/children/${profile.id}/devices`)).json();
    expect(devices.items[0].revokedAtUtc).not.toBeNull();
    for (const peer of peers) {
      const saved = await peer.context.request.get(peer.path);
      await expectChildResponse(saved);
      expect(await saved.json()).toEqual(peer.saved);
    }
    expect((await parent.request.delete('/api/templates', { headers })).status()).toBe(204);
    const revokedSibling = await siblingContext.request.get('/api/child/auth/me');
    expect(revokedSibling.status()).toBe(401);
    await expectChildResponse(revokedSibling);
    const foreign = peers[1];
    await foreign.page.reload();
    await expect(
      foreign.page.getByRole('textbox', { name: 'מה למדתם מהטקסט?', exact: true }),
    ).toHaveValue('משפחה אחרת');
    const surviving = await foreign.context.request.get(foreign.path);
    await expectChildResponse(surviving);
    expect(await surviving.json()).toEqual(foreign.saved);
    expect((await otherParent.request.get(`/api/instances/${foreignSnapshot.id}`)).status()).toBe(
      200,
    );
    expect(childRequests.filter((path) => !path.startsWith('/api/child/'))).toEqual([]);
    expect(await (await request.get('http://127.0.0.1:5203/__stats')).json()).toEqual(callsBefore);
  } finally {
    await Promise.all([
      context.close(),
      otherParent.close(),
      siblingContext.close(),
      foreignContext.close(),
    ]);
  }
});
