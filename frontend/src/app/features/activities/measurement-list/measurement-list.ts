import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MeasurementItem } from '../activity-document-view/measurements';

/** Saved length evidence: each text's word count against what its plan asked for. */
@Component({
  selector: 'app-measurement-list',
  template: `
    <ul role="list" class="grid gap-1" aria-label="אורך הטקסטים">
      @for (item of items(); track $index) {
        <li class="note" [attr.data-problem]="item.state === 'blocking' ? '' : null">
          <span class="icon" [class]="icons[item.state]" aria-hidden="true"></span>
          <bdi>{{ item.label }}</bdi> · {{ item.actual }} מילים ·
          {{ item.state === 'advisory' ? 'מבוקש' : 'נדרש' }}: {{ item.requirement }}
          @if (item.state === 'met') {
            <span class="sr-only">· עומד בדרישה</span>
          } @else if (item.state === 'blocking') {
            · יש לקצר או להאריך לפני סימון כמוכנה
          }
        </li>
      }
    </ul>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MeasurementList {
  readonly items = input.required<MeasurementItem[]>();
  protected readonly icons: Record<MeasurementItem['state'], string> = {
    advisory: 'icon-length',
    met: 'icon-check',
    blocking: 'icon-alert',
  };
}
