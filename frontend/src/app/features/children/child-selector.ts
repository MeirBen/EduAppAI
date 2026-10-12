import { ChangeDetectionStrategy, Component, computed, inject, input, model } from '@angular/core';
import { ParentChildrenApi } from '../../core/api/parent-children-api';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { parentTaskError } from '../../core/api/parent-task-error';

/**
 * Profile selection for parent assignment screens, offering every profile at once. While profiles
 * load, the disabled select says so itself, so no row shifts the layout.
 */
@Component({
  selector: 'app-child-selector',
  imports: [DisabledInteractive],
  templateUrl: './child-selector.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildSelector {
  readonly value = model('');
  readonly label = input('ילד או ילדה');
  /** In a filter toolbar the empty option names the choice, so the label is for screen readers only. */
  readonly hideLabel = input(false);
  readonly emptyLabel = input('בחרו פרופיל');
  readonly allowDisabled = input(false);
  readonly disabled = input(false);
  protected readonly children = inject(ParentChildrenApi).childOptions();
  protected readonly options = computed(() =>
    this.children.hasValue() ? this.children.value().items : [],
  );
  protected readonly failure = (error: unknown) =>
    parentTaskError(error, 'לא הצלחנו לטעון את הפרופילים. רעננו את הרשימה.');
  protected choose(event: Event) {
    this.value.set((event.target as HTMLSelectElement).value);
  }
}
