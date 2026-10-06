import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ChildInbox } from './child-inbox';

describe('Child inbox', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());
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
    const filter = root.querySelector('select')!;
    filter.value = 'submitted';
    filter.dispatchEvent(new Event('change'));
    (await vi.waitFor(() => http.expectOne('/api/child/assignments?state=submitted&page=1'))).flush(
      { items: [], page: 1, pageSize: 25, hasMore: false },
    );
    await fixture.whenStable();
    expect(root.textContent).toContain('עמוד 1');
    http.expectNone((r) => r.method !== 'GET' || r.url === '/api/limits');
  });
});
