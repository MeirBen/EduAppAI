import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SnapshotPreviewPage } from './snapshot-preview';
import { numericPlan } from '../../activities/learning-plan.fixture';
import { EditableQuestion } from '../../../core/api/models';

async function preview(questions: Pick<EditableQuestion, 'prompt' | 'interaction' | 'answer'>[]) {
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
      questions: questions.map((question, index) => ({
        id: `q${index}`,
        points: 1,
        origin: { kind: 'manual' },
        acceptance: null,
        ...question,
      })),
    },
  });
  await fixture.whenStable();
  return { root: fixture.nativeElement as HTMLElement, http };
}

describe('Immutable parent preview', () => {
  it('renders answer text without HTML and copies only after explicit new-draft action', async () => {
    const { root, http } = await preview([
      {
        prompt: 'שאלה',
        interaction: { type: 'text-input', options: null },
        answer: { value: '<img src=x onerror=alert(1)>' },
      },
    ]);
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

  it('isolates each question item so a calculation keeps its own order inside the RTL page', async () => {
    const { root } = await preview([
      {
        prompt: '58 - 23 = ?',
        interaction: { type: 'single-choice', options: ['35', '-35'] },
        answer: { value: '35' },
      },
    ]);
    const isolated = Array.from(root.querySelectorAll('ol bdi'), (item) =>
      item.textContent?.trim(),
    );
    expect(isolated).toEqual(['58 - 23 = ?', '35', '-35', '35']);
  });
});
