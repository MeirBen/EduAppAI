import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, of, tap } from 'rxjs';
import { ContentLimits } from './models';

/** The server's content limits, loaded once by the parent guard before any private page renders. */
@Injectable({ providedIn: 'root' })
export class Limits {
  private readonly http = inject(HttpClient);
  private loaded?: ContentLimits;

  /** Reads the limits on first use and reuses them for the rest of the session. */
  load(): Observable<ContentLimits> {
    return this.loaded
      ? of(this.loaded)
      : this.http.get<ContentLimits>('/api/limits').pipe(tap((limits) => (this.loaded = limits)));
  }

  /** The loaded limits; private pages can rely on the guard having loaded them. */
  get current(): ContentLimits {
    if (!this.loaded)
      throw new Error('Content limits are read before the parent guard loaded them.');
    return this.loaded;
  }
}

/** Formats a limit for Hebrew copy, such as 8,000. */
export const count = (value: number) => value.toLocaleString('he-IL');
