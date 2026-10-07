import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { dismissibleTooltips } from './dismissible-tooltips';

@Component({ template: '' })
class Host {
  constructor() {
    dismissibleTooltips();
  }
}

describe('dismissibleTooltips', () => {
  it('hides tooltips on Escape until the pointer or focus moves, and stops listening with its owner', () => {
    const fixture = TestBed.createComponent(Host);
    const hidden = () => document.documentElement.hasAttribute('data-tooltips-hidden');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(hidden()).toBe(true);
    document.dispatchEvent(new Event('pointerover'));
    expect(hidden()).toBe(false);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    document.dispatchEvent(new FocusEvent('focusin'));
    expect(hidden()).toBe(false);
    fixture.destroy();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(hidden()).toBe(false);
  });

  it("hides a pressed control's tooltip until the pointer or focus leaves that control", () => {
    const fixture = TestBed.createComponent(Host);
    const hidden = () => document.documentElement.hasAttribute('data-tooltips-hidden');
    const control = document.createElement('button');
    control.innerHTML = '<span class="icon"></span><span class="tooltip">הקטנת הסרגל</span>';
    const other = document.createElement('a');
    document.body.append(control, other);
    const at = (target: Element, type: string) =>
      target.dispatchEvent(new Event(type, { bubbles: true }));
    at(control.firstElementChild!, 'pointerdown');
    expect(hidden()).toBe(true);
    // The click focuses the same control and the pointer may cross its icon; neither shows it again.
    at(control, 'focusin');
    at(control.firstElementChild!, 'pointerover');
    expect(hidden()).toBe(true);
    at(other, 'pointerover');
    expect(hidden()).toBe(false);
    // Pressing something without a tooltip changes nothing.
    at(other, 'pointerdown');
    expect(hidden()).toBe(false);
    control.remove();
    other.remove();
    fixture.destroy();
  });
});
