import { LearningPlan, LengthMeasurement, ResolvedLength } from '../../../core/api/models';

/** One saved measurement in parent terms; advisory targets never read as failures. */
export interface MeasurementItem {
  label: string;
  /** Words in the saved text, as the server counted them. */
  actual: number;
  requirement: string;
  state: 'advisory' | 'met' | 'blocking';
}

/** A requirement from the server, or one typed in the editable plan before it is canonical. */
type WordRequirement = Pick<ResolvedLength, 'mode'> &
  Record<'value' | 'lower' | 'upper', number | string | null>;

/** Reads a generated-body requirement as parents say it. Input bounds are a separate permitted range. */
export function lengthText(expected: WordRequirement): string {
  return expected.mode === 'range'
    ? `${expected.lower}–${expected.upper} מילים`
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
    actual: measurement.actual,
    requirement: lengthText(measurement.expected),
    state: measurement.satisfied === null ? 'advisory' : measurement.satisfied ? 'met' : 'blocking',
  }));
}
