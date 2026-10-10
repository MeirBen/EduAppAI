import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, RouteReuseStrategy, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PageReuseStrategy } from '../../../core/page-reuse-strategy';
import { ActivityDetail, LearningPlan } from '../../../core/api/models';
import { ActivityWorkspace } from './activity-workspace';
import { numericPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';
import { provideLimits } from '../../../core/api/limits.fixture';

describe('ActivityWorkspace plan ownership', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const root = () => harness.routeNativeElement!;
  const field = (id: string) =>
    root().querySelector<HTMLInputElement | HTMLTextAreaElement>(`#${id}`)!;
  const click = async (id: string) => {
    root().querySelector<HTMLButtonElement>(`#${id}`)!.click();
    await settle();
  };
  const type = async (id: string, value: string) => {
    const input = field(id);
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  };
  async function settle() {
    await Promise.resolve();
    await harness.fixture.whenStable();
  }
  /** Lets the pause after the last change pass, so a valid buffer saves itself. */
  async function autosave() {
    vi.advanceTimersByTime(1000);
    await settle();
  }
  async function open() {
    await harness.navigateByUrl('/activities/new', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: true });
    await settle();
  }
  function draft(plan: LearningPlan): ActivityDetail {
    return {
      id: 'draft',
      revision: 1,
      plan,
      document: { title: '', instructions: null, materials: [], questions: [] },
      diagnostics: { questions: ['נדרשות שאלות'] },
      measurements: [],
      activeOperationId: null,
      releasedSnapshotId: null,
      releasedSourceRevision: null,
      createdAtUtc: '2026-10-09T00:00:00Z',
      updatedAtUtc: '2026-10-09T00:00:00Z',
      chat: [],
      canUndo: false,
    };
  }
  async function ask(message = 'תרגול חשבון') {
    await type('chat-message', message);
    await click('chat-send');
    return http.expectOne('/api/ai/activity-plans');
  }
  function reply(
    request: TestRequest,
    plan: LearningPlan | null = numericPlan,
    clarification: string | null = null,
    changes = [{ kind: 'added', path: 'plan' }],
  ) {
    request.flush({
      proposal: plan,
      reply: clarification ?? 'ההגדרות מוכנות.',
      changes,
      assumptions: [],
      requestId: request.request.body.requestId,
      baseRevision: request.request.body.baseRevision,
      generationMetadata: {
        provider: 'test',
        model: 'test',
        promptVersion: 'test',
        generatedAtUtc: '2026-10-01T00:00:00Z',
      },
    });
  }
  beforeEach(async () => {
    // Autosave waits for a pause in typing; tests advance that pause explicitly.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    TestBed.configureTestingModule({
      providers: [
        provideLimits(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [
            { path: 'activities/new', component: ActivityWorkspace },
            { path: 'activities/:activityId', component: ActivityWorkspace },
          ],
          withComponentInputBinding(),
        ),
        { provide: RouteReuseStrategy, useClass: PageReuseStrategy },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  it('retries an unavailable AI status without losing the request being written', async () => {
    await harness.navigateByUrl('/activities/new', ActivityWorkspace);
    const status = http.expectOne('/api/ai/status');
    expect(root().textContent).not.toContain('העוזר לא זמין כרגע');
    status.flush({}, { status: 503, statusText: 'Unavailable' });
    await settle();
    await type('chat-message', 'בקשה שעדיין לא נשלחה');
    const retry = root().querySelector<HTMLButtonElement>('#retry-ai-status');
    expect(retry).not.toBeNull();
    retry!.focus();
    retry!.click();
    TestBed.tick();
    http.expectOne('/api/ai/status').flush({ configured: true });
    await settle();
    expect(field('chat-message').value).toBe('בקשה שעדיין לא נשלחה');
    expect(document.activeElement?.id).toBe('chat-message');
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(false);
    http.expectNone('/api/ai/activity-plans');
  });

  it('retries an initial draft read after a transient failure', async () => {
    await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: true });
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle();
    const retry = root().querySelector<HTMLButtonElement>('#retry-activity');
    expect(retry).not.toBeNull();
    retry!.focus();
    retry!.click();
    TestBed.tick();
    http.expectOne('/api/activity-drafts/draft').flush(draft(numericPlan));
    await settle();
    expect(root().textContent).toContain(numericPlan.name);
    expect(document.activeElement?.id).toBe('workspace-title');
    expect(root().querySelector('#retry-activity')).toBeNull();
  });

  it('keeps focus beside a source after its confirmation button disappears', async () => {
    await open();
    reply(await ask(), suppliedPlan);
    await settle();
    const id = suppliedPlan.materials[0].id;
    root()
      .querySelector<HTMLButtonElement>('#confirm-source-' + id)!
      .focus();
    await click('confirm-source-' + id);
    expect(document.activeElement?.id).toBe('source-' + id);
  });

  it('keeps an empty initial source unconfirmed and shows its field error', async () => {
    await open();
    reply(await ask(), suppliedPlan);
    await settle();
    const id = suppliedPlan.materials[0].id;
    await type('source-' + id, ' \n ');
    await click('confirm-source-' + id);
    expect(root().querySelector('#confirm-source-' + id)).not.toBeNull();
    expect(field('source-' + id).getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById(id + '-source-errors')?.textContent?.trim()).toBeTruthy();
    http.expectNone((request) => request.method !== 'GET');
  });

  it('warns before leaving an unsent message or an unsaved clarification', async () => {
    await open();
    const workspace = harness.routeDebugElement!.componentInstance as ActivityWorkspace;
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    await type('chat-message', 'בקשה שעדיין לא נשלחה');
    expect(await workspace.canLeave()).toBe(false);
    const request = await ask();
    expect(await workspace.canLeave()).toBe(false);
    reply(request, null, 'לאיזה גיל?');
    await settle();
    expect(await workspace.canLeave()).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(3);
    confirm.mockRestore();
  });

  it('shows a concrete read-only summary after authoring, before the pause saves it', async () => {
    await open();
    reply(await ask());
    await settle();
    expect(root().querySelector('#plan-title')!.textContent).toContain('הגדרות הפעילות');
    expect(root().querySelector('#activity-topic, #plan-name, app-plan-editor')).toBeNull();
    expect(root().textContent).toContain(numericPlan.name);
    expect(root().textContent).toContain('לא נשמר');
    expect(root().querySelector('#create-activity')?.textContent).toContain('יצירת הפעילות');
    http.expectNone('/api/activity-drafts');
  });

  it('requiresSourceConfirmationBeforeSaving, including after the text changes', async () => {
    await open();
    reply(await ask(), suppliedPlan);
    await settle();
    await autosave();
    http.expectNone('/api/activity-drafts');
    expect(root().textContent).toContain('בדקו שהטקסט הועתק נכון');
    const id = suppliedPlan.materials[0].id;
    await click('confirm-source-' + id);
    await type('source-' + id, sourceText + '!');
    await autosave();
    http.expectNone('/api/activity-drafts');
    await click('confirm-source-' + id);
    await autosave();
    const save = http.expectOne('/api/activity-drafts');
    expect(save.request.body.plan.materials[0].text).toBe(sourceText + '!');
    expect(save.request.body.templateId).toBeUndefined();
    save.flush(draft(save.request.body.plan));
    await settle();
    http.expectNone((r) => r.url.endsWith('/operations'));
  });

  it('keeps the authoring conversation when confirming sources before saving', async () => {
    await open();
    reply(await ask('שאלות על הטקסט שלי'), suppliedPlan);
    await settle();
    await click('confirm-source-' + suppliedPlan.materials[0].id);
    await autosave();
    const save = http.expectOne('/api/activity-drafts');
    expect(save.request.body.chat).toHaveLength(2);
    expect(save.request.body.chat[0].text).toBe('שאלות על הטקסט שלי');
    save.flush(draft(suppliedPlan));
    await settle();
  });

  it('savesIncompleteDraftWithoutGeneration and imports its conversation', async () => {
    await open();
    reply(await ask());
    await settle();
    await autosave();
    const save = http.expectOne('/api/activity-drafts');
    expect(save.request.body.plan).toEqual(numericPlan);
    expect(save.request.body.chat.map((turn: { role: string }) => turn.role)).toEqual([
      'parent',
      'assistant',
    ]);
    save.flush(draft(numericPlan));
    await settle();
    http.expectNone((r) => r.url.endsWith('/operations'));
    expect(root().textContent).toContain('נשמר');
  });

  it('holds the first save while a request is authored, then creates the draft once with its result', async () => {
    await open();
    reply(await ask());
    await settle();
    const refinement = await ask('עוד שאלה');
    await autosave();
    http.expectNone('/api/activity-drafts');
    reply(refinement, { ...numericPlan, name: 'מעודכן' });
    await settle();
    await autosave();
    const save = http.expectOne('/api/activity-drafts');
    expect(save.request.body.plan.name).toBe('מעודכן');
    save.flush(draft(save.request.body.plan));
    await settle();
  });

  it('locks the page while the first save creates the draft, so no edit crosses it', async () => {
    await open();
    reply(await ask(), suppliedPlan);
    await settle();
    const id = suppliedPlan.materials[0].id;
    await click('confirm-source-' + id);
    await autosave();
    const save = http.expectOne('/api/activity-drafts');
    // The setup shows its source editor only while the plan is editable.
    const source = root().querySelector<HTMLTextAreaElement>('#source-' + id);
    expect(!source || source.disabled).toBe(true);
    expect(field('chat-message').disabled).toBe(true);
    save.flush(draft(save.request.body.plan));
    await settle();
    expect(field('chat-message').disabled).toBe(false);
  });

  it('retries the first save under the same draft identity after a lost response', async () => {
    await open();
    reply(await ask());
    await settle();
    await autosave();
    const lost = http.expectOne('/api/activity-drafts');
    expect(lost.request.body.id).toMatch(/^[0-9a-f-]{36}$/);
    lost.error(new ProgressEvent('error'));
    await settle();
    reply(await ask('עוד שאלה'), { ...numericPlan, name: 'מעודכן' });
    await settle();
    await autosave();
    const retry = http.expectOne('/api/activity-drafts');
    expect(retry.request.body.id).toBe(lost.request.body.id);
    retry.flush(draft(retry.request.body.plan));
    await settle();
  });

  it('copies an activity it created and approved under a new draft identity', async () => {
    await open();
    reply(await ask());
    await settle();
    await autosave();
    const create = http.expectOne('/api/activity-drafts');
    const created = draft(create.request.body.plan);
    create.flush({
      ...created,
      id: create.request.body.id,
      document: {
        ...created.document,
        title: 'תרגול',
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
      diagnostics: {},
    });
    await settle();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await click('release-activity');
    http.expectOne(`/api/activity-drafts/${create.request.body.id}/release`).flush({ id: 'ready' });
    await settle();
    await click('copy-released');
    const copy = http.expectOne('/api/activity-drafts');
    expect(copy.request.body.snapshotId).toBe('ready');
    expect(copy.request.body.id).not.toBe(create.request.body.id);
  });

  it('saves before starting one complete Create operation', async () => {
    await open();
    reply(await ask());
    await settle();
    await click('create-activity');
    const save = http.expectOne('/api/activity-drafts');
    http.expectNone((r) => r.url.endsWith('/operations'));
    save.flush(draft(numericPlan));
    await settle();
    const operation = http.expectOne('/api/activity-drafts/draft/operations');
    expect(operation.request.body).toMatchObject({ kind: 'Create', expectedRevision: 1 });
    operation.flush({ title: 'לא זמין' }, { status: 503, statusText: 'Unavailable' });
    await settle();
  });

  it.each(['source edit', 'undo', 'cancel'])('ignores late authoring after %s', async (action) => {
    await open();
    reply(await ask(), suppliedPlan);
    await settle();
    const request = await ask('לשנות את השם');
    if (action === 'source edit')
      await type('source-' + suppliedPlan.materials[0].id, 'עריכה מקומית');
    if (action === 'undo') await click('plan-undo');
    if (action === 'cancel') await click('chat-cancel');
    if (!request.cancelled) reply(request, { ...suppliedPlan, name: 'תגובה ישנה' });
    await settle();
    expect(root().textContent).not.toContain('תגובה ישנה');
  });

  it('cancels unsaved authoring on navigation', async () => {
    await open();
    const request = await ask();
    await harness.navigateByUrl('/activities/other', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: true });
    http.expectOne('/api/activity-drafts/other').flush({ ...draft(numericPlan), id: 'other' });
    await settle();
    expect(request.cancelled).toBe(true);
  });

  it('sends the whole conversation with every request, past the model window and after an applied plan', async () => {
    await open();
    const messages = ['הבקשה המקורית', 'תשובה 1', 'תשובה 2', 'תשובה 3'];
    for (const [index, message] of messages.entries()) {
      const request = await ask(message);
      expect(request.request.body.context).toHaveLength(index * 2);
      reply(request, null, `שאלה ${index}?`);
      await settle();
    }
    // Eight turns exceed the model's six-turn window; the server windows them, so sending stays open.
    const answered = await ask('תשובה 4');
    expect(answered.request.body.context).toHaveLength(8);
    expect(answered.request.body.context[0].text).toBe('הבקשה המקורית');
    reply(answered);
    await settle();
    const next = await ask('עוד שינוי');
    expect(next.request.body.context.slice(-2)).toEqual([
      { role: 'parent', text: 'תשובה 4' },
      { role: 'assistant', text: 'ההגדרות מוכנות.' },
    ]);
    expect(next.request.body.baseDefinition.name).toBe(numericPlan.name);
    reply(next, numericPlan, null, []);
    await settle();
  });
});
