import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  model,
  signal,
} from '@angular/core';
import { ParentChildrenApi } from '../../core/api/parent-children-api';
import { ChildSummary } from '../../core/api/assignment-models';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { parentTaskError } from '../../core/api/parent-task-error';

/** Bounded profile selection for parent assignment screens; paging preserves the chosen child. */
@Component({
  selector: 'app-child-selector',
  imports: [DisabledInteractive, LoadingIndicator],
  templateUrl: './child-selector.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildSelector {
  readonly value = model('');
  readonly label = input('ילד או ילדה');
  readonly emptyLabel = input('בחרו פרופיל');
  readonly allowDisabled = input(false);
  readonly disabled = input(false);
  protected readonly page = signal(1);
  protected readonly children = inject(ParentChildrenApi).children(this.page);
  private readonly selected = signal<ChildSummary | undefined>(undefined);
  protected readonly options = computed(() => {
    const items = this.children.hasValue() ? this.children.value().items : [];
    const selected = this.selected();
    return selected?.id === this.value() && !items.some((child) => child.id === selected.id)
      ? [selected, ...items]
      : items;
  });
  protected readonly failure = (error: unknown) =>
    parentTaskError(error, 'לא ניתן לקרוא את הפרופילים. רעננו את הרשימה.');
  protected choose(event: Event) {
    const id = (event.target as HTMLSelectElement).value;
    this.selected.set(this.options().find((child) => child.id === id));
    this.value.set(id);
  }
}
