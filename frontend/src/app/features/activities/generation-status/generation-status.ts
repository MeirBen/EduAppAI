import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { GenerationOperation } from '../../../core/api/models';

/** Pure status presentation. The workspace owns polling; no lifecycle display starts or retries AI. */
@Component({
  selector: 'app-generation-status',
  imports: [],
  templateUrl: './generation-status.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GenerationStatus {
  readonly operation = input.required<GenerationOperation>();
  readonly locked = input(false);
  readonly cancelled = output<void>();
  protected readonly active = computed(() =>
    ['queued', 'calling'].includes(this.operation().status),
  );
  protected readonly stages: Record<string, string> = {
    materials: 'יצירת חומרים',
    questions: 'יצירת שאלות',
    'replace-material': 'החלפת חומר',
    'replace-question': 'החלפת שאלה',
  };
  protected readonly statuses: Record<string, string> = {
    queued: 'ממתינה לביצוע',
    calling: 'פנייה לשירות ה־AI',
    completed: 'הפעולה הושלמה',
    failed: 'הפעולה נכשלה',
    conflict: 'הטיוטה השתנתה',
    cancelled: 'הפעולה בוטלה',
    unknown: 'תוצאה לא ידועה',
  };
  protected readonly outcomes: Record<string, string> = {
    calling: 'פנייה לשירות',
    accepted: 'תוכן התקבל',
    applied: 'תוכן התקבל',
    completed: 'הושלם',
    failed: 'נכשל',
    conflict: 'לא הוחל בגלל שינוי בטיוטה',
    cancelled: 'בוטל',
    unknown: 'תוצאה לא ידועה',
  };
  protected messages(diagnostics: Record<string, string[]> | null) {
    return Object.values(diagnostics ?? {}).flat();
  }
  protected candidateText(candidate: unknown, output: string | null | undefined) {
    return candidate == null ? (output ?? '') : JSON.stringify(candidate, null, 2);
  }
}
