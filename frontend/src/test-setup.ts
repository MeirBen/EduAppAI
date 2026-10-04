import { FakeEventSource } from './app/core/api/event-source.fixture';

// Runs before each spec file, so the reset applies in Vitest's non-isolated mode too.
vi.stubGlobal('EventSource', FakeEventSource);
beforeEach(() => {
  FakeEventSource.opened.length = 0;
});
