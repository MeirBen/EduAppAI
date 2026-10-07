import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
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
  async function open() {
    const fixture = TestBed.createComponent(AssignmentList),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http
      .expectOne('/api/assignments?page=1')
      .flush(page([assignment, { ...assignment, id: 'completed', status: 'completed' }], true));
    http
      .expectOne('/api/children?page=1')
      .flush(page([{ id: 'child', name: 'נועה', enabled: false }]));
    await fixture.whenStable();
    return { fixture, http, root: fixture.nativeElement as HTMLElement };
  }
  it('pages and resets paging on child/status filters, including disabled profiles', async () => {
    const { fixture, http, root } = await open();
    root
      .querySelector<HTMLButtonElement>('nav[aria-label="עמודי פעילויות"] button:last-of-type')!
      .click();
    TestBed.tick();
    http.expectOne('/api/assignments?page=2').flush(page([], false, 2));
    await fixture.whenStable();
    const child = root.querySelector<HTMLSelectElement>('app-child-selector select')!;
    expect(child.options[1].disabled).toBe(false);
    child.value = 'child';
    child.dispatchEvent(new Event('change'));
    TestBed.tick();
    http.expectOne('/api/assignments?page=1&childId=child').flush(page([]));
    await fixture.whenStable();
    const status = root.querySelectorAll('select')[1];
    status.value = 'awaiting-review';
    status.dispatchEvent(new Event('change'));
    TestBed.tick();
    http.expectOne('/api/assignments?page=1&childId=child&status=awaiting-review').flush(page([]));
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
    root.querySelector<HTMLButtonElement>('#refresh-assignments')!.click();
    TestBed.tick();
    http
      .expectOne('/api/assignments?page=1')
      .flush(page([{ ...assignment, status: 'awaiting-review' }]));
    await fixture.whenStable();
    expect(root.querySelector('[data-withdraw]')).toBeNull();
  });
  it('restores keyboard focus after a delayed refresh removes the withdrawn row', async () => {
    const { fixture, http, root } = await open();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const button = root.querySelector<HTMLButtonElement>('[data-withdraw]')!;
    button.focus();
    button.click();
    http
      .expectOne('/api/assignments/assignment/withdraw')
      .flush({ ...assignment, status: 'withdrawn' });
    const refresh = await vi.waitFor(() => http.expectOne('/api/assignments?page=1'));
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    refresh.flush(page([]));
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(document.activeElement).toBe(root.querySelector('#assignments-heading')),
    );
  });

  it('refreshes after acknowledged withdrawal and cancels pending writes on destruction', async () => {
    const { fixture, http, root } = await open();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-withdraw]')!.click();
    http
      .expectOne('/api/assignments/assignment/withdraw')
      .flush({ ...assignment, status: 'withdrawn' });
    (await vi.waitFor(() => http.expectOne('/api/assignments?page=1'))).flush(page([assignment]));
    await fixture.whenStable();
    root.querySelector<HTMLButtonElement>('[data-withdraw]')!.click();
    const request = http.expectOne('/api/assignments/assignment/withdraw');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
