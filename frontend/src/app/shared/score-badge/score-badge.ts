import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * A final score as a grade out of 100, in a badge whose band reads at a glance: `high` from 80,
 * `mid` from 55 and `low` below. The number stays its text, so color never carries the score
 * alone; a score without possible points shows nothing.
 */
@Component({
  selector: 'app-score-badge',
  imports: [DecimalPipe],
  template: `
    @if (grade(); as value) {
      <span class="badge" [attr.data-band]="band()"
        ><span class="sr-only">ציון </span>{{ value.shown | number: '1.0-1' }}</span
      >
    }
  `,
  host: { class: 'contents' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScoreBadge {
  readonly final = input.required<number | null>();
  readonly possible = input.required<number | null>();
  protected readonly grade = computed(() => {
    const final = this.final(),
      possible = this.possible();
    // One decimal, as shown, so the band always matches what the badge reads.
    return final !== null && possible
      ? { shown: Math.round((final / possible) * 1000) / 10 }
      : undefined;
  });
  protected readonly band = computed(() => {
    const shown = this.grade()?.shown ?? 0;
    return shown >= 80 ? 'high' : shown >= 55 ? 'mid' : 'low';
  });
}
