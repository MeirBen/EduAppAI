import { linkedSignal, Resource } from '@angular/core';

/**
 * What a page shows of a resource: its last value, kept while a new request loads, so a list or a
 * count never collapses, shifts or drops focus mid-change. A failed read, or another `owner` (such
 * as the child a device list belongs to), clears it.
 */
export function shownValue<T>(resource: Resource<T | undefined>, owner?: () => unknown) {
  return linkedSignal<{ owner: unknown; value?: T }, T | undefined>({
    source: () => ({ owner: owner?.(), value: resource.hasValue() ? resource.value() : undefined }),
    computation: ({ owner, value }, previous) =>
      value ??
      (resource.isLoading() && previous?.source.owner === owner ? previous?.value : undefined),
  });
}
