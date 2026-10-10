import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FakeEventSource } from '../../core/api/event-source.fixture';
import { ChildInbox } from './child-inbox';

describe('Child inbox', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  it('follows assignment changes, coalesces in-flight hints and closes with the page', async () => {
    const fixture = TestBed.createComponent(ChildInbox);
    const http = TestBed.inject(HttpTestingController);
    const path = '/api/child/assignments?state=available&page=1';
    const empty = { items: [], page: 1, pageSize: 25, hasMore: false };
    fixture.detectChanges();
    const initial = http.expectOne(path);
    const [stream] = FakeEventSource.opened;
    expect(stream?.url).toBe('/api/child/changes?ngsw-bypass');
    for (let i = 0; i < 10; i++) stream.send();
    TestBed.tick();
    http.expectNone(path);
    initial.flush(empty);
    await vi.waitFor(() => {
      TestBed.tick();
      http.expectOne(path).flush({
        ...empty,
        items: [{ id: 'new', title: 'פעילות חדשה', status: 'assigned' }],
      });
    });
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a[href="/child/assignments/new"]')).not.toBeNull();
    stream.send();
    TestBed.tick();
    http.expectOne(path).flush(empty);
    await fixture.whenStable();
    expect(root.querySelector('a[href="/child/assignments/new"]')).toBeNull();
    fixture.destroy();
    expect(stream.readyState).toBe(FakeEventSource.CLOSED);
  });

  it('uses the refresh button to recover a refused stream and reread the inbox', async () => {
    const fixture = TestBed.createComponent(ChildInbox);
    const http = TestBed.inject(HttpTestingController);
    const path = '/api/child/assignments?state=available&page=1';
    const empty = { items: [], page: 1, pageSize: 25, hasMore: false };
    fixture.detectChanges();
    http.expectOne(path).flush(empty);
    await fixture.whenStable();
    const [stream] = FakeEventSource.opened;
    expect(stream).toBeDefined();
    stream.readyState = FakeEventSource.CLOSED;
    stream.onerror?.();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="status"]')?.textContent).toContain('רענון הפעילויות');
    root.querySelector<HTMLButtonElement>('header button')!.click();
    TestBed.tick();
    expect(FakeEventSource.opened).toHaveLength(2);
    http.expectOne(path).flush(empty);
    await fixture.whenStable();
  });

  it('pages available work and resets paging when selecting submitted work without starting any session', async () => {
    const fixture = TestBed.createComponent(ChildInbox),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/child/assignments?state=available&page=1').flush({
      items: [
        {
          id: 'work',
          title: 'פעילות',
          status: 'assigned',
          revision: 1,
          createdAtUtc: '2026-10-01T00:00:00Z',
          hasStarted: true,
        },
      ],
      page: 1,
      pageSize: 25,
      hasMore: true,
    });
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a[href="/child/assignments/work"]')).not.toBeNull();
    const buttons = root.querySelectorAll<HTMLButtonElement>('nav button');
    buttons[1].click();
    (await vi.waitFor(() => http.expectOne('/api/child/assignments?state=available&page=2'))).flush(
      { items: [], page: 2, pageSize: 25, hasMore: false },
    );
    await fixture.whenStable();
    root.querySelector<HTMLInputElement>('input[value="submitted"]')!.click();
    (await vi.waitFor(() => http.expectOne('/api/child/assignments?state=submitted&page=1'))).flush(
      { items: [], page: 1, pageSize: 25, hasMore: false },
    );
    await fixture.whenStable();
    // The first page of the new view needs no paging controls.
    expect(root.querySelector('nav')).toBeNull();
    http.expectNone((r) => r.method !== 'GET' || r.url === '/api/limits');
  });
});
