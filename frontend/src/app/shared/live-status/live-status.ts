import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LiveUpdates } from '../../core/api/library-changes';
import { focusHolder } from '../focus-holder';

/**
 * The one presentation of a page's automatic updates: nothing while they work, and a short note with
 * a way to restart them once the connection was refused. Restarting also rereads what the page shows.
 * The host is its own live region, so while empty it takes no slot in its container.
 */
@Component({
  selector: 'app-live-status',
  template: `
    @if (updates().state() === 'unavailable') {
      <span class="note"
        ><span class="icon icon-alert" aria-hidden="true"></span>העדכונים האוטומטיים נעצרו.</span
      >
      <button type="button" class="text-link" (click)="reconnect()">
        <span class="icon icon-refresh" aria-hidden="true"></span><span>בדיקת עדכונים</span>
      </button>
    }
  `,
  host: { class: 'flex flex-wrap items-center gap-x-4', role: 'status' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LiveStatus {
  readonly updates = input.required<LiveUpdates>();
  private readonly holdFocus = focusHolder();
  protected reconnect() {
    const restoreFocus = this.holdFocus();
    this.updates().reconnect();
    restoreFocus();
  }
}
