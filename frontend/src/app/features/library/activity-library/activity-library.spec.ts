import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivityLibrary } from './activity-library';
import { provideLimits } from '../../../core/api/limits.fixture';
describe('Activity library', () => {
  it('separates editable drafts and ready snapshots, and deletes only the explicitly selected draft', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideLimits(),
      ],
    });
    const fixture = TestBed.createComponent(ActivityLibrary),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/templates').flush([]);
    http
      .expectOne('/api/instances')
      .flush([
        { id: 'ready', title: 'מוכנה', status: 'Ready', createdAtUtc: '2026-10-01T00:00:00Z' },
      ]);
    http.expectOne('/api/activity-drafts').flush([
      {
        id: 'draft',
        name: 'בעבודה',
        revision: 2,
        updatedAtUtc: '2026-10-01T00:00:00Z',
      },
    ]);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a[href="/activities/draft"]')).not.toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-delete-draft]')!.click();
    http
      .expectOne((r) => r.url === '/api/activity-drafts/draft' && r.method === 'DELETE')
      .flush(null);
    await vi.waitFor(() => expect(root.textContent).toContain('הפריט נמחק'));
    await fixture.whenStable();
    expect(root.querySelector('a[href="/activities/draft"]')).toBeNull();
    expect(root.querySelector('a[href="/instances/ready"]')).not.toBeNull();
    // The confirmed deletion leaves its list without refetching the library.
    http.verify();
    vi.restoreAllMocks();
  });
});
