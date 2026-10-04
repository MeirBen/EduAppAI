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
import { LearningPlan } from '../../../core/api/models';
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
  async function open(path = '/activities/new', plan?: LearningPlan) {
    await harness.navigateByUrl(path, ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: true, schemaVersion: 1 });
    if (plan)
      http
        .expectOne('/api/templates/example')
        .flush({ id: 'example', currentVersion: 3, versionId: 'v3', definition: plan });
    await settle();
  }
  async function ask(message = 'תרגול חשבון') {
    await type('chat-message', message);
    await click('chat-send');
    return http.expectOne('/api/ai/template-drafts');
  }
  function reply(
    request: TestRequest,
    plan: LearningPlan | null = numericPlan,
    clarification: string | null = null,
    changes = [{ kind: 'added', path: 'plan' }],
  ) {
    request.flush({
      proposal: plan,
      clarification,
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
    TestBed.configureTestingModule({
      providers: [
        provideLimits(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [
            { path: 'activities/new', component: ActivityWorkspace },
            {
              path: 'templates/:templateId/edit',
              component: ActivityWorkspace,
              data: { context: 'template' },
            },
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

  it('associates requested-choice errors with the native field while preserving invalid typing', async () => {
    const id = 'a'.repeat(32);
    await open('/templates/example/create', {
      ...numericPlan,
      controls: [{ id, label: 'היסט', meaning: 'היסט התרגול', type: 'integer', required: true }],
    });
    expect(field(id + '-input').getAttribute('aria-describedby')).toContain(id + '-input-help');
    expect(root().querySelector('#' + id + '-input-help')!.textContent).toContain('נדרש ערך');
    await type(id + '-input', '1.5');
    expect(root().querySelector('#' + id + '-input-help')!.textContent).toContain('מספר שלם');
    expect(field(id + '-input').value).toBe('1.5');
    expect(field(id + '-input').getAttribute('aria-invalid')).toBe('true');
    http.expectNone('/api/ai/template-drafts');
  });

  it('edits a template definition and its defaults without creating activities', async () => {
    await open('/templates/example/edit', numericPlan);
    expect(root().querySelector('#workspace-title')!.textContent).toContain('עריכת תבנית');
    expect(field('plan-name').closest('details')!.open).toBe(true);
    expect(root().querySelector('#choices-title')).not.toBeNull();
    expect(field('activity-topic').closest('details')).toBeNull();
    expect(root().querySelector('#plan-topic')).toBeNull();
    expect(root().querySelector('#generate-activity')).toBeNull();
    expect(root().querySelector('#save-activity')).toBeNull();
    expect(root().querySelector('#save-template')!.closest('details')).toBeNull();
    expect(
      root().querySelector<HTMLAnchorElement>('#create-from-template')!.getAttribute('href'),
    ).toBe('/templates/example/create');
    await type('activity-topic', 'נושא חדש');
    await click('save-template');
    const save = http.expectOne('/api/templates/example/versions');
    expect(save.request.body.definition.defaults.topic).toBe('נושא חדש');
    save.flush({
      id: 'example',
      currentVersion: 4,
      versionId: 'v4',
      definition: save.request.body.definition,
    });
    await settle();
    expect(root().textContent).toContain('התבנית נשמרה בספרייה.');
    expect(root().textContent).not.toContain('הפעילות לא השתנתה');
    expect(root().textContent).not.toContain('לא נשמר');
    http.expectNone('/api/activity-drafts');
  });

  it('keeps ordinary choices visible and template internals quiet for a new activity', async () => {
    await open();
    reply(await ask());
    await settle();
    expect(field('activity-topic').closest('details')).toBeNull();
    expect(field('activity-questionCount').value).toBe('2');
    expect(field('plan-name').closest('details')!.open).toBe(false);
    // What later activities may change is a template's concern; an activity sets its own values.
    expect(root().querySelector('#choices-title')).toBeNull();
    expect(root().querySelector('#save-template')!.closest('details')!.open).toBe(false);
    expect(root().querySelector('#generate-activity')!.closest('details')).toBeNull();
    expect(root().querySelector('#release-activity')).toBeNull();
  });

  it('ends typing coalescence at successful publication so Undo restores the saved content', async () => {
    await open('/templates/example/edit', numericPlan);
    await type('plan-name', 'השם שנשמר');
    await click('save-template');
    const save = http.expectOne('/api/templates/example/versions');
    save.flush({
      id: 'example',
      currentVersion: 4,
      versionId: 'v4',
      definition: save.request.body.definition,
    });
    await settle();
    await type('plan-name', 'עריכה אחרי שמירה');
    await click('plan-undo');
    expect(field('plan-name').value).toBe('השם שנשמר');
    http.expectNone('/api/templates/example/versions');
  });

  it('coalesces typing and retains only the most recent twenty editable states', async () => {
    await open('/templates/example/edit', numericPlan);
    await type('plan-name', 'מ');
    await type('plan-name', 'מספרים חדשים');
    await click('plan-undo');
    expect(field('plan-name').value).toBe('מספרים');
    for (let index = 0; index < 22; index++)
      await type(index % 2 === 0 ? 'plan-name' : 'plan-goal', `עריכה ${index}`);
    for (let index = 0; index < 20; index++) await click('plan-undo');
    expect(root().querySelector<HTMLButtonElement>('#plan-undo')!.disabled).toBe(true);
    expect(field('plan-name').value).toBe('עריכה 0');
    expect(field('plan-goal').value).toBe('עריכה 1');
    http.expectNone('/api/ai/template-drafts');
  });

  it.each(['/activities/new', '/templates/example/edit'])(
    'uses the visible settings as the plan defaults at %s, including in AI changes',
    async (path) => {
      if (path === '/activities/new') {
        await open();
        reply(await ask());
        await settle();
      } else await open(path, numericPlan);
      expect(root().querySelector('#plan-questionCount')).toBeNull();
      await type('activity-questionCount', '7');
      const refine = await ask('שאלות קשות יותר');
      expect(refine.request.body.baseDefinition.defaults.questionCount).toBe(7);
      reply(
        refine,
        { ...numericPlan, defaults: { ...numericPlan.defaults, questionCount: 9 } },
        null,
        [{ kind: 'changed', path: 'defaults' }],
      );
      await settle();
      expect(field('activity-questionCount').value).toBe('9');
    },
  );

  it('keeps invalid initial edits instead of sending a request without their context', async () => {
    await open();
    await type('plan-name', 'תכנית חלקית');
    await type('chat-message', 'תרגול חשבון');
    await click('chat-send');
    http.expectNone('/api/ai/template-drafts');
    expect(field('plan-name').value).toBe('תכנית חלקית');
    expect(root().textContent).toContain('תקנו את ההגדרות המסומנות');
  });

  it('does not mark early local typing saved when the status version arrives later', async () => {
    await harness.navigateByUrl('/activities/new', ActivityWorkspace);
    const status = http.expectOne('/api/ai/status');
    field('plan-name').value = 'טיוטה מקומית';
    field('plan-name').dispatchEvent(new Event('input', { bubbles: true }));
    status.flush({ configured: false, schemaVersion: 1 });
    await settle();
    expect(root().textContent).toContain('לא נשמר');
    await click('plan-undo');
    expect(field('plan-name').value).toBe('');
  });

  it('applies a clean proposal locally without publication and moves from the request to settings', async () => {
    await open();
    const chatFollowsSettings = () =>
      !!(
        field('plan-name').compareDocumentPosition(field('chat-message')) &
        Node.DOCUMENT_POSITION_FOLLOWING
      );
    expect(root().querySelector('#plan-title')!.textContent).toContain('מה תרצו להכין?');
    expect(field('chat-message').getAttribute('aria-labelledby')).toBe('plan-title');
    expect(chatFollowsSettings()).toBe(false);
    expect(root().querySelector('#save-template')).toBeNull();
    expect(root().querySelector('#generate-activity')).toBeNull();
    field('chat-message').focus();
    const request = await ask();
    reply(request);
    await settle();
    expect(field('plan-name').value).toBe('מספרים');
    expect(root().querySelector('#plan-title')!.textContent).toContain('הגדרות הפעילות');
    expect(chatFollowsSettings()).toBe(true);
    expect(document.activeElement?.id).toBe('plan-title');
    expect(root().textContent).toContain('הכנו הגדרות לפי הבקשה');
    expect(root().textContent).not.toContain('נוספו הגדרות');
    http.expectNone('/api/templates');
    http.expectNone('/api/activity-drafts');
    await click('plan-undo');
    expect(field('plan-name').value).toBe('');
  });

  it.each(['typing', 'invalid', 'undo', 'cancel'])(
    'does not apply a late proposal after %s',
    async (action) => {
      await open('/templates/example/edit', numericPlan);
      if (action === 'undo') await type('plan-name', 'שלי');
      const request = await ask('לשנות את השם');
      if (action === 'typing') await type('plan-name', 'עריכה מקומית');
      if (action === 'invalid') await type('activity-questionCount', '');
      if (action === 'undo') await click('plan-undo');
      if (action === 'cancel') await click('chat-cancel');
      if (!request.cancelled) reply(request, { ...numericPlan, name: 'ישן' });
      await settle();
      expect(field('plan-name').value).not.toBe('ישן');
      if (action === 'invalid') expect(field('activity-questionCount').value).toBe('');
    },
  );

  it('drops pending responses when leaving the route', async () => {
    await open();
    const request = await ask();
    await open('/templates/example/edit', numericPlan);
    expect(request.cancelled).toBe(true);
    expect(field('plan-name').value).toBe('מספרים');
  });

  it('treats identical proposals as no-ops and expires clarification on local edits', async () => {
    await open('/templates/example/edit', numericPlan);
    const identical = await ask();
    reply(identical, numericPlan, null, []);
    await settle();
    expect(root().querySelector<HTMLButtonElement>('#plan-undo')!.disabled).toBe(true);
    const pending = await ask('לשנות');
    reply(pending, null, 'איזה נושא?');
    await settle();
    expect(root().textContent).toContain('איזה נושא?');
    await type('plan-goal', 'מטרה אחרת');
    expect(root().querySelector('#chat-clarification')).toBeNull();
  });

  it('retains successive clarification context and explicitly consolidates before continuing', async () => {
    await open();
    for (let index = 0; index < 4; index++) {
      const request = await ask(index === 0 ? 'הבקשה המקורית' : `תשובה ${index}`);
      expect(request.request.body.context).toHaveLength(index * 2);
      if (index > 0) expect(request.request.body.context[0].text).toBe('הבקשה המקורית');
      reply(request, null, `שאלה ${index}?`);
      await settle();
    }
    expect(root().textContent).toContain('איחוד הבקשה');
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    await type('chat-consolidated', 'בקשה מאוחדת שכוללת את כל התשובות');
    await click('chat-consolidate');
    const consolidated = http.expectOne('/api/ai/template-drafts');
    expect(consolidated.request.body.context).toEqual([]);
    expect(consolidated.request.body.message).toContain('כל התשובות');
    reply(consolidated);
    await settle();
    const next = await ask('עוד שינוי');
    expect(next.request.body.context).toEqual([]);
    expect(next.request.body.baseDefinition.name).toBe('מספרים');
    reply(next, numericPlan, null, []);
    await settle();
  });

  it('requires explicit consolidation at the character cap before the turn cap is exceeded', async () => {
    await open();
    const message = 'א'.repeat(4000);
    const clarification = 'ב'.repeat(1000);
    for (let index = 0; index < 3; index++) {
      const request = await ask(message);
      expect(request.request.body.context).toHaveLength(index * 2);
      reply(request, null, clarification);
      await settle();
    }
    expect(root().querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    expect(root().textContent).toContain('איחוד הבקשה');
    expect(root().textContent).toContain(message);
    expect(root().textContent).toContain(clarification);
    await click('chat-send');
    http.expectNone('/api/ai/template-drafts');
  });

  it('requires source confirmation, tracks it in Undo and publishes exact canonical text only', async () => {
    await open();
    const request = await ask();
    reply(request, suppliedPlan);
    await settle();
    await click('save-template');
    http.expectNone('/api/templates');
    await click('confirm-source-11111111111111111111111111111111');
    await click('plan-undo');
    await click('save-template');
    http.expectNone('/api/templates');
    await type('source-11111111111111111111111111111111', sourceText + '!');
    await click('save-template');
    const save = http.expectOne('/api/templates');
    expect(save.request.body.materials[0].text).toBe(sourceText + '!');
    expect(JSON.stringify(save.request.body)).not.toContain('confirmed');
    save.flush({ id: 'saved', currentVersion: 1, versionId: 'v1', definition: save.request.body });
    await settle();
    http.expectNone('/api/activity-drafts');
  });

  it('accepts reloaded sources and locks publication until one success updates expectedVersion', async () => {
    await open('/templates/example/edit', suppliedPlan);
    await type('plan-name', 'חדש');
    await click('save-template');
    const save = http.expectOne('/api/templates/example/versions');
    expect(save.request.body.expectedVersion).toBe(3);
    expect(field('plan-name').disabled).toBe(true);
    await click('save-template');
    http.expectNone('/api/templates/example/versions');
    save.flush({
      id: 'example',
      currentVersion: 4,
      versionId: 'v4',
      definition: save.request.body.definition,
    });
    await settle();
    await click('save-template');
    http.expectNone('/api/templates/example/versions');
    await type('plan-name', 'נוסף');
    await click('save-template');
    const next = http.expectOne('/api/templates/example/versions');
    expect(next.request.body.expectedVersion).toBe(4);
    next.flush({ title: 'התבנית השתנתה' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(field('plan-name').value).toBe('נוסף');
  });

  it('keeps local input after a lost publication response and offers the library without retrying', async () => {
    await open('/templates/example/edit', numericPlan);
    await type('plan-name', 'נשמר אולי');
    await click('save-template');
    http.expectOne('/api/templates/example/versions').error(new ProgressEvent('error'));
    await settle();
    expect(field('plan-name').value).toBe('נשמר אולי');
    expect(root().querySelector<HTMLAnchorElement>('#check-library')!.getAttribute('href')).toBe(
      '/templates',
    );
    http.expectNone('/api/templates/example/versions');
  });
});
