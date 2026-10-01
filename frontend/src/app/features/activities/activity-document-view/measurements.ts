import { LearningPlan, LengthMeasurement, ResolvedLength } from '../../../core/api/models';

/** Parent wording for one saved measurement; advisory targets never read as failures. */
export interface MeasurementItem {
  label: string;
  requirement: string;
  actual: string;
  state: 'advisory' | 'met' | 'blocking';
}

/** Reads a generated-body requirement as parents say it. Input bounds are a separate permitted range. */
export function lengthText(expected: ResolvedLength): string {
  return expected.mode === 'range'
    ? `${expected.lower}–${expected.upper} מילים`
    : expected.mode === 'exact'
      ? `בדיוק ${expected.value} מילים`
      : `בערך ${expected.value} מילים`;
}

/** Formats server-owned counts; the client never reimplements Unicode counting or a target tolerance. */
export function measurementItems(
  measurements: LengthMeasurement[],
  plan?: LearningPlan,
): MeasurementItem[] {
  return measurements.map((measurement) => ({
    label:
      measurement.scope === 'total'
        ? 'כל הטקסטים יחד'
        : (plan?.materials.find((m) => m.id === measurement.scope)?.label ?? 'טקסט'),
    requirement:
      (measurement.satisfied === null ? 'אורך מבוקש: ' : 'אורך נדרש: ') +
      lengthText(measurement.expected),
    actual: `בפועל: ${measurement.actual} מילים`,
    state: measurement.satisfied === null ? 'advisory' : measurement.satisfied ? 'met' : 'blocking',
  }));
}

export function measurementText(measurements: LengthMeasurement[], plan?: LearningPlan): string[] {
  return measurementItems(measurements, plan).map(
    (item) =>
      `${item.label}: ${item.requirement} · ${item.actual}` +
      (item.state === 'blocking' ? ' · יש לתקן לפני סימון כמוכנה' : ''),
  );
}
