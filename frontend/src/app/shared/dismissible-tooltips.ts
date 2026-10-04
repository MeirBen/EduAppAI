import { DestroyRef, DOCUMENT, inject } from '@angular/core';

/**
 * Escape hides an open tooltip until the pointer or focus moves on, as WCAG 1.4.13 asks of content
 * shown on hover or focus. Call once in an injection context; the listeners need no change detection.
 */
export function dismissibleTooltips() {
  const document = inject(DOCUMENT);
  const root = document.documentElement;
  const listening = new AbortController();
  const show = () => root.removeAttribute('data-tooltips-hidden');
  const options = { signal: listening.signal };
  document.addEventListener(
    'keydown',
    (event) => event.key === 'Escape' && root.setAttribute('data-tooltips-hidden', ''),
    options,
  );
  document.addEventListener('pointerover', show, options);
  document.addEventListener('focusin', show, options);
  inject(DestroyRef).onDestroy(() => listening.abort());
}
