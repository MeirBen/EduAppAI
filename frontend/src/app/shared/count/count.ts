import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * An exact count beside a section title or a view's label. Until it is known its pill keeps its
 * place unseen, so nothing shifts when it arrives; an empty count is quiet.
 */
@Component({
  selector: 'app-count',
  template: '{{ value() ?? 0 }}',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'count',
    '[class.invisible]': 'value() === undefined',
    '[attr.data-tone]': "value() === 0 ? 'quiet' : null",
  },
})
export class Count {
  readonly value = input<number>();
}
