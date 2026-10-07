import { HttpErrorResponse } from '@angular/common/http';

/** A client error is a definite rejection: the server applied nothing. */
export function rejected(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500;
}

/** A failed write's message; unless the server definitely rejected it, it may have applied, so `check` says where to look. */
export function writeError(message: string, error: unknown, check: string): string {
  return rejected(error) ? message : `${message} ${check}`;
}

/** Hebrew plain-text errors; hides framework/provider details. Render through interpolation. */

export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'משהו השתבש. נסו שוב.';
  if (error.status === 0) return 'אין חיבור לשרת. בדקו את החיבור לאינטרנט ונסו שוב.';
  if (error.status === 401)
    return 'הכניסה לא הצליחה או שפג תוקפה. בדקו את הדוא״ל והסיסמה ונסו שוב.';
  if (error.status === 403) return 'אין לכם הרשאה לפעולה הזו.';
  if (error.status === 404) return 'לא מצאנו את מה שחיפשתם. ייתכן שהוא נמחק.';
  const body: unknown = error.error;
  const problem = body && typeof body === 'object' ? body : {};
  const type = 'type' in problem ? problem.type : null;
  if (error.status === 409 && type === 'urn:family-learning:device-session-conflict')
    return 'כבר יש חשבון מחובר בדפדפן הזה. התנתקו ממנו או השתמשו בדפדפן אחר.';
  if (error.status === 409) return 'הנתונים השתנו בינתיים. רעננו את העמוד ונסו שוב.';
  if (error.status === 413) return 'הבקשה ארוכה מדי ליצירה. קצרו את ההגדרות או את התוכן ונסו שוב.';
  if (error.status === 429) return 'נשלחו יותר מדי בקשות. נסו שוב בעוד כמה דקות.';
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
      return [...new Set(messages)].join(' ') + (aiValidation ? ' נסו שוב.' : '');
  }
  if (error.status === 502) {
    if (type === 'urn:family-learning:ai-output-limit')
      return 'התוכן שנוצר היה ארוך מדי ולא הושלם. נסו שוב.';
    return 'שירות ה־AI החזיר תוכן לא תקין. נסו שוב או דייקו את ההנחיות.';
  }
  if (error.status === 503) return 'שירות ה־AI לא זמין כרגע. נסו שוב בעוד רגע.';
  if (error.status === 504) return 'יצירת התוכן נמשכה יותר מדי זמן. נסו שוב.';
  if (error.status >= 500) return 'השרת לא הצליח להשלים את הבקשה. נסו שוב בעוד רגע.';
  return 'הבקשה לא התקבלה. בדקו את הפרטים, או רעננו את העמוד ונסו שוב.';
}
