import { HttpResourceRef } from '@angular/common/http';
import { refreshOnReturn } from '../page-visibility';
import { whenIdle } from '../when-idle';
import { libraryChanges } from './library-changes';

/**
 * Keeps a page's list resources current with the family's change notes. Each note, and each return
 * to the page, rereads every list once its running reads and the page's own `busy` writes settle;
 * notes meanwhile coalesce into that one reread. A reread keeps the shown rows until it answers, so
 * pages show their loading state only while a list has no value. Must run in an injection context.
 */
export function liveLists(
  lists: readonly HttpResourceRef<unknown>[],
  options: { path?: string; busy?: () => boolean } = {},
) {
  const updates = libraryChanges(options.path);
  const reload = whenIdle(
    () => !!options.busy?.() || lists.some((list) => list.isLoading()),
    () => {
      for (const list of lists) list.reload();
    },
  );
  updates.changes.subscribe(reload);
  refreshOnReturn(reload);
  return { state: updates.state, reconnect: updates.reconnect, reload };
}
