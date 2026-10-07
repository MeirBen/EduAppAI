import { HttpErrorResponse } from '@angular/common/http';

/** Maps an HTTP error or status to safe, child-specific feedback; never renders server payloads or parent/AI recovery instructions. */
export function childError(error: unknown) {
  switch (childStatus(error)) {
    case 401:
      return 'הגישה מהמכשיר הסתיימה. בקשו מההורה קוד הפעלה חדש.';
    case 403:
      return 'לא הצלחנו לאשר את הבקשה. רעננו את העמוד ונסו שוב.';
    case 404:
      return 'הפעילות אינה זמינה. אפשר לחזור לרשימת הפעילויות.';
    case 410:
      return 'ההורה ביטל את הפעילות. אי אפשר להמשיך לענות או להגיש אותה.';
    case 409:
      return 'התשובות השתנו או שכבר הוגשו. בדקו את העבודה השמורה לפני שממשיכים.';
    case 400:
      return 'לא הצלחנו לקבל את התשובות. בדקו את הערכים שהקלדתם.';
    case 429:
      return 'נשלחו בקשות רבות בזמן קצר. נסו שוב בעוד רגע.';
    default:
      return 'לא הצלחנו לטעון את הפעילויות. נסו שוב בעוד רגע.';
  }
}
export function childStatus(error: unknown) {
  return typeof error === 'number'
    ? error
    : error instanceof HttpErrorResponse
      ? error.status
      : undefined;
}
