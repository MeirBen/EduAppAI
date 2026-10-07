import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AssignmentResult } from './assignment-result';

const assignment = {
  id: 'assigned',
  childId: 'child',
  childName: 'נועה',
  snapshotId: 'snapshot',
  title: 'תרגול',
  status: 'awaiting-review',
  revision: 2,
  createdAtUtc: '2026-10-01T00:00:00Z',
  hasStarted: true,
};
const report = {
  assignment,
  revision: 4,
  answers: [{ questionId: 'text', value: '  תשובה\n ' }],
  document: {
    title: 'תרגול',
    instructions: null,
    materials: [],
    questions: [
      {
        id: 'auto',
        prompt: 'כמה?',
        interaction: { type: 'numeric-input', options: null },
        points: 5,
        answer: { value: '2' },
        origin: { kind: 'manual' },
        acceptance: null,
      },
      {
        id: 'text',
        prompt: 'מדוע?',
        interaction: { type: 'text-input', options: null },
        points: 3,
        answer: { value: '<private-key>' },
        origin: { kind: 'manual' },
        acceptance: null,
      },
    ],
  },
  evaluation: {
    questions: [
      { questionId: 'auto', possiblePoints: 5, awardedPoints: 5, gradingMethod: 'automatic' },
      { questionId: 'text', possiblePoints: 3, awardedPoints: null, gradingMethod: 'parent' },
    ],
    automaticSubtotal: 5,
    possibleTotal: 8,
    pendingCount: 1,
    finalTotal: null,
  },
  scoringPolicyVersion: 1,
  startedAtUtc: '2026-10-01T00:00:00Z',
  submittedAtUtc: '2026-10-01T01:00:00Z',
  savedAtUtc: null,
  reviewedAtUtc: null,
  reviewedByParentId: null,
};
const completed = () => ({
  ...report,
  revision: 5,
  assignment: { ...assignment, status: 'completed', revision: 3 },
  evaluation: {
    ...report.evaluation,
    pendingCount: 0,
    finalTotal: 7,
    questions: report.evaluation.questions.map((q) =>
      q.questionId === 'text' ? { ...q, awardedPoints: 2 } : q,
    ),
  },
  reviewedAtUtc: '2026-10-01T02:00:00Z',
  reviewedByParentId: 'parent',
});

