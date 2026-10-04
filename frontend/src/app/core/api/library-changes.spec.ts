import { TestBed } from '@angular/core/testing';
import { FakeEventSource } from './event-source.fixture';
import { libraryChanges } from './library-changes';

describe('Library change connection', () => {
  afterEach(() => vi.restoreAllMocks());

  function observe() {
    const stream = TestBed.runInInjectionContext(libraryChanges);
    const receive = vi.fn();
    stream.changes.subscribe(receive);
    return { ...stream, receive };
  }

  it('leaves reconnecting errors to the browser and exposes terminal failures for explicit retry', () => {
    const stream = observe();
    const [source] = FakeEventSource.opened;
    expect(stream.state()).toBe('connecting');
    source.readyState = FakeEventSource.OPEN;
    source.onopen?.();
    expect(stream.state()).toBe('connected');
    source.send();
    expect(stream.receive).toHaveBeenCalledTimes(1);
    source.readyState = FakeEventSource.CONNECTING;
    source.onerror?.();
    expect(stream.state()).toBe('connecting');
    expect(FakeEventSource.opened).toHaveLength(1);
    source.readyState = FakeEventSource.CLOSED;
    source.onerror?.();
    expect(stream.state()).toBe('unavailable');
    expect(FakeEventSource.opened).toHaveLength(1);
    stream.reconnect();
    expect(FakeEventSource.opened).toHaveLength(2);
    expect(stream.state()).toBe('connecting');
    expect(stream.receive).toHaveBeenCalledTimes(2);
    // A failed retry remains explicit rather than spawning a connection loop.
    const next = FakeEventSource.opened[1];
    next.readyState = FakeEventSource.CLOSED;
    next.onerror?.();
    expect(stream.state()).toBe('unavailable');
    expect(FakeEventSource.opened).toHaveLength(2);
  });

  it('closes the old source before retry and disposes the current source with its page', () => {
    const stream = observe();
    const [first] = FakeEventSource.opened;
    stream.reconnect();
    expect(first.readyState).toBe(FakeEventSource.CLOSED);
    const next = FakeEventSource.opened[1];
    TestBed.resetTestingModule();
    expect(next.readyState).toBe(FakeEventSource.CLOSED);
    stream.reconnect();
    expect(FakeEventSource.opened).toHaveLength(2);
  });

  it('stays paused while hidden and receives the new connection catch-up when shown', () => {
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    const stream = observe();
    expect(stream.state()).toBe('paused');
    stream.reconnect();
    expect(FakeEventSource.opened).toHaveLength(0);
    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    expect(FakeEventSource.opened).toHaveLength(1);
    FakeEventSource.opened[0].send();
    expect(stream.receive).toHaveBeenCalledTimes(1);
    visibility.mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    expect(stream.state()).toBe('paused');
    expect(FakeEventSource.opened[0].readyState).toBe(FakeEventSource.CLOSED);
  });
});
