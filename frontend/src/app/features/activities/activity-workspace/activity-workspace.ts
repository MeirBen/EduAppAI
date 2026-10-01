import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { apply, applyEach, disabled, form, maxLength, validate } from '@angular/forms/signals';
import {
  TaskSettingsDraft,
  taskSettingsDraft,
  taskSettingsSchema,
} from '../../../shared/forms/task-settings';
import { RouterLink } from '@angular/router';
import { Subject } from 'rxjs';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import {
  AuthoringTurn,
  LearningPlan,
  PlanChange,
  PlanTemplateDetail,
} from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import {
  controlForm,
  controlInputValue,
  inputForm,
  InputForm,
  materialForm,
  planForm,
  PlanForm,
  planFormSchema,
  planControls,
  planValue,
  requestValue,
} from '../plan-editor/plan-form';
import { PlanEditor, PlanStructureEdit } from '../plan-editor/plan-editor';
import { TemplateChat } from '../template-chat/template-chat';

/** One editable buffer; canonical plan/request values are derived projections, never a second draft. */
export interface WorkspaceForm {
  plan: PlanForm;
  input: InputForm;
}
interface UndoEntry {
  raw: WorkspaceForm;
  confirmed: Record<string, string>;
}

/** Route owner for local plan/input, chat correlation, source acceptance, bounded Undo and independent publication. */
@Component({
  selector: 'app-activity-workspace',
  imports: [PlanEditor, TemplateChat, LoadingIndicator, RouterLink],
  templateUrl: './activity-workspace.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ActivityWorkspace {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  readonly templateId = input<string>();
  readonly activityId = input<string>();
  readonly context = input<'template' | 'activity'>('activity');
  protected readonly template = this.api.planTemplate(this.templateId);
  protected readonly activity = this.api.activityPlan(this.activityId);
  protected readonly ai = this.api.aiStatus();
  protected readonly raw = signal<WorkspaceForm>({
    plan: planForm(),
    input: {
      settings: planForm().settings,
      questionFormat: '',
      choiceCount: '',
      totalWordCount: '',
      materials: [],
      controls: [],
    },
  });
  protected readonly chat = signal({ message: '', consolidated: '' });
  protected readonly confirmed = signal<Record<string, string>>({});
  protected readonly saving = signal(false);
  protected readonly authoring = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly attempted = signal(false);
  protected readonly clarification = signal('');
  protected readonly conversation = signal<AuthoringTurn[]>([]);
  protected readonly assumptions = signal<string[]>([]);
  protected readonly changes = signal<string[]>([]);
  protected readonly history = signal<UndoEntry[]>([]);
  protected readonly publication = signal<PlanTemplateDetail | undefined>(undefined);
  protected readonly loading = computed(
    () => this.template.isLoading() || this.activity.isLoading(),
  );
  protected readonly loadError = computed(() => this.template.error() || this.activity.error());
  protected readonly apiError = apiError;
  protected readonly projection = computed(() => planValue(this.raw().plan));
  protected readonly inputProjection = computed(() => {
    const plan = this.projection().value;
    return plan ? requestValue(plan, this.raw().input) : { errors: [] };
  });
  protected readonly pendingSources = computed(() =>
    this.raw()
      .plan.materials.filter(
        (material) =>
          material.source === 'fixed' && this.confirmed()[material.id] !== material.text,
      )
      .map((material) => material.id),
  );
  /** Local source/input readiness; it does not assert that activity content has been saved or generated. */
  protected readonly canSubmitActivity = computed(
    () =>
      !!this.projection().value && !!this.inputProjection().value && !this.pendingSources().length,
  );
  protected readonly needsConsolidation = computed(
    () =>
      this.conversation().length > 6 ||
      this.conversation().reduce((sum, turn) => sum + turn.text.length, 0) > 12000,
  );
  protected readonly fields = form(this.raw, (path) => {
    disabled(path, () => this.saving() || this.loading());
    apply(path.plan, planFormSchema);
    apply(path.input.settings, taskSettingsSchema);
    applyEach(path.input.materials, (material) => maxLength(material.sourceText, 4000));
    applyEach(path.input.controls, (control) =>
      validate(control.value, ({ valueOf }) => {
        const plan = this.projection().value;
        const definition =
          plan && planControls(plan).find((item) => item.id === valueOf(control.id));
        return definition
          ? controlInputValue(definition, {
              id: valueOf(control.id),
              provided: valueOf(control.provided),
              value: valueOf(control.value),
            }).errors.map((message) => ({ kind: 'choice', message }))
          : [];
      }),
    );
  });
  protected readonly chatFields = form(this.chat, (path) => {
    disabled(path, () => this.saving() || this.authoring());
    maxLength(path.message, 4000);
    maxLength(path.consolidated, 4000);
  });
  private readonly cancelled = new Subject<void>();
  private activeRequest: string | undefined;
  private clientRevision = 0;
  private initialized = false;
  private coalescingKey = '';
  private previous = this.snapshot();
  private readonly baseline = signal(structuredClone(this.raw()));
  private readonly publishedPlan = signal('');
  protected readonly dirty = computed(
    () => JSON.stringify(this.raw()) !== JSON.stringify(this.baseline()),
  );
  protected readonly templateChanged = computed(
    () => JSON.stringify(this.projection().value) !== this.publishedPlan(),
  );

  constructor() {
    effect(() => {
      const loaded = this.activity.value();
      const template = this.template.value();
      if (!this.initialized && (loaded || template)) {
        const plan = loaded?.plan ?? template!.definition;
        this.raw.set({ plan: planForm(plan), input: inputForm(plan, loaded?.input) });
        this.confirmed.set(
          Object.fromEntries(
            plan.materials
              .filter((material) => material.source === 'fixed')
              .map((material) => [material.id, material.text!]),
          ),
        );
        if (template) {
          this.publication.set(template);
          this.publishedPlan.set(JSON.stringify(planValue(this.raw().plan).value));
        }
        this.baseline.set(structuredClone(this.raw()));
        this.previous = this.snapshot();
        this.initialized = true;
      }
    });
    effect(() => {
      const version = this.ai.value()?.schemaVersion;
      if (version && !this.raw().plan.schemaVersion) {
        this.raw.update((raw) => ({ ...raw, plan: { ...raw.plan, schemaVersion: version } }));
        // Late configuration is not a save acknowledgement for anything typed while it loaded.
        this.baseline.update((raw) => ({ ...raw, plan: { ...raw.plan, schemaVersion: version } }));
        this.previous = {
          ...this.previous,
          raw: {
            ...this.previous.raw,
            plan: { ...this.previous.raw.plan, schemaVersion: version },
          },
        };
      }
    });
    this.lifetime.onDestroy(() => {
      this.activeRequest = undefined;
      this.cancelled.next();
      this.cancelled.complete();
    });
  }

  protected edited(edit: { key: string; sourceId?: string }) {
    if (this.saving()) return;
    if (edit.sourceId) {
      const source = this.raw().plan.materials.find((material) => material.id === edit.sourceId);
      if (source) this.confirmed.update((values) => ({ ...values, [source.id]: source.text }));
    }
    this.recordEdit(edit.key);
  }

  protected changeStructure(edit: PlanStructureEdit) {
    if (this.saving()) return;
    const raw = structuredClone(this.raw());
    if (edit.kind === 'add-material' && raw.plan.materials.length < 4)
      raw.plan.materials.push(materialForm());
    if (edit.kind === 'remove-material')
      raw.plan.materials = raw.plan.materials.filter((material) => material.id !== edit.id);
    if (edit.kind === 'add-control' || edit.kind === 'remove-control') {
      const controls =
        edit.scope === 'plan'
          ? raw.plan.controls
          : edit.scope === 'questions'
            ? raw.plan.questions.controls
            : raw.plan.materials.find((material) => material.id === edit.scope)?.controls;
      if (!controls) return;
      const count =
        raw.plan.controls.length +
        raw.plan.questions.controls.length +
        raw.plan.materials.reduce((sum, material) => sum + material.controls.length, 0);
      if (edit.kind === 'add-control' && count < 16) controls.push(controlForm());
      if (edit.kind === 'remove-control') {
        const index = controls.findIndex((control) => control.id === edit.id);
        if (index >= 0) controls.splice(index, 1);
      }
    }
    this.raw.set(raw);
    this.recordEdit('');
  }

  protected confirmSource(id: string) {
    if (this.saving()) return;
    const source = this.raw().plan.materials.find((material) => material.id === id);
    if (!source) return;
    this.confirmed.update((values) => ({ ...values, [id]: source.text }));
    this.recordEdit('');
  }

  private recordEdit(key: string) {
    this.reconcileInputs();
    const current = this.snapshot();
    if (JSON.stringify(current) === JSON.stringify(this.previous)) return;
    if (!key || key !== this.coalescingKey)
      this.history.update((entries) => [...entries.slice(-19), this.previous]);
    this.coalescingKey = key;
    this.previous = current;
    this.clientRevision++;
    this.cancelAuthor();
    this.conversation.set([]);
    this.clarification.set('');
    this.changes.set([]);
    this.assumptions.set([]);
    this.error.set('');
    this.notice.set('');
  }

  private reconcileInputs() {
    const raw = this.raw(),
      controls = [
        ...raw.plan.controls,
        ...raw.plan.materials.flatMap((material) => material.controls),
        ...raw.plan.questions.controls,
      ];
    const settings = this.followDefaults(
      this.previous.raw.plan.settings,
      raw.plan.settings,
      raw.input.settings,
    );
    this.raw.set({
      ...raw,
      input: {
        ...raw.input,
        settings,
        controls: controls.map(
          (control) =>
            raw.input.controls.find((value) => value.id === control.id) ?? {
              id: control.id,
              provided: false,
              value: '',
            },
        ),
        materials: raw.plan.materials.map(
          (material) =>
            raw.input.materials.find((value) => value.id === material.id) ?? {
              id: material.id,
              wordCount: '',
              sourceText: '',
            },
        ),
      },
    });
  }

  private followDefaults(
    before: TaskSettingsDraft,
    after: TaskSettingsDraft,
    input: TaskSettingsDraft,
  ): TaskSettingsDraft {
    return {
      topic: input.topic === before.topic ? after.topic : input.topic,
      audience: input.audience === before.audience ? after.audience : input.audience,
      difficulty: input.difficulty === before.difficulty ? after.difficulty : input.difficulty,
      questionCount:
        input.questionCount === before.questionCount ? after.questionCount : input.questionCount,
    };
  }

  protected undo() {
    if (this.saving() || !this.history().length) return;
    const entries = this.history();
    const previous = entries[entries.length - 1];
    this.history.set(entries.slice(0, -1));
    this.raw.set(structuredClone(previous.raw));
    this.confirmed.set({ ...previous.confirmed });
    this.previous = this.snapshot();
    this.coalescingKey = '';
    this.clientRevision++;
    this.cancelAuthor();
    this.conversation.set([]);
    this.clarification.set('');
    this.changes.set([]);
    this.assumptions.set([]);
    this.error.set('');
    this.notice.set('השינוי האחרון בוטל מקומית.');
  }

  protected cancelAuthor() {
    this.activeRequest = undefined; // Invalidate identity before unsubscribing from transport.
    this.cancelled.next();
    this.authoring.set(false);
  }

  protected async author(consolidate = false) {
    if (
      this.saving() ||
      this.authoring() ||
      !this.ai.value()?.configured ||
      (!consolidate && this.needsConsolidation())
    )
      return;
    const message = consolidate ? this.chat().consolidated : this.chat().message;
    if (!message.trim() || message.length > 4000) return;
    const baseDefinition = this.projection().value;
    const rawPlan = this.raw().plan;
    if (
      !baseDefinition &&
      JSON.stringify(rawPlan) !==
        JSON.stringify({ ...planForm(), schemaVersion: rawPlan.schemaVersion })
    ) {
      this.attempted.set(true);
      this.error.set('תקנו את פרטי התכנית לפני שליחת בקשה נוספת.');
      return;
    }
    const requestId = crypto.randomUUID(),
      baseRevision = this.clientRevision;
    const basis = JSON.stringify(this.raw()),
      context = consolidate ? [] : this.conversation();
    this.activeRequest = requestId;
    this.authoring.set(true);
    this.error.set('');
    try {
      const reply = await this.api.authorPlan(
        { message, baseDefinition, context, requestId, baseRevision },
        this.cancelled,
        this.lifetime,
      );
      if (
        this.lifetime.destroyed ||
        this.activeRequest !== requestId ||
        reply.requestId !== requestId ||
        reply.baseRevision !== baseRevision ||
        this.clientRevision !== baseRevision ||
        JSON.stringify(this.raw()) !== basis
      )
        return;
      this.assumptions.set(reply.assumptions);
      if (reply.proposal) {
        this.conversation.set([]);
        this.clarification.set('');
        this.changes.set(
          reply.changes.map((change) => this.changeLabel(change, baseDefinition, reply.proposal!)),
        );
        if (reply.changes.length) this.applyProposal(reply.proposal, !!baseDefinition);
        else this.notice.set('התכנית כבר תואמת לבקשה.');
      } else if (reply.clarification) {
        this.conversation.set([
          ...context,
          { role: 'parent', text: message },
          { role: 'assistant', text: reply.clarification },
        ]);
        this.clarification.set(reply.clarification);
      }
      this.chat.set({ message: '', consolidated: '' });
    } catch (error) {
      if (!this.lifetime.destroyed && this.activeRequest === requestId)
        this.error.set(apiError(error));
    } finally {
      if (this.activeRequest === requestId) {
        this.activeRequest = undefined;
        this.authoring.set(false);
      }
    }
  }

  private applyProposal(plan: LearningPlan, hadPlan: boolean) {
    this.history.update((entries) => [...entries.slice(-19), this.snapshot()]);
    const nextInput = inputForm(plan),
      priorInput = this.raw().input;
    if (hadPlan) {
      // Keep raw input values (including invalid typing) by identity; HTTP mapping omits inapplicable overrides.
      Object.assign(nextInput, {
        ...priorInput,
        settings: this.followDefaults(
          this.raw().plan.settings,
          taskSettingsDraft(plan.defaults),
          priorInput.settings,
        ),
        materials: nextInput.materials.map(
          (item) => priorInput.materials.find((old) => old.id === item.id) ?? item,
        ),
        controls: nextInput.controls.map(
          (item) => priorInput.controls.find((old) => old.id === item.id) ?? item,
        ),
      });
    }
    this.raw.set({ plan: planForm(plan), input: nextInput });
    const confirmed = this.confirmed();
    this.confirmed.set(
      Object.fromEntries(
        plan.materials
          .filter(
            (material) => material.source === 'fixed' && confirmed[material.id] === material.text,
          )
          .map((material) => [material.id, material.text!]),
      ),
    );
    this.clientRevision++;
    this.coalescingKey = '';
    this.previous = this.snapshot();
    this.notice.set('התכנית עודכנה מקומית. אפשר לערוך או לבטל את השינוי.');
  }

  protected async saveTemplate() {
    if (this.saving() || !this.templateChanged()) return;
    this.attempted.set(true);
    this.error.set('');
    const plan = this.projection().value;
    if (!plan || this.pendingSources().length) {
      this.error.set('יש להשלים תכנית תקינה ולאשר את המקורות לפני שמירה.');
      return;
    }
    this.cancelAuthor();
    this.saving.set(true);
    try {
      const saved = await this.api.savePlanTemplate(plan, this.publication(), this.lifetime);
      if (this.lifetime.destroyed) return;
      this.publication.set(saved);
      this.publishedPlan.set(JSON.stringify(plan));
      this.coalescingKey = '';
      // Template publication cannot claim that activity edits or per-task input were saved.
      if (!this.activityId())
        this.baseline.update((raw) => ({ ...raw, plan: structuredClone(this.raw().plan) }));
      this.notice.set('התבנית נשמרה בספרייה. הפעילות לא השתנתה.');
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.error.set(
          error instanceof HttpErrorResponse && error.status === 409
            ? 'התבנית השתנתה בינתיים. העריכה המקומית נשמרת כאן; בדקו את הגרסה בספרייה.'
            : `${apiError(error)} לא ידוע אם התבנית נשמרה. בדקו בספרייה לפני ניסיון נוסף.`,
        );
    } finally {
      if (!this.lifetime.destroyed) this.saving.set(false);
    }
  }

  private snapshot(): UndoEntry {
    return { raw: structuredClone(this.raw()), confirmed: { ...this.confirmed() } };
  }
  private changeLabel(
    change: PlanChange,
    before: LearningPlan | undefined,
    after: LearningPlan,
  ): string {
    if (change.path === 'plan') return 'נוספה תכנית';
    const labels: Record<string, string> = {
      name: 'שם התבנית',
      goal: 'מטרת הפעילות',
      guidance: 'ההנחיות',
      defaults: 'הגדרות ברירת המחדל',
      totalLength: 'האורך הכולל',
      questions: 'הגדרות השאלות',
    };
    const plan = change.kind === 'removed' ? before : after;
    const objects = plan
      ? [
          ...plan.materials,
          ...plan.controls,
          ...plan.questions.controls,
          ...plan.materials.flatMap((material) => material.controls),
        ]
      : [];
    const label =
      objects.find((item) => item.id === change.id)?.label ?? labels[change.path] ?? 'פרטי התכנית';
    return `${{ added: 'נוסף', removed: 'הוסר', moved: 'הועבר', changed: 'עודכן' }[change.kind]}: ${label}`;
  }
  /** Native route guard and browser-close warning protect local-only keystrokes. */
  canLeave() {
    return !this.dirty() || window.confirm('יש שינויים שלא נשמרו. לצאת מהעמוד?');
  }
  protected beforeUnload(event: BeforeUnloadEvent) {
    if (this.dirty()) event.preventDefault();
  }
}
