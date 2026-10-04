import { afterNextRender, DOCUMENT, inject, Injector } from '@angular/core';

/**
 * Keeps keyboard focus where the parent acted when an action replaces or removes the control that
 * held it. Create in an injection context; call the result before the change, and the function it
 * returns once the change has applied. Focus that fell to the page returns to the control, or when
 * it is gone to the successor its owner names, else to the heading of the nearest labelled region
 * (a card, item or section) that contained it and still exists. Focus the parent moved elsewhere
 * is never taken back, and nothing scrolls.
 */
export function focusHolder() {
  const document = inject(DOCUMENT);
  const injector = inject(Injector);
  return () => {
    const origin = document.activeElement;
    const headings: string[] = [];
    for (let region = origin?.closest('[aria-labelledby]'); region;) {
      headings.push(region.getAttribute('aria-labelledby')!.split(' ')[0]);
      region = region.parentElement?.closest('[aria-labelledby]');
    }
    return (successorId?: string) =>
      afterNextRender(
        () => {
          if (document.activeElement !== document.body) return;
          if (origin instanceof HTMLElement && origin.isConnected && !origin.matches(':disabled'))
            return origin.focus({ preventScroll: true });
          const target =
            (successorId && document.getElementById(successorId)) ||
            headings.map((id) => document.getElementById(id)).find(Boolean);
          if (!target) return;
          // A heading is a script focus target, so it takes focus without joining the tab order.
          if (!target.matches('a[href], button, input, select, textarea, summary, [tabindex]'))
            target.tabIndex = -1;
          target.focus({ preventScroll: true });
        },
        { injector },
      );
  };
}
