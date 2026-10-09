import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivityLibrary } from './activity-library';
import { provideLimits } from '../../../core/api/limits.fixture';
import { FakeEventSource } from '../../../core/api/event-source.fixture';

const draft = { id: 'draft', name: 'בעבודה', revision: 2, updatedAtUtc: '2026-10-01T00:00:00Z' };
const snapshot = {
  id: 'ready',
  title: 'מוכנה',
  status: 'Ready',
  hasAssignments: false,
  createdAtUtc: draft.updatedAtUtc,
};

describe('Activity library', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideLimits(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => vi.restoreAllMocks());

  async function render() {
    const fixture = TestBed.createComponent(ActivityLibrary);
    fixture.detectChanges();
    http.expectOne('/api/templates').flush([]);
    http.expectOne('/api/instances').flush([snapshot]);
    http.expectOne('/api/activity-drafts').flush([draft]);
    await fixture.whenStable();
    return fixture;
  }

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

  it('reloads its lists after a change note, waiting for running reads and keeping them shown', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const [stream] = FakeEventSource.opened;
    expect(stream.url).toBe('/api/library/changes?ngsw-bypass');
    stream.send();
    TestBed.tick();
    const running = ['/api/templates', '/api/instances', '/api/activity-drafts'].map((path) =>
      http.expectOne(path),
    );
    // This change may postdate the running reads, so the lists reload again once they settle.
    stream.send();
    TestBed.tick();
    http.expectNone('/api/activity-drafts');
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    running[0].flush([]);
    running[1].flush([snapshot]);
    running[2].flush([draft]);
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne('/api/activity-drafts').flush([{ ...draft, id: 'phone' }, draft]);
    });
    http.expectOne('/api/templates').flush([]);
    http.expectOne('/api/instances').flush([snapshot]);
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
    http.expectOne('/api/activity-drafts').flush([draft]);
    http.expectOne('/api/instances').flush([snapshot]);
    http.expectOne('/api/templates').flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    const templates = root.querySelector('[aria-labelledby="templates-title"]')!;
    expect(templates.textContent).not.toContain('עוד אין תבניות');
    templates.querySelector<HTMLButtonElement>('button')!.click();
    TestBed.tick();
    http.expectOne('/api/templates').flush([]);
    http.expectNone('/api/activity-drafts');
    http.expectNone('/api/instances');
    await fixture.whenStable();
    expect(templates.textContent).toContain('עוד אין תבניות');
    http.verify();
  });

  it('applies a confirmed reset even when a list resource was in error', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectOne('/api/activity-drafts').flush([draft]);
    http.expectOne('/api/instances').flush([snapshot]);
    http.expectOne('/api/templates').flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('details .button-danger')!.click();
    http
      .expectOne((request) => request.method === 'DELETE' && request.url === '/api/learning-data')
      .flush(null);
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
    const stale = http.expectOne('/api/activity-drafts');
    http.expectOne('/api/templates').flush([]);
    http.expectOne('/api/instances').flush([snapshot]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-delete-draft]')!.click();
    http.expectOne((request) => request.method === 'DELETE').flush(null);
    await fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectOne('/api/activity-drafts').flush([]);
    http.expectOne('/api/templates').flush([]);
    http.expectOne('/api/instances').flush([snapshot]);
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
    http.expectOne('/api/templates').flush([]);
    http.expectOne('/api/instances').flush([snapshot]);
    http.expectOne('/api/activity-drafts').flush([draft]);
    await fixture.whenStable();
    http.verify();
  });
});
