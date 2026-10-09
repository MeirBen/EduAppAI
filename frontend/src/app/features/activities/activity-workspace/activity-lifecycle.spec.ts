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
import { numericPlan, readingPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';
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

  document: { title: 'תרגול', instructions: null, materials: [], questions: [] },
  diagnostics: { questions: ['נדרשות שאלות'] },
  measurements: [],
  activeOperationId: null,
  releasedSnapshotId: null,
  releasedSourceRevision: null,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: '2026-10-01T00:00:00Z',
  chat: [],
  canUndo: false,
};
describe('Activity lifecycle', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const root = () => harness.routeNativeElement!;
  function advance(milliseconds: number) {
    vi.advanceTimersByTime(milliseconds);
    TestBed.tick();
  }
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
    if (!root().querySelector('#' + id) && root().querySelector('#edit-activity'))
      await click('edit-activity');
    const field = root().querySelector<HTMLInputElement>('#' + id)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }
  async function open(draft = savedActivity) {
    await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: true });
    http.expectOne('/api/activity-drafts/draft').flush(structuredClone(draft));
    await settle();
  }
  const titleValue = () =>
    root().querySelector<HTMLInputElement>('#document-title')?.value ??
    root()
      .querySelector('[aria-labelledby="document-heading"] app-activity-document-view h3')
      ?.textContent?.trim() ??
    '';
  beforeEach(async () => {
    // Final and replacing actions confirm; a test that cancels says so itself.
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.stubGlobal('matchMedia', () => ({ matches: true }));
    Element.prototype.scrollIntoView = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideLimits(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [{ path: 'activities/:activityId', component: ActivityWorkspace }],
          withComponentInputBinding(),
        ),
        { provide: RouteReuseStrategy, useClass: PageReuseStrategy },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => {
    // Restore first, so a test that fails verification cannot leak its mocks into the next one.
    vi.restoreAllMocks();
    http.verify();
  });
  it('targetShortcutOnlyFillsComposer and sends the exact target with Revise', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await click('ask-question-q');
    expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toContain('שאלה 1');
    expect(document.activeElement?.id).toBe('chat-message');
    expect(root().querySelector('#chat-target')!.textContent).toContain('שאלה 1');
    http.expectNone((r) => r.method === 'POST');
    await type('chat-message', 'הסבירו לי את השאלה');
    await click('chat-send');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    expect(start.request.body).toMatchObject({
      kind: 'Revise',
      message: 'הסבירו לי את השאלה',
      target: { kind: 'question', id: 'q' },
    });
    start.flush({ title: 'לא זמין' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe(
      'הסבירו לי את השאלה',
    );
  });

  it('finishes editing without saving and preserves local changes when editing resumes', async () => {
    await open();
    await type('document-title', 'כותרת חדשה');
    root().querySelector<HTMLButtonElement>('#finish-editing')?.focus();
    await click('finish-editing');
    expect(root().querySelector('#document-title')).toBeNull();
    expect(titleValue()).toBe('כותרת חדשה');
    expect(document.activeElement?.id).toBe('edit-activity');
    expect(root().textContent).toContain('לא נשמר');
    http.expectNone((request) => ['POST', 'PUT'].includes(request.method));
    await click('edit-activity');
    expect(titleValue()).toBe('כותרת חדשה');
  });

  it('keeps an empty draft editable while its last content is cleared', async () => {
    await open();
    await type('document-title', '');
    expect(root().querySelector('#document-title')).not.toBeNull();
    await type('document-title', 'עוד אפשר לערוך');
    await click('finish-editing');
    expect(titleValue()).toBe('עוד אפשר לערוך');
    http.expectNone((request) => ['POST', 'PUT'].includes(request.method));
  });

  it('keeps invalid edits visible when finishing and marks the fields to fix', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await type('question-0-points', 'no');
    await click('finish-editing');
    expect(root().querySelector('#document-title')).not.toBeNull();
    expect(root().querySelector('#question-0-points')?.getAttribute('aria-invalid')).toBe('true');
    expect(root().textContent).toContain('תקנו את השדות המסומנים.');
    http.expectNone((request) => ['POST', 'PUT'].includes(request.method));
  });

  it('blocks a removed target until the parent clears it', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await click('ask-question-q');
    await type('chat-message', 'בקשה שנשארת');
    await click('reload-activity');
    http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
    await settle();
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    expect(root().querySelector('#chat-target')!.textContent).toContain('אינו קיים');
    await click('clear-chat-target');
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(false);
  });

  it('sends only explicitly confirmed added sources, exactly as entered', async () => {
    await open();
    await click('add-chat-source');
    await type('chat-source-0-label', 'מקור חדש');
    await type('chat-source-0-text', '  טקסט מדויק\nביותר  ');
    await type('chat-message', 'הוסיפו את המקור');
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    await click('confirm-chat-source-0');
    await type('chat-source-0-text', '  תיקון מדויק\n  ');
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    await click('confirm-chat-source-0');
    await click('chat-send');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    expect(start.request.body.sources).toEqual([{ label: 'מקור חדש', text: '  תיקון מדויק\n  ' }]);
    start.flush({ title: 'לא זמין' }, { status: 503, statusText: 'Unavailable' });
    await settle();
  });

  it('explains whitespace-only source fields before confirmation and preserves valid text', async () => {
    await open();
    await click('add-chat-source');
    expect(document.activeElement?.id).toBe('chat-source-0-label');
    await type('chat-source-0-label', '  ');
    await type('chat-source-0-text', ' \n ');
    await type('chat-message', 'הוסיפו את המקור');
    await click('confirm-chat-source-0');
    for (const id of ['chat-source-0-label', 'chat-source-0-text']) {
      const input = root().querySelector<HTMLInputElement>('#' + id)!;
      expect(input.getAttribute('aria-invalid')).toBe('true');
      const description = input.getAttribute('aria-describedby')!;
      expect(description).toBeTruthy();
      expect(description.split(' ')).toContain(id + '-errors');
      expect(
        root()
          .querySelector('#' + id + '-errors')
          ?.textContent?.trim(),
      ).toBeTruthy();
    }
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    expect(root().textContent).not.toContain('הטקסט אושר לשליחה');
    await type('chat-source-0-label', '  המקור  ');
    await type('chat-source-0-text', '  טקסט מדויק\n  ');
    root().querySelector<HTMLButtonElement>('#confirm-chat-source-0')!.focus();
    await click('confirm-chat-source-0');
    expect(document.activeElement?.id).toBe('chat-message');
    await click('chat-send');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    expect(start.request.body.sources).toEqual([{ label: '  המקור  ', text: '  טקסט מדויק\n  ' }]);
    start.flush({}, { status: 503, statusText: 'Unavailable' });
    await settle();
  });

  it('keeps focus in chat when a target or the last source is removed', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await click('ask-question-q');
    root().querySelector<HTMLButtonElement>('#clear-chat-target')!.focus();
    await click('clear-chat-target');
    expect(document.activeElement?.id).toBe('chat-message');
    await click('add-chat-source');
    const remove = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find((button) =>
      button.textContent?.includes('הסרת הטקסט מהבקשה'),
    )!;
    remove.focus();
    remove.click();
    await settle();
    expect(document.activeElement?.id).toBe('add-chat-source');
  });

  it('focuses source replacement, explains blank text, and returns to its opener', async () => {
    const id = suppliedPlan.materials[0].id;
    await open({
      ...savedActivity,
      plan: suppliedPlan,
      document: {
        ...savedActivity.document,
        materials: [
          {
            id,
            title: null,
            body: sourceText,
            revision: 1,
            origin: { kind: 'supplied' },
            acceptance: null,
          },
        ],
      },
    });
    await click('edit-activity');
    const opener = Array.from(root().querySelectorAll<HTMLButtonElement>('button')).find((button) =>
      button.textContent?.trim().startsWith('החלפת הטקסט'),
    )!;
    opener.focus();
    opener.click();
    await settle();
    expect(document.activeElement?.id).toBe('replacement-source');
    await type('replacement-source', ' \n ');
    await click('accept-source-replacement');
    const field = root().querySelector<HTMLTextAreaElement>('#replacement-source')!;
    expect(field.getAttribute('aria-invalid')).toBe('true');
    expect(root().querySelector('#replacement-source-errors')?.textContent?.trim()).toBeTruthy();
    await type('replacement-source', '  מקור חדש\n  ');
    root().querySelector<HTMLButtonElement>('#accept-source-replacement')!.focus();
    await click('accept-source-replacement');
    expect(document.activeElement).toBe(opener);
    opener.click();
    await settle();
    expect(root().querySelector<HTMLTextAreaElement>('#replacement-source')!.value).toBe(
      '  מקור חדש\n  ',
    );
    const cancel = Array.from(
      root().querySelectorAll<HTMLButtonElement>('app-source-replacement button'),
    ).find((button) => button.textContent?.trim() === 'ביטול')!;
    cancel.focus();
    cancel.click();
    await settle();
    expect(document.activeElement).toBe(opener);
    http.expectNone((request) => request.method !== 'GET');
  });

  it('keeps the saved word count beside its text in reading and editing views', async () => {
    const id = readingPlan.materials[0].id;
    await open({
      ...savedActivity,
      plan: readingPlan,
      document: {
        ...savedActivity.document,
        materials: [
          {
            id,
            title: 'כותרת',
            body: 'טקסט לקריאה',
            revision: 1,
            origin: { kind: 'generated' },
            acceptance: null,
          },
        ],
      },
      measurements: [
        {
          scope: id,
          actual: 23,
          expected: { mode: 'target', value: 20, lower: null, upper: null },
          satisfied: null,
        },
      ],
    });
    expect(root().querySelector('app-activity-document-view section')!.textContent).toContain(
      '23 מילים',
    );
    await click('edit-activity');
    expect(root().querySelector('[aria-labelledby="material-0-heading"]')!.textContent).toContain(
      '23 מילים',
    );
    expect(root().querySelector('app-activity-review')!.textContent).not.toContain('23 מילים');
  });

  const attachedSource = { label: 'מקור חדש', text: '  "שָׁלוֹם" — Hello!\nטקסט מדויק  ' };
  const sourceMessage = 'הוסיפו את הטקסט הזה';
  const sourceTurn = {
    role: 'parent' as const,
    text: sourceMessage,
    atUtc: '2026-10-09T00:00:00Z',
    target: null,
    assumptions: null,
    operationId: 'op',
    outcome: null,
  };
  const sourceOperation = {
    id: 'op',
    draftId: 'draft',
    kind: 'Revise',
    status: 'queued',
    stage: 'revise',
    originalRevision: 1,
    expectedRevision: 1,
    failure: null,
    diagnosticsExpired: false,
    steps: [],
    artifacts: { sources: [attachedSource], steps: [] },
  };
  async function attachSource() {
    await click('add-chat-source');
    await type('chat-source-0-label', attachedSource.label);
    await type('chat-source-0-text', attachedSource.text);
    await click('confirm-chat-source-0');
  }
  async function observeSourceOutcome(status: string, draft: ActivityDetail, expired = false) {
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft/operations/op').flush({
      ...sourceOperation,
      status,
      expectedRevision: draft.revision,
      diagnosticsExpired: expired,
      artifacts: expired ? null : sourceOperation.artifacts,
    });
    http.expectOne('/api/activity-drafts/draft').flush(draft);
    await settle();
  }

  it.each(['failed', 'cancelled', 'completed'])(
    'recovers unconsumed confirmed sources after reopening a %s request',
    async (status) => {
      const draft = { ...savedActivity, chat: [sourceTurn] };
      await open({ ...draft, activeOperationId: 'op' });
      await observeSourceOutcome(status, draft);
      expect(root().querySelector<HTMLTextAreaElement>('#chat-source-0-text')?.value).toBe(
        attachedSource.text,
      );
      expect(root().querySelector('#confirm-chat-source-0')).toBeNull();
      if (status === 'completed') await type('chat-message', 'לכיתה ג');
      else
        expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe(
          sourceMessage,
        );
      await click('chat-send');
      const next = http.expectOne('/api/activity-drafts/draft/operations');
      expect(next.request.body.sources).toEqual([attachedSource]);
      next.flush({ title: 'לא זמין' }, { status: 503, statusText: 'Unavailable' });
      await settle();
    },
  );

  it('keeps confirmed sources through clarification and clears them only after incorporation', async () => {
    await open();
    await attachSource();
    await type('chat-message', sourceMessage);
    await click('chat-send');
    http.expectOne('/api/activity-drafts/draft/operations').flush(sourceOperation);
    await settle();
    await observeSourceOutcome('completed', {
      ...savedActivity,
      chat: [
        sourceTurn,
        { ...sourceTurn, role: 'assistant', text: 'לאיזה גיל?', outcome: 'completed' },
      ],
    });
    expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe('');
    await type('chat-message', 'לכיתה ג');
    await click('chat-send');
    const next = http.expectOne('/api/activity-drafts/draft/operations');
    expect(next.request.body.sources).toEqual([attachedSource]);
    next.flush({ ...sourceOperation, id: 'next' });
    await settle();
    FakeEventSource.opened[0].send();
    await settle();
    http
      .expectOne('/api/activity-drafts/draft/operations/next')
      .flush({ ...sourceOperation, id: 'next', status: 'completed', expectedRevision: 2 });
    http.expectOne('/api/activity-drafts/draft').flush({
      ...savedActivity,
      revision: 2,
      plan: {
        ...suppliedPlan,
        materials: [{ ...suppliedPlan.materials[0], text: attachedSource.text }],
      },
      chat: [{ ...sourceTurn, text: 'לכיתה ג', operationId: 'next' }],
    });
    await settle();
    expect(root().querySelector('#chat-source-0-text')).toBeNull();
  });

  it('asks for re-entry when request evidence expired instead of restoring a source-less retry', async () => {
    const draft = { ...savedActivity, chat: [sourceTurn] };
    await open({ ...draft, activeOperationId: 'op' });
    await observeSourceOutcome('failed', draft, true);
    expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe('');
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    expect(root().textContent).toContain('הוסיפו את הטקסט');
    http.expectNone((r) => r.method === 'POST');
  });

  it('does not replace local source input with a failed request from another device', async () => {
    await open();
    await click('add-chat-source');
    await type('chat-source-0-text', 'טקסט מקומי שלא נשלח');
    FakeEventSource.opened[0].send();
    await settle();
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({ ...savedActivity, activeOperationId: 'op' });
    await settle();
    await observeSourceOutcome('failed', { ...savedActivity, chat: [sourceTurn] });
    expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe('');
    expect(root().querySelector<HTMLTextAreaElement>('#chat-source-0-text')!.value).toBe(
      'טקסט מקומי שלא נשלח',
    );
    expect(root().querySelector('#confirm-chat-source-0')).not.toBeNull();
  });

  it('locks an open source replacement while another device starts an operation', async () => {
    const initial: ActivityDetail = {
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
    await open(initial);
    await click('edit-activity');
    Array.from(root().querySelectorAll('button'))
      .find((button) => button.textContent?.includes('החלפת הטקסט'))!
      .click();
    await settle();
    await type('replacement-source', 'מקור חדש');
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft').flush({ ...initial, activeOperationId: 'op' });
    await settle();
    expect(root().querySelector<HTMLTextAreaElement>('#replacement-source')!.disabled).toBe(true);
    expect(root().querySelector('#accept-source-replacement')!.getAttribute('aria-disabled')).toBe(
      'true',
    );
  });

  it('reads first and opens the same buffer for an explicit edit', async () => {
    await open({
      ...savedActivity,
      diagnostics: {},
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    expect(root().querySelector('#document-title')).toBeNull();
    expect(root().textContent).toContain('תרגול');
    await click('edit-activity');
    await type('document-title', 'כותרת שלי');
    await click('save-activity');
    const save = http.expectOne('/api/activity-drafts/draft');
    save.flush({
      ...savedActivity,
      revision: 2,
      diagnostics: {},
      document: { ...savedActivity.document, title: 'כותרת שלי', questions: [savedQuestion] },
    });
    await settle();
    expect(root().querySelector('#document-title')).toBeNull();
    expect(root().textContent).toContain('כותרת שלי');
  });

  it('failedSaveKeepsEditingAndPreventsAi', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await click('edit-activity');
    await type('document-title', 'עריכה שנשארת');
    await type('chat-message', 'להקל');
    await click('chat-send');
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({ title: 'התנגשות' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(titleValue()).toBe('עריכה שנשארת');
    expect(root().querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe('להקל');
    http.expectNone((r) => r.url.endsWith('/operations'));
  });

  it('questionRecoveryOfferSurvivesReload', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
      diagnostics: { 'questions[0].stale': ['הטקסט השתנה'] },
    });
    expect(root().querySelector('#regenerate-questions')!.textContent).toContain('עדכון השאלות');
    await click('adopt-questions');
    const adopt = http.expectOne('/api/activity-drafts/draft/adopt-content');
    expect(adopt.request.body).toEqual({
      expectedRevision: 1,
      materialIds: [],
      questionIds: ['q'],
    });
    adopt.flush({
      ...savedActivity,
      revision: 2,
      diagnostics: {},
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await settle();
    expect(root().querySelector('#adopt-questions')).toBeNull();
    http.expectNone((r) => r.url.endsWith('/operations'));
  });
  const generatedText = {
    id: readingPlan.materials[0].id,
    title: null,
    body: 'טקסט שנוצר',
    revision: 1,
    origin: { kind: 'generated' },
    acceptance: null,
  };
  const readingActivity: ActivityDetail = {
    ...savedActivity,
    plan: readingPlan,
  };
  it('asks before replacing generated content and sends nothing when the parent cancels', async () => {
    await open({
      ...readingActivity,
      document: {
        ...readingActivity.document,
        materials: [generatedText],
        questions: [savedQuestion],
      },
      diagnostics: { 'questions[0].stale': ['הטקסט השתנה.'] },
    });
    vi.mocked(window.confirm).mockReturnValue(false);
    await click('regenerate-questions');
    http.expectNone((r) => r.method === 'POST');
    expect(window.confirm).toHaveBeenCalledOnce();
  });
  it('freezes a version only after the parent confirms the release', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
      diagnostics: {},
    });
    vi.mocked(window.confirm).mockReturnValue(false);
    await click('release-activity');
    await settle();
    http.expectNone('/api/activity-drafts/draft/release');
    expect(window.confirm).toHaveBeenCalledOnce();
  });
  it('retains invalid points without a save or paid call', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await type('question-0-points', '1.5');
    await type('chat-message', 'שאלות קלות יותר');
    await click('chat-send');
    http.expectNone((r) => r.method === 'PUT' || r.method === 'POST');
    expect((root().querySelector('#question-0-points') as HTMLInputElement).value).toBe('1.5');
  });
  it('shows no release problems before there is content to mark ready', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, title: '' },
      diagnostics: { title: ['יש למלא תוכן בשדה הזה.'], questions: ['נדרשות שאלות'] },
    });
    expect(root().querySelector('#document-title')).toBeNull();
    expect(root().querySelectorAll('#document-title-errors p')).toHaveLength(0);
  });
  it('holds release problems while a generation is still writing the content', async () => {
    await open({
      ...savedActivity,
      activeOperationId: 'op',
      document: { ...savedActivity.document, title: '', questions: [savedQuestion] },
      diagnostics: { title: ['יש למלא תוכן בשדה הזה.'] },
    });
    expect(root().querySelector('#document-title')).toBeNull();
    expect(root().querySelector('app-activity-review')!.textContent).not.toContain('מוכנה לבדיקה');
  });
  it('shows a saved question problem at its own field until that field changes', async () => {
    await open({
      ...savedActivity,
      document: {
        ...savedActivity.document,
        questions: [savedQuestion, { ...savedQuestion, id: 'q2', answer: null }],
      },
      diagnostics: { 'questions[1].answer': ['יש להזין תשובה באורך של 1 עד 200 תווים.'] },
    });
    await click('edit-activity');
    const answer = root().querySelector('#question-1-answer')!;
    const review = root().querySelector('app-activity-review')!;
    expect(answer.getAttribute('aria-invalid')).toBe('true');
    const messages = root().querySelectorAll('#question-1-answer-errors p');
    expect([...messages].map((message) => message.textContent)).toEqual(['חסרה תשובה נכונה.']);
    expect(root().querySelector('#question-0-answer')!.getAttribute('aria-invalid')).toBeNull();
    expect(review.textContent).toContain('תקנו את המסומן בשאלה 2.');
    await type('question-1-answer', '4');
    expect(answer.getAttribute('aria-invalid')).toBeNull();
    expect(root().querySelector('#question-1-answer-errors')!.textContent!.trim()).toBe('');
    expect(review.textContent).not.toContain('תקנו את המסומן');
  });
  it('saves incomplete manual content and clears persistent undo', async () => {
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
    expect(root().querySelector('#plan-undo')!.getAttribute('aria-disabled')).toBe('true');
    http.expectNone((r) => r.method === 'POST');
  });
  it.each(['completed', 'conflict', 'cancelled'])(
    'waits for checkpoint ownership before applying content received between status reads (%s)',
    async (outcome) => {
      await open();
      await click('create-activity');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
        status: 'calling',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      };
      const newer = {
        ...savedActivity,
        revision: 2,
        activeOperationId: 'op',
        document: { ...savedActivity.document, title: 'תוכן חדש בשרת' },
      };
      http.expectOne('/api/activity-drafts/draft/operations').flush(operation);
      await settle();
      FakeEventSource.opened[0].send();
      await settle();
      http.expectOne('/api/activity-drafts/draft/operations/op').flush(operation);
      http.expectOne('/api/activity-drafts/draft').flush(newer);
      await settle();
      expect(titleValue()).toBe('תרגול');
      const terminal = {
        ...operation,
        status: outcome,
        expectedRevision: outcome === 'completed' ? 2 : 1,
      };
      if (outcome === 'cancelled') {
        await click('chat-cancel');
        http.expectOne('/api/activity-drafts/draft/operations/op/cancel').flush(terminal);
        (await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft'))).flush({
          ...newer,
          revision: 3,
          activeOperationId: null,
        });
      } else {
        FakeEventSource.opened[0].send();
        await settle();
        http.expectOne('/api/activity-drafts/draft/operations/op').flush(terminal);
        http.expectOne('/api/activity-drafts/draft').flush({ ...newer, activeOperationId: null });
      }
      await settle();
      expect(titleValue()).toBe(outcome === 'completed' ? 'תוכן חדש בשרת' : 'תרגול');
      expect(!!root().querySelector('#available-title')).toBe(outcome !== 'completed');
      if (outcome !== 'completed')
        expect(root().textContent).toContain('הפעילות עודכנה במכשיר אחר');
      http.expectNone((request) => request.method === 'POST');
    },
  );
  it('tells the parent about a revision saved elsewhere and loads it only on request', async () => {
    await open();
    await type('document-title', 'עריכה מקומית');
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
    await settle();
    expect(root().textContent).toContain('הפעילות עודכנה במכשיר אחר');
    expect(titleValue()).toBe('עריכה מקומית');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await click('reload-activity');
    const remote = { ...savedActivity.document, title: 'מהמכשיר האחר' };
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({ ...savedActivity, revision: 2, document: remote });
    await settle();
    expect(titleValue()).toBe('מהמכשיר האחר');
    expect(root().textContent).not.toContain('הפעילות עודכנה במכשיר אחר');
    vi.restoreAllMocks();
  });
  it('tells the parent when another device deletes this draft, keeping local work', async () => {
    await open();
    await type('document-title', 'עריכה מקומית');
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
    await settle();
    FakeEventSource.opened[0].send();
    await settle();
    http
      .expectOne('/api/activity-drafts/draft')
      .flush({ title: 'לא נמצא' }, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(root().textContent).toContain('הפעילות נמחקה במכשיר אחר');
    expect(root().querySelector('#reload-activity')).toBeNull();
    expect(titleValue()).toBe('עריכה מקומית');
  });
  it('offers external content without applying it to a clean buffer and keeps the newest offer', async () => {
    await open();
    const observe = async (revision: number, title: string) => {
      FakeEventSource.opened[0].send();
      await settle();
      http.expectOne('/api/activity-drafts/draft').flush({
        ...savedActivity,
        revision,
        document: { ...savedActivity.document, title },
      });
      await settle();
    };
    await observe(3, 'הגרסה החדשה');
    await observe(2, 'תוצאה ישנה');
    expect(titleValue()).toBe('תרגול');
    expect(root().querySelector('[aria-labelledby="available-title"]')!.textContent).toContain(
      'הגרסה החדשה',
    );
    expect(root().querySelector('[aria-labelledby="available-title"]')!.textContent).not.toContain(
      'תוצאה ישנה',
    );
    expect(root().textContent).toContain('הפעילות עודכנה במכשיר אחר');
  });

  it('locks an externally released draft without losing edits and can explicitly load its saved content', async () => {
    await open();
    await type('document-title', 'עריכה מקומית');
    const released = {
      ...savedActivity,
      revision: 2,
      releasedSnapshotId: 'ready',
      releasedSourceRevision: 2,
    };
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft').flush({ ...savedActivity, revision: 2 });
    await settle();
    FakeEventSource.opened[0].send();
    await settle();
    http.expectOne('/api/activity-drafts/draft').flush(released);
    await settle();
    const field = root().querySelector<HTMLInputElement>('#document-title')!;
    expect(field.value).toBe('עריכה מקומית');
    expect(field.disabled).toBe(true);
    expect(root().querySelector('a[href="/instances/ready"]')).not.toBeNull();
    expect(FakeEventSource.opened[0].readyState).toBe(FakeEventSource.CLOSED);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await click('reload-activity');
    http.expectOne('/api/activity-drafts/draft').flush(released);
    await settle();
    // Without local edits, the released draft reads as its frozen content.
    expect(root().querySelector('#document-title')).toBeNull();
    expect(root().querySelector('app-activity-document-view')!.textContent).toContain('תרגול');
    expect(root().querySelector('[aria-labelledby="available-title"]')).toBeNull();
    vi.restoreAllMocks();
  });
  it('never reports its own writes as changes made elsewhere', async () => {
    const ready = {
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
      diagnostics: {},
    };
    await open(ready);
    FakeEventSource.opened[0].send();
    await settle();
    const check = http.expectOne('/api/activity-drafts/draft');
    await click('release-activity');
    const release = await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft/release'));
    // Beginning the release cancels the older read; its own hint waits for the command.
    expect(check.cancelled).toBe(true);
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
    await click('create-activity');
    const start = http.expectOne('/api/activity-drafts/draft/operations');
    const body = start.request.body;
    start.error(new ProgressEvent('error'));
    await settle();
    expect(root().querySelector('#create-activity')!.getAttribute('aria-disabled')).toBe('true');
    expect(root().querySelector('#edit-activity')!.getAttribute('aria-disabled')).toBe('true');
    await click('recover-start');
    const replay = http.expectOne('/api/activity-drafts/draft/operations');
    expect(replay.request.body).toEqual(body);
    replay.flush({
      id: 'op',
      draftId: 'draft',
      kind: 'GenerateQuestions',
      status: 'completed',
      stage: 'questions',
      originalRevision: 1,
      expectedRevision: 2,
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
      await open({ ...savedActivity, activeOperationId: 'op' });
      advance(2000);
      http.expectOne('/api/activity-drafts/draft/operations/op').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
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
      await open({ ...savedActivity, activeOperationId: 'op' });
      show('hidden');
      advance(10_000);
      http.expectNone('/api/activity-drafts/draft/operations/op');
      show('visible');
      advance(0);
      http.expectOne('/api/activity-drafts/draft/operations/op').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
        status: 'completed',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 2,
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
    http.expectOne('/api/ai/status').flush({ configured: false });
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
    await click('edit-activity');
    const sourceButton = Array.from(root().querySelectorAll('button')).find((b) =>
      b.textContent?.trim().startsWith('החלפת הטקסט'),
    )!;
    sourceButton.click();
    await settle();
    await type('replacement-source', 'New!\nשלום');
    expect(root().querySelector('#release-activity')!.getAttribute('aria-disabled')).toBe('true');
    http.expectNone((r) => r.method === 'POST');
    root().querySelector<HTMLButtonElement>('#accept-source-replacement')!.click();
    await settle();
    const adopt = () =>
      Array.from(root().querySelectorAll('button')).find((b) => b.id === 'adopt-questions');
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
    http.expectNone('/api/ai/activity-plans');
  });
  it('releases only the exact saved revision and locks the terminal draft', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    await type('document-title', 'לבדיקה');
    await click('save-activity');
    const save = http.expectOne('/api/activity-drafts/draft');
    save.flush({
      ...savedActivity,
      revision: 2,
      document: { ...savedActivity.document, title: 'לבדיקה', questions: [savedQuestion] },
      diagnostics: {},
    });
    await settle();
    await click('release-activity');
    const release = await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft/release'));
    expect(release.request.body).toEqual({ expectedRevision: 2 });
    release.flush({ id: 'ready' });
    await vi.waitFor(() =>
      expect(root().querySelector('a[href="/instances/ready"]')).not.toBeNull(),
    );
    expect(root().querySelector('#document-title')).toBeNull();
    expect(root().querySelector('app-activity-document-view')).not.toBeNull();
    expect(root().querySelector('#release-activity')).toBeNull();
  });
  it('does not ask to approve a dirty or invalid saved revision', async () => {
    await open({
      ...savedActivity,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    const button = root().querySelector<HTMLButtonElement>('#release-activity')!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    button.click();
    await type('document-title', 'לבדיקה');
    button.click();
    await settle();
    http.expectNone((r) => r.method === 'POST');
    expect(window.confirm).not.toHaveBeenCalled();
  });
  it('keeps an undo checkpoint for automatically applied generation content', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open();
      await click('create-activity');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
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
      advance(2000);
      http
        .expectOne('/api/activity-drafts/draft/operations/op')
        .flush({ ...operation, status: 'completed', expectedRevision: 2 });
      http.expectOne('/api/activity-drafts/draft').flush({
        ...savedActivity,
        revision: 2,
        canUndo: true,
        document: { ...savedActivity.document, title: 'תוצאה' },
      });
      await settle();
      await click('plan-undo');
      const save = http.expectOne('/api/activity-drafts/draft/undo');
      expect(save.request.body.expectedRevision).toBe(2);
      expect(save.request.body).toEqual({ expectedRevision: 2 });
      save.flush({ ...savedActivity, revision: 3 });
      await settle();
    } finally {
      vi.useRealTimers();
    }
  });
  it('uses the new saved revision after cancelling without local edits', async () => {
    await open();
    await click('create-activity');
    const operation = {
      id: 'op',
      draftId: 'draft',
      kind: 'GenerateQuestions',
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
    await click('chat-cancel');
    http
      .expectOne('/api/activity-drafts/draft/operations/op/cancel')
      .flush({ ...operation, status: 'cancelled', expectedRevision: 2 });
    const read = await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft'));
    read.flush({ ...savedActivity, revision: 2 });
    await settle();
    await click('create-activity');
    const next = http.expectOne('/api/activity-drafts/draft/operations');
    expect(next.request.body.expectedRevision).toBe(2);
    next.flush({ ...operation, id: 'next' });
    await settle();
  });
  it('cancels an older poll before acknowledging cancellation so it cannot restore running status', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open({ ...savedActivity, activeOperationId: 'op' });
      advance(2000);
      http.expectOne('/api/activity-drafts/draft/operations/op').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
        status: 'calling',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      });
      http
        .expectOne('/api/activity-drafts/draft')
        .flush({ ...savedActivity, activeOperationId: 'op' });
      await settle();
      advance(2000);
      const oldPoll = http.expectOne('/api/activity-drafts/draft/operations/op');
      await click('chat-cancel');
      const cancellation = http.expectOne('/api/activity-drafts/draft/operations/op/cancel');
      expect(oldPoll.cancelled).toBe(true);
      cancellation.flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
        status: 'cancelled',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 2,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      });
      (await vi.waitFor(() => http.expectOne('/api/activity-drafts/draft'))).flush({
        ...savedActivity,
        revision: 2,
      });
      await settle();
      expect(root().querySelector('#chat-cancel')).toBeNull();
      advance(10_000);
      http.expectNone((request) => request.method === 'POST');
    } finally {
      vi.useRealTimers();
    }
  });

  it('follows a same-revision operation started elsewhere without replacing local edits', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open();
      await type('document-title', 'עריכה מקומית');
      FakeEventSource.opened[0].send();
      await settle();
      http
        .expectOne('/api/activity-drafts/draft')
        .flush({ ...savedActivity, activeOperationId: 'remote' });
      await settle();
      expect(root().querySelector('#create-activity')?.getAttribute('aria-disabled')).toBe('true');
      expect(titleValue()).toBe('עריכה מקומית');
      advance(2000);
      http.expectOne('/api/activity-drafts/draft/operations/remote').flush({
        id: 'remote',
        draftId: 'draft',
        kind: 'GenerateQuestions',
        status: 'completed',
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 2,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      });
      http.expectOne('/api/activity-drafts/draft').flush({
        ...savedActivity,
        revision: 2,
        document: { ...savedActivity.document, title: 'תוצאת המכשיר האחר' },
      });
      await settle();
      expect(titleValue()).toBe('עריכה מקומית');
      expect(root().textContent).toContain('הפעילות עודכנה במכשיר אחר');
      http.expectNone((request) => request.method === 'POST');
    } finally {
      vi.useRealTimers();
    }
  });
  it('reads a fresh draft after observing terminal status even if an earlier read preceded the checkpoint', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await open();
      await click('create-activity');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
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
      advance(2000);
      const status = http.expectOne('/api/activity-drafts/draft/operations/op');
      // A draft read already in flight saw the database before the worker committed.
      for (const read of http.match(
        (r) => r.method === 'GET' && r.url === '/api/activity-drafts/draft',
      ))
        read.flush(savedActivity);
      status.flush({ ...operation, status: 'completed', expectedRevision: 2 });
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
      expect(titleValue()).toBe('אחרי השמירה');
    } finally {
      vi.useRealTimers();
    }
  });
  it('keeps saved requirements read-only and refreshes reply-only chat at the same revision', async () => {
    await open();
    await type('chat-message', 'הוסף הסברים למפתח התשובות');
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      expect(root().querySelector('#plan-name')).toBeNull();
      root().querySelector<HTMLButtonElement>('#chat-send')!.click();
      await settle();
      const request = http.expectOne('/api/activity-drafts/draft/operations');
      expect(request.request.body).toMatchObject({
        kind: 'Revise',
        expectedRevision: 1,
        message: 'הוסף הסברים למפתח התשובות',
      });
      http.expectNone('/api/ai/activity-plans');
      const operation = {
        id: 'op',
        draftId: 'draft',
        kind: 'Revise',
        status: 'queued',
        stage: 'revise',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [],
        artifacts: null,
      };
      request.flush(operation);
      await settle();
      advance(2000);
      for (const read of http.match(
        (r) => r.method === 'GET' && r.url === '/api/activity-drafts/draft',
      ))
        read.flush(savedActivity);
      http
        .expectOne('/api/activity-drafts/draft/operations/op')
        .flush({ ...operation, status: 'completed' });
      await settle();
      const reply = {
        ...savedActivity,
        chat: [
          {
            role: 'assistant',
            text: 'הבקשה אינה נתמכת.',
            atUtc: '2026-10-09T00:00:00Z',
            target: null,
            operationId: 'op',
            assumptions: [],
            outcome: 'completed',
          },
        ],
      };
      http.expectOne('/api/activity-drafts/draft').flush(reply);
      await settle();
      expect(titleValue()).toBe(savedActivity.document.title);
      expect(root().textContent).toContain('הבקשה אינה נתמכת.');
    } finally {
      vi.useRealTimers();
    }
  });
  it('offers adoption only beside content the saved diagnostics mark as stale', async () => {
    await open({
      ...savedActivity,
      document: {
        ...savedActivity.document,
        questions: [savedQuestion, { ...savedQuestion, id: 'q2' }],
      },
      diagnostics: { 'questions[0].stale': ['השאלה דורשת יצירה מחדש או אימוץ.'] },
    });
    const adopt = Array.from(root().querySelectorAll('button')).filter(
      (b) => b.id === 'adopt-questions',
    );
    expect(adopt).toHaveLength(1);
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
    ['completed', 'accepted'],
    ['conflict', 'conflict'],
  ])(
    'keeps %s operation evidence readable without replacing the saved document',
    async (status, outcome) => {
      await open();
      await click('create-activity');
      http.expectOne('/api/activity-drafts/draft/operations').flush({
        id: 'op',
        draftId: 'draft',
        kind: 'GenerateQuestions',
        status,
        stage: 'questions',
        originalRevision: 1,
        expectedRevision: 1,
        failure: null,
        diagnosticsExpired: false,
        steps: [{ stage: 'questions', outcome, usage: null, metadata: null }],
        artifacts: {
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
      expect(root().querySelector('[data-edit-candidate]')).toBeNull();
      expect(root().querySelector('pre')!.textContent).toContain('מועמד');
      expect(root().querySelector('app-copy-button')).not.toBeNull();
      expect(titleValue()).toBe('תרגול');
      expect(root().querySelector('#question-0-prompt')).toBeNull();
      http.expectNone((r) => r.method === 'PUT');
    },
  );
  it('copies a released activity into a new draft only on request', async () => {
    await open({
      ...savedActivity,
      releasedSnapshotId: 'ready',
      releasedSourceRevision: 1,
      document: { ...savedActivity.document, questions: [savedQuestion] },
    });
    expect(root().querySelector('a[href="/instances/ready"]')!.textContent).toContain(
      'הקצאה לילדים',
    );
    http.expectNone((r) => r.method === 'POST');
    await click('copy-released');
    const copy = http.expectOne('/api/activity-drafts');
    expect(copy.request.body).toEqual({ snapshotId: 'ready' });
    copy.flush({ ...savedActivity, id: 'copy' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/activities/copy'));
    http.expectOne('/api/ai/status').flush({ configured: true });
    http.expectOne('/api/activity-drafts/copy').flush({ ...savedActivity, id: 'copy' });
    await settle();
  });
});
