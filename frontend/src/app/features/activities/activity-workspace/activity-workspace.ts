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
import {
  apply,
  applyEach,
  disabled,
  form,
  FormField,
  maxLength,
  validate,
} from '@angular/forms/signals';
import {
  TaskSettingsDraft,
  taskSettingsDraft,
  taskSettingsSchema,
} from '../../../shared/forms/task-settings';
import { measurementText } from '../activity-document-view/measurements';
import { candidateEdit } from '../generation-status/candidate-edit';
import { GenerationStatus } from '../generation-status/generation-status';
import { ActivityDocumentView } from '../activity-document-view/activity-document-view';
import { Location } from '@angular/common';
import { timer, exhaustMap, switchMap, map, takeWhile } from 'rxjs';
import { requestResult } from '../../../core/api/request-result';
import {
  ActivityDocumentEditor,
  DocumentEdit,
} from '../activity-document-editor/activity-document-editor';
import {
  DocumentForm,
  documentForm,
  documentValue,
  documentSchema,
} from '../activity-document-editor/document-form';
import { RouterLink } from '@angular/router';
import { Subject } from 'rxjs';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import {
  AuthoringTurn,
  LearningPlan,
  PlanChange,
  PlanTemplateDetail,
  PlanMaterial,
  ActivityDetail,
  GenerationKind,
  GenerationOperation,
  StartGeneration,
} from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import {
  controlForm,
  controlInputValue,
  inputForm,
  InputForm,
  materialForm,
  newPlanId,
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
  document: DocumentForm;
}
interface UndoEntry {
  raw: WorkspaceForm;
  confirmed: Record<string, string>;
}

