import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwUpdate, VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { AppUpdate } from './app-update';

describe('AppUpdate', () => {
  function setup(isEnabled = true) {
    const versionUpdates = new Subject<VersionEvent>();
    const unrecoverable = new Subject<{ type: 'UNRECOVERABLE_STATE'; reason: string }>();
    const checkForUpdate = vi.fn().mockRejectedValue(new Error('offline'));
    const page = Object.assign(new EventTarget(), {
      visibilityState: 'visible',
      location: { reload: vi.fn() },
    });
    TestBed.configureTestingModule({
      providers: [
        {
          provide: SwUpdate,
          useValue: { isEnabled, versionUpdates, unrecoverable, checkForUpdate },
        },
        { provide: DOCUMENT, useValue: page },
      ],
    });
    const show = (visibilityState: string) => {
      page.visibilityState = visibilityState;
      page.dispatchEvent(new Event('visibilitychange'));
    };
    return {
      update: TestBed.inject(AppUpdate),
      versionUpdates,
      unrecoverable,
      checkForUpdate,
      page,
      show,
    };
  }

  it('offers a reload only once a newer build is ready, and reloads on request', () => {
    const { update, versionUpdates, page } = setup();
    const version = { hash: 'next' };
    versionUpdates.next({ type: 'VERSION_DETECTED', version });
    expect(update.ready()).toBe(false);

    versionUpdates.next({
      type: 'VERSION_READY',
      currentVersion: { hash: 'old' },
      latestVersion: version,
    });
    expect(update.ready()).toBe(true);
    update.reload();
    expect(page.location.reload).toHaveBeenCalledOnce();
  });

  it('reports a cached build that can no longer load', () => {
    const { update, unrecoverable } = setup();
    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing file' });
    expect(update.broken()).toBe(true);
  });

  it('checks again each time an open tab returns, tolerating a failed check', () => {
    const { checkForUpdate, show } = setup();
    expect(checkForUpdate).not.toHaveBeenCalled();
    show('hidden');
    show('visible');
    expect(checkForUpdate).toHaveBeenCalledOnce();
  });

  it('never checks while the worker is disabled', () => {
    const { checkForUpdate, show } = setup(false);
    show('hidden');
    show('visible');
    expect(checkForUpdate).not.toHaveBeenCalled();
  });
});
