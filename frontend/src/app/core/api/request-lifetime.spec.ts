import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivityWorkspace } from '../../features/activities/activity-workspace/activity-workspace';
import { ActivityLibrary } from '../../features/library/activity-library/activity-library';
import { SnapshotPreviewPage } from '../../features/instances/snapshot-preview/snapshot-preview';
import { provideLimits } from '../../core/api/limits.fixture';
import { FakeEventSource } from './event-source.fixture';

describe('Page HTTP lifetime', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideLimits(),
      ],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('cancels old activity and status reads when the workspace changes route or is destroyed', () => {
    const fixture = TestBed.createComponent(ActivityWorkspace);
    fixture.componentRef.setInput('activityId', 'first');
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne('/api/activity-drafts/first');
    const status = http.expectOne('/api/ai/status');
    fixture.componentRef.setInput('activityId', 'second');
    TestBed.tick();
    const second = http.expectOne('/api/activity-drafts/second');
    fixture.destroy();
    expect(first.cancelled).toBe(true);
    expect(second.cancelled).toBe(true);
    expect(status.cancelled).toBe(true);
  });

  it('cancels a frozen preview read when its page is destroyed', () => {
    const fixture = TestBed.createComponent(SnapshotPreviewPage);
    fixture.componentRef.setInput('instanceId', 'snapshot');
    TestBed.tick();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/instances/snapshot');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });

  it('cancels every library read and closes its change stream when leaving the page', () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const requests = ['/api/instances', '/api/activity-drafts'].map((path) => http.expectOne(path));
    fixture.destroy();
    for (const request of requests) expect(request.cancelled).toBe(true);
    expect(FakeEventSource.opened.map((source) => source.readyState)).toEqual([
      FakeEventSource.CLOSED,
    ]);
  });

  it('cancels pending deletion without updating the destroyed view', async () => {
    const fixture = TestBed.createComponent(ActivityLibrary);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/instances').flush([]);
    http.expectOne('/api/activity-drafts').flush([{ id: 'draft', name: 'Saved', revision: 1 }]);
    await fixture.whenStable();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    fixture.nativeElement.querySelector('[data-delete-draft]').click();
    const deletion = http.expectOne('/api/activity-drafts/draft');
    expect(deletion.request.method).toBe('DELETE');
    fixture.destroy();
    expect(deletion.cancelled).toBe(true);
    await Promise.resolve();
  });
});
