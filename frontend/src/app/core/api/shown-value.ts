import { linkedSignal, Resource } from '@angular/core';

/**
 * What a page shows of a resource: its last value, kept while another page of the same `filter`
 * loads, so paging never collapses a list or drops focus. A new filter (another view, child or
 * owner) or a failed read clears it, so a page never shows another filter's results.
 */
export function shownValue<T>(resource: Resource<T | undefined>, filter?: () => unknown) {
  return linkedSignal<{ filter: unknown; value?: T }, T | undefined>({
    source: () => ({
      filter: filter?.(),
      value: resource.hasValue() ? resource.value() : undefined,
    }),
    computation: ({ filter, value }, previous) =>
      value ??
      (resource.isLoading() && previous?.source.filter === filter ? previous?.value : undefined),
  });
}
