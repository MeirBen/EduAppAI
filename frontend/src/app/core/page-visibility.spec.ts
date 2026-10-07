import {
  createEnvironmentInjector,
  EnvironmentInjector,
  runInInjectionContext,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { refreshOnReturn } from './page-visibility';

describe('refreshOnReturn', () => {
  let state: DocumentVisibilityState = 'visible';
  beforeEach(() => {
    state = 'visible';
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  });
  afterEach(() => Reflect.deleteProperty(document, 'visibilityState'));
  const show = (next: DocumentVisibilityState) => {
    state = next;
    document.dispatchEvent(new Event('visibilitychange'));
  };

  it('refreshes only when the page returns, and stops with its owner', () => {
    const injector = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
    const refresh = vi.fn();
    runInInjectionContext(injector, () => refreshOnReturn(refresh));
    expect(refresh).not.toHaveBeenCalled();
    show('hidden');
    expect(refresh).not.toHaveBeenCalled();
    show('visible');
    expect(refresh).toHaveBeenCalledTimes(1);
    injector.destroy();
    show('hidden');
    show('visible');
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});
