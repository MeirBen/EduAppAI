import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { GenerationKind, GenerationOperation } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { lengthText } from '../activity-document-view/measurements';
import { isRunning, stageNames } from './operation-state';

/** A parent-facing reading of one operation; `retry` is an explicit new request, never automatic. */
interface StatusView {
  title: string;
  details: string[];
  problem: boolean;
  retry?: { kind: GenerationKind; label: string };
  checkSaved?: boolean;
}

/**
 * Pure status presentation. The workspace owns polling and every request; this component only
 * emits explicit parent actions. Technical evidence stays available behind a disclosure.
 */
@Component({
  selector: 'app-generation-status',
  imports: [LoadingIndicator],
  templateUrl: './generation-status.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GenerationStatus {
  readonly operation = input.required<GenerationOperation>();
  readonly locked = input(false);
  /** Whether AI is configured; a retry is offered only when it could start. */
  readonly configured = input(false);
  readonly cancelled = output<void>();
  readonly retried = output<GenerationKind>();
  readonly checked = output<void>();
  protected readonly active = computed(() => isRunning(this.operation()));
  protected readonly stages: Record<string, string> = {
    materials: 'כותבים את חומר הלימוד',
    questions: 'מכינים את השאלות',
    'replace-material': 'כותבים גרסה חדשה לטקסט',
    'replace-question': 'מכינים גרסה חדשה לשאלה',
  };
  protected readonly stageNames = stageNames;
  protected readonly outcomes: Record<string, string> = {
    calling: 'בפנייה לשירות',
    accepted: 'תוכן התקבל',
    applied: 'תוכן התקבל',
    completed: 'הושלם',
    failed: 'נכשל',
    conflict: 'לא הוחל בגלל שינוי בטיוטה',
    cancelled: 'בוטל',
    unknown: 'תוצאה לא ידועה',
  };
  protected readonly view = computed((): StatusView => {
    const operation = this.operation();
    const scope =
      operation.kind === 'ReplaceMaterial'
        ? 'material'
        : operation.kind === 'ReplaceQuestion'
          ? 'question'
          : 'activity';
    switch (operation.status) {
      case 'queued':
      case 'calling':
        return {
          title:
            scope === 'material'
              ? 'משפרים את הטקסט…'
              : scope === 'question'
                ? 'משפרים את השאלה…'
                : 'יוצרים את הפעילות…',
          details: [this.stages[operation.stage] ?? ''],
          problem: false,
        };
      case 'completed':
        return {
          title:
            scope === 'material'
              ? 'הטקסט עודכן.'
              : scope === 'question'
                ? 'השאלה עודכנה.'
                : 'הפעילות נוצרה.',
          details: [
            scope === 'material'
              ? 'בדקו את הטקסט ואת השאלות שתלויות בו לפני סימון כמוכנה.'
              : 'עברו על התוכן והתשובות לפני סימון כמוכנה.',
          ],
          problem: false,
        };
      case 'failed':
        return this.failure(operation, scope);
      case 'conflict':
        return {
          title: 'הטיוטה השתנתה בזמן היצירה, ולכן התוצאה לא הוחלה.',
          details: ['העבודה שלכם לא הוחלפה.'],
          problem: true,
        };
      case 'cancelled':
        return {
          title: 'היצירה בוטלה.',
          details: ['תוכן שכבר נשמר נשאר בטיוטה.'],
          problem: false,
        };
      default:
        return {
          title: 'לא ידוע אם שירות ה־AI סיים את הבקשה.',
          details: ['לא הפעלנו ניסיון נוסף אוטומטית כדי למנוע חיוב כפול.'],
          problem: true,
          checkSaved: true,
        };
    }
  });
  private failure(operation: GenerationOperation, scope: string): StatusView {
    const unchanged = 'התוצאה לא החליפה את התוכן הקיים.';
    const lengths = (operation.artifacts?.steps ?? []).flatMap((step) =>
      Object.keys(step.diagnostics ?? {}).filter((key) => key.startsWith('length.')),
    );
    if (lengths.length) {
      const input = operation.artifacts?.input;
      const required = [...new Set(lengths)].flatMap((key) => {
        const scopeId = key.slice('length.'.length);
        const expected =
          scopeId === 'total'
            ? input?.totalLength
            : input?.materials.find((material) => material.id === scopeId)?.length;
        return expected ? [`נדרש: ${lengthText(expected)}`] : [];
      });
      return {
        title: 'הטקסט שנוצר לא עמד בדרישת האורך.',
        details: [...required, unchanged],
        problem: true,
        retry:
          scope === 'activity' ? { kind: 'GenerateActivity', label: 'ניסיון נוסף' } : undefined,
      };
    }
    if (scope !== 'activity')
      return {
        title: 'השיפור לא הצליח.',
        details: [unchanged, 'אפשר לנסות שוב מהחלק עצמו.'],
        problem: true,
      };
    if (operation.stage === 'questions') {
      const materialsKept = operation.steps.some(
        (step) =>
          step.stage === 'materials' && (step.outcome === 'accepted' || step.outcome === 'applied'),
      );
      return materialsKept
        ? {
            title: 'הטקסט נשמר, אבל יצירת השאלות נכשלה.',
            details: ['אפשר לנסות ליצור את השאלות שוב בלי לאבד את הטקסט.'],
            problem: true,
            retry: { kind: 'GenerateQuestions', label: 'יצירת השאלות שוב' },
          }
        : {
            title: 'יצירת השאלות נכשלה.',
            details: [unchanged],
            problem: true,
            retry: { kind: operation.kind, label: 'ניסיון נוסף' },
          };
    }
    return {
      title: 'יצירת הטקסט נכשלה.',
      details: [unchanged],
      problem: true,
      retry: { kind: 'GenerateActivity', label: 'ניסיון נוסף' },
    };
  }
  protected messages(diagnostics: Record<string, string[]> | null) {
    return Object.values(diagnostics ?? {}).flat();
  }
  protected candidateText(candidate: unknown, output: string | null | undefined) {
    return candidate == null ? (output ?? '') : JSON.stringify(candidate, null, 2);
  }
}
