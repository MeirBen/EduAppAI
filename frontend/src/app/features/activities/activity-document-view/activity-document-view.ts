import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { EditableActivity, RevisionTarget } from '../../../core/api/models';
import { ContentIssues } from '../activity-presentation';
import { CopyButton } from '../../../shared/copy-button/copy-button';
import { MeasurementItem } from '../activity-document-view/measurements';
import { MeasurementList } from '../measurement-list/measurement-list';

/** Read-only parent content shared by the workspace and frozen preview. Answers render only as text. */
@Component({
  selector: 'app-activity-document-view',
  imports: [MeasurementList, CopyButton],
  templateUrl: './activity-document-view.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-4' },
})
export class ActivityDocumentView {
  readonly measurements = input<MeasurementItem[]>([]);
  readonly canAsk = input(false);
  readonly asked = output<RevisionTarget>();
  readonly issues = input<ContentIssues>({ materials: new Map(), questions: new Map() });
  protected questionIssues(id: string): string[] {
    return Object.values(this.issues().questions.get(id) ?? {});
  }
  readonly document = input.required<EditableActivity>();
}
