import { TestBed } from '@angular/core/testing';
import { Theme } from './theme';

describe('Theme', () => {
  const root = document.documentElement;

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    root.removeAttribute('data-theme');
  });

  it('restores a saved choice and treats unknown values as the device preference', () => {
    localStorage.setItem('theme', 'dark');
    expect(TestBed.inject(Theme).preference()).toBe('dark');
    TestBed.resetTestingModule();
    localStorage.setItem('theme', 'sepia');
    expect(TestBed.inject(Theme).preference()).toBe('system');
  });

  it('applies and persists explicit choices, and clears both for the device preference', () => {
    const theme = TestBed.inject(Theme);
    theme.select('dark');
    TestBed.tick();
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem('theme')).toBe('dark');
    theme.select('system');
    TestBed.tick();
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem('theme')).toBeNull();
  });

  it('follows another tab and still applies a choice when storage is blocked', () => {
    const theme = TestBed.inject(Theme);
    localStorage.setItem('theme', 'light');
    window.dispatchEvent(new StorageEvent('storage', { key: 'theme', newValue: 'light' }));
    expect(theme.preference()).toBe('light');
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Blocked', 'SecurityError');
    });
    theme.select('dark');
    TestBed.tick();
    expect(root.getAttribute('data-theme')).toBe('dark');
  });
});
