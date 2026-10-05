import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ScopedRepair } from './scoped-repair';
import { provideLimits } from '../../../core/api/limits.fixture';

@Component({
  imports: [ScopedRepair],
  template: `<div (input)="bubbled = bubbled + 1" (change)="bubbled = bubbled + 1">
    <app-scoped-repair
      key="q"
      name="שאלה 1"
      [suggestions]="['ניסוח פשוט יותר']"
      (requested)="requests.push($event)"
    />
  </div>`,
})
class Host {
  bubbled = 0;
  requests: string[] = [];
}
describe('ScopedRepair', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    document.body.append(root);
    return { fixture, root, host: fixture.componentInstance };
  }

  it('keeps instruction typing out of content edits and sends the trimmed text once', async () => {
    const { fixture, root, host } = await render();
    const trigger = root.querySelector<HTMLButtonElement>('#q-improve')!;
    const form = root.querySelector<HTMLFormElement>('#q-improve-form')!;
    expect(form.hidden).toBe(true);
    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const instruction = root.querySelector<HTMLTextAreaElement>('#q-instruction')!;
    expect(instruction.maxLength).toBe(4000);
    instruction.value = '  פשטו את הניסוח  ';
    instruction.dispatchEvent(new Event('input', { bubbles: true }));
    instruction.dispatchEvent(new Event('change', { bubbles: true }));
    expect(host.bubbled).toBe(0);
    root.querySelector<HTMLButtonElement>('#q-improve-submit')!.click();
    await fixture.whenStable();
    expect(host.requests).toEqual(['פשטו את הניסוח']);
    expect(form.hidden).toBe(true);
    expect(document.activeElement).toBe(trigger);
    root.remove();
  });

  it('fills the instruction from a suggestion without sending, then sends it on submit', async () => {
    const { fixture, root, host } = await render();
    root.querySelector<HTMLButtonElement>('#q-improve')!.click();
    await fixture.whenStable();
    const suggestion = root.querySelector<HTMLButtonElement>('#q-improve-form .chip')!;
    suggestion.focus();
    suggestion.click();
    await fixture.whenStable();
    const instruction = root.querySelector<HTMLTextAreaElement>('#q-instruction')!;
    expect(instruction.value).toBe('ניסוח פשוט יותר');
    expect(document.activeElement).toBe(suggestion);
    expect(host.requests).toEqual([]);
    root.querySelector<HTMLButtonElement>('#q-improve-submit')!.click();
    expect(host.requests).toEqual(['ניסוח פשוט יותר']);
    root.remove();
  });

  it('discards the instruction on cancel and returns focus to the trigger', async () => {
    const { fixture, root, host } = await render();
    const trigger = root.querySelector<HTMLButtonElement>('#q-improve')!;
    trigger.click();
    await fixture.whenStable();
    const instruction = root.querySelector<HTMLTextAreaElement>('#q-instruction')!;
    instruction.value = 'טיוטה';
    instruction.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    Array.from(root.querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === 'ביטול')!
      .click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(trigger);
    trigger.click();
    await fixture.whenStable();
    expect(instruction.value).toBe('');
    expect(host.requests).toEqual([]);
    root.remove();
  });
});
