import { DestroyRef, DOCUMENT, inject } from '@angular/core';

/**
 * Escape hides an open tooltip until the pointer or focus moves on, as WCAG 1.4.13 asks of content
 * shown on hover or focus. Pressing a control hides its tooltip until the pointer or focus leaves
 * that control, as common toolkits do, so a click never leaves a label hanging over the page.
 * Call once in an injection context; the listeners need no change detection.
 */
export function dismissibleTooltips() {
  const document = inject(DOCUMENT);
  const root = document.documentElement;
  const listening = new AbortController();
  const options = { signal: listening.signal };
  // The pressed control; moving or focusing within it keeps its tooltip hidden.
  let pressed: Element | null = null;
  const hide = (control: Element | null) => {
    pressed = control;
    root.setAttribute('data-tooltips-hidden', '');
  };
  const show = (event: Event) => {
    if (pressed?.contains(event.target as Node)) return;
    pressed = null;
    root.removeAttribute('data-tooltips-hidden');
  };
  document.addEventListener('keydown', (event) => event.key === 'Escape' && hide(null), options);
  document.addEventListener(
    'pointerdown',
    (event) => {
      const control = tooltipOwner(event.target);
      if (control) hide(control);
    },
    options,
  );
  document.addEventListener('pointerover', show, options);
  document.addEventListener('focusin', show, options);
  inject(DestroyRef).onDestroy(() => listening.abort());
}

/** The control whose own tooltip sits at or above `target`, if any. */
function tooltipOwner(target: EventTarget | null): Element | null {
  for (
    let element = target instanceof Element ? target : null;
    element;
    element = element.parentElement
  )
    if ([...element.children].some((child) => child.classList.contains('tooltip'))) return element;
  return null;
}
