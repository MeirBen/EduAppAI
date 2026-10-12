import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FakeEventSource } from '../../core/api/event-source.fixture';
import { AssignmentList } from './assignment-list';

const assignment = {
  id: 'assignment',
  childId: 'child',
  childName: 'נועה',
  snapshotId: 'snapshot',
  title: 'תרגול',
  status: 'assigned',
  revision: 3,
  createdAtUtc: '2026-10-01T00:00:00Z',
  hasStarted: true,
};
const page = (items: object[], hasMore = false, number = 1) => ({
  items,
  page: number,
  pageSize: 25,
  hasMore,
});

const list = '/api/assignments?page=1&status=open';

describe('Parent assignment list', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });
  async function open(
    rows = [assignment, { ...assignment, id: 'completed', status: 'completed' }],
  ) {
    const fixture = TestBed.createComponent(AssignmentList),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(list).flush(page(rows, true));
    http
      .expectOne('/api/children?page=1')
      .flush(page([{ id: 'child', name: 'נועה', enabled: false }]));
    await fixture.whenStable();
    return { fixture, http, root: fixture.nativeElement as HTMLElement };
  }
  it('follows child submissions in place and reconnects a refused stream on request', async () => {
    const { fixture, http, root } = await open([assignment]);
    const [stream] = FakeEventSource.opened;
    expect(stream?.url).toBe('/api/library/changes?ngsw-bypass');
    stream.send();
    TestBed.tick();
    const reread = http.expectOne(list);
    fixture.detectChanges();
    // A background reread keeps the shown rows and adds no loading row above them.
    expect(root.querySelector('[data-withdraw]')).not.toBeNull();
    expect(root.querySelector('app-loading-indicator.loading-active')).toBeNull();
    reread.flush(page([{ ...assignment, status: 'awaiting-review' }]));
    await fixture.whenStable();
    expect(root.querySelector('[data-withdraw]')).toBeNull();
    expect(root.querySelector('app-live-status button')).toBeNull();
    stream.readyState = FakeEventSource.CLOSED;
    stream.onerror?.();
    await fixture.whenStable();
    expect(root.querySelector('app-live-status')!.textContent).toContain(
      'העדכונים האוטומטיים נעצרו',
    );
    root.querySelector<HTMLButtonElement>('app-live-status button')!.click();
    TestBed.tick();
    expect(FakeEventSource.opened).toHaveLength(2);
    http.expectOne(list).flush(page([{ ...assignment, status: 'completed' }]));
    await fixture.whenStable();
    fixture.destroy();
    expect(FakeEventSource.opened[1].readyState).toBe(FakeEventSource.CLOSED);
  });

  it('opens on open work, then pages and resets paging on view and child filters', async () => {
    const { fixture, http, root } = await open();
    expect(root.querySelector<HTMLInputElement>('input[value="open"]')!.checked).toBe(true);
    root
      .querySelector<HTMLButtonElement>('nav[aria-label="עמודי פעילויות"] button:last-of-type')!
      .click();
    TestBed.tick();
    http.expectOne('/api/assignments?page=2&status=open').flush(page([], false, 2));
    await fixture.whenStable();
    const child = root.querySelector<HTMLSelectElement>('app-child-selector select')!;
    expect(child.options[1].disabled).toBe(false);
    child.value = 'child';
    child.dispatchEvent(new Event('change'));
    TestBed.tick();
    http.expectOne('/api/assignments?page=1&status=open&childId=child').flush(page([]));
    await fixture.whenStable();
    root.querySelector<HTMLInputElement>('input[value="completed"]')!.click();
    TestBed.tick();
    http.expectOne('/api/assignments?page=1&status=completed&childId=child').flush(page([]));
    await fixture.whenStable();
    expect(root.querySelector('.empty-state')!.textContent).toContain('אין פעילויות שמתאימות');
    http.expectNone((r) => r.method !== 'GET');
  });
  it('withdraws assigned work only after confirmation, using its revision and no implicit retry', async () => {
    const { fixture, http, root } = await open();
    const buttons = root.querySelectorAll<HTMLButtonElement>('[data-withdraw]');
    expect(buttons.length).toBe(1);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    buttons[0].click();
    http.expectNone((r) => r.method === 'POST');
    confirm.mockReturnValue(true);
    buttons[0].click();
    const request = http.expectOne('/api/assignments/assignment/withdraw');
    expect(request.request.body).toEqual({ expectedRevision: 3 });
    buttons[0].click();
    http.expectNone((r) => r.method === 'POST');
    request.flush({}, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => expect(root.textContent).toContain('מצב ההקצאה השתנה'));
    // A conflict means the shown state is stale, so the list rereads itself.
    (await vi.waitFor(() => http.expectOne(list))).flush(
      page([{ ...assignment, status: 'awaiting-review' }]),
    );
    await fixture.whenStable();
    expect(root.querySelector('[data-withdraw]')).toBeNull();
  });
  it('restores keyboard focus after a delayed refresh removes the withdrawn row', async () => {
    const { fixture, http, root } = await open();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const button = root.querySelector<HTMLButtonElement>('[data-withdraw]')!;
    button.focus();
    button.click();
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectNone(list);
    http
      .expectOne('/api/assignments/assignment/withdraw')
      .flush({ ...assignment, status: 'withdrawn' });
    const refresh = await vi.waitFor(() => http.expectOne(list));
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    refresh.flush(page([]));
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(document.activeElement).toBe(root.querySelector('#assignments-heading')),
    );
  });

  it('restores withdrawn work at the revision shown, without a confirmation, then refreshes', async () => {
    const { fixture, http, root } = await open([
      { ...assignment, status: 'withdrawn', revision: 2 },
    ]);
    const confirm = vi.spyOn(window, 'confirm');
    expect(root.querySelector('[data-withdraw]')).toBeNull();
    root.querySelector<HTMLButtonElement>('[data-restore]')!.click();
    const request = http.expectOne('/api/assignments/assignment/restore');
    expect(request.request.body).toEqual({ expectedRevision: 2 });
    request.flush({ ...assignment, revision: 3 });
    (await vi.waitFor(() => http.expectOne(list))).flush(page([assignment]));
    await fixture.whenStable();
    expect(confirm).not.toHaveBeenCalled();
    expect(root.textContent).toContain('ההקצאה הוחזרה');
  });

  it('refreshes after acknowledged withdrawal and cancels pending writes on destruction', async () => {
    const { fixture, http, root } = await open();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-withdraw]')!.click();
    http
      .expectOne('/api/assignments/assignment/withdraw')
      .flush({ ...assignment, status: 'withdrawn' });
    (await vi.waitFor(() => http.expectOne(list))).flush(page([assignment]));
    await fixture.whenStable();
    root.querySelector<HTMLButtonElement>('[data-withdraw]')!.click();
    const request = http.expectOne('/api/assignments/assignment/withdraw');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
