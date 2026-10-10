import { DOCUMENT, inject, Injectable } from '@angular/core';

/** A sign-in screen preference, never an identity or authorization decision. */
export type EntryMode = 'parent' | 'child';

/** Remembers the last successful entry across expiry and sign-out; blocked storage stays in memory. */
@Injectable({ providedIn: 'root' })
export class DeviceEntry {
  private readonly document = inject(DOCUMENT);
  private fallback: EntryMode | null = null;
  private writeFailed = false;

  read(): EntryMode | null {
    if (this.writeFailed) return this.fallback;
    try {
      const storage = this.document.defaultView?.localStorage;
      if (!storage) return this.fallback;
      const saved = storage.getItem('entry-mode');
      return saved === 'parent' || saved === 'child' ? saved : null;
    } catch {
      return this.fallback;
    }
  }

  remember(mode: EntryMode) {
    this.fallback = mode;
    try {
      this.document.defaultView?.localStorage.setItem('entry-mode', mode);
      this.writeFailed = false;
    } catch {
      // Authentication still works when persistence is unavailable.
      this.writeFailed = true;
    }
  }
}
