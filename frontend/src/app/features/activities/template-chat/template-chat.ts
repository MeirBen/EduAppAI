import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { AuthoringTurn } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Presentation only: the route owns message text, unresolved context and all requests. */
@Component({
  imports: [FormField, LoadingIndicator],
  selector: 'app-template-chat',
  templateUrl: './template-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateChat {
  readonly fields = input.required<FieldTree<{ message: string; consolidated: string }>>();
  readonly configured = input(false);
  readonly busy = input(false);
  readonly locked = input(false);
  readonly clarification = input('');
  readonly context = input<AuthoringTurn[]>([]);
  readonly consolidationRequired = input(false);
  /** A plan exists, so the form becomes a compact change request instead of the first description. */
  readonly refining = input(false);
  /** ID of the owner's visible heading that names the first-description field. */
  readonly labelledBy = input('');
  readonly sent = output<void>();
  readonly consolidated = output<void>();
  readonly cancelled = output<void>();
}
