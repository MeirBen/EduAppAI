import { expect, type APIResponse, type Page } from '@playwright/test';

/** Exact wire allowlists, independent of client DTOs, for every exercised learner response. */
export async function expectChildResponse(response: APIResponse) {
  if (response.status() === 204) return;
  const body = await response.json();
  const path = new URL(response.url()).pathname;
  const keys = (value: object, allowed: string[]) =>
    expect(Object.keys(value).sort(), path).toEqual(allowed.sort());
  if (response.status() >= 400) {
    keys(body, ['type', 'title', 'status', 'traceId']);
    expect(body.status).toBe(response.status());
  } else if (path.endsWith('/csrf')) {
    keys(body, ['token']);
  } else if (path.endsWith('/me')) {
    keys(body, ['answerLength', 'childId', 'expiresAtUtc', 'name']);
  } else if (/\/session(?:\/submit)?$/.test(path)) {
    keys(body, [
      'answers',
      'assignmentId',
      'finalTotal',
      'possibleTotal',
      'reviewedAtUtc',
      'revision',
      'savedAtUtc',
      'startedAtUtc',
      'status',
      'submittedAtUtc',
    ]);
    for (const answer of body.answers) keys(answer, ['questionId', 'value']);
  } else if (path.endsWith('/assignments')) {
    keys(body, ['items', 'page', 'pageSize', 'hasMore']);
    for (const item of body.items)
      keys(item, ['createdAtUtc', 'hasStarted', 'id', 'revision', 'status', 'title']);
  } else {
    expect(path).toMatch(/\/api\/child\/assignments\/[^/]+$/);
    keys(body, ['createdAtUtc', 'document', 'id', 'revision', 'status']);
    keys(body.document, ['instructions', 'materials', 'questions', 'title']);
    for (const material of body.document.materials) keys(material, ['body', 'id', 'title']);
    for (const question of body.document.questions) {
      keys(question, ['id', 'interaction', 'points', 'prompt']);
      keys(question.interaction, ['options', 'type']);
    }
  }
}

/** Inspect the real server body before delivery; Angular request cleanup can evict Chromium's body. */
export async function inspectChildResponses(page: Page) {
  await page.route('**/api/child/**', async (route) => {
    // Streams stay unbuffered; backend tests verify their content-free wire format.
    if (route.request().resourceType() === 'eventsource') {
      expect(new URL(route.request().url()).pathname).toBe('/api/child/changes');
      return route.continue();
    }
    const response = await route.fetch({ maxRedirects: 0, maxRetries: 0 });
    await expectChildResponse(response);
    await route.fulfill({ response });
  });
}
