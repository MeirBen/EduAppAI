import { Location, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { apply, applyEach, disabled, form, maxLength, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { Subject } from 'rxjs';
import { apiError } from '../../../core/api/api-error';
import { LearningApi } from '../../../core/api/learning-api';
import { Limits } from '../../../core/api/limits';
import {
  ActivityDetail,
  AuthoringTurn,
  GenerationKind,
  GenerationOperation,
  LearningPlan,
  PlanTemplateDetail,
  StartGeneration,
} from '../../../core/api/models';
import { requestResult } from '../../../core/api/request-result';
import { focusHolder } from '../../../shared/focus-holder';
import { taskSettingsSchema } from '../../../shared/forms/task-settings';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import {
  ActivityDocumentEditor,
  DocumentEdit,
} from '../activity-document-editor/activity-document-editor';
import {
  documentForm,
  documentSchema,
  documentValue,
} from '../activity-document-editor/document-form';
import { ActivityDocumentView } from '../activity-document-view/activity-document-view';
import { measurementItems } from '../activity-document-view/measurements';
import {
  activitySummary,
  planChangeLabel,
  reviewIssues,
  staleContent,
} from '../activity-presentation';
import { ActivityReview } from '../activity-review/activity-review';
import { ActivitySetup } from '../activity-setup/activity-setup';
import { unappliedCandidates } from '../generation-status/candidate-edit';
import { GenerationStatus } from '../generation-status/generation-status';
import { isRunning } from '../generation-status/operation-state';
import { UnappliedResult } from '../generation-status/unapplied-result/unapplied-result';
import { PlanEditor, PlanStructureEdit } from '../plan-editor/plan-editor';
import { planControls, planFormSchema } from '../plan-editor/plan-form';
import { controlInputValue, planValue, requestValue } from '../plan-editor/plan-projection';
import { SourceReplacement } from '../source-replacement/source-replacement';
import { TemplateChat } from '../template-chat/template-chat';
import { draftElsewhere } from './draft-elsewhere';
import { pollOperation } from './operation-polling';
import { UndoHistory } from './undo-history';
import {
  ConfirmedSources,
  editDocumentStructure,
  editPlanStructure,
  emptyWorkspace,
  fixedSources,
  hasPlanContent,
  materialIdentity,
  proposedWorkspace,
  reconcile,
  replaceSourceText,
  sourceText,
  withSavedQuestionIds,
  withSchemaVersion,
  WorkspaceForm,
  workspaceForm,
  WorkspaceSnapshot,
} from './workspace-form';
import { DisabledInteractive } from '../../../shared/disabled-interactive';

/**
 * Route owner for one template or activity: the editable buffer, chat correlation, source
 * confirmation, bounded Undo, independent template publication, draft checkpoints and generation.
 * Buffer transitions live in `workspace-form`; presentation phases (describe, set up, review) are
 * derived from this state, never stored.
 */
@Component({
  selector: 'app-activity-workspace',
  imports: [
    DisabledInteractive,
    PlanEditor,
    TemplateChat,
    LoadingIndicator,
    RouterLink,
    ActivityDocumentEditor,
    ActivitySetup,
    ActivityReview,
    GenerationStatus,
    ActivityDocumentView,
    NgTemplateOutlet,
    SourceReplacement,
    UnappliedResult,
  ],
  templateUrl: './activity-workspace.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ActivityWorkspace {
  private readonly api = inject(LearningApi);
  private readonly location = inject(Location);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  /** Progress of the current operation; full generation brings it into view once. */
  private readonly progress = viewChild(GenerationStatus, { read: ElementRef });
  private readonly holdFocus = focusHolder();
  private readonly router = inject(Router);
  private readonly lifetime = inject(DestroyRef);
  protected readonly limits = inject(Limits).current;
  protected readonly apiError = apiError;

  readonly templateId = input<string>();
  readonly activityId = input<string>();
  readonly context = input<'template' | 'activity'>('activity');
  /** Operation named in the URL; shown after a reload when the draft has no active operation. */
  readonly resumeOperation = input<string | undefined>(undefined, { alias: 'operation' });

  protected readonly template = this.api.planTemplate(this.templateId);
  protected readonly activity = this.api.activity(this.activityId);
  protected readonly ai = this.api.aiStatus();
  protected readonly loading = computed(
    () => this.template.isLoading() || this.activity.isLoading(),
  );
  protected readonly loadError = computed(() => this.template.error() || this.activity.error());

  protected readonly raw = signal<WorkspaceForm>(emptyWorkspace());
  protected readonly confirmed = signal<ConfirmedSources>({});
  protected readonly history = new UndoHistory<WorkspaceSnapshot>(() => this.snapshot());
  /** The buffer as last loaded or saved; any difference is an unsaved local edit. */
  private readonly baseline = signal(structuredClone(this.raw()));
  /** The current template version's canonical plan, serialized to detect unpublished changes. */
  private readonly publishedPlan = signal('');
  protected readonly sourceReplacement = signal({ id: '', text: '' });
  /** Counts local changes; authoring replies and operation results apply only to the revision they started from. */
  private clientRevision = 0;
  private initialized = false;

  protected readonly saving = signal(false);
  protected readonly publishing = signal(false);
  protected readonly copying = signal(false);
  protected readonly attempted = signal(false);
  /** Template publication failure; authoring failures use their own channel beside the request. */
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly templateNotice = signal('');
  protected readonly activityError = signal('');
  protected readonly publication = signal<PlanTemplateDetail | undefined>(undefined);
  protected readonly saved = signal<ActivityDetail | undefined>(undefined);
  /** A newer server checkpoint held back because applying it would replace local edits. */
  protected readonly available = signal<ActivityDetail | undefined>(undefined);

  protected readonly chat = signal({ message: '', consolidated: '' });
  /** The request in flight; its text shows in the thread and returns to the composer unless answered. */
  private readonly request = signal<{ id: string; text: string; consolidate: boolean } | undefined>(
    undefined,
  );
  protected readonly authoring = computed(() => !!this.request());
  protected readonly pendingMessage = computed(() => this.request()?.text ?? '');
  /** The conversation behind the latest answer, shown until a local edit makes it stale. */
  private readonly answered = signal<AuthoringTurn[]>([]);
  protected readonly authorError = signal('');
  protected readonly clarification = signal('');
  protected readonly conversation = signal<AuthoringTurn[]>([]);
  protected readonly assumptions = signal<string[]>([]);
  protected readonly changes = signal<string[]>([]);
  private readonly cancelled = new Subject<void>();

  protected readonly operation = signal<GenerationOperation | undefined>(undefined);
  protected readonly operationId = signal<string | undefined>(undefined);
  /** A start whose response was lost; checking it replays the same key and request. */
  protected readonly startRecovery = signal<
    { draftId: string; request: StartGeneration } | undefined
  >(undefined);
  /** The client revision the current operation's results may replace; later edits fence them. */
  private operationClientRevision = 0;
  private readonly pollRefresh = signal(0);

  protected readonly released = computed(() => !!this.saved()?.releasedSnapshotId);
  /** Edits wait for any running save and stop for good once the activity is released. */
  protected readonly locked = computed(() => this.saving() || this.released());
  protected readonly aiConfigured = computed(() => this.ai.value()?.configured ?? false);
  /** Content actions wait for a running save or operation and for any unresolved start. */
  protected readonly contentBusy = computed(
    () => this.saving() || this.operationActive() || !!this.startRecovery(),
  );
  /** The editable draft changed or was deleted elsewhere; nothing is applied unasked. */
  protected readonly elsewhere = draftElsewhere(
    () => (this.released() ? undefined : (this.available() ?? this.saved())),
    () => this.contentBusy(),
  );
  protected readonly canGenerate = computed(() => this.aiConfigured() && !this.contentBusy());
  /** The visible settings are the plan defaults, in a template and an activity alike; one set of fields owns them. */
  protected readonly projection = computed(() => {
    const { plan, input } = this.raw();
    return planValue(plan, input.settings, this.limits);
  });
  protected readonly inputProjection = computed(() => {
    const plan = this.projection().value;
    return plan ? requestValue(plan, this.raw().input, this.limits) : { errors: [] };
  });
  protected readonly documentProjection = computed(() =>
    documentValue(this.raw().document, this.limits),
  );
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
  protected readonly thread = computed(() => [...this.answered(), ...this.conversation()]);
  protected readonly needsConsolidation = computed(
    () =>
      this.conversation().length > this.limits.maxContextTurns ||
      this.conversation().reduce((sum, turn) => sum + turn.text.length, 0) >
        this.limits.contextLength,
  );
  protected readonly dirty = computed(
    () =>
      JSON.stringify(this.raw()) !== JSON.stringify(this.baseline()) ||
      !!this.sourceReplacement().id,
  );
  protected readonly unsaved = computed(() => this.dirty() && !!this.saved());
  protected readonly templateChanged = computed(
    () => JSON.stringify(this.projection().value) !== this.publishedPlan(),
  );
  /**
   * A template or an activity's own plan defines what activities may change; an activity created
   * from a template, before or after its first save, only sets the values the template allows.
   */
  protected readonly definesChoices = computed(
    () => this.context() === 'template' || (!this.templateId() && !this.saved()?.templateVersionId),
  );
  /** Any proposed, loaded or typed plan; an invalid edit keeps the setup visible for correction. */
  protected readonly hasPlan = computed(() => hasPlanContent(this.raw().plan));
  protected readonly hasContent = computed(
    () => !!this.raw().document.materials.length || !!this.raw().document.questions.length,
  );
  protected readonly steps = ['תיאור', 'הגדרות', 'בדיקה', 'מוכנה'];
  /** The step indicator's phase: describe, set up, review, ready. */
  protected readonly stage = computed(() =>
    this.released() ? 3 : this.hasContent() ? 2 : this.hasPlan() ? 1 : 0,
  );
  /** Setup collapses to its summary once content exists; the parent can reopen it at any time. */
  protected readonly setupOpen = linkedSignal(() => !this.hasContent());
  protected readonly summary = computed(() => activitySummary(this.raw().plan, this.raw().input));
  protected readonly saveState = computed(() => {
    if (this.saving()) return 'שומרים…';
    if (this.elsewhere() === 'changed') return 'הפעילות עודכנה במכשיר אחר';
    if (this.elsewhere() === 'deleted') return 'הפעילות נמחקה במכשיר אחר';
    return this.dirty() ? 'לא נשמר' : this.saved() ? 'נשמר' : '';
  });
  protected readonly heading = computed(() => {
    if (this.context() === 'template') return this.templateId() ? 'עריכת תבנית' : 'תבנית חדשה';
    if (this.released()) return 'פעילות מוכנה';
    if (this.saved() || this.activityId()) return 'עריכת פעילות';
    return this.templateId() ? 'פעילות חדשה מתבנית' : 'פעילות חדשה';
  });
  /** One reload control: beside a newer server result, an error or a change saved elsewhere, or with the other actions. */
  protected readonly reloadPlacement = computed(() => {
    if (!this.saved() || this.elsewhere() === 'deleted') return 'none';
    if (this.available()) return 'available';
    if (this.activityError()) return 'error';
    return this.elsewhere() ? 'elsewhere' : 'more';
  });
  protected readonly measurements = computed(() =>
    measurementItems(this.saved()?.measurements ?? [], this.saved()?.plan),
  );
  protected readonly issues = computed(() => reviewIssues(this.saved()));
  protected readonly stale = computed(() => staleContent(this.saved()));
  protected readonly operationActive = computed(
    () => !!this.operationId() && (!this.operation() || isRunning(this.operation()!)),
  );
  /**
   * The scoped target whose card can display status. Removing it or changing its source keeps
   * progress and cancellation above the content instead; restoring the card moves them back.
   */
  protected readonly operationTarget = computed(() => {
    const operation = this.operation(),
      target = operation?.artifacts?.targetId;
    if (!target) return null;
    const { document, plan } = this.raw();
    const hasCard =
      operation.kind === 'ReplaceQuestion'
        ? document.questions.some((question) => question.id === target)
        : operation.kind === 'ReplaceMaterial' &&
          document.materials.some((material) => material.id === target) &&
          plan.materials.some(
            (material) => material.id === target && material.source === 'generated',
          );
    return hasCard ? target : null;
  });
  protected readonly editableCandidates = computed(() => {
    const plan = this.projection().value,
      operation = this.operation();
    return plan && operation && !this.operationActive() && !this.released()
      ? unappliedCandidates(operation, this.raw().document, plan, this.limits)
      : [];
  });

  protected readonly fields = form(this.raw, (path) => {
    disabled(path, () => this.saving() || this.loading() || this.released());
    apply(path.document, documentSchema(this.limits));
    apply(path.plan, planFormSchema(this.limits));
    apply(path.input.settings, taskSettingsSchema(this.limits));
    applyEach(path.input.materials, (material) =>
      maxLength(material.sourceText, this.limits.bodyLength),
    );
    applyEach(path.input.controls, (control) =>
      validate(control.value, ({ valueOf }) => {
        const plan = this.projection().value;
        const definition =
          plan && planControls(plan).find((item) => item.id === valueOf(control.id));
        return definition
          ? controlInputValue(definition, this.limits, {
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
    maxLength(path.message, this.limits.messageLength);
    maxLength(path.consolidated, this.limits.messageLength);
  });
  protected readonly sourceFields = form(this.sourceReplacement, (path) =>
    maxLength(path.text, this.limits.bodyLength),
  );

  constructor() {
    effect(() => {
      const loaded = this.activity.value();
      const template = this.template.value();
      if (this.initialized || !(loaded || template)) return;
      const plan = loaded?.plan ?? template!.definition;
      this.raw.set(workspaceForm(plan, loaded?.input, loaded?.document));
      if (loaded) {
        this.saved.set(loaded);
        this.operationId.set(loaded.activeOperationId ?? this.resumeOperation());
        this.operationClientRevision = this.clientRevision;
      }
      this.confirmed.set(fixedSources(plan));
      if (template) {
        this.publication.set(template);
        this.publishedPlan.set(JSON.stringify(this.projection().value));
      }
      this.baseline.set(structuredClone(this.raw()));
      this.history.checkpoint();
      this.initialized = true;
    });
    effect(() => {
      const version = this.ai.value()?.schemaVersion;
      if (!version || this.raw().plan.schemaVersion) return;
      this.raw.update((raw) => withSchemaVersion(raw, version));
      // Late configuration is not a save acknowledgement for anything typed while it loaded.
      this.baseline.update((raw) => withSchemaVersion(raw, version));
      this.history.amend((state) => ({ ...state, raw: withSchemaVersion(state.raw, version) }));
    });
    const savedId = computed(() => this.saved()?.id);
    effect((cleanup) => {
      this.pollRefresh();
      const id = savedId(),
        operationId = this.operationId();
      if (!id || !operationId) return;
      // One poll per draft and operation; a reload restarts it and the route disposes it.
      const subscription = pollOperation(this.api, this.document, id, operationId).subscribe({
        next: ({ operation, draft }) => {
          this.operation.set(operation);
          this.receiveCheckpoint(draft);
        },
        error: (error) => this.activityError.set(this.activityFailure(error)),
      });
      cleanup(() => subscription.unsubscribe());
    });
    this.lifetime.onDestroy(() => {
      this.request.set(undefined);
      this.cancelled.next();
      this.cancelled.complete();
    });
  }

  protected edited(edit: { key: string; sourceId?: string }) {
    if (this.locked()) return;
    if (edit.sourceId) {
      const source = this.raw().plan.materials.find((material) => material.id === edit.sourceId);
      if (source) this.confirmed.update((values) => ({ ...values, [source.id]: source.text }));
    }
    this.recordEdit(edit.key);
  }

  protected changeStructure(edit: PlanStructureEdit) {
    if (this.locked()) return;
    const next = editPlanStructure(this.raw(), edit, this.limits);
    if (!next) return;
    this.raw.set(next);
    this.recordEdit('');
  }

  protected confirmSource(id: string) {
    if (this.locked()) return;
    const source = this.raw().plan.materials.find((material) => material.id === id);
    if (!source) return;
    this.confirmed.update((values) => ({ ...values, [id]: source.text }));
    this.recordEdit('');
  }

  protected async undo() {
    if (this.locked() || !this.history.entries().length) return;
    const previous = this.history.pop()!;
    this.raw.set(structuredClone(previous.raw));
    this.confirmed.set({ ...previous.confirmed });
    this.history.checkpoint();
    this.markLocalChange();
    this.clearPlanFeedback();
    const saved = this.saved();
    if (saved) this.raw.update((raw) => withSavedQuestionIds(raw, saved));
    this.notice.set('השינוי האחרון בוטל.');
    if (saved) await this.activityAction('save');
  }

  protected cancelAuthor() {
    const pending = this.request();
    this.request.set(undefined); // Invalidate identity before unsubscribing from transport.
    this.cancelled.next();
    // An unanswered request returns to the composer for editing or sending again.
    if (pending)
      this.chat.update((chat) =>
        pending.consolidate
          ? { ...chat, consolidated: pending.text }
          : { ...chat, message: pending.text },
      );
  }

  /** Sends one correlated authoring request; any local change before the reply discards it. */
  protected async author(consolidate = false) {
    if (
      this.locked() ||
      this.authoring() ||
      !!this.sourceReplacement().id ||
      !this.aiConfigured() ||
      (!consolidate && this.needsConsolidation())
    )
      return;
    const message = consolidate ? this.chat().consolidated : this.chat().message;
    if (!message.trim() || message.length > this.limits.messageLength) return;
    const baseDefinition = this.projection().value;
    if (!baseDefinition && this.hasPlan()) {
      this.attempted.set(true);
      this.authorError.set('תקנו את ההגדרות המסומנות לפני שליחת בקשה נוספת.');
      return;
    }
    const requestId = crypto.randomUUID(),
      baseRevision = this.clientRevision;
    const basis = JSON.stringify(this.raw()),
      context = consolidate ? [] : this.conversation();
    const restoreFocus = this.holdFocus();
    this.request.set({ id: requestId, text: message, consolidate });
    this.chat.update((chat) =>
      consolidate ? { ...chat, consolidated: '' } : { ...chat, message: '' },
    );
    this.authorError.set('');
    try {
      const reply = await this.api.authorPlan(
        { message, baseDefinition, context, requestId, baseRevision },
        this.cancelled,
        this.lifetime,
      );
      if (
        this.lifetime.destroyed ||
        this.request()?.id !== requestId ||
        reply.requestId !== requestId ||
        reply.baseRevision !== baseRevision ||
        this.clientRevision !== baseRevision ||
        JSON.stringify(this.raw()) !== basis
      )
        return;
      this.request.set(undefined);
      this.assumptions.set(reply.assumptions);
      if (reply.proposal) {
        this.conversation.set([]);
        this.clarification.set('');
        // A first plan has no earlier version to compare; the setup itself shows what was proposed.
        this.changes.set(
          baseDefinition
            ? reply.changes.map((change) =>
                planChangeLabel(change, baseDefinition, reply.proposal!),
              )
            : [],
        );
        this.answered.set([
          ...context,
          { role: 'parent', text: message },
          {
            role: 'assistant',
            text: !reply.changes.length
              ? 'ההגדרות כבר תואמות לבקשה.'
              : baseDefinition
                ? 'ההגדרות עודכנו. אפשר לערוך אותן או לבטל את השינוי.'
                : this.context() === 'template'
                  ? 'הכנו הגדרות לפי הבקשה. בדקו אותן ושמרו את התבנית.'
                  : 'הכנו הגדרות לפי הבקשה. בדקו אותן וצרו את הפעילות.',
          },
        ]);
        if (reply.changes.length) {
          this.applyProposal(reply.proposal, !!baseDefinition);
          // The first plan re-creates the chat below the settings; focus returns to its composer.
          if (!baseDefinition) restoreFocus('chat-message');
        }
      } else if (reply.clarification) {
        this.answered.set([]);
        this.conversation.set([
          ...context,
          { role: 'parent', text: message },
          { role: 'assistant', text: reply.clarification },
        ]);
        this.clarification.set(reply.clarification);
      }
      this.chat.set({ message: '', consolidated: '' });
    } catch (error) {
      if (!this.lifetime.destroyed && this.request()?.id === requestId)
        this.authorError.set(apiError(error));
    } finally {
      if (this.request()?.id === requestId) this.cancelAuthor();
    }
  }

  protected async saveTemplate() {
    if (this.saving() || !this.templateChanged()) return;
    this.attempted.set(true);
    this.error.set('');
    this.templateNotice.set('');
    const plan = this.projection().value;
    if (!plan || this.pendingSources().length || this.sourceReplacement().id) {
      this.error.set('השלימו את ההגדרות ואשרו את הטקסט שלכם לפני שמירה כתבנית.');
      return;
    }
    this.cancelAuthor();
    const restoreFocus = this.holdFocus();
    this.saving.set(true);
    this.publishing.set(true);
    try {
      const saved = await this.api.savePlanTemplate(plan, this.publication(), this.lifetime);
      if (this.lifetime.destroyed) return;
      this.publication.set(saved);
      this.publishedPlan.set(JSON.stringify(plan));
      this.history.endCoalescing();
      if (this.context() === 'template') {
        // A template is its plan and defaults, so publication saves the whole buffer.
        this.baseline.set(structuredClone(this.raw()));
        this.templateNotice.set('התבנית נשמרה בספרייה.');
      } else {
        // Publication from an activity cannot claim that its edits or per-task input were saved.
        if (!this.saved())
          this.baseline.update((raw) => ({ ...raw, plan: structuredClone(this.raw().plan) }));
        this.templateNotice.set('התבנית נשמרה בספרייה. הפעילות לא השתנתה.');
      }
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.error.set(
          error instanceof HttpErrorResponse && error.status === 409
            ? 'התבנית השתנתה בינתיים. השינויים שלכם נשארים כאן; בדקו את הגרסה בספרייה.'
            : `${apiError(error)} לא ידוע אם התבנית נשמרה. בדקו בספרייה לפני ניסיון נוסף.`,
        );
    } finally {
      if (!this.lifetime.destroyed) {
        this.saving.set(false);
        this.publishing.set(false);
        restoreFocus();
      }
    }
  }

  protected editDocument(edit: DocumentEdit) {
    if (this.locked()) return;
    const next = editDocumentStructure(this.raw(), edit, this.limits);
    if (!next) return;
    this.raw.set(next);
    this.recordEdit('');
  }

  protected replaceSource(id: string) {
    const text = sourceText(this.raw(), id);
    if (text !== undefined) this.sourceReplacement.set({ id, text });
  }

  protected acceptSourceReplacement() {
    const { id, text } = this.sourceReplacement();
    if (!text.trim() || text.length > this.limits.bodyLength || this.saving()) return;
    const next = replaceSourceText(this.raw(), id, text);
    if (!next) return;
    this.raw.set(next);
    this.confirmed.update((values) => ({ ...values, [id]: text }));
    this.recordEdit('');
    this.sourceReplacement.set({ id: '', text: '' });
  }

  protected editCandidate(index: number) {
    if (this.saving()) return;
    const candidate = this.editableCandidates().find((c) => c.index === index);
    if (!candidate) return;
    this.raw.update((raw) => ({ ...raw, document: candidate.document }));
    this.recordEdit('');
    this.notice.set('התוצאה הועברה לעריכה. בדקו ושמרו אותה.');
  }

  /** Every content action flushes one validated checkpoint first; no failed save can start work. */
  protected async activityAction(
    action: 'save' | 'release' | 'adopt' | GenerationKind,
    target?: {
      targetId?: string;
      instruction?: string;
      materialIds?: string[];
      questionIds?: string[];
    },
  ) {
    if (this.locked() || (action !== 'save' && (this.operationActive() || this.startRecovery())))
      return;
    if (action !== 'save' && action !== 'release' && action !== 'adopt' && !this.aiConfigured())
      return;
    await this.runDraftRequest(async () => {
      this.cancelAuthor();
      this.activityError.set('');
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
          ...(target?.instruction ? { instruction: target.instruction } : {}),
        };
        this.startRecovery.set({ draftId: saved.id, request });
        await this.submitOperation(saved.id, request);
        if (action === 'GenerateActivity' || action === 'GenerateQuestions') this.revealProgress();
      }
    });
  }

  protected async reloadActivity() {
    const saved = this.saved();
    if (!saved || this.saving()) return;
    if (this.dirty() && !window.confirm('טעינת הגרסה השמורה תחליף את השינויים שלא נשמרו. להמשיך?'))
      return;
    await this.runDraftRequest(async () => {
      const latest = await requestResult(this.api.readActivity(saved.id), this.lifetime);
      this.markLocalChange();
      this.operationClientRevision = this.clientRevision;
      this.sourceReplacement.set({ id: '', text: '' });
      this.acceptCheckpoint(latest);
      this.history.clear();
      this.operationId.set(latest.activeOperationId ?? this.operationId());
      this.pollRefresh.update((value) => value + 1);
      this.activityError.set('');
    });
  }

  protected async recoverStart() {
    const pending = this.startRecovery();
    if (!pending || this.saving()) return;
    await this.runDraftRequest(async () => {
      await this.submitOperation(pending.draftId, pending.request);
      this.activityError.set('');
    });
  }

  protected async cancelGeneration() {
    const id = this.saved()?.id,
      operationId = this.operationId();
    if (!id || !operationId || this.saving()) return;
    await this.runDraftRequest(async () => {
      const result = await this.api.cancelGeneration(id, operationId, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.operation.set(result);
      const saved = await requestResult(this.api.readActivity(id), this.lifetime);
      if (!this.dirty()) this.acceptCheckpoint(saved);
      else this.receiveCheckpoint(saved);
    });
  }

  /** Explicit copy of the frozen snapshot into a new draft; no AI call and the snapshot never changes. */
  protected async copyReleased() {
    const id = this.saved()?.releasedSnapshotId;
    if (!id || this.copying()) return;
    this.copying.set(true);
    this.activityError.set('');
    try {
      const draft = await this.api.copySnapshot(id, this.lifetime);
      if (!this.lifetime.destroyed) await this.router.navigate(['/activities', draft.id]);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.activityError.set(
          apiError(error) + ' ייתכן שהעותק נשמר. בדקו בספרייה לפני ניסיון נוסף.',
        );
    } finally {
      if (!this.lifetime.destroyed) this.copying.set(false);
    }
  }

  protected async newActivity() {
    if (this.saving()) return;
    const plan = this.projection().value,
      input = this.inputProjection().value;
    if (!plan || !input || this.pendingSources().length) {
      this.activityError.set('השלימו את ההגדרות ואשרו את הטקסט שלכם.');
      return;
    }
    if (
      this.dirty() &&
      !window.confirm(
        'פעילות חדשה תיפתח בלי התוכן של הפעילות הזאת, ושינויים שלא נשמרו יאבדו. להמשיך?',
      )
    )
      return;
    await this.runDraftRequest(async () => {
      const created = await this.api.createActivity(plan, input, undefined, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.markLocalChange();
      this.history.clear();
      this.operationId.set(undefined);
      this.operation.set(undefined);
      this.startRecovery.set(undefined);
      this.sourceReplacement.set({ id: '', text: '' });
      this.acceptCheckpoint(created);
      this.location.replaceState('/activities/' + created.id);
    });
  }

  /** Native route guard and browser-close warning protect local-only keystrokes. */
  canLeave() {
    return !this.dirty() || window.confirm('יש שינויים שלא נשמרו. לצאת מהעמוד?');
  }

  protected beforeUnload(event: BeforeUnloadEvent) {
    if (this.dirty()) event.preventDefault();
  }

  /** Records one edit for Undo, coalescing by field key, and clears replies about the earlier plan. */
  private recordEdit(key: string) {
    this.raw.set(reconcile(this.raw(), this.saved()?.plan));
    if (!this.history.record(key)) return;
    this.markLocalChange();
    this.clearPlanFeedback();
    this.notice.set('');
    this.templateNotice.set('');
  }

  /** A local change: a pending authoring reply and the clarification thread no longer apply. */
  protected markLocalChange() {
    this.clientRevision++;
    this.cancelAuthor();
    this.conversation.set([]);
    this.clarification.set('');
  }

  private clearPlanFeedback() {
    this.answered.set([]);
    this.changes.set([]);
    this.assumptions.set([]);
    this.error.set('');
    this.authorError.set('');
  }

  private applyProposal(proposal: LearningPlan, refining: boolean) {
    const saved = this.saved()?.plan;
    const plan = {
      ...proposal,
      materials: proposal.materials.map((material) => ({
        ...material,
        id: materialIdentity(saved, material.id, material.source),
      })),
    };
    this.history.push();
    this.raw.set(proposedWorkspace(this.raw(), plan, refining));
    this.confirmed.set(fixedSources(plan, this.confirmed()));
    this.clientRevision++;
    this.history.checkpoint();
  }

  /** Runs one draft request under the shared saving lock; a failure keeps local work and says what to check. */
  private async runDraftRequest(work: () => Promise<void>) {
    const restoreFocus = this.holdFocus();
    this.saving.set(true);
    try {
      await work();
    } catch (error) {
      if (!this.lifetime.destroyed) this.activityError.set(this.activityFailure(error));
    } finally {
      if (!this.lifetime.destroyed) {
        this.saving.set(false);
        restoreFocus();
      }
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
      this.activityError.set('תקנו את השדות המסומנים ואשרו את הטקסט שלכם לפני שמירה.');
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
    this.raw.set(workspaceForm(saved.plan, saved.input, saved.document));
    this.confirmed.set(fixedSources(saved.plan));
    this.baseline.set(structuredClone(this.raw()));
    this.history.checkpoint();
    this.available.set(undefined);
  }

  /** Applies a newer server checkpoint unless that would replace local edits made since the operation started. */
  private receiveCheckpoint(saved: ActivityDetail) {
    if (saved.revision <= (this.saved()?.revision ?? 0)) return;
    if (!this.dirty() && !this.saving() && this.clientRevision === this.operationClientRevision) {
      const restoreFocus = this.holdFocus();
      this.history.push();
      this.clientRevision++;
      this.operationClientRevision = this.clientRevision;
      this.acceptCheckpoint(saved);
      // Content that replaces the panel holding focus, such as the create actions, takes it.
      restoreFocus('document-heading');
    } else {
      this.available.set(saved);
      this.notice.set('נוצרה תוצאה בזמן שהמשכתם לערוך. לא החלפנו את העבודה שלכם.');
    }
  }

  /**
   * Full generation rebuilds the content right below its progress, so that card comes into view
   * once, smoothly unless the parent prefers reduced motion. Focus stays on the started action.
   */
  private revealProgress() {
    afterNextRender(
      () => {
        const reduced = this.document.defaultView?.matchMedia('(prefers-reduced-motion: reduce)');
        this.progress()?.nativeElement.scrollIntoView({
          block: 'start',
          behavior: reduced?.matches ? 'auto' : 'smooth',
        });
      },
      { injector: this.injector },
    );
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
      // A client error is a definite rejection; anything else may have started and stays recoverable.
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500)
        this.startRecovery.set(undefined);
      throw error;
    }
  }

  private activityFailure(error: unknown): string {
    return error instanceof HttpErrorResponse && error.status === 409
      ? 'הטיוטה השתנתה בשרת. השינויים שלכם נשארים כאן; טענו את הגרסה השמורה לפני שממשיכים.'
      : apiError(error) +
          ' השינויים שלכם נשארים כאן. ייתכן שהבקשה נשמרה; בדקו את הגרסה השמורה לפני ניסיון נוסף.';
  }

  private snapshot(): WorkspaceSnapshot {
    return { raw: structuredClone(this.raw()), confirmed: { ...this.confirmed() } };
  }
}
