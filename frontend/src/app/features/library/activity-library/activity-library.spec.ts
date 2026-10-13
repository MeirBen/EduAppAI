import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivityLibrary } from './activity-library';
import { FakeEventSource } from '../../../core/api/event-source.fixture';

const draft = {
  id: 'draft',
  name: 'בעבודה',
  difficulty: 'easy',
  revision: 2,
  updatedAtUtc: '2026-10-01T00:00:00Z',
};
const snapshot = {
  id: 'ready',
  title: 'מוכנה',
  difficulty: 'hard',
  assignedAtUtc: '2026-10-02T00:00:00Z',
  completedAtUtc: null,
  createdAtUtc: draft.updatedAtUtc,
  archivedAtUtc: null,
  children: [
    { childName: 'נועה', status: 'awaiting-review', finalTotal: null, possibleTotal: null },
  ],
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
  const flushArchive = (items: object[] = []) =>
    http.expectOne('/api/instances/archive?page=1').flush(page(items));
  const flushCounts = (drafts = 1, assigned = 1, archived = 0) =>
    http
      .expectOne('/api/library/counts')
      .flush({ drafts, assigned, unassigned: 0, completed: 0, archived });

  async function render() {
    const fixture = TestBed.createComponent(ActivityLibrary);
    fixture.detectChanges();
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    flushArchive();
    flushCounts();
    await fixture.whenStable();
    return fixture;
  }

  it('loads only drafts and ready activities, with one creation entry', async () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    TestBed.tick();
    http.expectNone('/api/templates');
    http.expectOne('/api/activity-drafts?page=1').flush(page([]));
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([]));
    flushArchive();
    flushCounts(0, 0, 0);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('a[href="/activities/new"]')).toHaveLength(1);
    // Empty sections start folded and still say they are empty.
    for (const id of ['drafts-title', 'ready-title']) {
      const section = root.querySelector(`section[aria-labelledby="${id}"]`)!;
      expect(section.querySelector('details')!.open).toBe(false);
      expect(section.querySelector('app-count')!.textContent!.trim()).toBe('0');
    }
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
    // The removed activity may have moved into the archive.
    (await vi.waitFor(() => http.expectOne('/api/instances/archive?page=1'))).flush(page([]));
    flushCounts(1, 0, 1);
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
    // The confirmed deletion leaves its list without refetching it; only the counts reread.
    (await vi.waitFor(() => http.expectOne('/api/library/counts'))).flush({
      drafts: 0,
      assigned: 1,
      unassigned: 0,
      completed: 0,
      archived: 0,
    });
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    expect(root.querySelector('#drafts-title')!.textContent).toContain('0');
    http.verify();
  });

  it('pages a long list, counts only a whole list and steps back when a deletion empties a later page', async () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    fixture.detectChanges();
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft], true));
    flushArchive();
    flushCounts(26, 1, 0);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const drafts = root.querySelector('section[aria-labelledby="drafts-title"]')!;
    // Counts are exact whatever the page holds.
    expect(drafts.querySelector('h2')!.textContent).toContain('26');
    expect(root.querySelector('#ready-title')!.textContent).toContain('1');
    // Each view says its size, empty views included.
    const sizes = [...root.querySelectorAll('fieldset app-count')].map(
      (count) => count.textContent,
    );
    expect(sizes).toEqual(['1', '0', '0']);

    const pager = drafts.querySelector('nav[aria-label="עמודי טיוטות"]')!;
    [...pager.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('העמוד הבא'))!
      .click();
    TestBed.tick();
    // The shown page stays until the next arrives, so the list never collapses under the pager,
    // and it says it is busy meanwhile.
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    const list = drafts.querySelector('.list')!;
    expect(list.getAttribute('aria-busy')).toBe('true');
    const older = { ...draft, id: 'older', name: 'ישנה' };
    http.expectOne('/api/activity-drafts?page=2').flush(page([older], false, 2));
    await fixture.whenStable();
    expect(list.getAttribute('aria-busy')).toBe('false');
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
    flushCounts(25, 1, 0);
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    expect(drafts.querySelector('h2')!.textContent).toContain('25');
    http.verify();
  });

  it('reloads its lists after a change note, waiting for running reads and keeping them shown', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const [stream] = FakeEventSource.opened;
    expect(stream.url).toBe('/api/library/changes?ngsw-bypass');
    stream.send();
    TestBed.tick();
    const running = [
      '/api/instances?page=1&status=assigned',
      '/api/activity-drafts?page=1',
      '/api/instances/archive?page=1',
      '/api/library/counts',
    ].map((path) => http.expectOne(path));
    // This change may postdate the running reads, so the lists reload again once they settle.
    stream.send();
    TestBed.tick();
    http.expectNone('/api/activity-drafts?page=1');
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    running[0].flush(page([snapshot]));
    running[1].flush(page([draft]));
    running[2].flush(page([]));
    running[3].flush({ drafts: 1, assigned: 1, unassigned: 0, completed: 0, archived: 0 });
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne('/api/activity-drafts?page=1').flush(page([{ ...draft, id: 'phone' }, draft]));
    });
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    flushArchive();
    flushCounts();
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
    http
      .expectOne('/api/instances?page=1&status=assigned')
      .flush(null, { status: 503, statusText: 'Unavailable' });
    flushArchive();
    flushCounts();
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    const ready = root.querySelector('[aria-labelledby="ready-title"]')!;
    expect(ready.textContent).not.toContain('עוד אין פעילויות מוכנות');
    ready.querySelector<HTMLButtonElement>('button')!.click();
    TestBed.tick();
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([]));
    http.expectNone('/api/activity-drafts?page=1');
    await fixture.whenStable();
    expect(ready.textContent).toContain('אין כרגע פעילויות שמחכות לילדים או לבדיקה');
    http.verify();
  });

  it.each([
    { kind: 'activities', button: 0, done: 'כל הפעילויות נמחקו', draftKept: false },
    { kind: 'children', button: 1, done: 'נתוני הילדים נמחקו', draftKept: true },
  ])(
    'rereads the lists $kind deletion changes, clearing a list that had failed',
    async ({ kind, button, done, draftKept }) => {
      const fixture = await render();
      const root = fixture.nativeElement as HTMLElement;
      FakeEventSource.opened[0].send();
      TestBed.tick();
      http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
      http
        .expectOne('/api/instances?page=1&status=assigned')
        .flush(null, { status: 503, statusText: 'Unavailable' });
      flushArchive();
      flushCounts();
      await fixture.whenStable();
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      root.querySelectorAll<HTMLButtonElement>('details .button-danger')[button].click();
      http
        .expectOne(
          (request) => request.method === 'DELETE' && request.url === `/api/learning-data/${kind}`,
        )
        .flush(null);
      await vi.waitFor(() => {
        TestBed.tick();
        if (!draftKept) http.expectOne('/api/activity-drafts?page=1').flush(page([]));
        http.expectOne('/api/instances?page=1&status=assigned').flush(page([]));
        flushArchive();
        flushCounts();
      });
      await fixture.whenStable();
      expect(root.textContent).toContain(done);
      expect(!!root.querySelector('a[href="/activities/draft"]')).toBe(draftKept);
      expect(root.querySelector('a[href="/instances/ready"]')).toBeNull();
      expect(root.querySelector('[role="alert"]')).toBeNull();
      http.verify();
    },
  );

  it('cancels a pre-deletion list read and reconciles a subsequent server hint', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    FakeEventSource.opened[0].send();
    TestBed.tick();
    const stale = http.expectOne('/api/activity-drafts?page=1');
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    flushArchive();
    flushCounts();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-delete-draft]')!.click();
    http.expectOne((request) => request.method === 'DELETE').flush(null);
    (await vi.waitFor(() => http.expectOne('/api/library/counts'))).flush({
      drafts: 0,
      assigned: 1,
      unassigned: 0,
      completed: 0,
      archived: 0,
    });
    await fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    FakeEventSource.opened[0].send();
    TestBed.tick();
    http.expectOne('/api/activity-drafts?page=1').flush(page([]));
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    flushArchive();
    flushCounts();
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    http.verify();
  });

  it('keeps a section open when its view switches to an empty one', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const ready = root.querySelector('section[aria-labelledby="ready-title"]')!;
    ready.querySelector<HTMLInputElement>('input[value="completed"]')!.click();
    TestBed.tick();
    http.expectOne('/api/instances?page=1&status=completed').flush(page([]));
    await fixture.whenStable();
    expect(ready.querySelector('details')!.open).toBe(true);
    // The count is every ready activity, whichever view is shown.
    expect(ready.querySelector('app-count')!.textContent!.trim()).toBe('1');
    // Leaving an empty view, the list says it is loading until the next view answers, never
    // that the next view is empty.
    ready.querySelector<HTMLInputElement>('input[value="unassigned"]')!.click();
    TestBed.tick();
    expect(ready.querySelector('.empty-state')!.textContent).toContain('טוענים');
    http.expectOne('/api/instances?page=1&status=unassigned').flush(page([]));
    await fixture.whenStable();
    expect(ready.querySelector('.empty-state')!.textContent).toContain('שעוד לא הוקצו');
    http.verify();
  });

  it('opens ready activities on open work and shows completed ones with banded scores', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const ready = root.querySelector('section[aria-labelledby="ready-title"]')!;
    expect(ready.querySelector('details')!.open).toBe(true);
    // An assigned row names its latest assignment date and each child.
    expect(ready.textContent).toContain('הוקצתה לאחרונה ב־');
    expect(ready.querySelector('app-child-badge')!.textContent).toContain('נועה');
    expect(ready.querySelector('app-difficulty-badge')!.textContent).toContain('רמת קושי: קשה');
    // Only ready activities open with the page; drafts start folded.
    expect(
      root.querySelector<HTMLDetailsElement>('section[aria-labelledby="drafts-title"] details')!
        .open,
    ).toBe(false);
    ready.querySelector<HTMLInputElement>('input[value="completed"]')!.click();
    TestBed.tick();
    // A new view keeps the shown rows, busy, until its own arrive, so the page never shrinks.
    expect(ready.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    expect(ready.querySelector('.list')!.getAttribute('aria-busy')).toBe('true');
    expect(ready.querySelector('.empty-state')).toBeNull();
    const children = [
      { childName: 'נועה', status: 'completed', finalTotal: 9, possibleTotal: 10 },
      { childName: 'איתי', status: 'completed', finalTotal: 6, possibleTotal: 10 },
      { childName: 'רפאל', status: 'completed', finalTotal: 2, possibleTotal: 10 },
    ];
    const completedAtUtc = '2026-10-13T08:00:00Z';
    http
      .expectOne('/api/instances?page=1&status=completed')
      .flush(page([{ ...snapshot, assignedAtUtc: null, completedAtUtc, children }]));
    await fixture.whenStable();
    // A completed row is dated by its completion, not its approval.
    expect(ready.textContent).toContain('הושלמה ב־');
    const scores = [...ready.querySelectorAll('.badge[data-band]')].map((badge) => [
      badge.getAttribute('data-band'),
      badge.textContent!.replace(/\s+/g, ' ').trim(),
    ]);
    expect(scores).toEqual([
      ['high', 'ציון 90'],
      ['mid', 'ציון 60'],
      ['low', 'ציון 20'],
    ]);
    // A grade is its own badge beside the child's name, a plain number out of 100.
    const names = [...ready.querySelectorAll('app-child-badge > .badge:not([data-band])')];
    expect(names.map((name) => name.textContent)).toEqual(['נועה', 'איתי', 'רפאל']);
    expect(ready.textContent).not.toContain('%');
    http.verify();
  });

  it('counts the folded archive and deletes from it only after naming the lost work', async () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    fixture.detectChanges();
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    const archived = { ...snapshot, id: 'old', title: 'ישנה', archivedAtUtc: draft.updatedAtUtc };
    flushArchive([archived]);
    flushCounts(1, 1, 1);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const archive = root.querySelector<HTMLDetailsElement>(
      'section[aria-labelledby="archive-title"] details',
    )!;
    expect(archive.open).toBe(false);
    expect(root.querySelector('#archive-title')!.textContent).toContain('1');
    archive.open = true;
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const remove = archive.querySelector<HTMLButtonElement>('article button')!;
    expect(remove.getAttribute('aria-label')).toBe('מחיקה לצמיתות: ישנה');
    remove.click();
    expect(confirm.mock.calls[0][0]).toContain('הציונים');
    http.expectNone((r) => r.method === 'DELETE');
    confirm.mockReturnValue(true);
    remove.click();
    http
      .expectOne((r) => r.url === '/api/instances/archive/old' && r.method === 'DELETE')
      .flush(null);
    (await vi.waitFor(() => http.expectOne('/api/library/counts'))).flush({
      drafts: 1,
      assigned: 1,
      unassigned: 0,
      completed: 0,
      archived: 0,
    });
    await fixture.whenStable();
    expect(root.textContent).toContain('נמחקה לצמיתות');
    expect(root.querySelector('#archive-title')!.textContent).toContain('0');
    expect(archive.querySelector('a[href="/instances/old"]')).toBeNull();
    // Removing a ready activity may move it into the open archive, so the archive rereads.
    root
      .querySelector<HTMLButtonElement>('section[aria-labelledby="ready-title"] article button')!
      .click();
    http.expectOne((r) => r.url === '/api/instances/ready' && r.method === 'DELETE').flush(null);
    (await vi.waitFor(() => http.expectOne('/api/instances/archive?page=1'))).flush(
      page([{ ...snapshot, archivedAtUtc: draft.updatedAtUtc }]),
    );
    flushCounts(1, 0, 1);
    await fixture.whenStable();
    expect(archive.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    http.verify();
  });

  it('shows a closed stream and retries both observation and list reads on request', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;
    const [stream] = FakeEventSource.opened;
    stream.readyState = FakeEventSource.CLOSED;
    stream.onerror?.();
    await fixture.whenStable();
    const retry = root.querySelector<HTMLButtonElement>('app-live-status button');
    expect(retry).not.toBeNull();
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    retry!.click();
    TestBed.tick();
    expect(FakeEventSource.opened).toHaveLength(2);
    http.expectOne('/api/instances?page=1&status=assigned').flush(page([snapshot]));
    http.expectOne('/api/activity-drafts?page=1').flush(page([draft]));
    flushArchive();
    flushCounts();
    await fixture.whenStable();
    http.verify();
  });
});
