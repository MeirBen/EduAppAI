import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ActivityWorkspace } from './activity-workspace';
import { numericPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';
import { ActivityDetail } from '../../../core/api/models';

export const savedActivity: ActivityDetail = {
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
    await vi.waitFor(() => expect(button.disabled).toBe(false));
    button.click();
    await settle();
  }
  async function type(id: string, value: string) {
    const field = root().querySelector<HTMLInputElement>('#' + id)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }
  async function open(existing = true) {
    await harness.navigateByUrl(
      existing ? '/activities/draft' : '/templates/t/create',
      ActivityWorkspace,
    );
    http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
    if (existing)
      http.expectOne('/api/activity-drafts/draft').flush(structuredClone(savedActivity));
    else
      http
        .expectOne('/api/templates/t')
        .flush({ id: 't', currentVersion: 3, versionId: 'v3', definition: numericPlan });
    await settle();
  }
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [
            { path: 'activities/:activityId', component: ActivityWorkspace },
            { path: 'templates/:templateId/create', component: ActivityWorkspace },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());
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
      await open();
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
      expect(root().textContent).toContain('יש תוצאה חדשה בשרת');
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
  it('recovers a lost start response with the exact original operation key and revision', async () => {
    await open();
    await click('generate-activity');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    const body = start.request.body;
    start.error(new ProgressEvent('error'));
    await settle();
    expect((root().querySelector('#generate-activity') as HTMLButtonElement).disabled).toBe(true);
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
      await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
      http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
      http
        .expectOne('/api/activity-drafts/draft')
        .flush({ ...savedActivity, activeOperationId: 'op' });
      await settle();
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
      expect(root().textContent).toContain('תוצאה לא ידועה');
      http.expectNone((r) => r.method === 'POST');
    } finally {
      vi.useRealTimers();
    }
  });
  it('saves canonical replacement source and adopts only the inspected current question', async () => {
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
    const sourceButton = Array.from(root().querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'החלפת המקור',
    )!;
    sourceButton.click();
    await settle();
    await type('replacement-source', 'New!\nשלום');
    await click('release-activity');
    http.expectNone((r) => r.method === 'POST');
    root().querySelector<HTMLButtonElement>('#accept-source-replacement')!.click();
    await settle();
    const adopt = Array.from(root().querySelectorAll('button')).find((b) =>
      b.textContent?.includes('בדקתי את שאלה'),
    )!;
    adopt.click();
    await settle();
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
    });
    const adopted = await vi.waitFor(() =>
      http.expectOne('/api/activity-drafts/draft/adopt-content'),
    );
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
    await open();
    await type('document-title', 'לבדיקה');
    await click('release-activity');
    const save = http.expectOne('/api/activity-drafts/draft');
    save.flush({
      ...savedActivity,
      revision: 2,
      document: { ...savedActivity.document, title: 'לבדיקה' },
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
      });
      await settle();
      await type('question-0-prompt', 'שינוי מקומי');
      Array.from(root().querySelectorAll('button'))
        .find((b) =>
          b.textContent?.includes(
            action === 'replace' ? 'יצירה מחדש של השאלה כולה' : 'בדקתי את שאלה',
          ),
        )!
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
    const replace = Array.from(root().querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'החלפת המקור',
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
      expect(root().textContent).toContain('הטיוטה שמורה');
      await click('save-activity');
      http.expectNone((request) => request.method === 'PUT');
    } finally {
      confirm.mockRestore();
    }
  });
});
