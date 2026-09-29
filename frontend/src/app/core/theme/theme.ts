import { DOCUMENT } from '@angular/common';
import { DestroyRef, effect, inject, Injectable, signal } from '@angular/core';

/** `system` follows the device and is stored as the absence of a saved choice. */
export type ThemePreference = 'system' | 'light' | 'dark';

/** Shared with the pre-paint script in index.html. */
const storageKey = 'theme';

/**
 * Owns the device-level color theme. index.html applies a saved choice before the app starts;
 * this keeps `data-theme`, localStorage and other open tabs in sync afterwards. When storage is
 * unavailable, a choice still applies to the current page.
 */
@Injectable({ providedIn: 'root' })
export class Theme {
  private readonly document = inject(DOCUMENT);
  private readonly choice = signal(this.read());
  readonly preference = this.choice.asReadonly();

  constructor() {
    effect(() => this.apply(this.choice()));
    const view = this.document.defaultView;
    // A null key means another tab cleared storage.
    const sync = (event: StorageEvent) => {
      if (event.key === storageKey || event.key === null) this.choice.set(this.read());
    };
    view?.addEventListener('storage', sync);
    inject(DestroyRef).onDestroy(() => view?.removeEventListener('storage', sync));
  }

  select(preference: ThemePreference) {
    this.choice.set(preference);
  }

  private read(): ThemePreference {
    try {
      const saved = this.document.defaultView?.localStorage.getItem(storageKey);
      return saved === 'light' || saved === 'dark' ? saved : 'system';
    } catch {
      return 'system';
    }
  }

  private apply(preference: ThemePreference) {
    const root = this.document.documentElement;
    if (preference === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', preference);
    try {
      const storage = this.document.defaultView?.localStorage;
      if (preference === 'system') storage?.removeItem(storageKey);
      else storage?.setItem(storageKey, preference);
    } catch {
      // Blocked or full storage keeps the choice for this page only.
    }
  }
}
