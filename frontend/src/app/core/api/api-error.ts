import { HttpErrorResponse } from '@angular/common/http';

/** Hebrew plain-text errors; hides framework/provider details. Render through interpolation. */
export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'משהו השתבש. אפשר לנסות שוב.';
  if (error.status === 0) return 'לא ניתן להתחבר לשרת. יש לבדוק את החיבור ולנסות שוב.';
  if (error.status === 401)
    return 'הכניסה לא הצליחה או פגה. יש לבדוק את הדוא״ל והסיסמה ולנסות שוב.';
  if (error.status === 403) return 'אין הרשאה לביצוע הפעולה הזו.';
  if (error.status === 404) return 'הפריט המבוקש לא נמצא.';
  if (error.status === 409) return 'התבנית השתנתה. יש לרענן את העמוד לפני שמירת גרסה נוספת.';
  if (error.status === 429) return 'הגעתם למגבלת הבקשות. יש לנסות שוב מאוחר יותר.';
  const problem: unknown = error.error;
  if (error.status === 502) {
    if (
      problem &&
      typeof problem === 'object' &&
      'type' in problem &&
      problem.type === 'urn:family-learning:ai-output-limit'
    )
      return 'המודל הגיע למגבלת הפלט לפני שהשלים את התוכן. לא נשמר דבר. אפשר לנסות שוב.';
    return 'שירות ה־AI לא החזיר תוכן תקין. לא נשמר דבר. אפשר לנסות שוב או לדייק את ההנחיות.';
  }
  if (error.status === 503)
    return 'שירות ה־AI אינו זמין כרגע. יש לבדוק את החיבור לשירות או לנסות שוב בעוד רגע.';
  if (error.status === 504) return 'יצירת התוכן ארכה יותר מדי זמן. לא נשמר דבר. אפשר לנסות שוב.';
  if (error.status >= 500) return 'השרת לא הצליח להשלים את הבקשה. אפשר לנסות שוב בעוד רגע.';
  if (problem && typeof problem === 'object') {
    if ('errors' in problem && problem.errors && typeof problem.errors === 'object') {
      const messages = Object.values(problem.errors)
        .flat()
        .filter((value): value is string => typeof value === 'string');
      if (messages.length) return messages.join(' ');
    }
  }
  return 'הבקשה לא התקבלה. יש לבדוק את הפרטים או לרענן את העמוד ולנסות שוב.';
}
