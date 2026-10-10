import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import { ActivityChat } from './activity-chat';

@Component({
  imports: [ActivityChat],
  template:
    '<app-activity-chat [fields]="fields" [configured]="configured()" [busy]="busy()" [clarification]="question()" [refining]="refining()" [thread]="thread()" (sent)="submitted = raw().message" />',
})
class Host {
  readonly raw = signal({ message: '' });
  readonly fields = form(this.raw);
  readonly configured = signal(true);
  readonly question = signal('');
  readonly busy = signal(false);
  readonly refining = signal(false);
  readonly thread = signal<{ role: 'parent' | 'assistant'; text: string }[]>([]);
  submitted = '';
}
describe('ActivityChat presentation', () => {
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

  it('offers change suggestions for an existing plan, but not while a question awaits an answer', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    host.refining.set(true);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    const idea = root.querySelector<HTMLButtonElement>('.chip')!;
    idea.focus();
    idea.click();
    await fixture.whenStable();
    expect(root.querySelector<HTMLTextAreaElement>('#chat-message')!.value).toBe(
      idea.textContent!.trim(),
    );
    // An idea fills the composer in place; focus stays where the parent picked it.
    expect(document.activeElement).toBe(idea);
    expect(host.submitted).toBe('');
    host.question.set('לאיזה גיל?');
    await fixture.whenStable();
    expect(root.querySelector('app-suggestion-chips')).toBeNull();
  });

  it('does not take focus when an action outside chat disappears after creation', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    host.raw.set({ message: 'בקשה' });
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('#chat-send')!.focus();
    const create = document.body.appendChild(document.createElement('button'));
    create.focus();
    host.busy.set(true);
    await fixture.whenStable();
    create.remove();
    host.busy.set(false);
    await fixture.whenStable();
    expect(document.activeElement).toBe(document.body);
  });

  it('keeps keyboard focus on the swapped send and stop controls without taking it elsewhere', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    host.raw.set({ message: 'כיתה ג' });
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('#chat-send')!.focus();
    host.busy.set(true);
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('chat-cancel');
    host.busy.set(false);
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('chat-message');
    // Once there is history, a finished reply leaves focus on it rather than in the field.
    host.thread.set([{ role: 'assistant', text: 'תשובה' }]);
    root.querySelector<HTMLButtonElement>('#chat-send')!.focus();
    host.busy.set(true);
    await fixture.whenStable();
    host.busy.set(false);
    await fixture.whenStable();
    expect(document.activeElement?.getAttribute('role')).toBe('log');
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
