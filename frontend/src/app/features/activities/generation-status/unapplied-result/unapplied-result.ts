import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ActivityDocumentView } from '../../activity-document-view/activity-document-view';
import { UnappliedCandidate } from '../candidate-edit';
import { DisabledInteractive } from '../../../../shared/disabled-interactive';

/**
 * One operation result that was not applied, for inspection. Transfer is an explicit parent action
 * that replaces the matching part of the local edit; the workspace performs it.
 */
@Component({
  selector: 'app-unapplied-result',
  imports: [DisabledInteractive, ActivityDocumentView],
  templateUrl: './unapplied-result.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnappliedResult {
  readonly candidate = input.required<UnappliedCandidate>();
  readonly locked = input(false);
  readonly transferred = output<void>();
}
