import { TestBed } from '@angular/core/testing';
import { ActivityReview } from './activity-review';
import { measurementItems } from '../activity-document-view/measurements';
import { readingPlan } from '../learning-plan.fixture';

const scope = readingPlan.materials[0].id;
async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(ActivityReview);
  fixture.componentRef.setInput('issues', []);
  fixture.componentRef.setInput('measurements', []);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}
describe('ActivityReview', () => {
  it('reports a clean saved revision as ready for the parent, never as automatically correct', async () => {
    const root = await render({ saved: true });
    expect(root.querySelector('[role="status"]')!.textContent).toContain(
      'הפעילות מוכנה לבדיקה שלכם.',
    );
    expect(root.textContent).toContain('בדקו את השפה, העובדות והתשובות');
  });

  it('presents an advisory target as information and a strict mismatch as a blocker', async () => {
    const root = await render({
      saved: true,
      measurements: measurementItems(
        [
          {
            scope,
            expected: { mode: 'target', value: 300, lower: null, upper: null },
            actual: 284,
            satisfied: null,
          },
          {
            scope: 'total',
            expected: { mode: 'range', value: null, lower: 100, upper: 150 },
            actual: 82,
            satisfied: false,
          },
        ],
        readingPlan,
      ),
    });
    const [advisory, strict] = Array.from(root.querySelectorAll('li'));
    expect(advisory.textContent).toContain('284 מילים');
    expect(advisory.textContent).toContain('מבוקש: בערך 300 מילים');
    expect(advisory.textContent).not.toContain('לפני סימון כמוכנה');
    expect(strict.textContent).toContain('82 מילים');
    expect(strict.textContent).toContain('נדרש: 100–150 מילים');
    expect(strict.textContent).toContain('לפני סימון כמוכנה');
  });

  it('lists blockers and notes that unsaved edits are not yet checked', async () => {
    const root = await render({
      saved: true,
      outdated: true,
      issues: ['בשאלה 2 חסרה תשובה נכונה.'],
    });
    expect(root.querySelector('[role="status"]')!.textContent).toContain('לפני סימון כמוכנה');
    expect(root.textContent).toContain('בשאלה 2 חסרה תשובה נכונה.');
    expect(root.textContent).toContain('מתעדכנת אחרי שמירה');
  });
});
