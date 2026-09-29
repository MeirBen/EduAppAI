import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import test from 'node:test';

// This existing test dependency does not ship TypeScript declarations.
const { JSDOM } = createRequire(import.meta.url)('jsdom');

/** @typedef {{ method?: string, headers?: Record<string, string>, mode?: string, body?: string }} RequestOptions */
/** @typedef {{ ok: boolean, status: number, json?: () => Promise<unknown> }} FakeResponse */

const assetRoot = new URL('../../tools/FamilyLearning.Evaluation/wwwroot/', import.meta.url);
const source = await readFile(new URL('app.js', assetRoot), 'utf8');
const ui = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const html = await readFile(new URL('index.html', assetRoot), 'utf8');
const nextTurn = () => new Promise((resolve) => setTimeout(resolve, 0));
const setup = {
  profile: { Model: 'fake/model', FallbackModel: null },
  configured: true,
  cases: ['first', 'second'].map((id) => ({
    id,
    prompt: 'בקשה',
    reviewFocus: 'עברית',
    questionCount: 2,
    interaction: 'text-input',
  })),
  calibrationCount: 3,
  judgeAvailable: true,
  judgeError: null,
  csrfToken: 'test-token',
};

/** @param {Record<string, (options: RequestOptions) => FakeResponse>} overrides */
function mount(overrides = {}) {
  const dom = new JSDOM(html, { url: 'http://127.0.0.1:5180' });
  /** @type {{ url: string, options: RequestOptions }[]} */
  const requests = [];
  /** @param {string} url @param {RequestOptions} options */
  const fetch = async (url, options = {}) => {
    requests.push({ url, options });
    const route = overrides[url];
    if (route) return route(options);
    /** @type {Record<string, unknown>} */
    const routes = { '/api/setup': setup, '/api/active': null, '/api/runs': [] };
    const body = routes[url];
    assert.notEqual(body, undefined, `Unexpected request: ${url}`);
    return { ok: true, status: 200, json: async () => body };
  };
  const dashboard = ui.createDashboard(dom.window.document, fetch);
  return { dom, requests, dashboard, document: dom.window.document };
}

test('generated tasks and retained raw output render HTML-like values only as text', () => {
  const document = new JSDOM().window.document;
  const attack = '<img src=x onerror="alert(1)">';
  const task = ui.renderTask(document, {
    title: attack,
    instructions: 'הוראות',
    contentBlocks: [{ type: 'text', text: attack }],
    questions: [
      {
        id: 'q1',
        prompt: attack,
        interaction: { type: 'single-choice', options: [attack, 'ב'] },
        answer: { value: attack },
        points: 1,
      },
    ],
  });
  assert.equal(task.querySelectorAll('img, script').length, 0);
  assert.ok(task.textContent.includes(attack));
  assert.ok(task.querySelector('[dir="auto"]'));
  assert.ok(task.textContent.includes('Answer key'));
});

test('incomparable Hebrew and human deltas cannot appear as quality evidence', () => {
  const document = new JSDOM().window.document;
  const comparison = ui.renderComparison(document, {
    directlyComparable: false,
    incompatibilities: ['incomplete-run'],
    hebrewFindingsComparable: false,
    profileChanges: {},
    deltas: { generatedHebrewIssues: 987, 'humanReview.hebrew.average': 654 },
    checkFailureDeltas: {},
    hebrewKindDeltas: { spelling: 987 },
    humanReviewComparable: { hebrew: false },
    baseline: {},
    candidate: {},
  });
  const quality = comparison.querySelector('[data-section="hebrew"]');
  assert.match(quality.textContent, /not directly comparable/);
  assert.doesNotMatch(quality.textContent, /987/);
  assert.doesNotMatch(comparison.querySelector('[data-section="human"]').textContent, /654/);
  assert.match(comparison.firstElementChild.textContent, /incomplete/i);
});

