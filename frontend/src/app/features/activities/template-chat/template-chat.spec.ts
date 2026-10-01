import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import { TemplateChat } from './template-chat';

@Component({
  imports: [TemplateChat],
  template:
    '<app-template-chat [fields]="fields" [configured]="configured()" [clarification]="question()" (sent)="submitted = raw().message" />',
})
class Host {
  readonly raw = signal({ message: '', consolidated: '' });
  readonly fields = form(this.raw);
  readonly configured = signal(true);
  readonly question = signal('');
  submitted = '';
}
describe('TemplateChat presentation', () => {
  it('renders clarification as text and waits for an explicit submitted answer', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    host.question.set('<img src=x onerror=alert(1)> לאיזה גיל?');
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('img')).toBeNull();
    expect(host.submitted).toBe('');
    const field = root.querySelector<HTMLTextAreaElement>('#chat-message')!;
    field.value = 'כיתה ג';
    field.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.submitted).toBe('');
    root.querySelector<HTMLButtonElement>('#chat-send')!.click();
    expect(host.submitted).toBe('כיתה ג');
    host.configured.set(false);
    await fixture.whenStable();
    expect(root.querySelector<HTMLButtonElement>('#chat-send')!.disabled).toBe(true);
    expect(root.textContent).toContain('אפשר למלא את הפרטים ידנית');
  });
});
