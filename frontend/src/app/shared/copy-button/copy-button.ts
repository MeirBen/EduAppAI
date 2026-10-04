import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';

type CopyState = 'ready' | 'copied' | 'failed';

/** Copies a read-only box's text, such as an AI response; editable fields copy natively. */
@Component({
  selector: 'app-copy-button',
  template: `
    <button type="button" class="icon-button icon-button-quiet" (click)="copy()">
      <span class="icon" [class]="feedback[state()].icon" aria-hidden="true"></span
      ><span class="tooltip">{{ feedback[state()].message || label() }}</span>
    </button>
    <span role="status" class="sr-only">{{ feedback[state()].message }}</span>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CopyButton {
  readonly text = input.required<string>();
  readonly label = input.required<string>();
  protected readonly state = signal<CopyState>('ready');
  protected readonly feedback: Record<CopyState, { icon: string; message: string }> = {
    ready: { icon: 'icon-copy', message: '' },
    copied: { icon: 'icon-check', message: 'הועתק' },
    failed: { icon: 'icon-alert', message: 'לא ניתן להעתיק. סמנו את הטקסט והעתיקו ידנית.' },
  };
  private reset?: ReturnType<typeof setTimeout>;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.reset));
  }

  /** A success mark reverts after a moment; a failure stays until the next attempt. */
  protected async copy() {
    clearTimeout(this.reset);
    try {
      await navigator.clipboard.writeText(this.text());
      this.state.set('copied');
      this.reset = setTimeout(() => this.state.set('ready'), 2000);
    } catch {
      this.state.set('failed');
    }
  }
}
