import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';
import { filter, map } from 'rxjs';
import { refreshOnReturn } from './page-visibility';

/** `ready`: a newer build opens on reload. `broken`: the cached build can no longer load its files. */
export type UpdateNotice = 'ready' | 'broken';

/**
 * The service worker's build state for the whole app. Reloading opens the newer build, and each
 * page's unload warning still guards its unsaved work, so the reader decides when.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdate {
  private readonly updates = inject(SwUpdate);
  private readonly document = inject(DOCUMENT);
  private readonly ready = toSignal(
    this.updates.versionUpdates.pipe(
      filter((event) => event.type === 'VERSION_READY'),
      map(() => true),
    ),
    { initialValue: false },
  );
  private readonly broken = toSignal(this.updates.unrecoverable.pipe(map(() => true)), {
    initialValue: false,
  });
  private readonly dismissed = signal<UpdateNotice | null>(null);
  /** A broken cache outranks a ready build; dismissing hides only the notice shown, so a later failure still appears. */
  readonly notice = computed(() => {
    const notice: UpdateNotice | null = this.broken() ? 'broken' : this.ready() ? 'ready' : null;
    return notice === this.dismissed() ? null : notice;
  });

  constructor() {
    // The worker checks only on page loads; an open tab also checks on each return, as after a deployment.
    if (this.updates.isEnabled)
      refreshOnReturn(() => void this.updates.checkForUpdate().catch(() => undefined));
  }

  reload() {
    this.document.location.reload();
  }

  dismiss() {
    this.dismissed.set(this.notice());
  }
}
