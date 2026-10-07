import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ChildAuth } from '../../core/auth/child-auth';
import { LearnerSession } from '../../core/api/child-models';
import { ChildPlayer } from './child-player';

const assignment = {
  id: 'work',
  status: 'assigned',
  revision: 1,
  createdAtUtc: '2026-10-01T00:00:00Z',
  document: {
    title: 'פעילות <script>',
    instructions: 'ענו לפי הטקסט',
    materials: [
      { id: 'story', title: 'סיפור', body: 'פסקה א\n\nפסקה ב' },
      { id: 'poem', title: 'שיר', body: 'שורה א\nשורה ב' },
    ],
    questions: [
      {
        id: 'number',
        prompt: '2 + 3 = ?',
        interaction: { type: 'numeric-input', options: null },
        points: 2,
      },
      {
        id: 'text',
        prompt: 'מדוע?',
        interaction: { type: 'text-input', options: null },
        points: 3,
      },
      {
        id: 'choice',
        prompt: 'בחרו',
        interaction: { type: 'single-choice', options: ['אדום', 'blue'] },
        points: 1,
      },
    ],
  },
};
const session: LearnerSession = {
  assignmentId: 'work',
  revision: 1,
  status: 'assigned',
  answers: [],
  startedAtUtc: '2026-10-01T00:00:00Z',
  savedAtUtc: null,
  submittedAtUtc: null,
  reviewedAtUtc: null,
  finalTotal: null,
  possibleTotal: null,
};
const sessionUrl = '/api/child/assignments/work/session';

