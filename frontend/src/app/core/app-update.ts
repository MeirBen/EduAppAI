import { DOCUMENT, Injectable, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';
import { filter, map } from 'rxjs';
import { refreshOnReturn } from './page-visibility';

/**
 * The service worker's build state for the whole app. Reloading opens the newer build, and each
 * page's unload warning still guards its unsaved work, so the reader decides when.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdate {
  private readonly updates = inject(SwUpdate);
  private readonly document = inject(DOCUMENT);
  /** A newer build is installed and opens on reload. */
  readonly ready = toSignal(
    this.updates.versionUpdates.pipe(
      filter((event) => event.type === 'VERSION_READY'),
      map(() => true),
    ),
    { initialValue: false },
  );
  /** The cached build can no longer load its files, so only a reload recovers. */
  readonly broken = toSignal(this.updates.unrecoverable.pipe(map(() => true)), {
    initialValue: false,
  });

  constructor() {
    // The worker checks only on page loads; an open tab also checks on each return, as after a deployment.
    if (this.updates.isEnabled)
      refreshOnReturn(() => void this.updates.checkForUpdate().catch(() => undefined));
  }

  reload() {
    this.document.location.reload();
  }
}
