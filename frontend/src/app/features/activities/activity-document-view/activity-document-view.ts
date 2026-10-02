import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { EditableActivity } from '../../../core/api/models';
/** Read-only parent content shared by reconciliation and immutable preview. Answers render only as text. */
@Component({
  selector: 'app-activity-document-view',
  imports: [],
  templateUrl: './activity-document-view.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-4' },
})
export class ActivityDocumentView {
  readonly document = input.required<EditableActivity>();
}
