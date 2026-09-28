import { HttpErrorResponse } from '@angular/common/http';

export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
  if (error.status === 0) return 'Cannot reach the server. Check your connection and try again.';
  if (error.status === 401) return 'Please sign in again, or check your email and password.';
  if (error.status === 404) return 'This item could not be found.';
  if (error.status === 429) return 'Too many attempts. Wait a minute and try again.';
  if (error.status >= 500) return 'The server could not finish your request. Please try again.';
  const problem: unknown = error.error;
  if (problem && typeof problem === 'object') {
    if ('errors' in problem && problem.errors && typeof problem.errors === 'object') {
      const messages = Object.values(problem.errors)
        .flat()
        .filter((value): value is string => typeof value === 'string');
      if (messages.length) return messages.join(' ');
    }
    if ('title' in problem && typeof problem.title === 'string') return problem.title;
  }
  return 'Check your details and try again.';
}
