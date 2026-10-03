/** A form's canonical HTTP value, present only when the parent-language errors are empty. */
export interface Projection<T> {
  value?: T;
  errors: string[];
}
