import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SnapshotPreviewPage } from './snapshot-preview';
import { numericPlan } from '../../activities/learning-plan.fixture';
import { EditableQuestion } from '../../../core/api/models';

async function preview(
  questions: Pick<EditableQuestion, 'prompt' | 'interaction' | 'answer'>[],
  archivedAtUtc: string | null = null,
) {
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
    archivedAtUtc,
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
  if (!archivedAtUtc)
    (await vi.waitFor(() => http.expectOne('/api/children?page=1'))).flush({
      items: [{ id: 'child', name: 'נועה', enabled: true }],
      page: 1,
      pageSize: 25,
      hasMore: false,
    });
  await fixture.whenStable();
  return { fixture, root: fixture.nativeElement as HTMLElement, http };
}

describe('Immutable parent preview', () => {
  it('assigns the frozen snapshot once and links to an existing assignment on replay', async () => {
    const { root, http, fixture } = await preview([]);
    const selector = root.querySelector('select')!;
    selector.value = 'child';
    selector.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    const button = root.querySelector<HTMLButtonElement>('#assign-snapshot')!;
    button.click();
    const request = http.expectOne('/api/assignments');
    expect(request.request.body).toEqual({ childId: 'child', snapshotId: 'ready' });
    button.click();
    http.expectNone('/api/assignments');
    request.flush({ id: 'existing', status: 'completed', childName: 'נועה' });
    await fixture.whenStable();
    expect(root.textContent).toContain('כבר הוקצתה');
    expect(root.querySelector('a[href="/assignments/existing"]')).not.toBeNull();
    http.verify();
  });

  it('finds a withdrawn pair on assignment and restores it only on an explicit, current request', async () => {
    const { root, http, fixture } = await preview([]);
    const selector = root.querySelector('select')!;
    selector.value = 'child';
    selector.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    root.querySelector<HTMLButtonElement>('#assign-snapshot')!.click();
    http
      .expectOne('/api/assignments')
      .flush({ id: 'existing', status: 'withdrawn', revision: 2, childName: 'נועה' });
    await fixture.whenStable();
    expect(root.textContent).toContain('בוטלה קודם');
    expect(root.textContent).not.toContain('כבר הוקצתה');
    root.querySelector<HTMLButtonElement>('#restore-assignment')!.click();
    const restore = http.expectOne('/api/assignments/existing/restore');
    expect(restore.request.body).toEqual({ expectedRevision: 2 });
    restore.flush({ id: 'existing', status: 'assigned', revision: 3, childName: 'נועה' });
    await fixture.whenStable();
    expect(root.textContent).toContain('ההקצאה הוחזרה');
    expect(root.querySelector('a[href="/assignments/existing"]')).not.toBeNull();
    http.verify();
  });

  it('explains archived content while retaining the parent preview and copy action', async () => {
    const { root, http } = await preview([], '2026-10-06T00:00:00Z');
    expect(root.textContent).toContain('הפעילות בארכיון');
    expect(root.querySelector('#assign-snapshot')).toBeNull();
    expect(root.querySelector('#copy-snapshot')).not.toBeNull();
    http.verify();
  });

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
