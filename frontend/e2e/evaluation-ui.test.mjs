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
function deferred() {
  /** @type {(value: unknown) => void} */
  let resolve = () => {};
  const promise = new Promise((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}
const setup = {
  profile: { Model: 'fake/model', FallbackModel: null },
  configured: true,
  cases: ['first', 'second'].map((id, index) => ({
    id,
    prompt: 'בקשה',
    reviewFocus: 'עברית',
    questionCount: 2,
    additionalParameterCount: index,
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
    assert.match(document.querySelector('#cases').textContent, /additional fields: 0/);
    assert.match(document.querySelector('#cases').textContent, /additional fields: 1/);
    for (const checkbox of [...document.querySelectorAll('[name="caseId"]')].reverse())
      checkbox.checked = true;
    document.querySelector('#judge').checked = true;
    document.querySelector('#max-calls').value = '9';
    document
      .querySelector('#run-form')
      .dispatchEvent(new dom.window.Event('input', { bubbles: true }));
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
    assert.equal(JSON.parse(request.body).callDelaySeconds, 5);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('call spacing must be bounded and the chosen pause is included in confirmation', async () => {
  const app = mount();
  try {
    await app.dashboard.ready;
    const { document, dom } = app;
    document.querySelector('[name="caseId"]').checked = true;
    const delay = document.querySelector('#call-delay');
    const form = document.querySelector('#run-form');
    for (const value of ['', '-1', '61', '1.5']) {
      delay.value = value;
      form.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
      assert.equal(document.querySelector('#start-run').disabled, true);
    }
    for (const value of ['0', '15', '60']) {
      delay.value = value;
      form.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
      assert.equal(document.querySelector('#start-run').disabled, false);
      form.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }));
      assert.match(
        document.querySelector('#confirm-message').textContent,
        new RegExp(`${value}-second pause`),
      );
      document.querySelector('#dismiss-confirmation').click();
    }
    assert.ok(app.requests.every(({ options }) => options.method === 'GET'));
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

test('judge guidance separates per-result reviews from once-per-run calibration without starting work', async () => {
  const app = mount();
  try {
    await app.dashboard.ready;
    const { document, dom } = app;
    const estimate = () =>
      Object.fromEntries(
        [...document.querySelectorAll('#run-estimate dt')].map((label) => [
          label.textContent,
          label.nextElementSibling.textContent,
        ]),
      );
    document.querySelector('[name="caseId"]').checked = true;
    document.querySelector('#judge').checked = true;
    document
      .querySelector('#run-form')
      .dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    assert.equal(estimate()['Template + task calls'], '2');
    assert.equal(estimate()['Additional Hebrew review calls'], '1');
    assert.equal(estimate()['Calibration calls (once per run)'], '3');
    assert.equal(estimate()['Base calls (without retries)'], '6');
    assert.equal(document.querySelector('#start-run').disabled, true);

    document.querySelector('#repeat').value = '2';
    document.querySelector('#max-calls').value = '9';
    document
      .querySelector('#run-form')
      .dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    assert.equal(estimate()['Template + task calls'], '4');
    assert.equal(estimate()['Additional Hebrew review calls'], '2');
    assert.equal(estimate()['Calibration calls (once per run)'], '3');
    assert.equal(estimate()['Base calls (without retries)'], '9');
    assert.equal(document.querySelector('#start-run').disabled, false);

    document.querySelector('#judge').checked = false;
    document
      .querySelector('#run-form')
      .dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    assert.equal(estimate()['Additional Hebrew review calls'], '0');
    assert.equal(estimate()['Calibration calls (once per run)'], '0');
    assert.equal(estimate()['Base calls (without retries)'], '4');
    assert.ok(app.requests.every(({ options }) => options.method === 'GET'));
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
  automaticChecksVersion: 6,
  callDelaySeconds: 0,
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
const jsonResponse = (body) => ({ ok: true, status: 200, json: async () => structuredClone(body) });

test('rejected stages show field validation errors as text and retain them in the AI export', async () => {
  const message = 'יש לבחור שדה מספרי. <img src=x onerror="alert(1)">';
  const report = {
    ...completedReport,
    results: [
      {
        ...completedReport.results[0],
        authoring: {
          ...completedReport.results[0].authoring,
          failure: 'urn:family-learning:ai-validation',
          validationErrors: { 'generation.defaults.questionCount': [message] },
        },
      },
    ],
  };
  const app = mount({
    '/api/runs': () => jsonResponse([{ id: 'run-1', summary: runSummary }]),
    '/api/runs/run-1': () => jsonResponse({ report, summary: runSummary }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('#history-list button').click();
    await nextTurn();
    const stage = [...app.document.querySelectorAll('.stage')].find(
      (item) => item.querySelector('h4').textContent === 'Template authoring',
    );
    assert.ok(stage.textContent.includes('generation.defaults.questionCount'));
    assert.ok(stage.textContent.includes(message));
    assert.ok(!stage.textContent.includes('urn:family-learning:ai-validation'));
    assert.equal(stage.querySelectorAll('img, script').length, 0);
    assert.match(ui.reportBrief('run-1', report, runSummary), /validationErrors/);
    assert.ok(ui.reportBrief('run-1', report, runSummary).includes(JSON.stringify(message)));
    assert.ok(
      ui.reportBrief('run-1', report, runSummary).includes('urn:family-learning:ai-validation'),
    );
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('timeouts show a failure status while missing cost data stays separate from retry status', async () => {
  for (const [knownTotal, missingCalls, expected] of [
    [null, 1, 'Not reported for 1 call'],
    [null, 2, 'Not reported for 2 calls'],
    [0.25, 1, '0.25 credits reported; data missing for 1 call'],
    [0, 0, '0 credits'],
  ]) {
    const summary = { ...runSummary, costCredits: { knownTotal, missingCalls } };
    const report = {
      ...completedReport,
      results: [
        {
          ...completedReport.results[0],
          generation: {
            ...completedReport.results[0].generation,
            contractValid: false,
            statusCode: 504,
            failure: 'timeout',
            output: null,
          },
        },
      ],
    };
    const app = mount({
      '/api/runs': () => jsonResponse([{ id: 'run-1', summary }]),
      '/api/runs/run-1': () => jsonResponse({ report, summary }),
    });
    try {
      await app.dashboard.ready;
      assert.ok(app.document.querySelector('#history-list').textContent.includes(expected));
      app.document.querySelector('#history-list button').click();
      await nextTurn();
      const cost = [...app.document.querySelectorAll('#report dt')].find(
        (item) => item.textContent === 'Reported cost',
      );
      assert.equal(cost.nextElementSibling.textContent, expected);
      const generation = [...app.document.querySelectorAll('.stage')].find(
        (item) => item.querySelector('h4').textContent === 'Task generation',
      );
      assert.match(generation.textContent, /Timed out/);
      assert.doesNotMatch(generation.textContent, /retry|unknown/i);
    } finally {
      app.dashboard.dispose();
      app.dom.window.close();
    }
  }
});

test('findings expose captured source context and measurements without treating invalid calibration as misses', async () => {
  const source = 'יש ליצור כותרת מתאימה לפחות מפסקה אחת קצרה. <img src=x onerror="alert(1)">';
  const report = {
    ...completedReport,
    judgeEnabled: true,
    calibration: [
      {
        sample: { id: 'invalid-path', texts: [], expectedIssues: [] },
        call: { contractValid: false, responseReceived: true },
        passed: false,
        missingExpectedIssueCount: 3, // Invalid reviews must not present stored counts as measured misses.
        unexpectedFindingCount: 0,
      },
    ],
    results: [
      {
        ...completedReport.results[0],
        passageWordCount: 99,
        repeatedAnswerPosition: 1,
        judge: {
          contractValid: true,
          request: [
            {
              role: 'user',
              text: JSON.stringify({ texts: [{ path: 'template.instructions', text: source }] }),
            },
          ],
        },
        issues: [
          {
            path: 'template.instructions',
            quote: 'מתאימה לפחות',
            suggestion: 'מתאימה ולפחות',
            reason: 'חסר חיבור',
            kind: 'grammar-syntax',
          },
        ],
      },
    ],
  };
  const app = mount({
    '/api/runs': () => jsonResponse([{ id: 'run-1', summary: runSummary }]),
    '/api/runs/run-1': () =>
      jsonResponse({
        report,
        summary: { ...runSummary, judgeEnabled: true, judgeCalibrationPassed: false },
      }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('#history-list button').click();
    await nextTurn();
    const rendered = app.document.querySelector('#report');
    assert.ok(rendered.textContent.includes('Template instructions'));
    assert.ok(rendered.textContent.includes(source));
    assert.match(rendered.textContent, /Passage words: 99/);
    assert.match(rendered.textContent, /same option position \(1\)/);
    assert.match(rendered.textContent, /intentional ordering/);
    assert.match(rendered.textContent, /Invalid judge response; detection counts unavailable/);
    assert.equal(rendered.querySelectorAll('img, script').length, 0);
    const brief = ui.reportBrief('run-1', report, runSummary);
    assert.match(brief, /"passageWordCount": 99/);
    assert.match(brief, /"repeatedAnswerPosition": 1/);
    assert.match(brief, /"source": "Template instructions"/);
    assert.ok(brief.includes(JSON.stringify(source)));
    assert.match(brief, /"missingExpectedIssueCount": null/);
    assert.doesNotMatch(brief, /"missingExpectedIssueCount": 3/);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('history and reports distinguish unreviewed Hebrew from completed reviews with no findings', async () => {
  for (const [judgeEnabled, reviewed, findings, expected] of [
    [false, 0, 0, 'Not reviewed (judge disabled)'],
    [true, 0, 0, 'No completed content reviews'],
    [true, 1, 0, '0 findings across 1 reviewed result'],
    [true, 1, 2, '2 findings across 1 reviewed result'],
  ]) {
    const summary = {
      ...runSummary,
      judgeEnabled,
      generatedHebrewIssueCount: findings,
      generatedContentReviews: { succeeded: reviewed },
    };
    const app = mount({
      '/api/runs': () => jsonResponse([{ id: 'run-1', summary }]),
      '/api/runs/run-1': () =>
        jsonResponse({ report: { ...completedReport, judgeEnabled }, summary }),
    });
    try {
      await app.dashboard.ready;
      assert.ok(app.document.querySelector('#history-list').textContent.includes(expected));
      app.document.querySelector('#history-list button').click();
      await nextTurn();
      const label = [...app.document.querySelectorAll('#report dt')].find(
        (item) => item.textContent === 'Generated Hebrew findings',
      );
      assert.equal(label?.nextElementSibling.textContent, expected);
      assert.doesNotMatch(app.document.querySelector('#report').textContent, /Passage words:/);
      assert.ok(app.requests.every(({ options }) => options.method === 'GET'));
    } finally {
      app.dashboard.dispose();
      app.dom.window.close();
    }
  }
});

test('unavailable AI disables paid work while saved reports remain accessible', async () => {
  const app = mount({
    '/api/setup': () => jsonResponse({ ...setup, configured: false, profile: {} }),
    '/api/runs': () => jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }]),
    '/api/runs/run-1': () => jsonResponse({ report: completedReport, summary: runSummary }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('#select-all').click();
    assert.equal(app.document.querySelector('#start-run').disabled, true);
    assert.match(
      app.document.querySelector('#configuration-message').textContent,
      /missing or invalid/,
    );
    app.document
      .querySelector('#run-form')
      .dispatchEvent(new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
    assert.equal(app.document.querySelector('#run-confirmation').hasAttribute('open'), false);
    app.document.querySelector('#history-list button').click();
    await nextTurn();
    assert.equal(app.document.querySelector('.review fieldset').disabled, false);
    assert.ok(app.requests.every(({ options }) => options.method === 'GET'));
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

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
  const pending = deferred();
  const reportData = structuredClone(completedReport);
  reportData.results.push({ ...structuredClone(reportData.results[0]), caseId: 'second' });
  const savedSummary = {
    ...runSummary,
    humanReview: { hebrew: { reviewed: 1, unreviewed: 1, average: 2 } },
  };
  let copied = '';
  const app = mount({
    '/api/runs': () =>
      ++historyReads === 1
        ? jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }])
        : { ok: false, status: 500, json: async () => ({ title: 'History is unavailable.' }) },
    '/api/runs/run-1': () => jsonResponse({ id: 'run-1', report: reportData, summary: runSummary }),
    '/api/runs/run-1/review': () => ({
      ok: true,
      status: 200,
      json: async () => {
        await pending.promise;
        return savedSummary;
      },
    }),
  });
  try {
    Object.defineProperty(app.dom.window.navigator, 'clipboard', {
      value: {
        /** @param {string} text */
        writeText: async (text) => {
          copied = text;
        },
      },
    });
    await app.dashboard.ready;
    app.document.querySelector('#history-list button').click();
    await nextTurn();
    const report = app.document.querySelector('#report');
    assert.equal(report.querySelectorAll('img, script').length, 0);
    assert.match(report.textContent, /<img src=x onerror=/);
    assert.match(report.textContent, /<script>request<\/script>/);
    assert.match(report.textContent, /בדיקת עברית/);
    const form = report.querySelector('.review');
    const otherForm = report.querySelectorAll('.review')[1];
    otherForm.querySelector('textarea').value = 'Unsaved review stays here';
    form.querySelector('select').value = '2';
    form.querySelector('textarea').value = 'הערה <b>plain text</b>';
    form.dispatchEvent(new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
    await nextTurn();
    otherForm.dispatchEvent(
      new app.dom.window.Event('submit', { bubbles: true, cancelable: true }),
    );
    await nextTurn();
    assert.equal(app.requests.filter(({ options }) => options.method === 'PUT').length, 1);
    pending.resolve(null);
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
    assert.equal(otherForm.querySelector('textarea').value, 'Unsaved review stays here');
    assert.equal(
      [...report.querySelectorAll('.chip')].filter((chip) => chip.textContent === 'reviewed 1/6')
        .length,
      2,
    );
    report.querySelector('button.prominent').click();
    await nextTurn();
    assert.match(copied, /"humanReview"/);
    assert.match(copied, /"average": 2/);
    assert.equal(
      JSON.parse(report.querySelector('[data-saved-report] pre').textContent).results[0].review
        .hebrew,
      2,
    );
  } finally {
    pending.resolve(null);
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
      .dispatchEvent(new app.dom.window.Event('input', { bubbles: true }));
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

test('delayed report reads and errors cannot replace a newer selection or undo navigation', async () => {
  let pending = deferred();
  let failed = false;
  /** @param {string} label */
  const saved = (label) => ({ report: { ...completedReport, label }, summary: runSummary });
  const app = mount({
    '/api/runs': () => jsonResponse(['older', 'newer'].map((id) => ({ id, summary: runSummary }))),
    '/api/runs/older': () => ({
      ok: !failed,
      status: failed ? 500 : 200,
      json: () => pending.promise,
    }),
    '/api/runs/newer': () => jsonResponse(saved('Newer selection')),
  });
  try {
    await app.dashboard.ready;
    const [older, newer] = app.document.querySelectorAll('#history-list button');
    older.click();
    newer.click();
    await nextTurn();
    assert.equal(app.document.querySelector('#report h2').textContent, 'Newer selection');
    pending.resolve(saved('Older selection'));
    await nextTurn();
    assert.equal(app.document.querySelector('#report h2').textContent, 'Newer selection');

    pending = deferred();
    older.click();
    app.document.querySelector('[data-view="compare"]').click();
    pending.resolve(saved('Older selection'));
    await nextTurn();
    assert.equal(app.document.querySelector('#view-compare').hidden, false);
    assert.equal(app.document.querySelector('#report h2').textContent, 'Newer selection');

    pending = deferred();
    failed = true;
    older.click();
    app.document.querySelector('[data-view="new"]').click();
    pending.resolve({ title: 'Obsolete failure' });
    await nextTurn();
    assert.equal(app.document.querySelector('#message').hidden, true);
  } finally {
    pending.resolve(saved('Older selection'));
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('a completed run refreshes its report without navigating away from the current view', async () => {
  let running = true;
  const refreshed = deferred();
  const app = mount({
    '/api/active': () =>
      jsonResponse({ id: 'run-1', running, status: running ? 'running' : 'completed' }),
    '/api/runs/run-1': () => {
      if (!running) refreshed.resolve(undefined);
      return jsonResponse({
        report: { ...completedReport, status: running ? 'running' : 'completed' },
        summary: runSummary,
      });
    },
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('#open-active').click();
    await nextTurn();
    assert.equal(app.document.querySelector('.review fieldset').disabled, true);
    app.document.querySelector('[data-view="compare"]').click();
    running = false;
    await refreshed.promise;
    await nextTurn();
    assert.equal(app.document.querySelector('.review fieldset').disabled, false);
    assert.equal(app.document.querySelector('#view-compare').hidden, false);
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
      .dispatchEvent(new app.dom.window.Event('input', { bubbles: true }));
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

test('a status poll started before a new run cannot hide its cancellation controls', async () => {
  let polls = 0;
  const pending = deferred();
  const polling = deferred();
  const app = mount({
    '/api/active': () => ({
      ok: true,
      status: 200,
      json: async () => {
        if (++polls === 1) return null;
        polling.resolve(undefined);
        return pending.promise;
      },
    }),
    '/api/runs': (options) => jsonResponse(options.method === 'POST' ? { id: 'new-run' } : []),
  });
  try {
    await app.dashboard.ready;
    await polling.promise;
    app.document.querySelector('[name="caseId"]').checked = true;
    const form = app.document.querySelector('#run-form');
    form.dispatchEvent(new app.dom.window.Event('input', { bubbles: true }));
    form.dispatchEvent(new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
    app.document.querySelector('#confirm-run').click();
    await nextTurn();
    pending.resolve(null);
    await nextTurn();
    assert.equal(app.document.querySelector('#active-run').hidden, false);
    assert.equal(app.document.querySelector('#cancel-run').hidden, false);
    assert.equal(app.document.querySelector('#start-run').disabled, true);
  } finally {
    pending.resolve(null);
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('the case filter narrows the list and selecting follows it while clearing empties everything', async () => {
  const app = mount();
  try {
    await app.dashboard.ready;
    const { document, dom } = app;
    const filter = /** @type {HTMLInputElement} */ (document.querySelector('#case-filter'));
    filter.value = 'SECOND';
    filter.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    const visible = [...document.querySelectorAll('.case:not([hidden])')];
    assert.deepEqual(
      visible.map((entry) => entry.querySelector('input')?.value),
      ['second'],
    );
    document.querySelector('#select-all').click();
    const checked = () =>
      [...document.querySelectorAll('[name="caseId"]:checked')].map(
        (input) => /** @type {HTMLInputElement} */ (input).value,
      );
    assert.deepEqual(checked(), ['second']);
    assert.match(document.querySelector('#case-count').textContent, /1 of 2 selected/);
    filter.value = 'no such case';
    filter.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    assert.equal(document.querySelector('#cases-empty').hidden, false);
    document.querySelector('#select-none').click();
    assert.deepEqual(checked(), []);
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('opening a report shows waiting feedback until the read finishes', async () => {
  const pending = deferred();
  const app = mount({
    '/api/runs': () => jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }]),
    '/api/runs/run-1': () => ({
      ok: true,
      status: 200,
      json: async () => {
        await pending.promise;
        return { report: completedReport, summary: runSummary };
      },
    }),
  });
  try {
    await app.dashboard.ready;
    const open = app.document.querySelector('#history-list button');
    open.click();
    await nextTurn();
    assert.equal(open.getAttribute('aria-busy'), 'true');
    assert.ok(app.document.body.hasAttribute('data-loading'));
    pending.resolve(null);
    await nextTurn();
    await nextTurn();
    assert.equal(open.hasAttribute('aria-busy'), false);
    assert.equal(app.document.body.hasAttribute('data-loading'), false);
    assert.equal(app.document.querySelector('#report h2').textContent, 'Reviewed run');
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});

test('the AI brief carries context, requests, outputs, findings and reviews without repeating messages', async () => {
  const repeated = { ...completedReport.results[0], repetition: 2 };
  const report = {
    ...completedReport,
    automaticChecksVersion: 2,
    judgeEnabled: true,
    calibration: [
      {
        sample: {
          id: 'intentional-errors',
          request: 'הטעויות באפשרויות התשובה מכוונות.',
          texts: [{ path: 'task.options[0]', text: 'שלושה ילדות' }],
          expectedIssues: [],
          allowUnexpectedFindings: false,
        },
        call: { contractValid: true, request: [], output: '{"issues":[]}' },
        issues: [],
        passed: true,
      },
    ],
    results: [
      {
        ...completedReport.results[0],
        input: {
          settings: { topic: 'חלל', audience: 'מבוגרים', difficulty: 'hard', questionCount: 2 },
          parameters: {},
        },
        review: { hebrew: 2, notes: 'טוב' },
        authoring: { ...completedReport.results[0].authoring, output: '```\n# escaped' },
      },
      repeated,
    ],
  };
  const brief = ui.reportBrief('run-1', report, runSummary);
  assert.match(brief, /"automaticChecksVersion": 2/);
  for (const expected of [
    'Reading rules:',
    'Contains prompts and generated answer keys.',
    '#### Parent request\n```text\nבקשה',
    '"title": "בדיקת עברית"',
    '<script>request</script>',
    '"hebrew": 2',
    '#### Case expectations and task input',
    '"topic": "חלל"',
    '"audience": "מבוגרים"',
    '"difficulty": "hard"',
    'הטעויות באפשרויות התשובה מכוונות.',
    '"allowUnexpectedFindings": false',
  ])
    assert.ok(brief.includes(expected), expected);
  assert.equal(brief.split('<script>request</script>').length, 2);
  assert.match(brief, /- user: same as Result 1 \(first · repetition 1\) · Task generation · user/);
  assert.match(brief, /````text\n```\n# escaped\n````/);

  const app = mount({
    '/api/runs': () => jsonResponse([{ id: 'run-1', label: 'Saved run', summary: runSummary }]),
    '/api/runs/run-1': () => jsonResponse({ report: completedReport, summary: runSummary }),
  });
  try {
    await app.dashboard.ready;
    app.document.querySelector('#history-list button').click();
    await nextTurn();
    const buttons = [...app.document.querySelectorAll('#report button')];
    assert.ok(buttons.some((button) => button.textContent === 'Copy for AI'));
  } finally {
    app.dashboard.dispose();
    app.dom.window.close();
  }
});