/** Route owner for local plan/input, chat correlation, source acceptance, bounded Undo and independent publication. */
@Component({
  selector: 'app-activity-workspace',
  imports: [
    PlanEditor,
    TemplateChat,
    LoadingIndicator,
    RouterLink,
    ActivityDocumentEditor,
    FormField,
    GenerationStatus,
    ActivityDocumentView,
  ],
  templateUrl: './activity-workspace.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ActivityWorkspace {
  private readonly api = inject(LearningApi);
  private readonly location = inject(Location);
  private readonly lifetime = inject(DestroyRef);
  readonly templateId = input<string>();
  readonly activityId = input<string>();
  readonly context = input<'template' | 'activity'>('activity');
  protected readonly template = this.api.planTemplate(this.templateId);
  protected readonly activity = this.api.activity(this.activityId);
  protected readonly ai = this.api.aiStatus();
  protected readonly raw = signal<WorkspaceForm>({
    plan: planForm(),
    document: documentForm(),
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
  protected readonly saved = signal<ActivityDetail | undefined>(undefined);
  protected readonly available = signal<ActivityDetail | undefined>(undefined);
  protected readonly operation = signal<GenerationOperation | undefined>(undefined);
  protected readonly operationId = signal<string | undefined>(undefined);
  protected readonly operationActive = computed(
    () =>
      !!this.operationId() &&
      (!this.operation() || ['queued', 'calling'].includes(this.operation()!.status)),
  );
  protected readonly startRecovery = signal<
    { draftId: string; request: StartGeneration } | undefined
  >(undefined);
  protected readonly activityError = signal('');
  protected readonly documentProjection = computed(() => documentValue(this.raw().document));
  readonly resumeOperation = input<string | undefined>(undefined, { alias: 'operation' });
  private readonly pollRefresh = signal(0);
  protected readonly measurements = computed(() =>
    measurementText(this.saved()?.measurements ?? [], this.saved()?.plan),
  );
  protected readonly diagnostics = computed(() =>
    Object.values(this.saved()?.diagnostics ?? {}).flat(),
  );
  protected readonly released = computed(() => !!this.saved()?.releasedSnapshotId);
  protected readonly sourceReplacement = signal({ id: '', text: '' });
  protected readonly sourceFields = form(this.sourceReplacement, (p) => maxLength(p.text, 4000));
  private operationClientRevision = 0;
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
    disabled(path, () => this.saving() || this.loading() || this.released());
    apply(path.document, documentSchema);
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
    disabled(path, () => this.saving() || this.authoring() || this.released());
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
    () =>
      JSON.stringify(this.raw()) !== JSON.stringify(this.baseline()) ||
      !!this.sourceReplacement().id,
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
        this.raw.set({
          plan: planForm(plan),
          input: inputForm(plan, loaded?.input),
          document: documentForm(loaded?.document),
        });
        if (loaded) {
          this.saved.set(loaded);
          this.operationId.set(loaded.activeOperationId ?? this.resumeOperation());
          this.operationClientRevision = this.clientRevision;
        }
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
    const savedId = computed(() => this.saved()?.id);
    effect((cleanup) => {
      this.pollRefresh();
      const id = savedId(),
        operationId = this.operationId();
      if (!id || !operationId) return;
      // Polling is read-only, never overlaps itself, and is disposed with the route/operation.
      const subscription = timer(2000, 2000)
        .pipe(
          exhaustMap(() =>
            this.api.operation(id, operationId).pipe(
              // Read the checkpoint after status so a terminal result cannot hide its final commit.
              switchMap((operation) =>
                this.api.readActivity(id).pipe(map((draft) => ({ operation, draft }))),
              ),
            ),
          ),
          takeWhile((result) => ['queued', 'calling'].includes(result.operation.status), true),
        )
        .subscribe({
          next: (result) => {
            this.operation.set(result.operation);
            this.receiveCheckpoint(result.draft);
          },
          error: (error) => this.activityError.set(this.activityFailure(error)),
        });
      cleanup(() => subscription.unsubscribe());
    });
    this.lifetime.onDestroy(() => {
      this.activeRequest = undefined;
      this.cancelled.next();
      this.cancelled.complete();
    });
  }

  protected edited(edit: { key: string; sourceId?: string }) {
    if (this.saving() || this.released()) return;
    if (edit.sourceId) {
      const source = this.raw().plan.materials.find((material) => material.id === edit.sourceId);
      if (source) this.confirmed.update((values) => ({ ...values, [source.id]: source.text }));
    }
    this.recordEdit(edit.key);
  }

  protected changeStructure(edit: PlanStructureEdit) {
    if (this.saving() || this.released()) return;
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
    if (this.saving() || this.released()) return;
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
    const current = this.raw();
    const raw = {
      ...current,
      plan: {
        ...current.plan,
        materials: current.plan.materials.map((material) => ({
          ...material,
          id: this.sourceId(material.id, material.source),
        })),
      },
    };
    const controls = [
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
      document: {
        ...raw.document,
        materials: raw.document.materials.filter((m) =>
          raw.plan.materials.some((p) => p.id === m.id),
        ),
      },
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

  private sourceId(id: string, source: PlanMaterial['source']): string {
    const existing = this.saved()?.plan.materials.find((material) => material.id === id);
    // A source-kind change is a new material, not a rewrite of the persisted material's identity.
    return existing && existing.source !== source ? newPlanId() : id;
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

  protected async undo() {
    if (this.saving() || this.released() || !this.history().length) return;
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
    // IDs removed by a previous save cannot be replayed; restored questions receive new server IDs.
    const ids = new Set(this.saved()?.document.questions.map((q) => q.id) ?? []);
    if (this.saved())
      this.raw.update((raw) => ({
        ...raw,
        document: {
          ...raw.document,
          questions: raw.document.questions.map((q) => ({ ...q, id: ids.has(q.id) ? q.id : '' })),
        },
      }));
    this.notice.set('השינוי האחרון בוטל מקומית.');
    if (this.saved()) await this.activityAction('save');
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
      this.released() ||
      !!this.sourceReplacement().id ||
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
    plan = {
      ...plan,
      materials: plan.materials.map((material) => ({
        ...material,
        id: this.sourceId(material.id, material.source),
      })),
    };
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
    this.raw.set({
      ...this.raw(),
      plan: planForm(plan),
      input: nextInput,
      document: {
        ...this.raw().document,
        materials: this.raw().document.materials.filter((material) =>
          plan.materials.some((item) => item.id === material.id),
        ),
      },
    });
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
    if (!plan || this.pendingSources().length || this.sourceReplacement().id) {
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
      if (!this.saved())
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

  protected editDocument(edit: DocumentEdit) {
    if (this.saving() || this.released()) return;
    const raw = structuredClone(this.raw()),
      doc = raw.document;
    if (edit.kind === 'add-question' && doc.questions.length < 3999)
      doc.questions.push({
        id: '',
        key: crypto.randomUUID(),
        prompt: '',
        type: 'numeric-input',
        options: [],
        answer: '',
        points: '1',
      });
    else if (edit.kind === 'add-material' && !doc.materials.some((m) => m.id === edit.id))
      doc.materials.push({ id: edit.id, title: '', body: '' });
    else if ('index' in edit) {
      const question = doc.questions[edit.index];
      if (!question) return;
      if (edit.kind === 'remove-question') doc.questions.splice(edit.index, 1);
      if (edit.kind === 'add-option' && question.options.length < 6)
        question.options.push({ value: '' });
      if (edit.kind === 'remove-option') question.options.splice(edit.option, 1);
      const next =
        edit.kind === 'move-up' ? edit.index - 1 : edit.kind === 'move-down' ? edit.index + 1 : -1;
      if (next >= 0 && next < doc.questions.length)
        [doc.questions[edit.index], doc.questions[next]] = [doc.questions[next], question];
    }
    this.raw.set(raw);
    this.recordEdit('');
  }

  protected replaceSource(id: string) {
    const material = this.raw().plan.materials.find((m) => m.id === id);
    if (!material || material.source === 'generated') return;
    const text =
      material.source === 'fixed'
        ? material.text
        : (this.raw().input.materials.find((m) => m.id === id)?.sourceText ?? '');
    this.sourceReplacement.set({ id, text });
  }
  protected sourceTyping() {
    this.clientRevision++;
    this.cancelAuthor();
    this.conversation.set([]);
    this.clarification.set('');
  }
  protected acceptSourceReplacement() {
    const { id, text } = this.sourceReplacement();
    if (!text.trim() || text.length > 4000 || this.saving()) return;
    const raw = structuredClone(this.raw()),
      material = raw.plan.materials.find((m) => m.id === id);
    if (!material) return;
    if (material.source === 'fixed') material.text = text;
    else {
      const input = raw.input.materials.find((m) => m.id === id);
      if (input) input.sourceText = text;
    }
    const content = raw.document.materials.find((m) => m.id === id);
    if (content) content.body = text;
    this.raw.set(raw);
    this.confirmed.update((c) => ({ ...c, [id]: text }));
    this.recordEdit('');
    this.sourceReplacement.set({ id: '', text: '' });
  }

  /** Every content action flushes one validated checkpoint first; no failed save can start work. */
  protected async activityAction(
    action: 'save' | 'release' | 'adopt' | GenerationKind,
    target?: { targetId?: string; materialIds?: string[]; questionIds?: string[] },
  ) {
    if (
      this.saving() ||
      this.released() ||
      (action !== 'save' && this.operationActive()) ||
      (action !== 'save' && this.startRecovery())
    )
      return;
    if (
      action !== 'save' &&
      action !== 'release' &&
      action !== 'adopt' &&
      !this.ai.value()?.configured
    )
      return;
    this.saving.set(true);
    this.cancelAuthor();
    this.activityError.set('');
    try {
      const saved = await this.flushDraft();
      if (!saved || this.lifetime.destroyed || action === 'save') return;
      if (action === 'adopt') {
        this.acceptCheckpoint(
          await this.api.adoptActivity(
            saved.id,
            saved.revision,
            target?.materialIds ?? [],
            target?.questionIds ?? [],
            this.lifetime,
          ),
        );
      } else if (action === 'release') {
        const snapshot = await this.api.releaseActivity(saved.id, saved.revision, this.lifetime);
        if (!this.lifetime.destroyed)
          this.saved.set({
            ...saved,
            releasedSnapshotId: snapshot.id,
            releasedSourceRevision: saved.revision,
          });
      } else {
        this.operationClientRevision = this.clientRevision;
        const request: StartGeneration = {
          operationKey: crypto.randomUUID(),
          expectedRevision: saved.revision,
          kind: action,
          ...(target?.targetId ? { targetId: target.targetId } : {}),
        };
        this.startRecovery.set({ draftId: saved.id, request });
        await this.submitOperation(saved.id, request);
      }
    } catch (error) {
      if (!this.lifetime.destroyed) this.activityError.set(this.activityFailure(error));
    } finally {
      if (!this.lifetime.destroyed) this.saving.set(false);
    }
  }

  private async flushDraft(): Promise<ActivityDetail | undefined> {
    this.attempted.set(true);
    const plan = this.projection().value,
      input = this.inputProjection().value,
      document = this.documentProjection().value;
    if (
      !plan ||
      !input ||
      !document ||
      this.pendingSources().length ||
      this.sourceReplacement().id
    ) {
      this.activityError.set('תקנו את השדות ואשרו את המקורות לפני שמירת הפעילות.');
      return;
    }
    let saved = this.saved();
    if (!saved) {
      saved = await this.api.createActivity(plan, input, this.template.value(), this.lifetime);
      if (this.lifetime.destroyed) return;
      this.saved.set(saved);
      // Keep this workspace and its pending action alive while making reload reopen the durable draft.
      this.location.replaceState('/activities/' + saved.id);
      if (JSON.stringify(this.raw().document) === JSON.stringify(documentForm())) {
        this.acceptCheckpoint(saved);
        return saved;
      }
    } else if (!this.dirty()) return saved;
    saved = await this.api.saveActivity(
      saved.id,
      saved.revision,
      plan,
      input,
      document,
      this.lifetime,
    );
    if (!this.lifetime.destroyed) this.acceptCheckpoint(saved);
    return saved;
  }

  private acceptCheckpoint(saved: ActivityDetail) {
    if (this.lifetime.destroyed) return;
    this.saved.set(saved);
    this.raw.set({
      plan: planForm(saved.plan),
      input: inputForm(saved.plan, saved.input),
      document: documentForm(saved.document),
    });
    this.confirmed.set(
      Object.fromEntries(
        saved.plan.materials.filter((m) => m.source === 'fixed').map((m) => [m.id, m.text!]),
      ),
    );
    this.baseline.set(structuredClone(this.raw()));
    this.previous = this.snapshot();
    this.coalescingKey = '';
    this.available.set(undefined);
  }
  private receiveCheckpoint(saved: ActivityDetail) {
    if (saved.revision <= (this.saved()?.revision ?? 0)) return;
    if (!this.dirty() && !this.saving() && this.clientRevision === this.operationClientRevision) {
      this.history.update((entries) => [...entries.slice(-19), this.snapshot()]);
      this.clientRevision++;
      this.operationClientRevision = this.clientRevision;
      this.acceptCheckpoint(saved);
    } else this.available.set(saved);
  }
  protected async reloadActivity() {
    const saved = this.saved();
    if (!saved || this.saving()) return;
    if (this.dirty() && !window.confirm('טעינת המצב השמור תחליף את העריכה המקומית. להמשיך?'))
      return;
    this.saving.set(true);
    try {
      const latest = await requestResult(this.api.readActivity(saved.id), this.lifetime);
      this.cancelAuthor();
      this.clientRevision++;
      this.operationClientRevision = this.clientRevision;
      this.sourceReplacement.set({ id: '', text: '' });
      this.acceptCheckpoint(latest);
      this.history.set([]);
      this.conversation.set([]);
      this.clarification.set('');
      this.operationId.set(latest.activeOperationId ?? this.operationId());
      this.pollRefresh.update((value) => value + 1);
      this.activityError.set('');
    } catch (error) {
      if (!this.lifetime.destroyed) this.activityError.set(this.activityFailure(error));
    } finally {
      if (!this.lifetime.destroyed) this.saving.set(false);
    }
  }
  private async submitOperation(id: string, request: StartGeneration) {
    try {
      const operation = await this.api.startGeneration(id, request, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.operation.set(operation);
      this.operationId.set(operation.id);
      this.location.replaceState(
        '/activities/' + id,
        'operation=' + encodeURIComponent(operation.id),
      );
      this.startRecovery.set(undefined);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500)
        this.startRecovery.set(undefined);
      throw error;
    }
  }
  protected async recoverStart() {
    const pending = this.startRecovery();
    if (!pending || this.saving()) return;
    this.saving.set(true);
    try {
      await this.submitOperation(pending.draftId, pending.request);
      this.activityError.set('');
    } catch (error) {
      if (!this.lifetime.destroyed) this.activityError.set(this.activityFailure(error));
    } finally {
      if (!this.lifetime.destroyed) this.saving.set(false);
    }
  }
  protected async cancelGeneration() {
    const id = this.saved()?.id,
      operationId = this.operationId();
    if (!id || !operationId || this.saving()) return;
    this.saving.set(true);
    try {
      const result = await this.api.cancelGeneration(id, operationId, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.operation.set(result);
      const saved = await requestResult(this.api.readActivity(id), this.lifetime);
      if (!this.dirty()) this.acceptCheckpoint(saved);
      else this.receiveCheckpoint(saved);
    } catch (error) {
      if (!this.lifetime.destroyed) this.activityError.set(this.activityFailure(error));
    } finally {
      if (!this.lifetime.destroyed) this.saving.set(false);
    }
  }
  protected readonly editableCandidates = computed(() => {
    const plan = this.projection().value,
      operation = this.operation();
    if (!plan || !operation || this.operationActive() || this.released()) return [];
    return (operation.artifacts?.steps ?? []).flatMap((step, index) => {
      const document = candidateEdit(
        step.stage,
        step.candidate ?? step.call?.output,
        operation.artifacts?.targetId ?? null,
        this.raw().document,
        plan,
      );
      return document ? [{ index, document }] : [];
    });
  });
  protected editCandidate(index: number) {
    if (this.saving()) return;
    const candidate = this.editableCandidates().find((c) => c.index === index);
    if (!candidate) return;
    this.raw.update((raw) => ({ ...raw, document: candidate.document }));
    this.recordEdit('');
    this.notice.set('הצעת התוכן הועתקה לעריכה מקומית בלבד. יש לבדוק ולשמור אותה.');
  }

  protected async newActivity() {
    if (this.saving()) return;
    const plan = this.projection().value,
      input = this.inputProjection().value;
    if (!plan || !input || this.pendingSources().length) {
      this.activityError.set('יש להשלים תכנית, בחירות ואישור מקורות.');
      return;
    }
    if (
      this.dirty() &&
      !window.confirm('פעילות חדשה תיפתח ללא התוכן והשינויים המקומיים בפעילות הזאת. להמשיך?')
    )
      return;
    this.saving.set(true);
    try {
      const created = await this.api.createActivity(plan, input, undefined, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.cancelAuthor();
      this.clientRevision++;
      this.history.set([]);
      this.conversation.set([]);
      this.clarification.set('');
      this.operationId.set(undefined);
      this.operation.set(undefined);
      this.startRecovery.set(undefined);
      this.sourceReplacement.set({ id: '', text: '' });
      this.acceptCheckpoint(created);
      this.location.replaceState('/activities/' + created.id);
    } catch (error) {
      if (!this.lifetime.destroyed) this.activityError.set(this.activityFailure(error));
    } finally {
      if (!this.lifetime.destroyed) this.saving.set(false);
    }
  }

  private activityFailure(error: unknown): string {
    return error instanceof HttpErrorResponse && error.status === 409
      ? 'הטיוטה השתנתה בשרת. העריכה המקומית נשמרת כאן; טענו את המצב השמור לפני המשך.'
      : apiError(error) +
          ' העריכה נשארת כאן. ייתכן שהבקשה נשמרה; בדקו את המצב השמור לפני ניסיון נוסף.';
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
