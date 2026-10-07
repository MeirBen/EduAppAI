import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ChildActivation } from './child-activation';
import { ChildAuth } from '../../core/auth/child-auth';

const identity = {
  childId: 'child',
  name: 'נועה',
  expiresAtUtc: '2026-11-01T00:00:00Z',
  answerLength: 200,
};
describe('Child activation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });
  async function open() {
    const fixture = TestBed.createComponent(ChildActivation),
      http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.autoDetectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const code = root.querySelector<HTMLInputElement>('#activation-code')!;
    code.value = 'bcdf-ghjk';
    code.dispatchEvent(new Event('input', { bubbles: true }));
    const send = () =>
      root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    return { fixture, http, navigate, root, code, send };
  }
  it('asks for the whole eight-letter code before sending anything', async () => {
    const { http, root, code, send } = await open();
    code.value = 'bcdf-ghj';
    code.dispatchEvent(new Event('input', { bubbles: true }));
    send();
    await vi.waitFor(() => expect(root.textContent).toContain('8 אותיות'));
    http.expectNone('/api/child/auth/activate');
  });
  it('clears the code and waits for identity-bound CSRF before entering the inbox', async () => {
    const { fixture, http, navigate, code, send } = await open();
    send();
    (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
    const activation = await vi.waitFor(() => http.expectOne('/api/child/auth/activate'));
    // The server reads the code regardless of case, spaces or dashes, so it travels as typed.
    expect(activation.request.body).toEqual({ code: 'bcdf-ghjk' });
    activation.flush(null);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(identity);
    const token = await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'));
    expect(navigate).not.toHaveBeenCalled();
    expect(TestBed.inject(ChildAuth).identity()).toBeNull();
    token.flush({});
    await fixture.whenStable();
    await vi.waitFor(() => expect(code.value).toBe(''));
    expect(navigate).toHaveBeenCalledWith('/child', { replaceUrl: true });
  });
  it.each([true, false])(
    'checks a lost activation response without replaying the code (cookie arrived: %s)',
    async (cookie) => {
      const { fixture, http, root, code, send, navigate } = await open();
      send();
      (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
      (await vi.waitFor(() => http.expectOne('/api/child/auth/activate'))).error(
        new ProgressEvent('error'),
      );
      await fixture.whenStable();
      await vi.waitFor(() => expect(code.value).toBe(''));
      expect(navigate).not.toHaveBeenCalled();
      await vi.waitFor(() => expect(root.querySelector('#check-child-access')).not.toBeNull());
      root.querySelector<HTMLButtonElement>('#check-child-access')!.click();
      const me = http.expectOne('/api/child/auth/me');
      if (cookie) {
        me.flush(identity);
        (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
      } else me.flush({}, { status: 401, statusText: 'Unauthorized' });
      await fixture.whenStable();
      http.expectNone('/api/child/auth/activate');
      expect(navigate.mock.calls.length).toBe(cookie ? 1 : 0);
      if (!cookie) expect(root.textContent).toContain('קוד חדש');
    },
  );
  it('recovers a failed post-activation CSRF refresh without redeeming again', async () => {
    const { fixture, http, root, send, navigate } = await open();
    send();
    (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
    (await vi.waitFor(() => http.expectOne('/api/child/auth/activate'))).flush(null);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(identity);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush(
      {},
      { status: 503, statusText: 'Unavailable' },
    );
    await vi.waitFor(() => expect(root.querySelector('#check-child-access')).not.toBeNull());
    expect(navigate).not.toHaveBeenCalled();
    expect(TestBed.inject(ChildAuth).identity()).toBeNull();
    root.querySelector<HTMLButtonElement>('#check-child-access')!.click();
    http.expectOne('/api/child/auth/me').flush(identity);
    (await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'))).flush({});
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith('/child', { replaceUrl: true });
    http.expectNone('/api/child/auth/activate');
  });
  it('cancels activation when the page is destroyed', async () => {
    const { fixture, http, send } = await open();
    send();
    const request = await vi.waitFor(() => http.expectOne('/api/child/auth/csrf'));
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
