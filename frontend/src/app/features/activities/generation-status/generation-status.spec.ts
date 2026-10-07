import { TestBed } from '@angular/core/testing';
import { GenerationStatus } from './generation-status';
import { GenerationKind, GenerationOperation } from '../../../core/api/models';

const unknownOperation: GenerationOperation = {
  id: 'op',
  draftId: 'draft',
  kind: 'GenerateMaterials',
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
    targetId: null,
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
  fixture.componentRef.setInput('configured', true);
  const retried: GenerationKind[] = [];
  let checked = 0;
  fixture.componentInstance.retried.subscribe((kind) => retried.push(kind));
  fixture.componentInstance.checked.subscribe(() => checked++);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  // Text a parent sees without opening the technical disclosure.
  const visible = () =>
    Array.from(root.querySelectorAll('[role="status"], button'))
      .map((element) => element.textContent)
      .join(' ');
  return { root, visible, retried, checked: () => checked };
}
describe('GenerationStatus', () => {
  it('explains an unknown outcome without a retry and keeps technical evidence behind a disclosure', async () => {
    const { root, visible, retried, checked } = await render(unknownOperation);
    expect(visible()).toContain('לא ידוע אם שירות ה־AI סיים את הבקשה.');
    expect(visible()).toContain('לא הפעלנו ניסיון נוסף אוטומטית כדי למנוע חיוב כפול.');
    expect(root.querySelector('#retry-generation')).toBeNull();
    root.querySelector<HTMLButtonElement>('#check-saved')!.click();
    expect(checked()).toBe(1);
    expect(retried).toEqual([]);
    expect(visible()).not.toContain('0.25');
    const technical = root.querySelector('details')!;
    expect(technical.querySelector('summary')!.textContent).toContain('פרטים טכניים');
    expect(technical.textContent).toContain('רעיונות לטקסט · תוכן התקבל');
    expect(technical.textContent).toContain('0.25');
    expect(root.querySelector('script')).toBeNull();
    expect(technical.textContent).toContain('<script>');
    expect(root.querySelector('[data-edit-candidate]')).toBeNull();
  });

  it('offers only an explicit retry of the failed part', async () => {
    const { visible, root, retried } = await render({
      ...unknownOperation,
      kind: 'GenerateQuestions',
      status: 'failed',
      stage: 'questions',
      failure: 'invalid-output',
      steps: [{ stage: 'questions', outcome: 'failed', usage: null, metadata: null }],
    });
    expect(visible()).toContain('יצירת השאלות נכשלה.');
    expect(visible()).toContain('התוצאה לא החליפה את התוכן הקיים.');
    expect(retried).toEqual([]);
    root.querySelector<HTMLButtonElement>('#retry-generation')!.click();
    expect(retried).toEqual(['GenerateQuestions']);
  });

  it('keeps the written text after a failed polish and offers no retry', async () => {
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
        targetId: null,
        steps: [
          {
            stage: 'material-polish',
            candidate: null,
            diagnostics: { 'length.m': ['אורך הטקסט אינו עומד בדרישה המדויקת או בטווח.'] },
          },
        ],
      },
    });
    expect(visible()).toContain('הטקסט נשמר, אבל שיפור הניסוח לא הושלם.');
    expect(visible()).not.toContain('לא עמד בדרישת האורך');
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
        targetId: null,
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
    expect(visible()).toContain('הטקסט שנוצר לא עמד בדרישת האורך.');
    expect(visible()).toContain('נדרש: 100–150 מילים');
    expect(visible()).toContain('התוצאה לא החליפה את התוכן הקיים.');
    expect(visible()).not.toContain('הטקסט נוצר');
  });

  it('shows plain progress while running, without stages, percentages or cost', async () => {
    const { visible, root } = await render({
      ...unknownOperation,
      status: 'calling',
      stage: 'materials',
    });
    expect(visible()).toContain('יוצרים את הטקסט…');
    expect(visible()).toContain('כותבים את הטקסט');
    expect(visible()).not.toMatch(/%|עלות/);
    expect(root.querySelector('#cancel-generation')).not.toBeNull();
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