describe('Child player', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
  });
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      vi.restoreAllMocks();
      TestBed.resetTestingModule();
    }
  });
  async function open(initial = session) {
    const http = TestBed.inject(HttpTestingController);
    const check = firstValueFrom(TestBed.inject(ChildAuth).loadSession());
    http.expectOne('/api/child/auth/me').flush({
      childId: 'child',
      name: 'נועה',
      expiresAtUtc: '2026-11-01T00:00:00Z',
      answerLength: 200,
    });
    http.expectOne('/api/child/auth/csrf').flush({});
    await check;
    const fixture = TestBed.createComponent(ChildPlayer);
    fixture.componentRef.setInput('assignmentId', 'work');
    fixture.autoDetectChanges();
    http.expectOne('/api/child/assignments/work').flush(assignment);
    (
      await vi.waitFor(() => http.expectOne((r) => r.url === sessionUrl && r.method === 'POST'))
    ).flush(initial);
    await vi.waitFor(() => expect(fixture.nativeElement.querySelector('ol')).not.toBeNull());
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const field = (id: string) =>
      root.querySelector<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(
        '#answer-' + id,
      )!;
    const type = (id: string, value: string) => {
      const control = field(id);
      control.value = value;
      control.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
    };
    const choose = (option: string) => {
      root.querySelector<HTMLInputElement>(`input[type="radio"][value="${option}"]`)!.click();
      fixture.detectChanges();
    };
    const save = () => root.querySelector<HTMLButtonElement>('#save-answers')!.click();
    const submit = () =>
      root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    return { http, fixture, root, field, type, choose, save, submit };
  }
  it('renders learner text faithfully with numbered questions, isolated calculations and native choices', async () => {
    const { root, field } = await open();
    expect(root.querySelector('[data-material="story"]')!.textContent).toBe('פסקה א\n\nפסקה ב');
    expect(root.querySelector('[data-material="poem"]')!.textContent).toBe('שורה א\nשורה ב');
    expect(root.querySelector('[data-material]')!.classList.contains('whitespace-pre-wrap')).toBe(
      true,
    );
    expect(root.querySelectorAll('ol[role="list"] > li')).toHaveLength(3);
    expect(root.querySelector('label bdi')!.textContent).toBe('2 + 3 = ?');
    expect(field('number').getAttribute('type')).toBe('text');
    expect(field('number').getAttribute('dir')).toBe('ltr');
    // Every option stays visible as a native radio, labelled by the question in its legend.
    const options = root.querySelectorAll<HTMLInputElement>('fieldset input[type="radio"]');
    expect([...options].map((option) => option.value)).toEqual(['אדום', 'blue']);
    expect(new Set([...options].map((option) => option.name)).size).toBe(1);
    expect(root.querySelector('fieldset legend')!.textContent!.trim()).not.toBe('');
    expect(root.querySelector('script')).toBeNull();
    expect(root.querySelector('details')).toBeNull();
  });
  it('saves incomplete numeric text and exact multiline answers only explicitly, then clears dirty state', async () => {
    const { http, fixture, root, type, choose, save } = await open();
    type('number', '-');
    type('text', '  תשובה\n ');
    choose('blue');
    await fixture.whenStable();
    http.expectNone((r) => r.method !== 'GET');
    vi.mocked(window.confirm).mockReturnValue(false);
    expect(fixture.componentInstance.canLeave()).toBe(false);
    const beforeUnload = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(beforeUnload);
    expect(beforeUnload.defaultPrevented).toBe(true);
    save();
    const request = await vi.waitFor(() =>
      http.expectOne((r) => r.url === sessionUrl && r.method === 'PUT'),
    );
    expect(request.request.body).toEqual({
      expectedRevision: 1,
      answers: [
        { questionId: 'number', value: '-' },
        { questionId: 'text', value: '  תשובה\n ' },
        { questionId: 'choice', value: 'blue' },
      ],
    });
    save();
    http.expectNone((r) => r.method === 'PUT');
    request.flush({
      ...session,
      revision: 2,
      answers: request.request.body.answers,
      savedAtUtc: '2026-10-01T00:05:00Z',
    });
    await fixture.whenStable();
    expect(fixture.componentInstance.canLeave()).toBe(true);
    expect(root.querySelector('.action-bar-status [role="status"]')!.textContent!.trim()).toBe(
      'נשמר',
    );
  });
  it('asks before every submission, since it is final, and sends nothing when the child cancels', async () => {
    const { http, fixture, type, choose, submit } = await open();
    type('number', '3');
    type('text', 'תשובה');
    choose('blue');
    vi.mocked(window.confirm).mockReturnValue(false);
    submit();
    await fixture.whenStable();
    http.expectNone((r) => r.url.endsWith('/submit'));
    expect(window.confirm).toHaveBeenLastCalledWith(
      'להגיש להורה? אחרי ההגשה אי אפשר לשנות את התשובות.',
    );
  });
  it('accepts a number with the surrounding spaces a keyboard adds and sends it without them', async () => {
    const { http, type, submit } = await open();
    type('number', ' 12.5 ');
    vi.mocked(window.confirm).mockReturnValue(true);
    submit();
    const request = await vi.waitFor(() => http.expectOne(sessionUrl + '/submit'));
    expect(request.request.body.answers).toContainEqual({ questionId: 'number', value: '12.5' });
  });
  it.each(['-', '1.', '.5', '1e2', '1,000', '1 2'])(
    'keeps invalid numeric input %j visible and focuses it on submit',
    async (value) => {
      const { http, fixture, field, type, submit } = await open();
      type('number', value);
      submit();
      await fixture.whenStable();
      http.expectNone((r) => r.url.endsWith('/submit'));
      expect(field('number').value).toBe(value);
      expect(document.activeElement).toBe(field('number'));
    },
  );
  it('requires confirmation for missing answers and freezes a pending receipt without a final score', async () => {
    const { http, fixture, root, type, submit } = await open();
    type('text', '  הסבר\n');
    vi.mocked(window.confirm).mockReturnValue(false);
    submit();
    await fixture.whenStable();
    http.expectNone((r) => r.url.endsWith('/submit'));
    vi.mocked(window.confirm).mockReturnValue(true);
    submit();
    const request = await vi.waitFor(() => http.expectOne(sessionUrl + '/submit'));
    request.flush({
      ...session,
      revision: 2,
      status: 'awaiting-review',
      answers: request.request.body.answers,
      submittedAtUtc: '2026-10-01T00:06:00Z',
      possibleTotal: 6,
    });
    await fixture.whenStable();
    expect(root.textContent).toContain('העבודה הוגשה');
    expect(root.textContent).not.toContain('הציון:');
    expect(root.querySelector('#save-answers')).toBeNull();
    expect(root.querySelector('[data-saved-answer="text"]')!.textContent).toBe('  הסבר\n');
    expect(fixture.componentInstance.canLeave()).toBe(true);
  });
  it.each([0, 409, 503])(
    'preserves input after save HTTP %s, reads only on request and explicitly replaces a newer checkpoint',
    async (status) => {
      const { http, fixture, root, field, type, save } = await open();
      type('text', 'שלי');
      save();
      (await vi.waitFor(() => http.expectOne((r) => r.method === 'PUT'))).flush(
        {},
        { status, statusText: 'Failure' },
      );
      await fixture.whenStable();
      expect(field('text').value).toBe('שלי');
      save();
      http.expectNone((r) => r.method === 'PUT' || r.method === 'GET');
      expect(root.querySelector('[role="alert"]')!.textContent).not.toMatch(/AI|תבנית|כניסת הורים/);
      root.querySelector<HTMLButtonElement>('#read-saved-session')!.click();
      http
        .expectOne(sessionUrl)
        .flush({ ...session, revision: 2, answers: [{ questionId: 'text', value: 'שמור' }] });
      await fixture.whenStable();
      expect(field('text').value).toBe('שלי');
      vi.mocked(window.confirm).mockReturnValue(false);
      root.querySelector<HTMLButtonElement>('#use-saved-session')!.click();
      expect(field('text').value).toBe('שלי');
      vi.mocked(window.confirm).mockReturnValue(true);
      root.querySelector<HTMLButtonElement>('#use-saved-session')!.click();
      await fixture.whenStable();
      expect(field('text').value).toBe('שמור');
      expect(fixture.componentInstance.canLeave()).toBe(true);
    },
  );
  it('recovers a committed submit through a read without resubmitting or replacing local text implicitly', async () => {
    const { http, fixture, root, field, type, submit } = await open();
    type('text', 'הטקסט שלי');
    submit();
    const request = await vi.waitFor(() => http.expectOne(sessionUrl + '/submit'));
    request.error(new ProgressEvent('error'));
    await fixture.whenStable();
    root.querySelector<HTMLButtonElement>('#read-saved-session')!.click();
    http.expectOne(sessionUrl).flush({
      ...session,
      revision: 2,
      status: 'completed',
      answers: [{ questionId: 'text', value: 'טקסט שנשלח' }],
      submittedAtUtc: '2026-10-01T00:06:00Z',
      finalTotal: 0,
      possibleTotal: 0,
    });
    await fixture.whenStable();
    expect(field('text').value).toBe('הטקסט שלי');
    expect(field('text').disabled).toBe(true);
    expect(root.textContent).toContain('הציון:');
    expect(root.textContent).not.toContain('%');
    http.expectNone((r) => r.method === 'POST');
  });
  it.each([401, 404, 410])('locks work but retains local answers after HTTP %s', async (status) => {
    const { http, fixture, root, field, type, save } = await open();
    type('text', 'לשמור');
    save();
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'PUT'))).flush(
      {},
      { status, statusText: 'Unavailable' },
    );
    await fixture.whenStable();
    expect(field('text').value).toBe('לשמור');
    expect(field('text').disabled).toBe(true);
    expect(root.querySelector('a[href="/child/activate"]') !== null).toBe(status === 401);
    if (status === 410) expect(root.textContent).toContain('בוטלה');
  });
  it.each([401, 404, 410])(
    'cannot unlock a known HTTP %s denial by accepting an older offered checkpoint',
    async (status) => {
      const { http, fixture, root, field, type, save } = await open();
      type('text', 'מקומי');
      save();
      (await vi.waitFor(() => http.expectOne((r) => r.method === 'PUT'))).flush(
        {},
        { status: 409, statusText: 'Conflict' },
      );
      await fixture.whenStable();
      root.querySelector<HTMLButtonElement>('#read-saved-session')!.click();
      http
        .expectOne(sessionUrl)
        .flush({ ...session, revision: 2, answers: [{ questionId: 'text', value: 'ישן' }] });
      await fixture.whenStable();
      root.querySelector<HTMLButtonElement>('#read-saved-session')!.click();
      http.expectOne(sessionUrl).flush({}, { status, statusText: 'Unavailable' });
      await fixture.whenStable();
      expect(field('text').disabled).toBe(true);
      root.querySelector<HTMLButtonElement>('#use-saved-session')?.click();
      await fixture.whenStable();
      expect(field('text').disabled).toBe(true);
      expect(field('text').value).toBe('מקומי');
      expect(root.querySelector('[role="alert"]')).not.toBeNull();
      save();
      http.expectNone((r) => r.method === 'PUT');
    },
  );
  it('offers the saved-work check only to recover, not beside a plain session', async () => {
    const { http, fixture, root, type, save } = await open();
    expect(root.querySelector('#read-saved-session')).toBeNull();
    type('text', 'שלי');
    save();
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'PUT'))).flush(
      {},
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    expect(root.querySelector('#read-saved-session')).not.toBeNull();
  });
  it('renders completed sessions read-only and without a percentage for zero possible points', async () => {
    const { root } = await open({
      ...session,
      status: 'completed',
      finalTotal: 0,
      possibleTotal: 0,
      submittedAtUtc: '2026-10-01T00:06:00Z',
    });
    expect(root.querySelector('form')).toBeNull();
    expect(root.textContent).toContain('הציון:');
    expect(root.textContent).not.toContain('%');
    // A submitted child checks here for the parent's grade.
    expect(root.querySelector('#read-saved-session')).not.toBeNull();
  });
  it('cancels a pending save when the player is destroyed', async () => {
    const { http, fixture, type, save } = await open();
    type('text', 'מקומי');
    save();
    const request = await vi.waitFor(() => http.expectOne((r) => r.method === 'PUT'));
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
