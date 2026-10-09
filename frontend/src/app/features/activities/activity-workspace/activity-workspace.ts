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
  signal,
  viewChild,
} from '@angular/core';
import {
  apply,
  disabled,
  form,
  FormField,
  maxLength,
  validate,
  validateTree,
} from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { apiError, rejected, writeError } from '../../../core/api/api-error';
import { LearningApi } from '../../../core/api/learning-api';
import { Limits } from '../../../core/api/limits';
import {
  ActivityDetail,
  GenerationKind,
  GenerationOperation,
  LearningPlan,
  StartGeneration,
  RevisionTarget,
} from '../../../core/api/models';
import { focusHolder } from '../../../shared/focus-holder';
import { validationErrors } from '../../../shared/forms/projection';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { ActivityDocumentEditor } from '../activity-document-editor/activity-document-editor';
import { documentSchema, documentValue } from '../activity-document-editor/document-form';
import { ActivityDocumentView } from '../activity-document-view/activity-document-view';
import { measurementItems } from '../activity-document-view/measurements';
import {
  fieldPointers,
  reviewIssues,
  savedContentIssues,
  staleContent,
} from '../activity-presentation';
import { ActivityReview } from '../activity-review/activity-review';
import { ActivitySetup } from '../activity-setup/activity-setup';
import { GenerationStatus } from '../generation-status/generation-status';
import { isRunning } from '../generation-status/operation-state';
import { planFormSchema } from './plan-form';
import { planValue } from './plan-projection';
import { SourceReplacement } from '../source-replacement/source-replacement';
import { ActivityChat } from '../activity-chat/activity-chat';
import { libraryChanges } from '../../../core/api/library-changes';
import { DraftObservation, observeDraft } from './draft-observer';
import { UndoHistory } from './undo-history';
import { ChatSession } from './chat-session';
import {
  ConfirmedSources,
  fixedSources,
  replaceSourceText,
  sourceText,
  WorkspaceForm,
  workspaceForm,
  WorkspaceSnapshot,
} from './workspace-form';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { FieldValidity } from '../../../shared/forms/field-validity';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { ActionBar, ActionBarToggle } from '../../../shared/action-bar/action-bar';

/**
 * Owns one activity buffer, source confirmation and durable draft transitions.
 * Views share its fields; the chat session owns local requests and the observer owns saved reads.
 */