test('startup is read-only; live submission needs confirmation and sends one bounded ordered request with CSRF', async () => {
  const app = mount({
    '/api/runs': (options) => ({
      ok: true,
      status: options.method === 'POST' ? 202 : 200,
      json: async () => (options.method === 'POST' ? { id: 'run-1' } : []),
    }),
  });
  try {
    await app.dashboard.ready;
    assert.ok(app.requests.every(({ options }) => !options.method || options.method === 'GET'));
    const { document, dom } = app;
    for (const checkbox of [...document.querySelectorAll('[name="caseId"]')].reverse())
      checkbox.checked = true;
    document.querySelector('#judge').checked = true;
    document.querySelector('#max-calls').value = '9';
    document
      .querySelector('#run-form')
      .dispatchEvent(new dom.window.Event('change', { bubbles: true }));
    document
      .querySelector('#run-form')
      .dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }));
    assert.equal(app.requests.filter(({ options }) => options.method === 'POST').length, 0);
    assert.match(document.querySelector('#confirm-message').textContent, /9 OpenRouter calls/);
    document.querySelector('#confirm-run').click();
    await nextTurn();
    const writes = app.requests.filter(({ options }) => options.method === 'POST');
    assert.equal(writes.length, 1);
    const request = writes[0].options;
    assert.ok(request.headers);
    assert.ok(request.body);
    assert.equal(request.headers['X-Evaluation-CSRF'], 'test-token');
    assert.equal(request.mode, 'same-origin');
    assert.deepEqual(JSON.parse(request.body).caseIds, ['first', 'second']);
    assert.equal(JSON.parse(request.body).confirmed, true);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('failed paid submissions are never retried automatically and expose only ProblemDetails title', async () => {
  const app = mount({
    '/api/runs': (options) => ({
      ok: options.method !== 'POST',
      status: options.method === 'POST' ? 409 : 200,
      json: async () =>
        options.method === 'POST'
          ? { title: 'A run is already active.', detail: 'secret provider response' }
          : [],
    }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('[name="caseId"]').checked = true;
    app.document
      .querySelector('#run-form')
      .dispatchEvent(new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
    app.document.querySelector('#confirm-run').click();
    await nextTurn();
    assert.equal(app.requests.filter(({ options }) => options.method === 'POST').length, 1);
    assert.match(app.document.querySelector('#message').textContent, /already active/);
    assert.doesNotMatch(app.document.body.textContent, /secret provider response/);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

const validTask = {
  title: 'בדיקת עברית',
  instructions: 'הוראות',
  contentBlocks: [{ type: 'text', text: 'קטע קריאה' }],
  questions: [
    {
      id: 'q1',
      prompt: 'שאלה',
      interaction: { type: 'text-input' },
      answer: { value: 'תשובה' },
      points: 1,
    },
  ],
};
const completedReport = {
  status: 'completed',
  label: 'Reviewed run',
  runNotes: 'Run notes',
  startedAtUtc: '2026-09-01T10:00:00Z',
  finishedAtUtc: '2026-09-01T10:01:00Z',
  cases: setup.cases,
  profile: setup.profile,
  judgeEnabled: false,
  results: [
    {
      caseId: 'first',
      repetition: 1,
      checks: { questionCount: true },
      generation: {
        contractValid: true,
        finishedAtUtc: '2026-09-01T10:01:00Z',
        output: JSON.stringify(validTask),
        request: [{ role: 'user', text: '<script>request</script>' }],
      },
      authoring: {
        contractValid: false,
        finishedAtUtc: '2026-09-01T10:00:10Z',
        output: '<img src=x onerror="alert(1)">',
      },
      review: {},
    },
  ],
};
const runSummary = {
  status: 'completed',
  caseCount: 1,
  repeat: 1,
  scenarioAutomaticPasses: 1,
  plannedCaseRuns: 1,
  actualModels: ['fake/model'],
  judgeEnabled: false,
  generatedHebrewIssueCount: 0,
  costCredits: { knownTotal: null, missingCalls: 2 },
  averageLatencyMilliseconds: null,
};
/** @param {unknown} body */
const jsonResponse = (body) => ({ ok: true, status: 200, json: async () => body });

test('invalid case fixtures leave saved history and active-run polling available without enabling paid work', async () => {
  const caseError = 'Evaluation cases are unavailable or invalid. Saved runs remain available.';
  const app = mount({
    '/api/setup': () => jsonResponse({ ...setup, cases: [], caseError }),
    '/api/runs': () => jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }]),
  });
  try {
    await app.dashboard.ready;
    assert.match(
      app.document.querySelector('#cases').textContent,
      /cases are unavailable or invalid/,
    );
    assert.match(app.document.querySelector('#history-list').textContent, /Saved run/);
    assert.ok(app.requests.some(({ url }) => url === '/api/active'));
    assert.equal(app.document.querySelector('#start-run').disabled, true);
    assert.ok(app.requests.every(({ options }) => options.method === 'GET'));
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('saved reports safely render retained output and submit only human review fields', async () => {
  let historyReads = 0;
  const app = mount({
    '/api/runs': () =>
      ++historyReads === 1
        ? jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }])
        : { ok: false, status: 500, json: async () => ({ title: 'History is unavailable.' }) },
    '/api/runs/run-1': () =>
      jsonResponse({ id: 'run-1', report: completedReport, summary: runSummary }),
    '/api/runs/run-1/review': () => ({ ok: true, status: 204 }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('#history-list button').click();
    await nextTurn();
    const report = app.document.querySelector('#report');
    assert.equal(report.querySelectorAll('img, script').length, 0);
    assert.match(report.textContent, /<img src=x onerror=/);
    assert.match(report.textContent, /<script>request<\/script>/);
    assert.match(report.textContent, /בדיקת עברית/);
    const form = report.querySelector('.review');
    form.querySelector('select').value = '2';
    form.querySelector('textarea').value = 'הערה <b>plain text</b>';
    form.dispatchEvent(new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
    await nextTurn();
    const write = app.requests.find(({ options }) => options.method === 'PUT');
    assert.ok(write);
    assert.ok(write.options.body);
    assert.deepEqual(JSON.parse(write.options.body), {
      caseId: 'first',
      repetition: 1,
      review: {
        hebrew: 2,
        correctness: null,
        ageFit: null,
        adherence: null,
        answerClarity: null,
        consistency: null,
        notes: 'הערה <b>plain text</b>',
      },
    });
    assert.equal(write.options.headers?.['X-Evaluation-CSRF'], 'test-token');
    assert.match(form.textContent, /Review saved/);
    assert.equal(
      JSON.parse(report.querySelector('[data-saved-report] pre').textContent).results[0].review
        .hebrew,
      2,
    );
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('active runs disable new paid work and review editing; cancellation is CSRF protected', async () => {
  const app = mount({
    '/api/active': () =>
      jsonResponse({
        id: 'run-1',
        running: true,
        status: 'running',
        progress: { stage: 'generation', completedCalls: 1, plannedCalls: 2 },
      }),
    '/api/runs': () => jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }]),
    '/api/runs/run-1': () =>
      jsonResponse({
        id: 'run-1',
        report: { ...completedReport, status: 'running', finishedAtUtc: null },
        summary: runSummary,
      }),
    '/api/runs/run-1/cancel': () => ({
      ok: true,
      status: 202,
      json: async () => ({ status: 'cancelling' }),
    }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('[name="caseId"]').checked = true;
    app.document
      .querySelector('#run-form')
      .dispatchEvent(new app.dom.window.Event('change', { bubbles: true }));
    assert.equal(app.document.querySelector('#start-run').disabled, true);
    app.document.querySelector('#open-active').click();
    await nextTurn();
    assert.equal(app.document.querySelector('.review fieldset').disabled, true);
    app.document.querySelector('#cancel-run').click();
    await nextTurn();
    const writes = app.requests.filter(({ options }) => options.method === 'POST');
    assert.equal(writes.length, 1);
    assert.equal(writes[0].url, '/api/runs/run-1/cancel');
    assert.equal(writes[0].options.headers?.['X-Evaluation-CSRF'], 'test-token');
    assert.match(
      app.document.querySelector('#active-progress').textContent,
      /Cancellation requested/,
    );
    assert.equal(app.document.querySelector('#message').hidden, true);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('selection budget must cover all planned calls before a confirmation can open', async () => {
  const app = mount();
  try {
    await app.dashboard.ready;
    app.document.querySelector('#select-all').click();
    app.document.querySelector('#judge').checked = true;
    app.document
      .querySelector('#run-form')
      .dispatchEvent(new app.dom.window.Event('change', { bubbles: true }));
    assert.equal(app.document.querySelector('#start-run').disabled, true);
    assert.match(app.document.querySelector('#budget-message').textContent, /at least 9 calls/);
    app.document
      .querySelector('#run-form')
      .dispatchEvent(new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
    assert.equal(app.document.querySelector('#run-confirmation').hasAttribute('open'), false);
    assert.equal(app.requests.filter(({ options }) => options.method === 'POST').length, 0);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});
