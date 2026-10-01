import { LearningPlan, LengthMeasurement } from '../../../core/api/models';
/** Formats server-owned counts; the client never reimplements Unicode counting or a target tolerance. */
export function measurementText(measurements: LengthMeasurement[], plan?: LearningPlan): string[] {
  return measurements.map((measurement) => {
    const label =
      measurement.scope === 'total'
        ? 'כל החומרים שנוצרו'
        : (plan?.materials.find((m) => m.id === measurement.scope)?.label ?? 'חומר');
    const expected = measurement.expected;
    const requirement =
      expected.mode === 'range'
        ? `טווח ${expected.lower}–${expected.upper}`
        : expected.mode === 'exact'
          ? `בדיוק ${expected.value}`
          : `יעד משוער ${expected.value}`;
    const result =
      measurement.satisfied === null
        ? 'היעד משוער ואינו חוסם המשך'
        : measurement.satisfied
          ? 'עומד בדרישה'
          : 'נדרש תיקון לפני יצירת שאלות או סימון כמוכנה';
    return `${label}: ${measurement.actual} מילים; ${requirement}. ${result}.`;
  });
}
