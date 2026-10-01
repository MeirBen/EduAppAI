import { TestBed } from '@angular/core/testing';
import { GenerationStatus } from './generation-status';
import { GenerationOperation } from '../../../core/api/models';

export const unknownOperation: GenerationOperation = {
  id: 'op',
  draftId: 'draft',
  kind: 'GenerateActivity',
  status: 'unknown',
  stage: 'questions',
  originalRevision: 1,
  expectedRevision: 2,
  failure: 'interrupted',
  diagnosticsExpired: false,
  steps: [
    { stage: 'materials', outcome: 'accepted', usage: null, metadata: null },
    { stage: 'questions', outcome: 'unknown', usage: null, metadata: null },
  ],
  artifacts: {
    targetId: null,
    steps: [
      {
        stage: 'questions',
        candidate: null,
        diagnostics: null,
        call: { output: '<script>alert(1)</script>' },
      },
    ],
  },
};
describe('GenerationStatus', () => {
  it('separates accepted stages from unknown work and warns that another attempt may cost money', async () => {
    const fixture = TestBed.createComponent(GenerationStatus);
    fixture.componentRef.setInput('operation', unknownOperation);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('תוצאה לא ידועה');
    expect(root.textContent).toContain('תוכן התקבל');
    expect(root.textContent).toContain('חיוב נוסף');
    expect(root.querySelector('script')).toBeNull();
    expect(root.textContent).toContain('<script>');
    expect(root.querySelector('[data-edit-candidate]')).toBeNull();
  });
  it('reports expired diagnostics without inventing missing usage or exposing a copy action', async () => {
    const fixture = TestBed.createComponent(GenerationStatus);
    fixture.componentRef.setInput('operation', {
      ...unknownOperation,
      status: 'failed',
      diagnosticsExpired: true,
      artifacts: null,
    });
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('פג תוקף');
    expect(root.textContent).toContain('לא ידועה');
    expect(root.querySelector('[data-edit-candidate]')).toBeNull();
  });
});
