import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SnapshotPreviewPage } from './snapshot-preview';
import { numericPlan } from '../../activities/learning-plan.fixture';
describe('Immutable parent preview', () => {
  it('renders answer text without HTML and copies only after explicit new-draft action', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(SnapshotPreviewPage),
      http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('instanceId', 'ready');
    fixture.detectChanges();
    http.expectOne('/api/instances/ready').flush({
      id: 'ready',
      sourceDraftId: 'draft',
      sourceDraftRevision: 3,
      plan: numericPlan,
      input: { settings: numericPlan.defaults },
      reviewedAtUtc: '2026-10-01T00:00:00Z',
      measurements: [],
      document: {
        title: 'מוכנה',
        instructions: null,
        materials: [],
        questions: [
          {
            id: 'q',
            prompt: 'שאלה',
            interaction: { type: 'text-input', options: null },
            answer: { value: '<img src=x onerror=alert(1)>' },
            points: 1,
            origin: { kind: 'manual' },
            acceptance: null,
          },
        ],
      },
    });
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('img')).toBeNull();
    expect(root.textContent).toContain('<img');
    expect(root.querySelector('textarea')).toBeNull();
    http.expectNone((r) => r.method === 'POST');
    root.querySelector<HTMLButtonElement>('#copy-snapshot')!.click();
    const copy = http.expectOne('/api/activity-drafts');
    expect(copy.request.body).toEqual({ snapshotId: 'ready' });
    copy.flush({ id: 'new' });
    await vi.waitFor(() => expect(root.querySelector('a[href="/activities/new"]')).not.toBeNull());
    http.verify();
  });
});
