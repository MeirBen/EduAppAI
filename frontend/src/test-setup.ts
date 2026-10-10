import { FakeEventSource } from './app/core/api/event-source.fixture';

// Runs before each spec file, so the reset applies in Vitest's non-isolated mode too.
vi.stubGlobal('EventSource', FakeEventSource);
// jsdom lays nothing out, so size observation has nothing to report.
vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    disconnect() {}
  },
);
beforeEach(() => {
  FakeEventSource.opened.length = 0;
  localStorage.clear();
});
