import { generateDraft } from './generate-draft';
import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { expectChildResponse } from './child-responses';
import { sourceText, suppliedPlan } from '../src/app/features/activities/learning-plan.fixture';

/** Approves isolated mixed-interaction content through real parent APIs using the suite's isolated provider. */
export async function createReviewSnapshot(
  parent: APIRequestContext,
  headers: Record<string, string>,
  title = 'בדיקת תשובות',
) {
  const plan = {
    ...suppliedPlan,
    questions: { ...suppliedPlan.questions, formats: ['numeric-input', 'text-input'] },
  };
  const created = await parent.post('/api/activity-drafts', {
    headers,
    data: { id: crypto.randomUUID(), plan },
  });
  expect(created.status()).toBe(201);
  const draft = await generateDraft(parent, headers, await created.json());
  const draftPath = `/api/activity-drafts/${draft.id}`;
  const saved = await parent.put(draftPath, {
    headers,
    data: {
      expectedRevision: draft.revision,
      plan,
      document: {
        title,
        instructions: 'ענו על השאלות',
        materials: [{ id: plan.materials[0].id, title: null, body: sourceText }],
        questions: [
          {
            id: draft.document.questions[0].id,
            prompt: 'מספר?',
            interaction: { type: 'numeric-input' },
            answer: { value: '2' },
            points: 5,
          },
          {
            id: draft.document.questions[1].id,
            prompt: 'הסבר?',
            interaction: { type: 'text-input' },
            answer: { value: 'private-parent-key' },
            points: 3,
          },
        ],
      },
    },
  });
  expect(saved.status()).toBe(200);
  const released = await parent.post(`${draftPath}/release`, {
    headers,
    data: { expectedRevision: (await saved.json()).revision },
  });
  expect(released.status()).toBe(201);
  const snapshot = await released.json();
  return snapshot;
}

/** Exercises parent grading using the workflow's existing independently authenticated contexts. */
export async function verifyParentReview(page: Page, child: APIRequestContext, childId: string) {
  const parent = page.request;
  const childBase = new URL(page.url()).origin;
  const headers = {
    'X-XSRF-TOKEN': (await (await parent.get('/api/auth/csrf')).json()).token,
  };
  const childHeaders = {
    'X-XSRF-TOKEN': (await (await child.get(childBase + '/api/child/auth/csrf')).json()).token,
  };
  const snapshot = await createReviewSnapshot(parent, headers);
  const assignment = await (
    await parent.post('/api/assignments', {
      headers,
      data: { childId: childId, snapshotId: snapshot.id },
    })
  ).json();
  const sessionPath = `${childBase}/api/child/assignments/${assignment.id}/session`;
  expect((await child.post(sessionPath, { headers: childHeaders })).status()).toBe(201);
  const answers = [
    { questionId: snapshot.document.questions[0].id, value: '+02.00' },
    { questionId: snapshot.document.questions[1].id, value: '  תְּשׁוּבָה בעברית\n  ' },
  ];
  const submitted = await child.post(`${sessionPath}/submit`, {
    headers: childHeaders,
    data: { expectedRevision: 1, answers },
  });
  expect(submitted.status()).toBe(200);
  expect((await submitted.json()).status).toBe('awaiting-review');
  const resultPath = `/api/assignments/${assignment.id}/result`;
  const reviewPath = `/api/assignments/${assignment.id}/review`;
  expect((await child.get(childBase + resultPath)).status()).toBe(401);
  const pending = await (await parent.get(resultPath)).json();
  expect(pending.document).toEqual(snapshot.document);
  expect(pending.document.materials[0].body).toBe(sourceText);
  expect(pending.answers).toEqual(answers);
  expect(pending.evaluation).toMatchObject({
    automaticSubtotal: 5,
    pendingCount: 1,
    finalTotal: null,
  });
  const review = {
    expectedRevision: pending.revision,
    grades: [{ questionId: snapshot.document.questions[1].id, points: 2 }],
  };
  expect(
    (await child.post(childBase + reviewPath, { headers: childHeaders, data: review })).status(),
  ).toBe(401);
  expect((await parent.delete(`/api/instances/${snapshot.id}`, { headers })).status()).toBe(204);
  const graded = await parent.post(reviewPath, { headers, data: review });
  expect(graded.status()).toBe(200);
  const result = await graded.json();
  expect(result.evaluation).toMatchObject({
    automaticSubtotal: 5,
    pendingCount: 0,
    finalTotal: 7,
    possibleTotal: 8,
  });
  expect(result.document).toEqual(pending.document);
  expect(result.answers).toEqual(answers);
  expect(result.reviewedByParentId).not.toBeNull();
  const replay = await parent.post(reviewPath, { headers, data: review });
  expect(replay.status()).toBe(200);
  expect(await replay.json()).toEqual(result);
  expect(await (await parent.get(resultPath)).json()).toEqual(result);
  const childResponse = await child.get(sessionPath);
  await expectChildResponse(childResponse);
  const childResult = await childResponse.json();
  expect(childResult).toMatchObject({
    status: 'completed',
    finalTotal: 7,
    possibleTotal: 8,
    answers,
  });
}
