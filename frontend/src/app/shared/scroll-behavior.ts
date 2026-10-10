/** Programmatic scrolls glide, or jump when the parent prefers reduced motion. */
export function scrollBehavior(document: Document): ScrollBehavior {
  return document.defaultView?.matchMedia('(prefers-reduced-motion: reduce)').matches
    ? 'instant'
    : 'smooth';
}
