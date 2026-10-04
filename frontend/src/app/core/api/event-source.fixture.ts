/** Replaces the EventSource jsdom lacks in every unit test; `opened` lists the current test's streams. */
export class FakeEventSource {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSED = 2;
  static readonly opened: FakeEventSource[] = [];
  readyState = 0;
  onmessage: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onopen: (() => void) | null = null;
  constructor(readonly url: string) {
    FakeEventSource.opened.push(this);
  }
  /** Delivers one change note. */
  send() {
    this.onmessage?.();
  }
  close() {
    this.readyState = FakeEventSource.CLOSED;
  }
}