@Component({
  selector: 'app-activity-workspace',
  imports: [
    FormField,
    FieldErrors,
    FieldValidity,
    FieldDirection,
    DisabledInteractive,
    ActivityChat,
    LoadingIndicator,
    RouterLink,
    ActivityDocumentEditor,
    ActivitySetup,
    ActivityReview,
    GenerationStatus,
    ActivityDocumentView,
    NgTemplateOutlet,
    SourceReplacement,
    ActionBar,
    ActionBarToggle,
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

  readonly activityId = input<string>();
  /** Operation named in the URL; shown after a reload when the draft has no active operation. */
  readonly resumeOperation = input<string | undefined>(undefined, { alias: 'operation' });

  protected readonly activity = this.api.activity(this.activityId);
  protected readonly ai = this.api.aiStatus();
  protected readonly loading = this.activity.isLoading;
  protected readonly loadError = this.activity.error;
  private activityReadFocus?: () => void;
  private aiReadFocus?: () => void;

  protected readonly raw = signal<WorkspaceForm>(workspaceForm());
  protected readonly confirmed = signal<ConfirmedSources>({});
  protected readonly history = new UndoHistory<WorkspaceSnapshot>(() => this.snapshot());
  protected readonly sourceReplacement = signal({ id: '', text: '' });
  protected readonly replacementError = signal('');
  /** Counts local changes; authoring replies and operation results apply only to the revision they started from. */
  private clientRevision = 0;
  private initialized = false;

  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly copying = signal(false);
  protected readonly notice = signal('');
  /** The last action's problem; `reload` marks a request whose outcome the saved version may show. */
  protected readonly activityError = signal<{ message: string; reload?: boolean } | undefined>(
    undefined,
  );
  protected readonly readError = signal('');
  protected readonly saved = signal<ActivityDetail | undefined>(undefined);
  /** Derived only from the accepted checkpoint; newer remote content cannot erase local edits. */
  private readonly baseline = computed(() => {
    const saved = this.saved();
    return workspaceForm(saved?.plan, saved?.document);
  });
  /** A newer server checkpoint held back because applying it would replace local edits. */
  protected readonly available = signal<ActivityDetail | undefined>(undefined);

  private readonly chatView = viewChild(ActivityChat);
  protected readonly operation = signal<GenerationOperation | undefined>(undefined);
  protected readonly operationId = signal<string | undefined>(undefined);
  /** A start whose response was lost; checking it replays the same key and request. */
  protected readonly startRecovery = signal<
    { draftId: string; request: StartGeneration } | undefined
  >(undefined);
  /** The edit fence for locally followed generation; external operations never replace the buffer. */
  private operationClientRevision: number | undefined;

  protected readonly released = computed(
    () => !!(this.available() ?? this.saved())?.releasedSnapshotId,
  );
  /** Editing pauses during work and stays locked once the activity is released. */
  protected readonly locked = computed(
    () => this.saving() || this.released() || this.operationActive() || !!this.startRecovery(),
  );
  protected readonly aiConfigured = computed(
    () => this.ai.hasValue() && this.ai.value().configured,
  );
  /** Content actions wait for a running save or operation and for any unresolved start. */
  protected readonly contentBusy = computed(
    () => this.saving() || this.operationActive() || !!this.startRecovery(),
  );
  protected readonly updates = libraryChanges();
  /** The editable draft changed or was deleted elsewhere; nothing is applied unasked. */
  protected readonly elsewhere = signal<'changed' | 'deleted' | undefined>(undefined);
  private readonly observer = observeDraft({
    draftId: () => this.saved()?.id,
    operationId: () => this.operationId(),
    enabled: () => !this.released() && this.elsewhere() !== 'deleted',
    busy: () => this.saving() || !!this.startRecovery(),
    changes: this.updates.changes,
    changed: (observation) => this.receiveObservation(observation),
    failed: (error) => {
      if (error instanceof HttpErrorResponse && error.status === 404) this.elsewhere.set('deleted');
      else this.readError.set(apiError(error));
    },
  });
  protected readonly canGenerate = computed(() => this.aiConfigured() && !this.contentBusy());
  /** The plan owns the activity's concrete settings. */
  protected readonly projection = computed(() => planValue(this.raw().plan, this.limits));
  protected readonly documentProjection = computed(() =>
    documentValue(this.raw().document, this.limits),
  );
  protected readonly pendingSources = computed(() =>
    this.raw()
      .plan.materials.filter(
        (material) =>
          material.source === 'supplied' && this.confirmed()[material.id] !== material.text,
      )
      .map((material) => material.id),
  );
  /** Why the buffer cannot be sent yet, in parent language; empty once it can. */
  protected readonly blocker = computed(() => {
    if (this.sourceReplacement().id) return 'אשרו את הטקסט החדש או בטלו את ההחלפה.';
    if (this.pendingSources().length) return 'אשרו שהטקסט שלכם הועתק נכון.';
    const valid = this.projection().value && this.documentProjection().value;
    return valid ? '' : 'תקנו את השדות המסומנים.';
  });
  protected readonly thread = computed(
    () => (this.available() ?? this.saved())?.chat ?? this.chatSession.authorThread(),
  );
  protected readonly dirty = computed(
    () =>
      JSON.stringify(this.raw()) !== JSON.stringify(this.baseline()) ||
      !!this.sourceReplacement().id,
  );
  private readonly localWork = computed(
    () =>
      this.dirty() ||
      this.chatSession.authoring() ||
      (!this.saved() && !!this.chatSession.authorThread().length) ||
      (!this.operationActive() && !this.startRecovery() && this.chatSession.hasComposerWork()),
  );
  protected readonly unsaved = computed(() => this.dirty() && !!this.saved());
  /** Schema 0 is empty setup; a server plan exists even when edited source text is invalid. */
  protected readonly hasPlan = computed(() => this.raw().plan.schemaVersion > 0);
  protected readonly hasContent = computed(
    () =>
      !!this.raw().document.title.trim() ||
      !!this.raw().document.instructions.trim() ||
      !!this.raw().document.materials.length ||
      !!this.raw().document.questions.length,
  );
  /** Create completes missing content in one atomic operation. */
  protected readonly createAction = computed(() =>
    this.raw().document.questions.length
      ? undefined
      : { kind: 'Create' as const, label: 'יצירת הפעילות' },
  );
  /** The pinned save state and current actions, once there is anything to save, undo or open. */
  protected readonly actionBar = computed(
    () => this.hasPlan() || !!this.saved() || !!this.history.entries().length,
  );
  protected readonly canUndo = computed(() =>
    this.saved() ? !!this.saved()?.canUndo && !this.dirty() : !!this.history.entries().length,
  );
  protected readonly saveState = computed(() => {
    if (this.saving()) return 'שומרים…';
    if (this.elsewhere() === 'changed') return 'הפעילות עודכנה במכשיר אחר';
    if (this.elsewhere() === 'deleted') return 'הפעילות נמחקה במכשיר אחר';
    return this.dirty() ? 'לא נשמר' : this.saved() ? 'נשמר' : '';
  });
  protected readonly heading = computed(() => {
    if (this.released()) return 'פעילות מוכנה';
    if (this.saved() || this.activityId()) return 'טיוטת פעילות';
    return 'פעילות חדשה';
  });
  /** One reload control: beside a newer server result or error, otherwise with the other actions. */
  protected readonly reloadPlacement = computed(() => {
    if (!this.saved() || this.elsewhere() === 'deleted') return 'none';
    if (this.available()) return 'available';
    if (this.activityError()?.reload || this.readError()) return 'error';
    return 'more';
  });
  protected readonly measurements = computed(() =>
    measurementItems(this.saved()?.measurements ?? [], this.saved()?.plan),
  );
  protected readonly totalMeasurements = computed(() =>
    this.measurements().filter((item) => item.scope === 'total'),
  );
  /** Release problems matter once content is ready to review: present, and no generation writing it. */
  protected readonly contentIssues = computed(() =>
    savedContentIssues(
      this.hasContent() && !this.operationActive() ? this.saved() : undefined,
      this.raw().document,
      this.baseline().document,
    ),
  );
  protected readonly issues = computed(() => [
    ...reviewIssues(this.saved()),
    ...fieldPointers(this.contentIssues(), this.raw().document, this.saved()?.plan),
  ]);
  protected readonly approvalBlocked = computed(
    () => !this.saved() || !!Object.keys(this.saved()!.diagnostics).length,
  );
  protected readonly stale = computed(() => staleContent(this.saved()));
  protected readonly operationActive = computed(
    () => !!this.operationId() && (!this.operation() || isRunning(this.operation()!)),
  );
  protected readonly fields = form(this.raw, (path) => {
    disabled(path, () => this.locked() || this.loading());
    disabled(path.plan, () => !!this.saved());
    apply(path.document, documentSchema(this.limits));
    apply(path.plan, planFormSchema(this.limits));
    // The projections own every rule; each problem shows on the field that can fix it.
    validateTree(path, ({ fieldTree }) =>
      validationErrors(fieldTree, [
        ...this.projection().errors,
        ...this.documentProjection().errors,
      ]),
    );
  });
  protected readonly chatSession = new ChatSession(
    () => this.raw().document,
    () => this.locked(),
  );
  protected readonly sourceFields = form(this.sourceReplacement, (path) => {
    disabled(path, () => this.locked());
    maxLength(path.text, this.limits.bodyLength);
    validate(path.text, ({ value }) =>
      value().trim() ? undefined : { kind: 'required', message: 'יש להזין טקסט.' },
    );
  });

  protected clearTarget() {
    const restoreFocus = this.holdFocus();
    this.chatSession.clearTarget();
    restoreFocus('chat-message');
  }
  protected retryAiStatus() {
    const restoreFocus = this.holdFocus();
    this.aiReadFocus = () => restoreFocus(this.ai.error() ? 'retry-ai-status' : 'chat-message');
    this.ai.reload();
  }
  protected retryActivity() {
    const restoreFocus = this.holdFocus();
    this.activityReadFocus = () =>
      restoreFocus(this.activity.error() ? 'retry-activity' : 'workspace-title');
    this.activity.reload();
  }
  protected selectTarget(target: RevisionTarget): void {
    if (this.locked()) return;
    this.chatSession.selectTarget(target);
    this.chatView()?.focusComposer();
  }

  constructor() {
    // Retried reads replace their error controls only after the resource settles.
    effect(() => {
      if (this.activity.isLoading()) return;
      this.activityReadFocus?.();
      this.activityReadFocus = undefined;
    });
    effect(() => {
      if (this.ai.isLoading()) return;
      this.aiReadFocus?.();
      this.aiReadFocus = undefined;
    });
    effect(() => {
      if (this.initialized || !this.activity.hasValue()) return;
      const loaded = this.activity.value();
      this.acceptCheckpoint(loaded);
      this.operationId.set(loaded.activeOperationId ?? this.resumeOperation());
      this.operationClientRevision = this.operationId() ? this.clientRevision : undefined;
      this.initialized = true;
    });
  }

  protected edited(edit: { key: string }) {
    if (this.locked()) return;
    this.recordEdit(edit.key);
  }

  protected confirmSource(id: string) {
    if (this.locked()) return;
    const index = this.raw().plan.materials.findIndex((material) => material.id === id);
    if (index < 0) return;
    const source = this.fields.plan.materials[index].text();
    source.markAsTouched();
    if (source.invalid()) {
      this.activityError.set({ message: 'תקנו את הטקסט המסומן.' });
      return;
    }
    const restoreFocus = this.holdFocus();
    this.activityError.set(undefined);
    this.confirmed.update((values) => ({ ...values, [id]: source.value() }));
    this.recordEdit('');
    restoreFocus('source-' + id);
  }

  protected async undo() {
    if (this.locked() || !this.canUndo()) return;
    const checkpoint = this.saved();
    if (checkpoint) {
      await this.runDraftRequest(async () => {
        const restored = await this.api.undoActivity(
          checkpoint.id,
          checkpoint.revision,
          this.lifetime,
        );
        if (!this.lifetime.destroyed) this.acceptCheckpoint(restored);
      });
      return;
    }
    const previous = this.history.pop()!;
    this.raw.set(structuredClone(previous.raw));
    this.confirmed.set({ ...previous.confirmed });
    this.history.checkpoint();
    this.markLocalChange();
    this.chatSession.clearFeedback();
    this.notice.set('השינוי האחרון בוטל.');
  }

  /** Authoring keeps local correlation in the session; saved revisions use durable operations. */
  protected async author(consolidate = false) {
    const session = this.chatSession;
    if (
      this.locked() ||
      session.authoring() ||
      !!this.sourceReplacement().id ||
      !this.aiConfigured() ||
      (!consolidate && session.needsConsolidation())
    )
      return;
    const message = consolidate ? session.chat().consolidated : session.chat().message;
    if (!message.trim() || message.length > this.limits.messageLength) return;
    if (this.saved()) {
      if (session.invalidTarget() || !session.sourcesReady()) return;
      session.authorError.set('');
      await this.activityAction('Revise', {
        message,
        target: session.selectedTarget(),
        sources: session.addedSources().map(({ label, text }) => ({ label, text })),
      });
      return;
    }
    const baseDefinition = this.projection().value;
    if (!baseDefinition && this.hasPlan()) {
      this.fields().markAsTouched();
      session.authorError.set('תקנו את ההגדרות המסומנות לפני שליחת בקשה נוספת.');
      return;
    }
    const baseRevision = this.clientRevision,
      basis = JSON.stringify(this.raw());
    const isCurrent = () =>
      this.clientRevision === baseRevision && JSON.stringify(this.raw()) === basis;
    const restoreFocus = this.holdFocus();
    const proposal = await session.author(baseDefinition, baseRevision, isCurrent, consolidate);
    if (proposal && !this.lifetime.destroyed && isCurrent()) {
      this.applyProposal(proposal);
      // The first plan replaces the request with settings; reading starts at their heading.
      if (!baseDefinition) restoreFocus('plan-title');
    }
  }

  protected finishEditing() {
    if (this.locked()) return;
    if (this.blocker()) {
      this.fields().markAsTouched();
      this.activityError.set({ message: this.blocker() });
      return;
    }
    const restoreFocus = this.holdFocus();
    this.editing.set(false);
    this.activityError.set(undefined);
    restoreFocus('edit-activity');
  }

  protected editContent() {
    if (this.locked()) return;
    const restoreFocus = this.holdFocus();
    this.editing.set(true);
    restoreFocus('document-title');
  }

  protected adoptQuestions() {
    return this.activityAction('adopt', { questionIds: [...this.stale().questions] });
  }

  protected replaceSource(id: string) {
    if (this.locked()) return;
    const text = sourceText(this.raw(), id);
    if (text === undefined) return;
    this.sourceFields().reset({ id, text });
    this.replacementError.set('');
    afterNextRender(() => this.sourceFields.text().focusBoundControl(), {
      injector: this.injector,
    });
  }

  protected cancelSourceReplacement() {
    if (this.locked()) return;
    const restoreFocus = this.holdFocus();
    const id = this.sourceReplacement().id;
    this.sourceFields().reset({ id: '', text: '' });
    this.replacementError.set('');
    restoreFocus('replace-source-' + id);
  }

  protected acceptSourceReplacement() {
    if (this.locked()) return;
    this.sourceFields.text().markAsTouched();
    if (this.sourceFields.text().invalid()) {
      this.replacementError.set('תקנו את הטקסט המסומן.');
      return;
    }
    const { id, text } = this.sourceReplacement();
    const next = replaceSourceText(this.raw(), id, text);
    if (!next) return;
    const restoreFocus = this.holdFocus();
    this.raw.set(next);
    this.confirmed.update((values) => ({ ...values, [id]: text }));
    this.recordEdit('');
    this.sourceFields().reset({ id: '', text: '' });
    this.replacementError.set('');
    restoreFocus('replace-source-' + id);
  }

  /** Explicit question recovery replaces the existing set after parent confirmation. */
  protected regenerateQuestions() {
    return window.confirm('ליצור את כל השאלות מחדש? השאלות הנוכחיות יוחלפו.')
      ? this.activityAction('GenerateQuestions')
      : undefined;
  }
  /** Every content action flushes one validated checkpoint first; no failed save can start work. */
  protected async activityAction(
    action: 'save' | 'release' | 'adopt' | GenerationKind,
    target?: {
      materialIds?: string[];
      questionIds?: string[];
      message?: string;
      target?: RevisionTarget;
      sources?: StartGeneration['sources'];
    },
  ) {
    if (this.locked()) return;
    if (action !== 'save' && action !== 'release' && action !== 'adopt' && !this.aiConfigured())
      return;
    // Shows every field's state; fields take touched only while enabled, so before the lock.
    this.fields().markAsTouched();
    await this.runDraftRequest(async () => {
      this.chatSession.cancelAuthor();
      this.activityError.set(undefined);
      const saved = await this.flushDraft();
      if (!saved || this.lifetime.destroyed) return;
      this.editing.set(false);
      if (action === 'save') return;
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
        // Every saved diagnostic blocks release, and each already shows at its content or review.
        if (Object.keys(saved.diagnostics).length) {
          this.activityError.set({ message: 'תקנו את המסומן לפני אישור הפעילות.' });
          return;
        }
        // Asked only once release can succeed: it freezes this version for assignment.
        if (
          !window.confirm(
            'לאשר את הפעילות? היא תישמר כעותק קבוע שאפשר להקצות, ושינויים ייעשו בטיוטה חדשה.',
          )
        )
          return;
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
          ...(target?.message ? { message: target.message } : {}),
          ...(target?.target ? { target: target.target } : {}),
          ...(target?.sources?.length ? { sources: target.sources } : {}),
        };
        this.startRecovery.set({ draftId: saved.id, request });
        await this.submitOperation(saved.id, request);
        if (action === 'Create' || action === 'GenerateQuestions') this.revealProgress();
      }
    });
  }

  protected async reloadActivity() {
    const saved = this.saved();
    if (!saved || this.saving()) return;
    if (this.dirty() && !window.confirm('טעינת הגרסה השמורה תחליף את השינויים שלא נשמרו. להמשיך?'))
      return;
    await this.runDraftRequest(async () => {
      const latest = await this.observer.read();
      this.markLocalChange();
      this.operationClientRevision = this.clientRevision;
      this.sourceReplacement.set({ id: '', text: '' });
      this.receiveObservation(latest, true);
      this.history.clear();
      this.activityError.set(undefined);
    });
  }

  protected async recoverStart() {
    const pending = this.startRecovery();
    if (!pending || this.saving()) return;
    await this.runDraftRequest(async () => {
      await this.submitOperation(pending.draftId, pending.request);
      this.activityError.set(undefined);
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
      this.receiveObservation(await this.observer.read(result));
    });
  }

  /** Explicit copy of the frozen snapshot into a new draft; no AI call and the snapshot never changes. */
  protected async copyReleased() {
    const id = (this.available() ?? this.saved())?.releasedSnapshotId;
    if (!id || this.copying()) return;
    this.copying.set(true);
    this.activityError.set(undefined);
    try {
      const draft = await this.api.copySnapshot(id, this.lifetime);
      if (!this.lifetime.destroyed) await this.router.navigate(['/activities', draft.id]);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.activityError.set({
          message: writeError(
            apiError(error),
            error,
            'ייתכן שהטיוטה נוצרה. בדקו במרחב שלנו לפני ניסיון נוסף.',
          ),
        });
    } finally {
      if (!this.lifetime.destroyed) this.copying.set(false);
    }
  }

  /** Native route guard and browser-close warning protect local-only keystrokes. */
  canLeave() {
    return !this.localWork() || window.confirm('יש שינויים שלא נשמרו. לצאת מהעמוד?');
  }

  protected beforeUnload(event: BeforeUnloadEvent) {
    if (this.localWork()) event.preventDefault();
  }

  /** Local setup edits coalesce for Undo; every edit clears replies about the earlier plan. */
  private recordEdit(key: string) {
    if (!this.saved()) {
      if (!this.history.record(key)) return;
    }
    this.markLocalChange();
    this.chatSession.clearFeedback();
    this.notice.set('');
  }

  /** A local change: a pending authoring reply and the clarification thread no longer apply. */
  protected markLocalChange() {
    this.clientRevision++;
    this.chatSession.resetContext();
  }

  private applyProposal(plan: LearningPlan) {
    this.history.push();
    this.raw.set(workspaceForm(plan));
    this.confirmed.set(fixedSources(plan, this.confirmed()));
    this.clientRevision++;
    this.history.checkpoint();
  }

  /** Runs one draft request under the shared saving lock; a failure keeps local work and says what to check. */
  private async runDraftRequest(work: () => Promise<void>) {
    const restoreFocus = this.holdFocus();
    this.observer.suspend();
    this.saving.set(true);
    try {
      await work();
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.activityError.set({ message: this.activityFailure(error), reload: true });
    } finally {
      if (!this.lifetime.destroyed) {
        this.saving.set(false);
        restoreFocus();
      }
    }
  }

  private async flushDraft(): Promise<ActivityDetail | undefined> {
    const plan = this.projection().value,
      document = this.documentProjection().value;
    if (!plan || !document || this.blocker()) {
      this.activityError.set({ message: this.blocker() });
      return;
    }
    let saved = this.saved();
    if (!saved) {
      saved = await this.api.createActivity(plan, this.lifetime, this.chatSession.authorThread());
      if (this.lifetime.destroyed) return;
      // Keep this workspace and its pending action alive while making reload reopen the durable draft.
      this.location.replaceState('/activities/' + saved.id);
      this.acceptCheckpoint(saved);
      return saved;
    }
    if (!this.dirty()) return saved;
    const previousPlan = saved.plan;
    saved = await this.api.saveActivity(
      saved.id,
      saved.revision,
      plan,
      document,
      this.lifetime,
      plan.materials
        .filter(
          (m) =>
            m.source === 'supplied' &&
            previousPlan.materials.some((p) => p.id === m.id && p.text !== m.text),
        )
        .map((m) => ({ id: m.id, text: m.text! })),
    );
    if (!this.lifetime.destroyed) this.acceptCheckpoint(saved);
    return saved;
  }

  private acceptCheckpoint(saved: ActivityDetail) {
    if (this.lifetime.destroyed) return;
    this.saved.set(saved);
    this.editing.set(false);
    this.raw.set(workspaceForm(saved.plan, saved.document));
    this.confirmed.set(fixedSources(saved.plan));
    this.history.clear();
    this.available.set(undefined);
    this.elsewhere.set(undefined);
  }

  /** Metadata follows the server even without a content revision; only the workspace applies content. */
  private receiveObservation({ draft, operation }: DraftObservation, apply = false) {
    this.readError.set('');
    const id = draft.activeOperationId ?? operation?.id;
    if (id !== this.operationId()) {
      this.operationClientRevision = undefined;
      this.operationId.set(id);
      this.operation.set(operation?.id === id ? operation : undefined);
    } else if (operation) this.operation.set(operation);
    if (apply) this.acceptCheckpoint(draft);
    else this.receiveCheckpoint(draft);
    this.chatSession.settleOperation(draft, operation, this.operationClientRevision !== undefined);
  }

  /** Only an operation-owned checkpoint may replace its unchanged local buffer; ambiguous reads wait for confirmation. */
  private receiveCheckpoint(saved: ActivityDetail) {
    if (this.available()?.revision === saved.revision) this.available.set(saved);
    const current = this.saved();
    if (current && saved.revision === current.revision) {
      // Replies, Stop and review can change metadata without replacing the local editing buffer.
      this.saved.set({
        ...saved,
        plan: current.plan,
        document: current.document,
        diagnostics: current.diagnostics,
        measurements: current.measurements,
      });
      return;
    }
    if (
      saved.revision < (current?.revision ?? 0) ||
      saved.revision < (this.available()?.revision ?? 0)
    )
      return;
    const operation = this.operation();
    const own =
      this.operationClientRevision !== undefined && saved.revision === operation?.expectedRevision;
    if (own && !this.dirty() && this.clientRevision === this.operationClientRevision) {
      const restoreFocus = this.holdFocus();
      this.clientRevision++;
      this.operationClientRevision = this.clientRevision;
      this.acceptCheckpoint(saved);
      // Content that replaces the control holding focus, such as the create action, takes it.
      restoreFocus('document-heading');
    } else {
      if (saved.revision > (this.available()?.revision ?? 0)) this.available.set(saved);
      if (own) this.elsewhere.set(undefined);
      else if (this.operationClientRevision === undefined || !operation || !isRunning(operation))
        this.elsewhere.set('changed');
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
      // Anything but a definite rejection may have started and stays recoverable.
      if (rejected(error)) this.startRecovery.set(undefined);
      throw error;
    }
  }

  private activityFailure(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 409)
      return 'הטיוטה השתנתה בינתיים. השינויים שלכם נשארו כאן. טענו את הגרסה השמורה לפני שממשיכים.';
    return writeError(
      `${apiError(error)} השינויים שלכם נשארו כאן.`,
      error,
      'ייתכן שהבקשה נשמרה. בדקו את הגרסה השמורה לפני ניסיון נוסף.',
    );
  }

  private snapshot(): WorkspaceSnapshot {
    return { raw: structuredClone(this.raw()), confirmed: { ...this.confirmed() } };
  }
}
