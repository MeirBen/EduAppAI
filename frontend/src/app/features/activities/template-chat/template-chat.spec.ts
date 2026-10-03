import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import { TemplateChat } from './template-chat';

@Component({
  imports: [TemplateChat],
  template:
    '<app-template-chat [fields]="fields" [configured]="configured()" [busy]="busy()" [clarification]="question()" (sent)="submitted = raw().message" />',
})
class Host {
  readonly raw = signal({ message: '', consolidated: '' });
  readonly fields = form(this.raw);
  readonly configured = signal(true);
  readonly question = signal('');
  readonly busy = signal(false);
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

  it('sends on Enter only, and a suggestion fills the composer without sending', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('ul[role="list"] button')!.click();
    await fixture.whenStable();
    const field = root.querySelector<HTMLTextAreaElement>('#chat-message')!;
    expect(field.value).not.toBe('');
    expect(host.submitted).toBe('');
    const press = (shiftKey: boolean) =>
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey, bubbles: true }));
    press(true);
    expect(host.submitted).toBe('');
    press(false);
    expect(host.submitted).toBe(field.value);
  });

  it('keeps keyboard focus on the swapped send and stop controls without taking it elsewhere', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    host.raw.set({ message: 'כיתה ג', consolidated: '' });
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('#chat-send')!.focus();
    host.busy.set(true);
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('chat-cancel');
    host.busy.set(false);
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('chat-message');
    const other = document.body.appendChild(document.createElement('input'));
    other.focus();
    host.busy.set(true);
    await fixture.whenStable();
    host.busy.set(false);
    await fixture.whenStable();
    expect(document.activeElement).toBe(other);
    other.remove();
  });
});
