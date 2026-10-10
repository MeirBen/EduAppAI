import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { App } from '../../app';
import { appConfig } from '../../app.config';
import { ChildAuth } from './child-auth';

const identity = {
  childId: 'child',
  name: 'נועה',
  expiresAtUtc: '2026-11-01T00:00:00Z',
  answerLength: 200,
};

describe('Child route boundary', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    vi.restoreAllMocks();
  });

  it('reopens a remembered child device at activation after its session is lost', async () => {
    localStorage.setItem('entry-mode', 'child');
    const fixture = TestBed.createComponent(App),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const navigation = TestBed.inject(Router).navigateByUrl('/');
    for (let check = 0; check < 2; check++)
      (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(
        {},
        { status: 401, statusText: 'Unauthorized' },
      );
    await navigation;
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/child/activate');
    expect(fixture.nativeElement.querySelector('#activation-code')).not.toBeNull();
    expect(localStorage.getItem('entry-mode')).toBe('child');
    http.expectNone((r) => r.url.startsWith('/api/auth') || r.url === '/api/limits');
  });

  it.each(['/child', '/'])('restores %s without parent requests', async (url) => {
    const fixture = TestBed.createComponent(App),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const navigation = TestBed.inject(Router).navigateByUrl(url);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(identity);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
    if (url === '/') (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(identity);
    await navigation;
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/child/assignments?state=available&page=1'))).flush(
      { items: [], page: 1, pageSize: 25, hasMore: false },
    );
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(TestBed.inject(Router).url).toBe('/child');
    expect(root.querySelector('a[href="/child"]')).not.toBeNull();
    expect(root.querySelector('a[href="/activities"]')).toBeNull();
    expect(root.querySelector('[aria-label="ניהול המשפחה"]')).toBeNull();
    expect(root.querySelector('#main-content')).not.toBeNull();
    expect(root.querySelector('app-theme-picker')).not.toBeNull();
    http.expectNone((r) => r.url.startsWith('/api/auth') || r.url === '/api/limits');
  });

  it.each([
    ['/child', 401, '/child/activate'],
    ['/child', 503, '/child/access-unavailable'],
    ['/', 503, '/access-unavailable'],
    ['/', 403, '/access-unavailable'],
  ] as const)('%s handles HTTP %s without parent requests', async (startUrl, status, url) => {
    const fixture = TestBed.createComponent(App),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const navigation = TestBed.inject(Router).navigateByUrl(startUrl);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(
      {},
      { status, statusText: 'Unavailable' },
    );
    if (status === 401)
      (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(
        {},
        { status: 401, statusText: 'Unauthorized' },
      );
    await navigation;
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe(url);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('#activation-code') !== null).toBe(status === 401);
    expect(root.textContent).not.toContain('כניסת הורים');
    http.expectNone((r) => r.url.startsWith('/api/auth') || r.url === '/api/limits');
  });

  it.each([
    ['/child', 'session'],
    ['/child', 'token'],
    ['/', 'session'],
    ['/', 'token'],
  ])('cancels superseded %s %s checks', async (url, stage) => {
    const fixture = TestBed.createComponent(App),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const navigation = TestBed.inject(Router).navigateByUrl(url);
    let pending = await vi.waitFor(() => http.expectOne('/api/child/auth/me'));
    if (stage === 'token') {
      pending.flush(identity);
      pending = await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'));
    }
    await TestBed.inject(Router).navigateByUrl('/child/access-unavailable');
    await navigation;
    expect(pending.cancelled).toBe(true);
  });
  async function openPlayer() {
    const fixture = TestBed.createComponent(App),
      http = TestBed.inject(HttpTestingController),
      router = TestBed.inject(Router);
    fixture.detectChanges();
    const navigation = router.navigateByUrl('/child/assignments/work');
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(identity);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
    await navigation;
    (await vi.waitFor(() => http.expectOne('/api/child/assignments/work'))).flush({
      id: 'work',
      status: 'assigned',
      revision: 1,
      createdAtUtc: '2026-10-01T00:00:00Z',
      document: {
        title: 'תרגול',
        instructions: null,
        materials: [],
        questions: [
          {
            id: 'text',
            prompt: 'שאלה',
            interaction: { type: 'text-input', options: null },
            points: 1,
          },
        ],
      },
    });
    (await vi.waitFor(() => http.expectOne('/api/child/assignments/work/session'))).flush({
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
    });
    const field = await vi.waitFor(() => {
      const element = fixture.nativeElement.querySelector('#answer-text') as HTMLTextAreaElement;
      expect(element).not.toBeNull();
      return element;
    });
    field.value = 'טקסט מקומי';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    return { fixture, http, router, field };
  }
  it('preserves local text when the child cancels leaving after a 401 session check', async () => {
    const { fixture, http, router, field } = await openPlayer();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const navigation = router.navigateByUrl('/child');
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(
      {},
      { status: 401, statusText: 'Unauthorized' },
    );
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(
      {},
      { status: 401, statusText: 'Unauthorized' },
    );
    await navigation;
    await fixture.whenStable();
    expect(router.url).toBe('/child/assignments/work');
    expect(field.value).toBe('טקסט מקומי');
    expect(TestBed.inject(ChildAuth).identity()).toBeNull();
    expect(window.confirm).toHaveBeenCalledTimes(1);
    expect(field.disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('a[href="/child/activate"]')).not.toBeNull();
    fixture.nativeElement.querySelector('#save-answers').click();
    http.expectNone((r) => r.method === 'PUT');
  });
  it('does not revoke access when unsaved answers cancel disconnect', async () => {
    const { fixture, http, router } = await openPlayer();
    vi.spyOn(window, 'confirm').mockReturnValueOnce(true).mockReturnValueOnce(false);
    fixture.nativeElement.querySelector('nav button').click();
    await vi.waitFor(() => expect(window.confirm).toHaveBeenCalledTimes(2));
    await fixture.whenStable();
    expect(router.url).toBe('/child/assignments/work');
    http.expectNone((r) => r.url.endsWith('/logout') || r.url.endsWith('/csrf'));
    expect(TestBed.inject(ChildAuth).identity()?.childId).toBe('child');
  });
  it('cancels an old player write when navigating to a different assignment', async () => {
    const { fixture, http, router } = await openPlayer();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    fixture.nativeElement.querySelector('#save-answers').click();
    const save = await vi.waitFor(() => http.expectOne((r) => r.method === 'PUT'));
    const navigation = router.navigateByUrl('/child/assignments/next');
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(identity);
    await navigation;
    (await vi.waitFor(() => http.expectOne('/api/child/assignments/next'))).flush(
      {},
      { status: 404, statusText: 'Missing' },
    );
    expect(save.cancelled).toBe(true);
  });
});
