import { HttpErrorResponse } from '@angular/common/http';
import { apiError } from './api-error';

describe('Hebrew API feedback', () => {
  it('explains device-session conflicts without presenting a template conflict or server text', () => {
    const error = { type: 'urn:family-learning:device-session-conflict', title: 'private detail' };
    const message = apiError(new HttpErrorResponse({ status: 409, error }));
    expect(message).toContain('דפדפן');
    expect(message).toContain('להתנתק');
    expect(message).not.toContain('התבנית');
    expect(message).not.toContain('private detail');
    expect(apiError(new HttpErrorResponse({ status: 409, error: {} }))).toContain('התבנית');
  });

  it('shows AI validation failures only for the application-owned problem type', () => {
    const error = {
      type: 'urn:family-learning:ai-validation',
      title: 'private diagnostic',
      errors: { questions: ['מספר השאלות שנוצרו אינו תואם למספר שנבחר.'] },
    };
    const message = apiError(new HttpErrorResponse({ status: 502, error }));
    expect(message).toContain('מספר השאלות');
    expect(message).not.toContain('לא נשמר');
    expect(message).not.toContain('private diagnostic');
    expect(
      apiError(new HttpErrorResponse({ status: 502, error: { ...error, type: 'provider-error' } })),
    ).not.toContain('מספר השאלות');
    expect(apiError(new HttpErrorResponse({ status: 500, error }))).not.toContain('מספר השאלות');
  });

  it.each([400, 401, 500])('hides framework titles for HTTP %s', (status) => {
    const message = apiError(
      new HttpErrorResponse({ status, error: { title: 'Internal diagnostic' } }),
    );
    expect(message).toMatch(/[א-ת]/);
    expect(message).not.toContain('Internal diagnostic');
  });

  it('shows API validation feedback but never server-error details', () => {
    const error = {
      errors: { questionCount: ['המספר מחוץ לטווח המותר.'], total: ['המספר מחוץ לטווח המותר.'] },
    };
    expect(apiError(new HttpErrorResponse({ status: 400, error }))).toBe('המספר מחוץ לטווח המותר.');
    expect(apiError(new HttpErrorResponse({ status: 500, error }))).not.toContain('המספר');
    expect(apiError(new Error('Technical details'))).not.toContain('Technical details');
  });

  it('distinguishes an AI output limit from invalid JSON without displaying provider text', () => {
    const error = {
      type: 'urn:family-learning:ai-output-limit',
      title: 'private provider details',
    };
    const message = apiError(new HttpErrorResponse({ status: 502, error }));
    expect(message).toContain('מגבלת הפלט');
    expect(message).not.toContain('לא נשמר');
    expect(message).not.toContain('private provider');
    expect(apiError(new HttpErrorResponse({ status: 504, error }))).not.toContain('מגבלת הפלט');
    expect(apiError(new HttpErrorResponse({ status: 502, error: {} }))).not.toContain('מגבלת הפלט');
  });

  it.each([502, 503, 504])('does not infer persistence from a failed AI call (%s)', (status) => {
    expect(apiError(new HttpErrorResponse({ status }))).not.toContain('לא נשמר');
  });
});
