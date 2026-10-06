import { HttpErrorResponse } from '@angular/common/http';

/** Hebrew plain-text errors; hides framework/provider details. Render through interpolation. */
/** A client error is a definite rejection: the server applied nothing. */
export function rejected(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500;
}

export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'משהו השתבש. אפשר לנסות שוב.';
  if (error.status === 0) return 'לא ניתן להתחבר לשרת. יש לבדוק את החיבור ולנסות שוב.';
  if (error.status === 401)
    return 'הכניסה לא הצליחה או פגה. יש לבדוק את הדוא״ל והסיסמה ולנסות שוב.';
  if (error.status === 403) return 'אין הרשאה לביצוע הפעולה הזו.';
  if (error.status === 404) return 'הפריט המבוקש לא נמצא.';
  const body: unknown = error.error;
  const problem = body && typeof body === 'object' ? body : {};
  const type = 'type' in problem ? problem.type : null;
  if (error.status === 409 && type === 'urn:family-learning:device-session-conflict')
    return 'כבר קיימת כניסה פעילה בדפדפן. יש להתנתק ממנה או להשתמש בדפדפן נפרד.';
  if (error.status === 409) return 'התבנית השתנתה. יש לרענן את העמוד לפני שמירת גרסה נוספת.';
  if (error.status === 429) return 'הגעתם למגבלת הבקשות. יש לנסות שוב מאוחר יותר.';
  const aiValidation = error.status === 502 && type === 'urn:family-learning:ai-validation';
  if (
    (error.status < 500 || aiValidation) &&
    'errors' in problem &&
    problem.errors &&
    typeof problem.errors === 'object'
  ) {
    const messages = Object.values(problem.errors)
      .flat()
      .filter((value): value is string => typeof value === 'string');
    if (messages.length)
      return [...new Set(messages)].join(' ') + (aiValidation ? ' אפשר לנסות שוב.' : '');
  }
  if (error.status === 502) {
    if (type === 'urn:family-learning:ai-output-limit')
      return 'המודל הגיע למגבלת הפלט לפני שהשלים את התוכן. אפשר לנסות שוב.';
    return 'שירות ה־AI לא החזיר תוכן תקין. אפשר לנסות שוב או לדייק את ההנחיות.';
  }
  if (error.status === 503) return 'שירות ה־AI אינו זמין כרגע. אפשר לנסות שוב בעוד רגע.';
  if (error.status === 504) return 'יצירת התוכן ארכה יותר מדי זמן. אפשר לנסות שוב.';
  if (error.status >= 500) return 'השרת לא הצליח להשלים את הבקשה. אפשר לנסות שוב בעוד רגע.';
  return 'הבקשה לא התקבלה. יש לבדוק את הפרטים או לרענן את העמוד ולנסות שוב.';
}
