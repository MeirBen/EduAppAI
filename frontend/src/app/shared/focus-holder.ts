import { afterNextRender, DOCUMENT, inject, Injector } from '@angular/core';

/**
 * A pressed button is disabled or replaced while its request runs, which drops keyboard focus to
 * the page. Create in an injection context; call the result before a request, and the function it
 * returns once the request settles. Focus still on the page then returns to the button, or to the
 * heading of the section that contained it; focus the parent moved elsewhere is never taken back.
 */
export function focusHolder() {
  const document = inject(DOCUMENT);
  const injector = inject(Injector);
  return () => {
    const origin = document.activeElement;
    const headingId = origin?.closest('section[aria-labelledby]')?.getAttribute('aria-labelledby');
    return () =>
      afterNextRender(
        () => {
          if (document.activeElement !== document.body) return;
          if (origin instanceof HTMLElement && origin.isConnected && !origin.matches(':disabled'))
            return origin.focus({ preventScroll: true });
          const heading = headingId ? document.getElementById(headingId) : null;
          if (!heading) return;
          // A heading is a script focus target, so it takes focus without joining the tab order.
          heading.tabIndex = -1;
          heading.focus({ preventScroll: true });
        },
        { injector },
      );
  };
}
