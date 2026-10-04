import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ActivityDetail, GenerationOperation } from '../../../core/api/models';
import { numericPlan } from '../learning-plan.fixture';
import { DraftObservation, observeDraft } from './draft-observer';

const draft: ActivityDetail = {
  id: 'draft',
  revision: 1,
  plan: numericPlan,
  input: { settings: numericPlan.defaults },
  document: { title: 'תרגול', instructions: null, materials: [], questions: [] },
  diagnostics: {},
  measurements: [],
  activeOperationId: null,
  templateVersionId: null,
  releasedSnapshotId: null,
  releasedSourceRevision: null,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: '2026-10-01T00:00:00Z',
};
const operation: GenerationOperation = {
  id: 'op',
  draftId: 'draft',
  kind: 'GenerateActivity',
  status: 'calling',
  stage: 'materials',
  originalRevision: 1,
  expectedRevision: 1,
  failure: null,
  diagnosticsExpired: false,
  steps: [],
  artifacts: null,
};

describe('Draft observation', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    http.verify();
  });
  function open(op?: string) {
    const id = signal<string | undefined>('draft');
    const operationId = signal(op);
    const busy = signal(false),
      enabled = signal(true);
    const changes = new Subject<void>();
    const seen: DraftObservation[] = [],
      errors: unknown[] = [];
    const observer = TestBed.runInInjectionContext(() =>
      observeDraft({
        draftId: id,
        operationId,
        enabled,
        busy,
        changes,
        changed: (value) => seen.push(value),
        failed: (error) => errors.push(error),
      }),
    );
    TestBed.tick();
    return { ...observer, id, operationId, busy, enabled, changes, seen, errors };
  }
  function advance(milliseconds = 2000) {
    vi.advanceTimersByTime(milliseconds);
    TestBed.tick();
  }
  function show(state: DocumentVisibilityState) {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state);
    document.dispatchEvent(new Event('visibilitychange'));
    TestBed.tick();
  }

  it('coalesces a burst during a read into one trailing read', () => {
    const observer = open();
    observer.changes.next();
    TestBed.tick();
    const first = http.expectOne('/api/activity-drafts/draft');
    for (let i = 0; i < 20; i++) observer.changes.next();
    TestBed.tick();
    http.expectNone('/api/activity-drafts/draft');
    first.flush(draft);
    TestBed.tick();
    http.expectOne('/api/activity-drafts/draft').flush({ ...draft, revision: 2 });
    TestBed.tick();
    expect(observer.seen.map((value) => value.draft.revision)).toEqual([1, 2]);
  });

  it('cancels background reads for a command without cancelling its explicit acknowledgement read', async () => {
    const observer = open('op');
    advance();
    const stale = http.expectOne('/api/activity-drafts/draft/operations/op');
    observer.suspend();
    observer.busy.set(true);
    const result = observer.read({ ...operation, status: 'cancelled' });
    const fresh = http.expectOne('/api/activity-drafts/draft');
    TestBed.tick();
    expect(stale.cancelled).toBe(true);
    expect(fresh.cancelled).toBe(false);
    show('hidden');
    expect(fresh.cancelled).toBe(false);
    fresh.flush({ ...draft, revision: 2 });
    expect((await result).operation?.status).toBe('cancelled');
    expect(observer.seen).toEqual([]);
    http.expectNone('/api/activity-drafts/draft/operations/op');
  });

  it('cancels hidden and obsolete background reads, then catches up on the current identity', () => {
    const observer = open();
    observer.changes.next();
    TestBed.tick();
    const hidden = http.expectOne('/api/activity-drafts/draft');
    show('hidden');
    expect(hidden.cancelled).toBe(true);
    observer.changes.next();
    advance(10_000);
    http.expectNone('/api/activity-drafts/draft');
    show('visible');
    const old = http.expectOne('/api/activity-drafts/draft');
    observer.id.set('next');
    TestBed.tick();
    expect(old.cancelled).toBe(true);
    observer.changes.next();
    TestBed.tick();
    http.expectOne('/api/activity-drafts/next').flush({ ...draft, id: 'next' });
    expect(observer.seen.map((value) => value.draft.id)).toEqual(['next']);
  });

  it('keeps polling valid stage transitions and reads late metadata after terminal state without a timer', () => {
    const observer = open('op');
    advance();
    http.expectOne('/api/activity-drafts/draft/operations/op').flush(operation);
    http.expectOne('/api/activity-drafts/draft').flush(draft);
    advance();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush({ ...operation, status: 'queued', stage: 'questions' });
    http.expectOne('/api/activity-drafts/draft').flush({ ...draft, revision: 2 });
    advance();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush({ ...operation, status: 'completed', stage: 'questions' });
    http.expectOne('/api/activity-drafts/draft').flush({ ...draft, revision: 3 });
    advance(10_000);
    http.expectNone('/api/activity-drafts/draft/operations/op');
    observer.changes.next();
    TestBed.tick();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush({ ...operation, status: 'completed', diagnosticsExpired: true });
    http.expectOne('/api/activity-drafts/draft').flush({ ...draft, revision: 3 });
    expect(observer.seen.map((value) => value.operation?.status)).toEqual([
      'calling',
      'queued',
      'completed',
      'completed',
    ]);
    expect(observer.seen.at(-1)?.operation?.diagnosticsExpired).toBe(true);
  });

  it('retries transient reads at the normal cadence and keeps hints available after a forbidden response', () => {
    const observer = open('op');
    advance();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush(null, { status: 503, statusText: 'Unavailable' });
    advance();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush(null, { status: 403, statusText: 'Forbidden' });
    advance(10_000);
    http.expectNone('/api/activity-drafts/draft/operations/op');
    observer.changes.next();
    TestBed.tick();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush({ ...operation, status: 'failed' });
    http.expectOne('/api/activity-drafts/draft').flush(draft);
    expect(observer.errors).toHaveLength(2);
    expect(observer.seen[0].operation?.status).toBe('failed');
  });

  it('checks the draft after an operation 404 instead of declaring the draft deleted', () => {
    const observer = open('op');
    advance();
    http
      .expectOne('/api/activity-drafts/draft/operations/op')
      .flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/activity-drafts/draft').flush(draft);
    expect(observer.errors).toEqual([]);
    expect(observer.seen[0].draft.id).toBe('draft');
    expect(observer.seen[0].operation).toBeUndefined();
    advance(10_000);
    http.expectNone('/api/activity-drafts/draft/operations/op');
  });

  it('disposes reads and timers with the workspace', () => {
    const observer = open('op');
    advance();
    const pending = http.expectOne('/api/activity-drafts/draft/operations/op');
    TestBed.resetTestingModule();
    expect(pending.cancelled).toBe(true);
    observer.changes.next();
    vi.advanceTimersByTime(10_000);
    http.expectNone('/api/activity-drafts/draft');
  });
});
