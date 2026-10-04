import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DisabledInteractive } from './disabled-interactive';

@Component({
  imports: [DisabledInteractive],
  template: `<form (submit)="$event.preventDefault(); submitted = submitted + 1">
    <input id="field" />
    <button
      id="action"
      type="submit"
      [disabledInteractive]="busy()"
      (click)="clicked = clicked + 1"
    >
      שמירה
    </button>
  </form>`,
})
class Host {
  readonly busy = signal(true);
  clicked = 0;
  submitted = 0;
}

describe('DisabledInteractive', () => {
  it('keeps an unavailable button focusable while it ignores activation and submission', async () => {
    const fixture = TestBed.createComponent(Host),
      host = fixture.componentInstance;
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector('#action') as HTMLButtonElement;
    button.focus();
    expect(document.activeElement).toBe(button);
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    button.click();
    expect([host.clicked, host.submitted]).toEqual([0, 0]);
    host.busy.set(false);
    await fixture.whenStable();
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    button.click();
    expect([host.clicked, host.submitted]).toEqual([1, 1]);
  });
});
