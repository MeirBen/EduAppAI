import { elapsedClock } from './elapsed-clock';

describe('elapsedClock', () => {
  const start = '2026-10-01T10:00:00Z';
  const at = (seconds: number) => Date.parse(start) + seconds * 1000;

  it('counts minutes and seconds, then hours, from the server start', () => {
    expect(elapsedClock(start, at(0))).toBe('0:00');
    expect(elapsedClock(start, at(65.9))).toBe('1:05');
    expect(elapsedClock(start, at(3600 + 61))).toBe('1:01:01');
    expect(elapsedClock(start, at(26 * 3600))).toBe('26:00:00');
  });

  it('reads a device clock behind the server as zero and an unreadable start as nothing', () => {
    expect(elapsedClock(start, at(-30))).toBe('0:00');
    expect(elapsedClock('not a date', at(10))).toBe('');
  });
});
