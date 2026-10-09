import { TestBed } from '@angular/core/testing';
import { GenerationStatus } from './generation-status';
import { GenerationOperation } from '../../../core/api/models';

const unknownOperation: GenerationOperation = {
  id: 'op',
  draftId: 'draft',
  kind: 'Create',
  status: 'unknown',
  stage: 'materials',
  originalRevision: 1,
  expectedRevision: 2,
  failure: 'interrupted',
  diagnosticsExpired: false,
  steps: [
    { stage: 'material-ideas', outcome: 'accepted', usage: { costCredits: 0.25 }, metadata: null },
    { stage: 'materials', outcome: 'unknown', usage: null, metadata: null },
  ],
  artifacts: {
    steps: [
      {
        stage: 'materials',
        candidate: null,
        diagnostics: null,
        call: { output: '<script>alert(1)</script>' },
      },
    ],
  },
};
async function render(operation: GenerationOperation) {
  const fixture = TestBed.createComponent(GenerationStatus);
  fixture.componentRef.setInput('operation', operation);
  let checked = 0;
  fixture.componentInstance.checked.subscribe(() => checked++);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  // Text a parent sees without opening the technical disclosure.
  const visible = () =>
    Array.from(root.querySelectorAll('[role="status"], button'))
      .map((element) => element.textContent)
      .join(' ');
  return { root, visible, checked: () => checked };
}
describe('GenerationStatus', () => {
  it('explains an unknown outcome without a retry and keeps technical evidence behind a disclosure', async () => {
    const { root, visible, checked } = await render(unknownOperation);
    expect(visible()).toContain('לא ידוע אם שירות ה־AI סיים את הבקשה.');
    expect(visible()).toContain('לא הפעלנו ניסיון נוסף אוטומטית כדי למנוע חיוב כפול.');
    expect(root.querySelector('#retry-generation')).toBeNull();
    root.querySelector<HTMLButtonElement>('#check-saved')!.click();
    expect(checked()).toBe(1);
    expect(visible()).not.toContain('0.25');
    const technical = root.querySelector('details')!;
    expect(technical.querySelector('summary')!.textContent).toContain('פרטים טכניים');
    expect(technical.textContent).toContain('רעיונות לטקסט · תוכן התקבל');
    expect(technical.textContent).toContain('0.25');
    expect(root.querySelector('script')).toBeNull();
    expect(technical.textContent).toContain('<script>');
    expect(root.querySelector('[data-edit-candidate]')).toBeNull();
  });

  it('announces completion quietly and retains its technical disclosure', async () => {
    const { root } = await render({ ...unknownOperation, status: 'completed' });
    expect(root.querySelector('.panel')).toBeNull();
    expect(root.querySelector('[role="status"]')?.classList.contains('sr-only')).toBe(true);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('הבקשה הושלמה.');
    expect(root.querySelector('details')?.textContent).toContain('0.25');
  });

  it('reports failure without a second retry control', async () => {
    const { visible, root } = await render({
      ...unknownOperation,
      kind: 'GenerateQuestions',
      status: 'failed',
      stage: 'questions',
      failure: 'invalid-output',
      steps: [{ stage: 'questions', outcome: 'failed', usage: null, metadata: null }],
    });
    expect(visible()).toContain('לא הצלחנו להשלים את הבקשה.');
    expect(visible()).toContain('התוכן השמור לא השתנה.');
    expect(root.querySelector('#retry-generation')).toBeNull();
  });

  it('does not claim that intermediate text was saved after a failed polish', async () => {
    const { visible, root } = await render({
      ...unknownOperation,
      status: 'failed',
      stage: 'material-polish',
      steps: [
        { stage: 'material-ideas', outcome: 'accepted', usage: null, metadata: null },
        { stage: 'materials', outcome: 'accepted', usage: null, metadata: null },
        { stage: 'material-polish', outcome: 'failed', usage: null, metadata: null },
      ],
      artifacts: {
        steps: [
          {
            stage: 'material-polish',
            candidate: null,
            diagnostics: { 'length.m': ['אורך הטקסט אינו עומד בדרישה המדויקת או בטווח.'] },
          },
        ],
      },
    });
    expect(visible()).toContain('התוכן השמור לא השתנה.');
    expect(visible()).not.toContain('הטקסט נשמר');
    expect(root.querySelector('#retry-generation')).toBeNull();
    expect(root.querySelector('details')!.textContent).toContain('טקסט בניסוח משופר · נכשל');
  });

  it('states the strict requirement after a length rejection without implying success', async () => {
    const { visible } = await render({
      ...unknownOperation,
      status: 'failed',
      stage: 'materials',
      steps: [{ stage: 'materials', outcome: 'failed', usage: null, metadata: null }],
      artifacts: {
        input: {
          materials: [
            {
              id: 'm',
              label: 'קטע',
              length: { mode: 'range', value: null, lower: 100, upper: 150 },
            },
          ],
          totalLength: null,
        },
        steps: [
          {
            stage: 'materials',
            candidate: null,
            diagnostics: { 'length.m': ['אורך הטקסט אינו עומד בדרישה המדויקת או בטווח.'] },
          },
        ],
      },
    });
    expect(visible()).toContain('הטקסט שנוצר לא התאים לאורך המבוקש.');
    expect(visible()).toContain('האורך המבוקש: 100–150 מילים');
    expect(visible()).toContain('התוכן השמור לא השתנה.');
    expect(visible()).not.toContain('הטקסט נוצר');
  });

  it('shows plain progress while running, without stages, percentages or cost', async () => {
    const { visible, root } = await render({
      ...unknownOperation,
      status: 'calling',
      stage: 'materials',
    });
    expect(visible()).toContain('מכינים את הפעילות…');
    expect(visible()).toContain('כותבים את הטקסט');
    expect(visible()).not.toMatch(/%|עלות/);
    expect(root.querySelector('#cancel-generation')).toBeNull();
    expect(root.querySelector('#retry-generation')).toBeNull();
  });

  it('reports expired diagnostics without inventing missing usage or exposing a copy action', async () => {
    const { root } = await render({
      ...unknownOperation,
      status: 'failed',
      diagnosticsExpired: true,
      artifacts: null,
    });
    expect(root.textContent).toContain('הפרטים הטכניים כבר לא זמינים');
    expect(root.textContent).toContain('לא ידועה');
    expect(root.querySelector('[data-edit-candidate]')).toBeNull();
  });
});
