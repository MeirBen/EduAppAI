import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { GenerationKind, GenerationOperation } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { lengthText } from '../activity-document-view/measurements';
import { isRunning, stageNames } from './operation-state';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { CopyButton } from '../../../shared/copy-button/copy-button';

type Scope = 'text' | 'questions' | 'material' | 'question';
const scopes: Partial<Record<GenerationKind, Scope>> = {
  GenerateQuestions: 'questions',
  ReplaceMaterial: 'material',
  ReplaceQuestion: 'question',
};
const runningTitles: Record<Scope, string> = {
  text: 'יוצרים את הטקסט…',
  questions: 'יוצרים את השאלות…',
  material: 'משפרים את הטקסט…',
  question: 'משפרים את השאלה…',
};
const completedTitles: Record<Scope, string> = {
  text: 'הטקסט נוצר.',
  questions: 'השאלות נוצרו.',
  material: 'הטקסט עודכן.',
  question: 'השאלה עודכנה.',
};

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
  imports: [DisabledInteractive, LoadingIndicator, CopyButton],
  templateUrl: './generation-status.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GenerationStatus {
  readonly operation = input.required<GenerationOperation>();
  /** Shown inside the card of the material or question it changes, under that card's heading. */
  readonly inCard = input(false);
  readonly locked = input(false);
  /** Whether AI is configured; a retry is offered only when it could start. */
  readonly configured = input(false);
  readonly cancelled = output<void>();
  readonly retried = output<GenerationKind>();
  readonly checked = output<void>();
  protected readonly active = computed(() => isRunning(this.operation()));
  protected readonly stages: Record<string, string> = {
    'material-ideas': 'בוחרים רעיון לטקסט',
    materials: 'כותבים את הטקסט',
    'material-polish': 'משפרים את ניסוח הטקסט',
    questions: 'מכינים את השאלות',
    'replace-material': 'כותבים גרסה חדשה לטקסט',
    'replace-question': 'מכינים גרסה חדשה לשאלה',
  };
  protected readonly stageNames = stageNames;
  protected readonly outcomes: Partial<Record<string, { label: string; icon: string }>> = {
    calling: { label: 'בפנייה לשירות', icon: 'icon-refresh' },
    accepted: { label: 'תוכן התקבל', icon: 'icon-check' },
    applied: { label: 'תוכן התקבל', icon: 'icon-check' },
    completed: { label: 'הושלם', icon: 'icon-check' },
    failed: { label: 'נכשל', icon: 'icon-alert' },
    conflict: { label: 'לא הוחל בגלל שינוי בטיוטה', icon: 'icon-alert' },
    cancelled: { label: 'בוטל', icon: 'icon-close' },
    unknown: { label: 'תוצאה לא ידועה', icon: 'icon-alert' },
  };
  protected readonly view = computed((): StatusView => {
    const operation = this.operation();
    const scope = scopes[operation.kind] ?? 'text';
    switch (operation.status) {
      case 'queued':
      case 'calling':
        return {
          title: runningTitles[scope],
          details: [this.stages[operation.stage] ?? ''],
          problem: false,
        };
      case 'completed':
        return {
          title: completedTitles[scope],
          // The review panel below already asks for a general review; text asks for its own check.
          details:
            scope === 'text'
              ? ['בדקו את הטקסט ותקנו אותו לפי הצורך, ואז צרו את השאלות.']
              : scope === 'material'
                ? ['בדקו את הטקסט ואת השאלות שתלויות בו לפני סימון כמוכנה.']
                : [],
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
  private failure(operation: GenerationOperation, scope: Scope): StatusView {
    // A failed polish leaves the text its writing stage saved, so no retry is offered.
    if (operation.stage === 'material-polish')
      return {
        title: 'הטקסט נשמר, אבל שיפור הניסוח לא הושלם.',
        details: ['בדקו את הטקסט בעצמכם לפני שיוצרים את השאלות.'],
        problem: true,
      };
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
        retry: scope === 'text' ? { kind: 'GenerateMaterials', label: 'ניסיון נוסף' } : undefined,
      };
    }
    if (scope === 'material' || scope === 'question')
      return {
        title: 'השיפור לא הצליח.',
        details: [unchanged, 'אפשר לנסות שוב מהחלק עצמו.'],
        problem: true,
      };
    return scope === 'questions'
      ? {
          title: 'יצירת השאלות נכשלה.',
          details: [unchanged],
          problem: true,
          retry: { kind: 'GenerateQuestions', label: 'ניסיון נוסף' },
        }
      : {
          title: 'יצירת הטקסט נכשלה.',
          details: [unchanged],
          problem: true,
          retry: { kind: 'GenerateMaterials', label: 'ניסיון נוסף' },
        };
  }
  protected messages(diagnostics: Record<string, string[]> | null) {
    return Object.values(diagnostics ?? {}).flat();
  }
  protected candidateText(candidate: unknown, output: string | null | undefined) {
    return candidate == null ? (output ?? '') : JSON.stringify(candidate, null, 2);
  }
}
