import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TaskSettings } from '../../core/api/models';

export const difficultyNames = { easy: 'קל', medium: 'בינוני', hard: 'קשה' };

/**
 * An activity's difficulty, relative to its audience. A meter fills one, two or three steps beside
 * the level's name, so the level reads by shape and never borrows a status or score color.
 */
@Component({
  selector: 'app-difficulty-badge',
  template: `
    <span class="badge" [attr.data-level]="level()"
      ><span class="icon text-brand" [class]="'icon-level-' + level()" aria-hidden="true"></span
      ><span class="sr-only">רמת קושי: </span>{{ names[level()] }}</span
    >
  `,
  host: { class: 'contents' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DifficultyBadge {
  readonly level = input.required<TaskSettings['difficulty']>();
  protected readonly names = difficultyNames;
}
