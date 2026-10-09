import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { GenerationOperation } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { lengthText } from '../activity-document-view/measurements';
import { isRunning, stageNames } from './operation-state';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { CopyButton } from '../../../shared/copy-button/copy-button';

/** A parent-facing reading of one operation. New attempts belong to the activity/chat actions. */
interface StatusView {
  title: string;
  details: string[];
  problem: boolean;
  checkSaved?: boolean;
}

/**
 * Pure status presentation. The workspace owns polling and every request; this component only
 * emits explicit parent actions. Technical evidence stays available behind a disclosure.
 */
@Component({
  selector: 'app-generation-status',
  imports: [DisabledInteractive, LoadingIndicator, CopyButton],
  templateUrl: './generation-status.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GenerationStatus {
  readonly operation = input.required<GenerationOperation>();
  readonly locked = input(false);
  readonly checked = output<void>();
  protected readonly active = computed(() => isRunning(this.operation()));
  protected readonly stages: Record<string, string> = {
    revise: 'בודקים את הבקשה',
    'rewrite-material': 'מעדכנים טקסט',
    'revise-question': 'מעדכנים שאלה',
    'append-questions': 'מוסיפים שאלות',
    'material-ideas': 'בוחרים רעיון לטקסט',
    materials: 'כותבים את הטקסט',
    'material-polish': 'משפרים את ניסוח הטקסט',
    questions: 'מכינים את השאלות',
  };
  protected readonly stageNames = stageNames;
  protected readonly outcomes: Partial<Record<string, { label: string; icon: string }>> = {
    calling: { label: 'בפנייה לשירות', icon: 'icon-refresh' },
    accepted: { label: 'תוכן התקבל', icon: 'icon-check' },
    failed: { label: 'נכשל', icon: 'icon-alert' },
    conflict: { label: 'לא הוחל בגלל שינוי בטיוטה', icon: 'icon-alert' },
    cancelled: { label: 'בוטל', icon: 'icon-close' },
    unknown: { label: 'תוצאה לא ידועה', icon: 'icon-alert' },
  };
  protected readonly view = computed((): StatusView => {
    const operation = this.operation();
    switch (operation.status) {
      case 'queued':
      case 'calling':
        return {
          title: 'מכינים את הפעילות…',
          details: [this.stages[operation.stage] ?? ''],
          problem: false,
        };
      case 'completed':
        return {
          title: 'הבקשה הושלמה.',
          details: [],
          problem: false,
        };
      case 'failed':
        return this.failure(operation);
      case 'conflict':
        return {
          title: 'הטיוטה השתנתה בזמן היצירה, ולכן התוצאה לא הוחלה.',
          details: ['העבודה שלכם לא הוחלפה.'],
          problem: true,
        };
      case 'cancelled':
        return {
          title: 'הבקשה בוטלה.',
          details: ['התוכן השמור לא השתנה.'],
          problem: false,
        };
      case 'unknown':
        return {
          title: 'לא ידוע אם שירות ה־AI סיים את הבקשה.',
          details: ['לא הפעלנו ניסיון נוסף אוטומטית כדי למנוע חיוב כפול.'],
          problem: true,
          checkSaved: true,
        };
    }
  });
  private failure(operation: GenerationOperation): StatusView {
    const lengths = (operation.artifacts?.steps ?? []).flatMap((step) =>
      Object.keys(step.diagnostics ?? {}).filter((key) => key.startsWith('length.')),
    );
    const required = [...new Set(lengths)].flatMap((key) => {
      const scope = key.slice('length.'.length),
        input = operation.artifacts?.input;
      const length =
        scope === 'total'
          ? input?.totalLength
          : input?.materials.find((m) => m.id === scope)?.length;
      return length ? [`האורך המבוקש: ${lengthText(length)}`] : [];
    });
    return {
      title: lengths.length ? 'הטקסט שנוצר לא התאים לאורך המבוקש.' : 'לא הצלחנו להשלים את הבקשה.',
      details: [...required, 'התוכן השמור לא השתנה.'],
      problem: true,
    };
  }
  protected messages(diagnostics: Record<string, string[]> | null) {
    return Object.values(diagnostics ?? {}).flat();
  }
  protected candidateText(candidate: unknown, output: string | null | undefined) {
    return candidate == null ? (output ?? '') : JSON.stringify(candidate, null, 2);
  }
}
