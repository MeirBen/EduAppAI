import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { focusHolder } from './focus-holder';

@Component({
  template: `<section aria-labelledby="list-title">
    <h2 id="list-title">רשימה</h2>
    <ol>
      @if (listed()) {
        <li aria-labelledby="item-title">
          <h3 id="item-title">פריט</h3>
          @if (shown()) {
            <button id="remove" type="button">הסרה</button>
          }
        </li>
      }
    </ol>
    <button id="next" type="button">הבא</button>
  </section>`,
})
class Host {
  readonly shown = signal(true);
  readonly listed = signal(true);
  readonly holdFocus = focusHolder();
}

async function render() {
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const root: HTMLElement = fixture.nativeElement;
  root.querySelector<HTMLButtonElement>('#remove')!.focus();
  return { fixture, host: fixture.componentInstance, root };
}

describe('focusHolder', () => {
  it('moves focus from a removed control to the successor its owner names', async () => {
    const { fixture, host } = await render();
    const restore = host.holdFocus();
    host.shown.set(false);
    restore('next');
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('next');
  });

  it('falls back to the nearest labelled region heading without a successor', async () => {
    const { fixture, host } = await render();
    const restore = host.holdFocus();
    host.shown.set(false);
    restore();
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('item-title');
  });

  it('falls back to the nearest surviving region when the control region is gone', async () => {
    const { fixture, host } = await render();
    const restore = host.holdFocus();
    host.listed.set(false);
    restore();
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('list-title');
  });

  it('never takes focus back from a control the parent chose', async () => {
    const { fixture, host, root } = await render();
    const restore = host.holdFocus();
    root.querySelector<HTMLButtonElement>('#next')!.focus();
    host.shown.set(false);
    restore('list-title');
    await fixture.whenStable();
    expect(document.activeElement?.id).toBe('next');
  });
});
