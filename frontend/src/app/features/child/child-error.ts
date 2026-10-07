import { HttpErrorResponse } from '@angular/common/http';

/** Maps an HTTP error or status to safe, child-specific feedback; never renders server payloads or parent/AI recovery instructions. */
export function childError(error: unknown) {
  switch (childStatus(error)) {
    case 401:
      return 'צריך להיכנס מחדש. בקשו מההורה קוד חדש.';
    case 403:
      return 'משהו לא עבד. רעננו את העמוד ונסו שוב.';
    case 404:
      return 'הפעילות לא זמינה. חזרו לרשימת הפעילויות.';
    case 410:
      return 'הפעילות בוטלה, אז אי אפשר להמשיך בה.';
    case 409:
      return 'התשובות השתנו במקום אחר. לחצו על "בדיקת עדכונים" לפני שממשיכים.';
    case 400:
      return 'יש תשובה שלא הצלחנו לקבל. בדקו מה כתבתם ונסו שוב.';
    case 429:
      return 'יותר מדי לחיצות ברצף. נסו שוב עוד רגע.';
    default:
      return 'לא הצלחנו להביא את הפעילויות. נסו שוב עוד רגע.';
  }
}
export function childStatus(error: unknown) {
  return typeof error === 'number'
    ? error
    : error instanceof HttpErrorResponse
      ? error.status
      : undefined;
}
