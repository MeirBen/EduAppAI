import { signal } from '@angular/core';

const entryLimit = 20;

/**
 * Bounded Undo over detached snapshots. Consecutive edits under one non-empty key, such as typing
 * in one field, coalesce into a single entry; any other change starts a new entry.
 */
export class UndoHistory<T> {
  private readonly stack = signal<T[]>([]);
  /** Restorable snapshots, oldest first. */
  readonly entries = this.stack.asReadonly();
  private last: T;
  private key = '';

  /** @param snapshot Captures the current state as a detached copy. */
  constructor(private readonly snapshot: () => T) {
    this.last = snapshot();
  }

  /** Records the change since the last recorded state; returns false when nothing changed. */
  record(key: string): boolean {
    const current = this.snapshot();
    if (JSON.stringify(current) === JSON.stringify(this.last)) return false;
    if (!key || key !== this.key) this.push(this.last);
    this.key = key;
    this.last = current;
    return true;
  }

  /** Saves a snapshot, by default the current state, before a change that bypasses `record`. */
  push(entry: T = this.snapshot()) {
    this.stack.update((entries) => [...entries.slice(1 - entryLimit), entry]);
  }

  /** Removes and returns the newest snapshot. */
  pop(): T | undefined {
    const entries = this.stack();
    this.stack.set(entries.slice(0, -1));
    return entries.at(-1);
  }

  /** Accepts the current state as recorded and ends coalescing. */
  checkpoint() {
    this.last = this.snapshot();
    this.key = '';
  }

  clear() {
    this.stack.set([]);
  }
}