describe('Parent frozen result and grading', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });
  async function open(result: object = report) {
    const fixture = TestBed.createComponent(AssignmentResult),
      http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('assignmentId', 'assigned');
    fixture.detectChanges();
    http
      .expectOne('/api/assignments/assigned')
      .flush({ assignment, snapshot: { archivedAtUtc: null, document: report.document } });
    (await vi.waitFor(() => http.expectOne('/api/assignments/assigned/result'))).flush(result);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const type = (value: string) => {
      const field = root.querySelector<HTMLInputElement>('#grade-text')!;
      field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true }));
    };
    const submit = () =>
      root.querySelector('#review-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    return { fixture, http, root, type, submit };
  }
  it('shows frozen keys and exact child text only as text, with automatic awards read-only and pending grades blank', async () => {
    const { root } = await open();
    expect(root.textContent).toContain('<private-key>');
    expect(root.querySelector('private-key')).toBeNull();
    expect(root.querySelector('[data-child-answer="text"]')!.textContent).toBe('  תשובה\n ');
    expect(root.querySelector('#grade-auto')).toBeNull();
    expect(root.querySelector<HTMLInputElement>('#grade-text')!.value).toBe('');
    expect(root.textContent).toContain('ניקוד אוטומטי');
    expect(root.textContent).not.toContain('ציון סופי:');
  });
  it.each([
    ['2026-10-01T00:59:30Z', 'פחות מדקה'],
    ['2026-10-01T00:53:00Z', '7 דקות'],
    ['2026-09-30T23:53:00Z', 'שעה ו־7 דקות'],
    ['2026-09-29T23:00:00Z', '26 שעות'],
    [null, 'משך הזמן לא זמין'],
    ['invalid', 'משך הזמן לא זמין'],
    ['2026-10-01T02:00:00Z', 'משך הזמן לא זמין'],
  ])(
    'shows elapsed time for start %s independently of review time',
    async (startedAtUtc, expected) => {
      const { root } = await open({ ...completed(), startedAtUtc });
      expect(root.textContent).toContain('זמן מהפתיחה עד ההגשה (כולל הפסקות)');
      expect(root.querySelector('[data-elapsed-time]')?.textContent?.trim()).toBe(expected);
    },
  );
  it.each([null, report.startedAtUtc])(
    'shows unsubmitted start %s without a completed duration or creating work',
    async (startedAtUtc) => {
      const fixture = TestBed.createComponent(AssignmentResult),
        http = TestBed.inject(HttpTestingController);
      fixture.componentRef.setInput('assignmentId', 'assigned');
      fixture.detectChanges();
      http.expectOne('/api/assignments/assigned').flush({
        assignment: { ...assignment, status: 'assigned', hasStarted: startedAtUtc !== null },
        snapshot: { archivedAtUtc: null, document: report.document },
        startedAtUtc,
        submittedAtUtc: null,
      });
      await fixture.whenStable();
      const root = fixture.nativeElement as HTMLElement;
      expect(root.textContent).toContain(startedAtUtc ? 'נפתחה ב־' : 'עוד לא נפתחה');
      expect(root.textContent).toContain('עוד לא הוגשה');
      expect(root.querySelector('[data-elapsed-time]')).toBeNull();
      http.expectNone((r) => r.method !== 'GET' || r.url.endsWith('/result'));
    },
  );
  it.each([null, 'invalid'])(
    'shows unavailable duration for a submitted result with missing or invalid end %s',
    async (submittedAtUtc) => {
      const { root } = await open({ ...completed(), submittedAtUtc });
      expect(root.querySelector('[data-elapsed-time]')?.textContent?.trim()).toBe(
        'משך הזמן לא זמין',
      );
      expect(root.textContent).not.toContain('עוד לא הוגשה');
    },
  );
  it.each(['', '-1', '4', '1.5', '1e0'])(
    'rejects invalid grade %s without mutating',
    async (value) => {
      const { fixture, http, type, submit } = await open();
      type(value);
      submit();
      await fixture.whenStable();
      http.expectNone((r) => r.method === 'POST');
    },
  );
  it('uses the session revision, blocks duplicate submissions and freezes acknowledged grades', async () => {
    const { fixture, http, root, type, submit } = await open();
    type('2');
    submit();
    TestBed.tick();
    const request = http.expectOne('/api/assignments/assigned/review');
    expect(request.request.body).toEqual({
      expectedRevision: 4,
      grades: [{ questionId: 'text', points: 2 }],
    });
    submit();
    http.expectNone((r) => r.method === 'POST');
    request.flush(completed());
    await fixture.whenStable();
    expect(root.querySelector('#grade-text')).toBeNull();
    expect(root.textContent).toContain('ציון סופי:');
  });
  it.each([0, 409])(
    'preserves edits on status %s, reads saved grades only explicitly and warns on leaving',
    async (status) => {
      const { fixture, http, root, type, submit } = await open();
      type('1');
      submit();
      TestBed.tick();
      http
        .expectOne('/api/assignments/assigned/review')
        .flush({}, { status, statusText: 'Failure' });
      await fixture.whenStable();
      await vi.waitFor(() => expect(root.querySelector('[role="alert"]')).not.toBeNull());
      expect(root.querySelector<HTMLInputElement>('#grade-text')!.value).toBe('1');
      http.expectNone((r) => r.url.endsWith('/result') || r.method === 'POST');
      vi.mocked(window.confirm).mockReturnValue(false);
      expect(fixture.componentInstance.canLeave()).toBe(false);
      root.querySelector<HTMLButtonElement>('#read-saved-result')!.click();
      http.expectOne('/api/assignments/assigned/result').flush(completed());
      await fixture.whenStable();
      expect(root.querySelector<HTMLInputElement>('#grade-text')!.value).toBe('1');
      await vi.waitFor(() => expect(root.querySelector('#use-saved-result')).not.toBeNull());
      vi.mocked(window.confirm).mockReturnValue(true);
      root.querySelector<HTMLButtonElement>('#use-saved-result')!.click();
      await fixture.whenStable();
      expect(root.querySelector('#grade-text')).toBeNull();
      expect(fixture.componentInstance.canLeave()).toBe(true);
    },
  );
  it('cancels review when its page is destroyed', async () => {
    const { fixture, http, type, submit } = await open();
    type('0');
    submit();
    TestBed.tick();
    const request = http.expectOne('/api/assignments/assigned/review');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
  it('shows no percentage for zero possible points', async () => {
    const final = completed();
    final.evaluation.possibleTotal = 0;
    final.evaluation.finalTotal = 0;
    const { root } = await open(final);
    expect(root.textContent).toContain('ציון סופי:');
    expect(root.textContent).not.toContain('%');
  });
});
