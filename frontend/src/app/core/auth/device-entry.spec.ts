import { TestBed } from '@angular/core/testing';
import { DeviceEntry } from './device-entry';

describe('Device entry preference', () => {
  afterEach(() => vi.restoreAllMocks());

  it.each(['parent', 'child'] as const)('restores %s after restarting the app', (mode) => {
    TestBed.inject(DeviceEntry).remember(mode);
    TestBed.resetTestingModule();
    expect(TestBed.inject(DeviceEntry).read()).toBe(mode);
    expect(Object.keys(localStorage)).toEqual(['entry-mode']);
  });

  it('ignores unknown values and respects cleared storage', () => {
    const entry = TestBed.inject(DeviceEntry);
    localStorage.setItem('entry-mode', 'unknown');
    expect(entry.read()).toBeNull();
    entry.remember('child');
    localStorage.clear();
    expect(entry.read()).toBeNull();
  });

  it.each([false, true])(
    'keeps a preference when writes fail (reads also fail: %s)',
    (readsFail) => {
      for (const method of readsFail ? (['getItem', 'setItem'] as const) : (['setItem'] as const))
        vi.spyOn(Storage.prototype, method).mockImplementation(() => {
          throw new DOMException('Blocked', 'SecurityError');
        });
      const entry = TestBed.inject(DeviceEntry);
      expect(entry.read()).toBeNull();
      entry.remember('child');
      expect(entry.read()).toBe('child');
      TestBed.resetTestingModule();
      expect(TestBed.inject(DeviceEntry).read()).toBeNull();
    },
  );
});
