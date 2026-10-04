import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  provideRouter,
  Router,
  RouteReuseStrategy,
  withComponentInputBinding,
} from '@angular/router';
import { PageReuseStrategy } from '../../../core/page-reuse-strategy';
import { RouterTestingHarness } from '@angular/router/testing';
import { ActivityWorkspace } from './activity-workspace';
import { numericPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';
import { ActivityDetail } from '../../../core/api/models';
import { provideLimits } from '../../../core/api/limits.fixture';
import { FakeEventSource } from '../../../core/api/event-source.fixture';

const savedQuestion = {
  id: 'q',
  prompt: 'שאלה',
  interaction: { type: 'numeric-input' as const, options: null },
  answer: { value: '1' },
  points: 1,
  origin: { kind: 'manual' },
  acceptance: null,
};
const savedActivity: ActivityDetail = {
  id: 'draft',
  revision: 1,
  plan: numericPlan,
  input: { settings: numericPlan.defaults },
  document: { title: 'תרגול', instructions: null, materials: [], questions: [] },
  diagnostics: { questions: ['נדרשות שאלות'] },
  measurements: [],
  activeOperationId: null,
  templateVersionId: null,
  releasedSnapshotId: null,
  releasedSourceRevision: null,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: '2026-10-01T00:00:00Z',
};
describe('Activity lifecycle', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const root = () => harness.routeNativeElement!;
  async function settle() {
    await Promise.resolve();
    await harness.fixture.whenStable();
  }
  async function click(id: string) {
    const button = root().querySelector<HTMLButtonElement>('#' + id)!;
    // Busy actions stay focusable and ignore activation until they are available again.
    await vi.waitFor(() => expect(button.getAttribute('aria-disabled')).toBeNull());
    button.click();
    await settle();
  }
  async function type(id: string, value: string) {
    const field = root().querySelector<HTMLInputElement>('#' + id)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }
  async function open(existing = true, draft = savedActivity) {
    await harness.navigateByUrl(
      existing ? '/activities/draft' : '/templates/t/create',
      ActivityWorkspace,
    );
    http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
    if (existing) http.expectOne('/api/activity-drafts/draft').flush(structuredClone(draft));
    else
      http
        .expectOne('/api/templates/t')
        .flush({ id: 't', currentVersion: 3, versionId: 'v3', definition: numericPlan });
    await settle();
  }
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideLimits(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [
            { path: 'activities/:activityId', component: ActivityWorkspace },
            { path: 'templates/:templateId/create', component: ActivityWorkspace },
          ],
          withComponentInputBinding(),
        ),
        { provide: RouteReuseStrategy, useClass: PageReuseStrategy },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());
  it.each([
    ['its own plan', true, null, true],
    ['a template being used', false, null, false],
    ['a draft from a template', true, 'v3', false],
  ] as const)(
    'defines what activities may change only in %s',
    async (_, existing, templateVersionId, defines) => {
      await open(existing, { ...savedActivity, templateVersionId });
      expect(!!root().querySelector('#choices-title')).toBe(defines);
    },
  );
  it('creates a durable draft from a valid plan before starting generation without template publication', async () => {
    await open(false);
    await click('generate-activity');
    const create = http.expectOne('/api/activity-drafts');
    expect(create.request.body).toMatchObject({
      plan: numericPlan,
      input: { settings: numericPlan.defaults },
      templateId: 't',
      expectedVersion: 3,
    });
    http.expectNone('/api/activity-drafts/draft/operations');
    create.flush(savedActivity);
    await settle();
    const operation = http.expectOne('/api/activity-drafts/draft/operations');
    expect(operation.request.body).toMatchObject({ expectedRevision: 1, kind: 'GenerateActivity' });
    operation.flush({ title: 'שירות לא זמין' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    http.expectNone('/api/templates');
  });
  it.each(['generate-activity', 'generate-questions', 'release-activity'])(
    'does not perform %s after a required save fails',
    async (action) => {
      await open(true, {
        ...savedActivity,
        document: { ...savedActivity.document, questions: [savedQuestion] },
      });
      await type('document-title', 'עריכה שלי');
      await click(action);
      const save = http.expectOne('/api/activity-drafts/draft');
      expect(save.request.method).toBe('PUT');
      save.flush({ title: 'התנגשות' }, { status: 409, statusText: 'Conflict' });
      await settle();
      expect((root().querySelector('#document-title') as HTMLInputElement).value).toBe('עריכה שלי');
      http.expectNone((r) => r.method === 'POST');
    },
  );
  it('retains invalid points without a save or paid call', async () => {
    await open();
    await click('add-question');
    await type('question-0-points', '1.5');
    await click('generate-activity');
    http.expectNone((r) => r.method === 'PUT' || r.method === 'POST');
    expect((root().querySelector('#question-0-points') as HTMLInputElement).value).toBe('1.5');
  });
  it('saves incomplete manual content and undo as new revisions without acceptance metadata', async () => {
    await open();
    await type('document-title', 'תיקון');
    await click('save-activity');
    const save = http.expectOne('/api/activity-drafts/draft');
    expect(save.request.body.document).toEqual({
      title: 'תיקון',
      instructions: null,
      materials: [],
      questions: [],
    });
    save.flush({
      ...savedActivity,
      revision: 2,
      document: { ...savedActivity.document, title: 'תיקון' },
    });
    await settle();
    await click('plan-undo');
    const undo = http.expectOne('/api/activity-drafts/draft');
    expect(undo.request.body.expectedRevision).toBe(2);
    expect(undo.request.body.document.title).toBe('תרגול');
    expect(JSON.stringify(undo.request.body)).not.toContain('acceptance');
    undo.flush({ ...savedActivity, revision: 3 });
    await settle();
    http.expectNone((r) => r.method === 'POST');
  });
  it('preserves local typing after operation completion and requires explicit reload', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open();
      await click('generate-activity');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateActivity',
        status: 'queued',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      };
      http.expectOne('/api/activity-drafts/draft/operations').flush(operation);
      await settle();
      await type('document-title', 'עריכה מקומית');
      vi.advanceTimersByTime(2000);
      http
        .expectOne('/api/activity-drafts/draft/operations/op')
        .flush({ ...operation, status: 'completed' });
      const complete = {
        ...savedActivity,
        revision: 2,
        document: { ...savedActivity.document, title: 'תוצאת שרת' },
      };
      http.expectOne('/api/activity-drafts/draft').flush(complete);
      await settle();
      expect((root().querySelector('#document-title') as HTMLInputElement).value).toBe(
        'עריכה מקומית',
      );
      expect(root().textContent).toContain('נוצרה תוצאה בזמן שהמשכתם לערוך');
      // The offered result is this page's own, not a change made elsewhere.
      FakeEventSource.opened[0].send();
      await settle();
      http.expectOne('/api/activity-drafts/draft').flush(complete);
      await settle();
      expect(root().textContent).not.toContain('במכשיר אחר');
      await click('save-activity');
      const save = http.expectOne('/api/activity-drafts/draft');
      expect(save.request.body.expectedRevision).toBe(1);
      save.flush({}, { status: 409, statusText: 'Conflict' });
      await settle();
      expect((root().querySelector('#document-title') as HTMLInputElement).value).toBe(
        'עריכה מקומית',
      );
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      await click('reload-activity');
      http.expectOne('/api/activity-drafts/draft').flush(complete);
      await settle();
      expect((root().querySelector('#document-title') as HTMLInputElement).value).toBe('תוצאת שרת');
      http.expectNone((r) => r.method === 'POST');
    } finally {
      vi.useRealTimers();
      vi.restoreAllMocks();
    }
  });
  it('tells the parent about a revision saved elsewhere and loads it only on request', async () => {
    await open();
    await type('document-title', 'עריכה מקומית');
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
    await settle();
    expect(root().textContent).toContain('הפעילות עודכנה במכשיר אחר');
    expect(root().querySelector<HTMLInputElement>('#document-title')!.value).toBe('עריכה מקומית');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await click('reload-activity');
    const remote = { ...savedActivity.document, title: 'מהמכשיר האחר' };
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({ ...savedActivity, revision: 2, document: remote });
    await settle();
    expect(root().querySelector<HTMLInputElement>('#document-title')!.value).toBe('מהמכשיר האחר');
    expect(root().textContent).not.toContain('הפעילות עודכנה במכשיר אחר');
    vi.restoreAllMocks();
  });
  it('tells the parent when another device deletes this draft, keeping local work', async () => {
    await open();
    await type('document-title', 'עריכה מקומית');
    FakeEventSource.opened[0].send();
    await settle();
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({ title: 'לא נמצא' }, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(root().textContent).toContain('הפעילות נמחקה במכשיר אחר');
    expect(root().querySelector('#reload-activity')).toBeNull();
    expect(root().querySelector<HTMLInputElement>('#document-title')!.value).toBe('עריכה מקומית');
  });
  it('never reports its own writes as changes made elsewhere', async () => {
    const ready = {
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
      diagnostics: {},
    };
    await open(true, ready);
    FakeEventSource.opened[0].send();
    await settle();
    const check = http.expectOne('/api/activity-drafts/draft');
    await click('release-activity');
    const release = await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft/release'));
    // The check reads the revision this release created, and the release's own note arrives meanwhile.
    check.flush({ ...ready, revision: 2, releasedSnapshotId: 'ready' });
    FakeEventSource.opened[0].send();
    await settle();
    http.expectNone((r) => r.method === 'GET');
    expect(root().querySelector('header #reload-activity')).toBeNull();
    release.flush({ id: 'ready' });
    await vi.waitFor(() =>
      expect(root().querySelector('a[href="/instances/ready"]')).not.toBeNull(),
    );
    http.expectNone((r) => r.method === 'GET');
    expect(root().textContent).not.toContain('הפעילות עודכנה במכשיר אחר');
  });
  it('recovers a lost start response with the exact original operation key and revision', async () => {
    await open();
    await click('generate-activity');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    const body = start.request.body;
    start.error(new ProgressEvent('error'));
    await settle();
    expect(root().querySelector('#generate-activity')!.getAttribute('aria-disabled')).toBe('true');
    await type('document-title', 'עריכה אחרי השליחה');
    await click('recover-start');
    const replay = http.expectOne('/api/activity-drafts/draft/operations');
    expect(replay.request.body).toEqual(body);
    replay.flush({
      id: 'op',
      draftId: 'draft',
      kind: 'GenerateActivity',
      status: 'completed',
      stage: 'questions',
      originalRevision: 1,
      expectedRevision: 1,
      failure: null,
      diagnosticsExpired: true,
      steps: [],
      artifacts: null,
    });
    await settle();
    http.expectNone((r) => r.method === 'PUT');
  });

  it('resumes saved operation polling after reload without starting any new work', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open(true, { ...savedActivity, activeOperationId: 'op' });
      vi.advanceTimersByTime(2000);
      http.expectOne('/api/activity-drafts/draft/operations/op').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateActivity',
        status: 'unknown',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: 'restart',
        diagnosticsExpired: true,
        steps: [],
        artifacts: null,
      });
      http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
      await settle();
      expect(root().textContent).toContain('לא ידוע אם שירות ה־AI סיים את הבקשה');
      expect(root().textContent).toContain('לא הפעלנו ניסיון נוסף אוטומטית');
      http.expectNone((r) => r.method === 'POST');
    } finally {
      vi.useRealTimers();
    }
  });
  it('pauses operation polling while the page is hidden and reads at once when shown again', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const visibility = vi.spyOn(document, 'visibilityState', 'get');
    const show = (state: DocumentVisibilityState) => {
      visibility.mockReturnValue(state);
      document.dispatchEvent(new Event('visibilitychange'));
    };
    try {
      await open(true, { ...savedActivity, activeOperationId: 'op' });
      show('hidden');
      vi.advanceTimersByTime(10_000);
      http.expectNone('/api/activity-drafts/draft/operations/op');
      show('visible');
      vi.advanceTimersByTime(0);
      http.expectOne('/api/activity-drafts/draft/operations/op').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateActivity',
        status: 'completed',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      });
      http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
      await settle();
    } finally {
      visibility.mockRestore();
      vi.useRealTimers();
    }
  });
  it('saves canonical replacement source, then offers adoption only for the question it made stale', async () => {
    await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: false, schemaVersion: 1 });
    const source = {
      id: suppliedPlan.materials[0].id,
      title: null,
      body: sourceText,
      revision: 1,
      origin: { kind: 'supplied' },
      acceptance: null,
    };
    const question = {
      id: 'q',
      prompt: 'שאלה',
      interaction: { type: 'numeric-input', options: null },
      answer: { value: '1' },
      points: 1,
      origin: { kind: 'manual' },
      acceptance: null,
    };
    const initial = {
      ...savedActivity,
      plan: suppliedPlan,
      document: { ...savedActivity.document, materials: [source], questions: [question] },
    };
    http.expectOne('/api/activity-drafts/draft').flush(initial);
    await settle();
    const sourceButton = Array.from(root().querySelectorAll('button')).find((b) =>
      b.textContent?.trim().startsWith('החלפת הטקסט'),
    )!;
    sourceButton.click();
    await settle();
    await type('replacement-source', 'New!\nשלום');
    await click('release-activity');
    http.expectNone((r) => r.method === 'POST');
    root().querySelector<HTMLButtonElement>('#accept-source-replacement')!.click();
    await settle();
    const adopt = () =>
      Array.from(root().querySelectorAll('button')).find((b) =>
        b.textContent?.includes('בדקתי, אפשר להשתמש בגרסה הזו'),
      );
    expect(adopt()).toBeUndefined();
    await click('save-activity');
    const save = http.expectOne('/api/activity-drafts/draft');
    expect(save.request.body.plan.materials[0].text).toBe('New!\nשלום');
    save.flush({
      ...initial,
      revision: 2,
      plan: { ...suppliedPlan, materials: [{ ...suppliedPlan.materials[0], text: 'New!\nשלום' }] },
      document: {
        ...initial.document,
        materials: [{ ...source, body: 'New!\nשלום', revision: 2 }],
      },
      diagnostics: { 'questions[0].stale': ['השאלה דורשת יצירה מחדש או אימוץ.'] },
    });
    await settle();
    expect(root().textContent).toContain('הטקסט השתנה מאז שנוצרו השאלות');
    adopt()!.click();
    await settle();
    http.expectNone((r) => r.method === 'PUT');
    const adopted = http.expectOne('/api/activity-drafts/draft/adopt-content');
    expect(adopted.request.body).toEqual({
      expectedRevision: 2,
      materialIds: [],
      questionIds: ['q'],
    });
    adopted.flush({ ...initial, revision: 3 });
    await settle();
    http.expectNone('/api/ai/template-drafts');
  });
  it('releases only the exact saved revision and locks the terminal draft', async () => {
    await open(true, {
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await type('document-title', 'לבדיקה');
    await click('release-activity');
    const save = http.expectOne('/api/activity-drafts/draft');
    save.flush({
      ...savedActivity,
      revision: 2,
      document: { ...savedActivity.document, title: 'לבדיקה', questions: [savedQuestion] },
      diagnostics: {},
    });
    const release = await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft/release'));
    expect(release.request.body).toEqual({ expectedRevision: 2 });
    release.flush({ id: 'ready' });
    await vi.waitFor(() =>
      expect(root().querySelector('a[href="/instances/ready"]')).not.toBeNull(),
    );
    expect(root().querySelector<HTMLInputElement>('#document-title')!.disabled).toBe(true);
    expect(root().querySelector('#release-activity')).toBeNull();
  });
  it('keeps an undo checkpoint for automatically applied generation content', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open();
      await click('generate-activity');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateActivity',
        status: 'queued',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      };
      http.expectOne('/api/activity-drafts/draft/operations').flush(operation);
      await settle();
      vi.advanceTimersByTime(2000);
      http
        .expectOne('/api/activity-drafts/draft/operations/op')
        .flush({ ...operation, status: 'completed' });
      http.expectOne('/api/activity-drafts/draft').flush({
        ...savedActivity,
        revision: 2,
        document: { ...savedActivity.document, title: 'תוצאה' },
      });
      await settle();
      await click('plan-undo');
      const save = http.expectOne('/api/activity-drafts/draft');
      expect(save.request.body.expectedRevision).toBe(2);
      expect(save.request.body.document.title).toBe('תרגול');
      save.flush({ ...savedActivity, revision: 3 });
      await settle();
    } finally {
      vi.useRealTimers();
    }
  });
  it.each(['replace', 'adopt'])(
    'does not %s a question after a required save fails',
    async (action) => {
      await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
      http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
      http.expectOne('/api/activity-drafts/draft').flush({
        ...savedActivity,
        document: {
          ...savedActivity.document,
          questions: [
            {
              id: 'q',
              prompt: 'שאלה',
              interaction: { type: 'numeric-input', options: null },
              answer: { value: '1' },
              points: 1,
              origin: { kind: 'manual' },
              acceptance: null,
            },
          ],
        },
        diagnostics: { 'questions[0].stale': ['השאלה דורשת יצירה מחדש או אימוץ.'] },
      });
      await settle();
      await type('question-0-prompt', 'שינוי מקומי');
      if (action === 'replace') {
        root().querySelector<HTMLButtonElement>('#question-0-improve')!.click();
        await settle();
        await type('question-0-instruction', 'פשטו את הניסוח');
        root().querySelector<HTMLButtonElement>('#question-0-improve-submit')!.click();
      } else
        Array.from(root().querySelectorAll('button'))
          .find((b) => b.textContent?.includes('בדקתי, אפשר להשתמש בגרסה הזו'))!
          .click();
      await settle();
      http
        .expectOne('/api/activity-drafts/draft')
        .flush({}, { status: 400, statusText: 'Bad Request' });
      await settle();
      http.expectNone((r) => r.method === 'POST');
      expect(root().querySelector<HTMLInputElement>('#question-0-prompt')!.value).toBe(
        'שינוי מקומי',
      );
    },
  );
  it('uses the new saved revision after cancelling without local edits', async () => {
    await open();
    await click('generate-activity');
    const operation = {
      id: 'op',
      draftId: 'draft',
      kind: 'GenerateActivity',
      status: 'queued',
      stage: 'questions',
      originalRevision: 1,
      expectedRevision: 1,
      failure: null,
      diagnosticsExpired: false,
      steps: [],
      artifacts: null,
    };
    http.expectOne('/api/activity-drafts/draft/operations').flush(operation);
    await settle();
    await click('cancel-generation');
    http
      .expectOne('/api/activity-drafts/draft/operations/op/cancel')
      .flush({ ...operation, status: 'cancelled' });
    const read = await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft'));
    read.flush({ ...savedActivity, revision: 2 });
    await settle();
    await click('generate-activity');
    const next = http.expectOne('/api/activity-drafts/draft/operations');
    expect(next.request.body.expectedRevision).toBe(2);
    next.flush({ ...operation, id: 'next' });
    await settle();
  });
  it('reads a fresh draft after observing terminal status even if an earlier read preceded the checkpoint', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open();
      await click('generate-activity');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateActivity',
        status: 'queued',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      };
      http.expectOne('/api/activity-drafts/draft/operations').flush(operation);
      await settle();
      vi.advanceTimersByTime(2000);
      const status = http.expectOne('/api/activity-drafts/draft/operations/op');
      // A draft read already in flight saw the database before the worker committed.
      for (const read of http.match(
        (r) => r.method === 'GET' && r.url === '/api/activity-drafts/draft',
      ))
        read.flush(savedActivity);
      status.flush({ ...operation, status: 'completed' });
      await settle();
      for (const read of http.match(
        (r) => r.method === 'GET' && r.url === '/api/activity-drafts/draft',
      ))
        read.flush({
          ...savedActivity,
          revision: 2,
          document: { ...savedActivity.document, title: 'אחרי השמירה' },
        });
      await settle();
      expect(root().querySelector<HTMLInputElement>('#document-title')!.value).toBe('אחרי השמירה');
    } finally {
      vi.useRealTimers();
    }
  });
  it.each(['manual', 'proposal'])(
    'replaces a saved material identity after a %s source-kind change',
    async (change) => {
      await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
      http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
      http.expectOne('/api/activity-drafts/draft').flush({
        ...savedActivity,
        plan: suppliedPlan,
        document: {
          ...savedActivity.document,
          materials: [
            {
              id: suppliedPlan.materials[0].id,
              title: null,
              body: sourceText,
              revision: 1,
              origin: { kind: 'supplied' },
              acceptance: null,
            },
          ],
        },
      });
      await settle();
      if (change === 'manual') {
        const select = root().querySelector<HTMLSelectElement>(
          '[id="' + suppliedPlan.materials[0].id + '-source"]',
        )!;
        select.value = 'generated';
        select.dispatchEvent(new Event('input', { bubbles: true }));
      } else {
        await type('chat-message', 'להחליף את המקור בחומר חדש שנוצר לפעילות');
        await click('chat-send');
        const request = http.expectOne('/api/ai/template-drafts');
        request.flush({
          requestId: request.request.body.requestId,
          baseRevision: request.request.body.baseRevision,
          proposal: {
            ...suppliedPlan,
            materials: suppliedPlan.materials.map((material) => ({
              ...material,
              source: 'generated',
              text: null,
            })),
          },
          changes: [{ kind: 'changed', path: 'materials' }],
          assumptions: [],
          clarification: null,
        });
      }
      await settle();
      await click('save-activity');
      const save = http.expectOne('/api/activity-drafts/draft');
      expect(save.request.body.plan.materials[0].source).toBe('generated');
      expect(save.request.body.plan.materials[0].id).not.toBe(suppliedPlan.materials[0].id);
      expect(save.request.body.document.materials).toEqual([]);
      save.flush({ ...savedActivity, revision: 2, plan: save.request.body.plan });
      await settle();
    },
  );
  it('collapses setup to its summary once content exists and keeps uncommon actions secondary', async () => {
    await open(true, {
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    const body = root().querySelector<HTMLElement>('#setup-body')!;
    const toggle = root().querySelector<HTMLButtonElement>('[aria-controls="setup-body"]')!;
    expect(body.hidden).toBe(true);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(root().querySelector('#plan-title')!.nextElementSibling?.textContent).toContain(
      '2 שאלות מספריות',
    );
    expect(
      root().querySelector<HTMLDetailsElement>('#generate-activity')!.closest('details')!.open,
    ).toBe(false);
    expect(root().querySelector('#release-activity')!.closest('details')).toBeNull();
    toggle.click();
    await settle();
    expect(body.hidden).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(root().querySelector('#plan-title')!.nextElementSibling).toBeNull();
  });
  it.each([
    ['פשטו את הניסוח', { instruction: 'פשטו את הניסוח' }],
    ['   ', {}],
  ])('sends only the selected question and an optional instruction (%j)', async (text, extra) => {
    await open(true, {
      ...savedActivity,
      document: {
        ...savedActivity.document,
        questions: [savedQuestion, { ...savedQuestion, id: 'q2', prompt: 'שנייה' }],
      },
    });
    await click('question-1-improve');
    await type('question-1-instruction', text);
    await click('question-1-improve-submit');
    http.expectNone((r) => r.method === 'PUT');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    expect(start.request.body).toEqual({
      operationKey: start.request.body.operationKey,
      expectedRevision: 1,
      kind: 'ReplaceQuestion',
      targetId: 'q2',
      ...extra,
    });
    start.flush({ title: 'שירות לא זמין' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(root().querySelector<HTMLTextAreaElement>('#question-0-prompt')!.value).toBe('שאלה');
  });
  it('offers adoption only beside content the saved diagnostics mark as stale', async () => {
    await open(true, {
      ...savedActivity,
      document: {
        ...savedActivity.document,
        questions: [savedQuestion, { ...savedQuestion, id: 'q2' }],
      },
      diagnostics: { 'questions[0].stale': ['השאלה דורשת יצירה מחדש או אימוץ.'] },
    });
    const adopt = Array.from(root().querySelectorAll('button')).filter((b) =>
      b.textContent?.includes('בדקתי, אפשר להשתמש בגרסה הזו'),
    );
    expect(adopt.map((b) => b.closest('li')!.querySelector('h3')!.textContent)).toEqual(['שאלה 1']);
    expect(adopt[0].closest('details')).toBeNull();
    expect(root().textContent).toContain('ההגדרות השתנו מאז שנוצרו השאלות');
    adopt[0].click();
    await settle();
    const adopted = http.expectOne('/api/activity-drafts/draft/adopt-content');
    expect(adopted.request.body).toEqual({
      expectedRevision: 1,
      materialIds: [],
      questionIds: ['q'],
    });
    adopted.flush({ ...savedActivity, revision: 2 });
    await settle();
  });
  it.each([
    ['completed', 'accepted', false],
    ['conflict', 'conflict', true],
  ])(
    'offers a %s result for editing only when it was not applied',
    async (status, outcome, offered) => {
      await open();
      await click('generate-activity');
      http.expectOne('/api/activity-drafts/draft/operations').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateActivity',
        status,
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [{ stage: 'questions', outcome, usage: null, metadata: null }],
        artifacts: {
          targetId: null,
          steps: [
            {
              stage: 'questions',
              diagnostics: null,
              candidate: {
                title: 'מועמד',
                instructions: null,
                questions: [
                  {
                    prompt: 'כמה?',
                    interaction: { type: 'numeric-input', options: null },
                    answer: { value: '2' },
                    points: 1,
                  },
                ],
              },
            },
          ],
        },
      });
      await settle();
      expect(!!root().querySelector('[data-edit-candidate]')).toBe(offered);
      if (offered) {
        expect(root().textContent).toContain('נוצרה תוצאה בזמן שהמשכתם לערוך');
        expect(root().textContent).toContain('שאלות שנוצרו');
        root().querySelector<HTMLButtonElement>('[data-edit-candidate]')!.click();
        await settle();
        expect(root().querySelector<HTMLTextAreaElement>('#question-0-prompt')!.value).toBe('כמה?');
        http.expectNone((r) => r.method === 'PUT');
      }
    },
  );
  it('copies a released activity into a new draft only on request', async () => {
    await open(true, {
      ...savedActivity,
      releasedSnapshotId: 'ready',
      releasedSourceRevision: 1,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    expect(root().querySelector('a[href="/instances/ready"]')!.textContent).toContain(
      'צפייה בפעילות המוכנה',
    );
    http.expectNone((r) => r.method === 'POST');
    await click('copy-released');
    const copy = http.expectOne('/api/activity-drafts');
    expect(copy.request.body).toEqual({ snapshotId: 'ready' });
    copy.flush({ ...savedActivity, id: 'copy' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/activities/copy'));
    http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
    http.expectOne('/api/activity-drafts/copy').flush({ ...savedActivity, id: 'copy' });
    await settle();
  });
  it('discards a pending source replacement when explicitly opening a fresh activity', async () => {
    await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: false, schemaVersion: 1 });
    const supplied = {
      ...savedActivity,
      plan: suppliedPlan,
      document: {
        ...savedActivity.document,
        materials: [
          {
            id: suppliedPlan.materials[0].id,
            title: null,
            body: sourceText,
            revision: 1,
            origin: { kind: 'supplied' },
            acceptance: null,
          },
        ],
      },
    };
    http.expectOne('/api/activity-drafts/draft').flush(supplied);
    await settle();
    const replace = Array.from(root().querySelectorAll('button')).find((button) =>
      button.textContent?.trim().startsWith('החלפת הטקסט'),
    )!;
    replace.click();
    await settle();
    await type('replacement-source', 'שינוי מקומי שלא אושר');
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    try {
      await click('new-activity');
      http.expectOne('/api/activity-drafts').flush({ ...supplied, id: 'fresh' });
      await settle();
      expect(confirm).toHaveBeenCalledOnce();
      expect(root().querySelector('#replacement-source')).toBeNull();
      expect(root().textContent).toContain('נשמר');
      await click('save-activity');
      http.expectNone((request) => request.method === 'PUT');
    } finally {
      confirm.mockRestore();
    }
  });
});
