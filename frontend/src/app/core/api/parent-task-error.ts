import { HttpErrorResponse } from '@angular/common/http';
import { apiError } from './api-error';

/** These operations never use AI; their conflicts concern saved learning/access state. */
export function parentTaskError(error: unknown, conflict: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 409) return conflict;
    if (error.status >= 500) return 'השרת לא הצליח להשלים את הבקשה. אפשר לנסות שוב בעוד רגע.';
  }
  return apiError(error);
}
