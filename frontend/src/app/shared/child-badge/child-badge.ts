import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AssignedChild } from '../../core/api/assignment-models';
import { ScoreBadge } from '../score-badge/score-badge';

/** A child's work at a glance: the banded score once it has one, otherwise the child's name. */
@Component({
  selector: 'app-child-badge',
  imports: [ScoreBadge],
  template: `
    @let work = child();
    @if (work.finalTotal !== null && work.possibleTotal) {
      <app-score-badge
        [label]="work.childName"
        [final]="work.finalTotal"
        [possible]="work.possibleTotal"
      />
    } @else {
      <span class="badge"
        ><bdi>{{ work.childName }}</bdi></span
      >
    }
  `,
  host: { class: 'contents' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildBadge {
  readonly child = input.required<AssignedChild>();
}
