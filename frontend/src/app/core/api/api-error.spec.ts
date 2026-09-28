import { HttpErrorResponse } from '@angular/common/http';
import { apiError } from './api-error';

describe('Hebrew API feedback', () => {
  it.each([0, 400, 401, 403, 404, 409, 429, 500])(
    'hides framework titles for HTTP %s',
    (status) => {
      const message = apiError(
        new HttpErrorResponse({ status, error: { title: 'Internal diagnostic' } }),
      );
      expect(message).toMatch(/[א-ת]/);
      expect(message).not.toContain('Internal diagnostic');
    },
  );

  it('shows API validation feedback but never server-error details', () => {
    const error = { errors: { questionCount: ['המספר מחוץ לטווח המותר.'] } };
    expect(apiError(new HttpErrorResponse({ status: 400, error }))).toBe('המספר מחוץ לטווח המותר.');
    expect(apiError(new HttpErrorResponse({ status: 500, error }))).not.toContain('המספר');
    expect(apiError(new Error('Technical details'))).not.toContain('Technical details');
  });
});
