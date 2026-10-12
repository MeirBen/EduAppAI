import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AssignedChild } from '../../core/api/assignment-models';
import { ScoreBadge } from '../score-badge/score-badge';

/** A child's work at a glance: the child's name, and beside it their grade once there is one. */
@Component({
  selector: 'app-child-badge',
  imports: [ScoreBadge],
  template: `
    <span class="badge"
      ><bdi>{{ child().childName }}</bdi></span
    >
    <app-score-badge [final]="child().finalTotal" [possible]="child().possibleTotal" />
  `,
  // The pair sits closer than the badges around it, so each grade reads as its child's.
  host: { class: 'inline-flex items-center gap-1' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildBadge {
  readonly child = input.required<AssignedChild>();
}
