import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MeasurementItem } from '../activity-document-view/measurements';

/** Saved length evidence: each text's word count against what its plan asked for. */
@Component({
  selector: 'app-measurement-list',
  template: `
    @if (shown().length) {
      <ul role="list" class="grid gap-1" aria-label="אורך הטקסטים">
        @for (item of shown(); track $index) {
          <li class="note" [attr.data-problem]="item.state === 'blocking' ? '' : null">
            <span class="icon" [class]="icons[item.state]" aria-hidden="true"></span>
            <bdi>{{ item.label }}</bdi> · {{ item.actual }} מילים ·
            {{ item.state === 'advisory' ? 'מבוקש' : 'נדרש' }}: {{ item.requirement }}
            @if (item.state === 'met') {
              <span class="sr-only">· עומד בדרישה</span>
            } @else if (item.state === 'blocking') {
              · התאימו את האורך לפני אישור הפעילות
            }
          </li>
        }
      </ul>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MeasurementList {
  readonly items = input.required<MeasurementItem[]>();
  readonly scope = input<string>();
  protected readonly shown = computed(() =>
    this.scope() ? this.items().filter((item) => item.scope === this.scope()) : this.items(),
  );
  protected readonly icons: Record<MeasurementItem['state'], string> = {
    advisory: 'icon-length',
    met: 'icon-check',
    blocking: 'icon-alert',
  };
}
