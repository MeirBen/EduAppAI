import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { disabled, form, FormField, maxLength, submit, validate } from '@angular/forms/signals';
import { ChildActivation, ChildDevice, ChildSummary } from '../../core/api/assignment-models';
import { Limits, count } from '../../core/api/limits';
import { whenIdle } from '../../core/when-idle';
import { ParentChildrenApi } from '../../core/api/parent-children-api';
import { FieldDirection } from '../../shared/forms/field-direction';
import { FieldErrors } from '../../shared/forms/field-errors';
import { FieldValidity } from '../../shared/forms/field-validity';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { focusHolder } from '../../shared/focus-holder';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { parentTaskError } from '../../core/api/parent-task-error';

/** One local profile buffer; list refreshes never replace edits or retain activation secrets. */
@Component({
  selector: 'app-children-page',
  imports: [
    DatePipe,
    FormField,
    FieldDirection,
    FieldErrors,
    FieldValidity,
    DisabledInteractive,
    LoadingIndicator,
  ],
  templateUrl: './children-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ChildrenPage {
  private readonly api = inject(ParentChildrenApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  private readonly limits = inject(Limits).current;
  protected readonly page = signal(1);
  protected readonly children = this.api.children(this.page);
  protected readonly selected = signal<ChildSummary | undefined>(undefined);
  protected readonly devicePage = signal(1);
  protected readonly devices = this.api.devices(() => this.selected()?.id, this.devicePage);
  protected readonly busy = signal(false);
  private pendingDeviceFocus?: () => void;
  // The revoked row stays visible during reload; wait for its button to disappear before restoring.
  private readonly restoreAfterDevices = whenIdle(
    () => this.busy() || this.devices.isLoading(),
    () => {
      this.pendingDeviceFocus?.();
      this.pendingDeviceFocus = undefined;
    },
  );
  protected readonly error = signal('');
  protected readonly accessError = signal('');
  protected readonly notice = signal('');
  protected readonly activation = signal<ChildActivation | undefined>(undefined);
  protected readonly model = signal({ name: '', enabled: true });
  protected readonly fields = form(this.model, (path) => {
    disabled(path, () => this.busy());
    maxLength(path.name, this.limits.nameLength);
    validate(path.name, ({ value }) =>
      value().trim() ? undefined : { kind: 'required', message: 'יש להזין שם.' },
    );
  });
  protected readonly deviceModel = signal({ label: '' });
  protected readonly deviceFields = form(this.deviceModel, (path) => {
    disabled(path, () => this.busy() || !this.selected()?.enabled);
    maxLength(path.label, this.limits.nameLength);
    validate(path.label, ({ value }) =>
      value().trim() ? undefined : { kind: 'required', message: 'יש להזין שם למכשיר.' },
    );
  });
  protected readonly nameHint = `עד ${count(this.limits.nameLength)} תווים.`;
  protected readonly failure = (error: unknown) =>
    parentTaskError(
      error,
      'הפרופיל השתנה או אינו פעיל. רעננו את הרשימה ובחרו את הפרופיל המעודכן לפני שמירה נוספת. השינויים כאן נשמרו.',
    );
  private readonly dirty = computed(
    () =>
      this.model().name !== (this.selected()?.name ?? '') ||
      this.model().enabled !== (this.selected()?.enabled ?? true),
  );

  constructor() {
    this.lifetime.onDestroy(() => this.activation.set(undefined));
  }
  /** Protects unsaved profile changes on navigation and explicit selection. */
  canLeave() {
    return !this.dirty() || window.confirm('יש שינויים בפרופיל שלא נשמרו. לצאת בלי לשמור?');
  }
  protected beforeUnload(event: BeforeUnloadEvent) {
    if (this.dirty()) event.preventDefault();
  }
  protected select(child?: ChildSummary) {
    if (this.busy() || !this.canLeave()) return;
    const restore = this.holdFocus();
    this.acceptProfile(child);
    this.error.set('');
    this.accessError.set('');
    this.notice.set('');
    restore();
  }
  private acceptProfile(child?: ChildSummary) {
    this.selected.set(child);
    this.fields().reset({ name: child?.name ?? '', enabled: child?.enabled ?? true });
    this.deviceFields().reset({ label: '' });
    this.activation.set(undefined);
    this.devicePage.set(1);
  }
  protected async save(event: Event) {
    event.preventDefault();
    if (this.busy()) return;
    this.error.set('');
    await submit(this.fields, async () => {
      const child = this.selected(),
        { name, enabled } = this.model();
      if (
        child?.enabled &&
        !enabled &&
        !window.confirm(
          'להשבית את הפרופיל? כל המכשירים וקודי ההפעלה יבוטלו. העבודה והציונים יישמרו. הפעלה מחדש תדרוש קוד חדש.',
        )
      )
        return;
      const restore = this.holdFocus();
      this.busy.set(true);
      try {
        const saved = child
          ? await this.api.update(child, name.trim(), enabled, this.lifetime)
          : await this.api.create(name.trim(), this.lifetime);
        if (this.lifetime.destroyed) return;
        this.acceptProfile(saved);
        this.children.reload();
        this.devices.reload();
        this.notice.set('הפרופיל נשמר.');
      } catch (error) {
        if (!this.lifetime.destroyed)
          this.error.set(
            this.failure(error) + ' ייתכן שהפעולה נשמרה. בדקו ברשימה לפני ניסיון נוסף.',
          );
      } finally {
        if (!this.lifetime.destroyed) {
          this.busy.set(false);
          restore();
        }
      }
    });
  }
  protected async issue(event: Event) {
    event.preventDefault();
    const child = this.selected();
    if (this.busy() || !child?.enabled) return;
    this.accessError.set('');
    await submit(this.deviceFields, async () => {
      this.busy.set(true);
      this.activation.set(undefined);
      try {
        const code = await this.api.issue(child.id, this.deviceModel().label.trim(), this.lifetime);
        if (!this.lifetime.destroyed) this.activation.set(code);
      } catch (error) {
        if (!this.lifetime.destroyed)
          this.accessError.set(this.failure(error) + ' יצירת קוד נוסף תבטל כל קוד קודם שטרם נוצל.');
      } finally {
        if (!this.lifetime.destroyed) this.busy.set(false);
      }
    });
  }
  protected async revoke(device: ChildDevice) {
    const child = this.selected();
    if (
      this.busy() ||
      !child ||
      !window.confirm(
        `לבטל את הגישה מהמכשיר "${device.deviceLabel}"? העבודה תישמר. חיבור מחדש ידרוש קוד חדש.`,
      )
    )
      return;
    const restore = this.holdFocus();
    this.busy.set(true);
    this.accessError.set('');
    try {
      await this.api.revoke(child.id, device.id, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.devices.reload();
      this.notice.set('הגישה מהמכשיר בוטלה.');
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.accessError.set(
          this.failure(error) + ' רעננו את רשימת המכשירים כדי לבדוק את הגישה השמורה.',
        );
    } finally {
      if (!this.lifetime.destroyed) {
        this.busy.set(false);
        this.pendingDeviceFocus = restore;
        this.restoreAfterDevices();
      }
    }
  }
}
