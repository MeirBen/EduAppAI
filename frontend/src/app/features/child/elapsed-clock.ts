/**
 * Elapsed time as a clock: m:ss within the first hour, then h:mm:ss. A device clock running behind
 * the server reads as zero; an unreadable start shows nothing rather than a guess.
 */
export function elapsedClock(startedAtUtc: string, now: number): string {
  const started = Date.parse(startedAtUtc);
  if (!Number.isFinite(started)) return '';
  const total = Math.max(0, Math.floor((now - started) / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor(total / 60) % 60;
  const seconds = String(total % 60).padStart(2, '0');
  return hours
    ? `${hours}:${String(minutes).padStart(2, '0')}:${seconds}`
    : `${minutes}:${seconds}`;
}
