import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivityLibrary } from './activity-library';
import { FakeEventSource } from '../../../core/api/event-source.fixture';

const draft = { id: 'draft', name: 'בעבודה', revision: 2, updatedAtUtc: '2026-10-01T00:00:00Z' };
const snapshot = {
  id: 'ready',
  title: 'מוכנה',
  status: 'Ready',
  hasAssignments: false,
  createdAtUtc: draft.updatedAtUtc,
};
const page = <T>(items: T[], hasMore = false, number = 1) => ({
  items,
  page: number,
  pageSize: 25,
  hasMore,
});

describe('Activity library', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => vi.restoreAllMocks());

  async function render() {
    const fixture = TestBed.createComponent(ActivityLibrary);
    fixture.detectChanges();
    http.expectOne('/api/instances?page=1').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    await fixture.whenStable();
    return fixture;
  }

  it('loads only drafts and ready activities, with one creation entry', async () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    TestBed.tick();
    http.expectNone('/api/templates');
    http.expectOne('/api/activity-drafts?page=1').flush(page([]));
    http.expectOne('/api/instances?page=1').flush(page([]));
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('a[href="/activities/new"]')).toHaveLength(1);
    expect(root.textContent).not.toContain('תבני');
    http.verify();
  });

  it('describes both snapshot removal outcomes and acknowledges removal without claiming permanent deletion', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    root
      .querySelector<HTMLButtonElement>('section[aria-labelledby="ready-title"] article button')!
      .click();
    expect(confirm.mock.calls[0][0]).toContain('ארכיון');
    expect(confirm.mock.calls[0][0]).toContain('לצמיתות');
    http.expectOne((r) => r.url === '/api/instances/ready' && r.method === 'DELETE').flush(null);
    await fixture.whenStable();
    expect(root.textContent).toContain('הפעילות הוסרה');
    expect(root.textContent).not.toContain('נמחקה');
    expect(root.querySelector('a[href="/instances/ready"]')).toBeNull();
    http.verify();
  });

  it('separates editable drafts and ready snapshots, and deletes only the explicitly selected draft', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-delete-draft]')!.click();
    http
      .expectOne((r) => r.url === '/api/activity-drafts/draft' && r.method === 'DELETE')
      .flush(null);
    await vi.waitFor(() => expect(root.textContent).toContain('הטיוטה נמחקה'));
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    // The confirmed deletion leaves its list without refetching the library.
    http.verify();
  });

  it('pages a long list, counts only a whole list and steps back when a deletion empties a later page', async () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    fixture.detectChanges();
    http.expectOne('/api/instances?page=1').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft], true));
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const drafts = root.querySelector('section[aria-labelledby="drafts-title"]')!;
    expect(drafts.querySelector('h2')!.textContent).not.toMatch(/\d/);
    expect(root.querySelector('#ready-title')!.textContent).toContain('1');

    const pager = drafts.querySelector('nav[aria-label="עמודי טיוטות"]')!;
    [...pager.querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === 'הבא')!
      .click();
    TestBed.tick();
    const older = { ...draft, id: 'older', name: 'ישנה' };
    http.expectOne('/api/activity-drafts?page=2').flush(page([older], false, 2));
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/activities/older"]')).not.toBeNull();

    vi.spyOn(window, 'confirm').mockReturnValue(true);
    drafts.querySelector<HTMLButtonElement>('[data-delete-draft]')!.click();
    http
      .expectOne((r) => r.url === '/api/activity-drafts/older' && r.method === 'DELETE')
      .flush(null);
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    });
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    expect(drafts.querySelector('h2')!.textContent).toContain('1');
    http.verify();
  });

  it('reloads its lists after a change note, waiting for running reads and keeping them shown', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const [stream] = FakeEventSource.opened;
    expect(stream.url).toBe('/api/library/changes?ngsw-bypass');
    stream.send();
    TestBed.tick();
    const running = ['/api/instances?page=1', '/api/activity-drafts?page=1'].map((path) =>
      http.expectOne(path),
    );
    // This change may postdate the running reads, so the lists reload again once they settle.
    stream.send();
    TestBed.tick();
    http.expectNone('/api/activity-drafts?page=1');
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    running[0].flush(page([snapshot]));
    running[1].flush(page([draft]));
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne('/api/activity-drafts?page=1').flush(page([{ ...draft, id: 'phone' }, draft]));
    });
    http.expectOne('/api/instances?page=1').flush(page([snapshot]));
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/phone"]')).not.toBeNull();
  });

  it('holds the change stream only while the page is visible', async () => {
    const visibility = vi.spyOn(document, 'visibilityState', 'get');
    const show = (state: DocumentVisibilityState) => {
      visibility.mockReturnValue(state);
      document.dispatchEvent(new Event('visibilitychange'));
    };
    await render();
    show('hidden');
    expect(FakeEventSource.opened.map((source) => source.readyState)).toEqual([
      FakeEventSource.CLOSED,
    ]);
    show('visible');
    expect(FakeEventSource.opened.map((source) => source.readyState)).toEqual([
      FakeEventSource.CLOSED,
      0,
    ]);
  });

  it('keeps successful lists usable when another list fails and retries only that section', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    http.expectOne('/api/instances?page=1').flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    const ready = root.querySelector('[aria-labelledby="ready-title"]')!;
    expect(ready.textContent).not.toContain('עוד אין פעילויות מוכנות');
    ready.querySelector<HTMLButtonElement>('button')!.click();
    TestBed.tick();
    http.expectOne('/api/instances?page=1').flush(page([]));
    http.expectNone('/api/activity-drafts?page=1');
    await fixture.whenStable();
    expect(ready.textContent).toContain('עוד אין פעילויות מוכנות');
    http.verify();
  });

  it('rereads both lists after a confirmed reset, clearing a list that had failed', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    http.expectOne('/api/instances?page=1').flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('details .button-danger')!.click();
    http
      .expectOne((request) => request.method === 'DELETE' && request.url === '/api/learning-data')
      .flush(null);
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne('/api/activity-drafts?page=1').flush(page([]));
      http.expectOne('/api/instances?page=1').flush(page([]));
    });
    await fixture.whenStable();
    expect(root.textContent).toContain('נתוני הלמידה נמחקו');
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).toBeNull();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    http.verify();
  });

  it('cancels a pre-deletion list read and reconciles a subsequent server hint', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    FakeEventSource.opened[0].send();
    TestBed.tick();
    const stale = http.expectOne('/api/activity-drafts?page=1');
    http.expectOne('/api/instances?page=1').flush(page([snapshot]));
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-delete-draft]')!.click();
    http.expectOne((request) => request.method === 'DELETE').flush(null);
    await fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectOne('/api/activity-drafts?page=1').flush(page([]));
    http.expectOne('/api/instances?page=1').flush(page([snapshot]));
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    http.verify();
  });

  it('shows a closed stream and retries both observation and list reads on request', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const [stream] = FakeEventSource.opened;
    stream.readyState = FakeEventSource.CLOSED;
    stream.onerror?.();
    await fixture.whenStable();
    const retry = root.querySelector<HTMLButtonElement>('#reconnect-library');
    expect(retry).not.toBeNull();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    retry!.click();
    TestBed.tick();
    expect(FakeEventSource.opened).toHaveLength(2);
    http.expectOne('/api/instances?page=1').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    await fixture.whenStable();
    http.verify();
  });
});
