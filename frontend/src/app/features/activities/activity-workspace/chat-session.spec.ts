import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideLimits } from '../../../core/api/limits.fixture';
import { ActivityDetail } from '../../../core/api/models';
import { numericPlan } from '../learning-plan.fixture';
import { ActivityWorkspace } from './activity-workspace';

describe('Activity chat target composer', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const root = () => harness.routeNativeElement!;
  const composer = () => root().querySelector<HTMLTextAreaElement>('#chat-message')!;
  async function click(id: string) {
    root()
      .querySelector<HTMLButtonElement>('#' + id)!
      .click();
    await harness.fixture.whenStable();
  }
  async function type(message: string) {
    composer().value = message;
    composer().dispatchEvent(new Event('input', { bubbles: true }));
    await harness.fixture.whenStable();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideLimits(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [{ path: 'activities/:activityId', component: ActivityWorkspace }],
          withComponentInputBinding(),
        ),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/activities/draft', ActivityWorkspace);
    http.expectOne('/api/ai/status').flush({ configured: true });
    const draft: ActivityDetail = {
      id: 'draft',
      revision: 1,
      plan: numericPlan,
      document: {
        title: 'תרגול',
        instructions: null,
        materials: [],
        questions: ['q1', 'q2'].map((id) => ({
          id,
          prompt: 'כמה?',
          interaction: { type: 'numeric-input', options: null },
          answer: { value: '1' },
          points: 1,
          origin: { kind: 'manual' },
          acceptance: null,
        })),
      },
      diagnostics: {},
      measurements: [],
      activeOperationId: null,
      releasedSnapshotId: null,
      releasedSourceRevision: null,
      createdAtUtc: '2026-10-09T00:00:00Z',
      updatedAtUtc: '2026-10-09T00:00:00Z',
      chat: [],
      canUndo: false,
    };
    http.expectOne('/api/activity-drafts/draft').flush(draft);
    await harness.fixture.whenStable();
  });
  afterEach(() => http.verify());

  it('updates its own target prefix while keeping the parent suffix', async () => {
    await click('ask-question-q1');
    await type(composer().value + 'הסבירו את הפתרון');
    await click('ask-question-q2');
    expect(composer().value).toBe('לגבי שאלה 2: הסבירו את הפתרון');
    expect(root().querySelector('#chat-target')!.textContent).toContain('שאלה 2');
    expect(document.activeElement?.id).toBe('chat-message');
    http.expectNone((request) => request.method === 'POST');
  });

  it('removes only its own prefix when clearing a target', async () => {
    await click('ask-question-q1');
    await type(composer().value + 'הסבירו את הפתרון');
    await click('clear-chat-target');
    expect(composer().value).toBe('הסבירו את הפתרון');
    expect(root().querySelector('#chat-target')).toBeNull();
  });

  it('preserves parent-edited wording when selecting and clearing targets', async () => {
    await click('ask-question-q1');
    await type('על השאלה הראשונה והשנייה: האם יש הבדל?');
    await click('ask-question-q2');
    await click('clear-chat-target');
    expect(composer().value).toBe('על השאלה הראשונה והשנייה: האם יש הבדל?');
  });
});
