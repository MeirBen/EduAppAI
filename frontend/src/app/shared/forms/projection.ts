import { ValidationError } from '@angular/forms/signals';

/** A broken rule at the path of the workspace field that holds it; an empty path is the whole form. */
export interface FieldIssue {
  path: readonly (string | number)[];
  message: string;
}

/** A form's canonical HTTP value, present only when it has no issues. */
export interface Projection<T> {
  value?: T;
  errors: FieldIssue[];
}

/** Attaches each issue to its field, so every message shows where the parent can fix it. */
export function validationErrors(root: unknown, issues: readonly FieldIssue[]) {
  return issues.map(({ path, message }): ValidationError.WithOptionalFieldTree => ({
    kind: 'projection',
    message,
    // Field trees index like the model they mirror; a path it no longer has means the whole form.
    fieldTree: path.reduce((tree: any, key) => tree?.[key], root),
  }));
}
