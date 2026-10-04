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
});
