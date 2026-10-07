import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActionBar, ActionBarToggle } from './action-bar';

@Component({
  imports: [ActionBar, ActionBarToggle],
  template: `<section class="action-bar" actionBar><app-action-bar-toggle /></section>`,
})
class Host {}

describe('ActionBar', () => {
  it('lets the toggle shrink and restore its bar, reporting the state to assistive technology', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const bar = root.querySelector('section')!;
    const toggle = root.querySelector('button')!;
    expect(bar.hasAttribute('data-minimized')).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    await fixture.whenStable();
    expect(bar.hasAttribute('data-minimized')).toBe(true);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    await fixture.whenStable();
    expect(bar.hasAttribute('data-minimized')).toBe(false);
  });
});
