import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Login } from './login';

describe('Sign-in request lifetime', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', component: Login }]),
      ],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('does not treat a saved connection query parameter as a current failure', async () => {
    const harness = await RouterTestingHarness.create('/login?connection=unavailable');
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')).toBeNull();
  });

  async function startSignIn() {
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    for (const [id, value] of [
      ['email', 'parent@example.test'],
      ['password', 'TestOnly!Parent12345'],
    ]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
    return fixture;
  }

  it('opens the application entry route after successful sign-in', async () => {
    const fixture = await startSignIn();
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    http.expectOne('/api/auth/csrf').flush({ token: 'anonymous' });
    (await vi.waitFor(() => http.expectOne('/api/auth/login'))).flush(null);
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({ token: 'signed-in' });
    await fixture.whenStable();
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/'));
  });

  it.each(['token', 'credentials', 'refreshed-token'])(
    'cancels the pending %s request when the login page closes',
    async (stage) => {
      const fixture = await startSignIn();
      const http = TestBed.inject(HttpTestingController);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
      let request = http.expectOne('/api/auth/csrf');
      if (stage !== 'token') {
        request.flush({ token: 'anonymous' });
        request = await vi.waitFor(() => http.expectOne('/api/auth/login'));
      }
      if (stage === 'refreshed-token') {
        request.flush(null);
        request = await vi.waitFor(() => http.expectOne('/api/auth/csrf'));
      }
      fixture.destroy();
      expect(request.cancelled).toBe(true);
      await Promise.resolve();
      expect(navigate).not.toHaveBeenCalled();
    },
  );

  it('keeps credentials fixed and blocks duplicate submissions while signing in', async () => {
    const fixture = await startSignIn();
    const element: HTMLElement = fixture.nativeElement;
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/auth/csrf');
    expect(element.querySelector<HTMLInputElement>('#email')?.disabled).toBe(true);
    expect(element.querySelector<HTMLInputElement>('#password')?.disabled).toBe(true);
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectNone('/api/auth/csrf');
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(element.querySelector<HTMLInputElement>('#email')?.disabled).toBe(false);
    expect(element.querySelector<HTMLInputElement>('#email')?.value).toBe('parent@example.test');
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('does not send credentials when the page closes just as its token arrives', async () => {
    const fixture = await startSignIn();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/auth/csrf').flush({ token: 'anonymous' });
    fixture.destroy();
    await Promise.resolve();
    http.expectNone('/api/auth/login');
  });

  it('does not redirect when the page closes just as the final token arrives', async () => {
    const fixture = await startSignIn();
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    http.expectOne('/api/auth/csrf').flush({ token: 'anonymous' });
    const login = await vi.waitFor(() => http.expectOne('/api/auth/login'));
    login.flush(null);
    const token = await vi.waitFor(() => http.expectOne('/api/auth/csrf'));
    token.flush({ token: 'signed-in' });
    fixture.destroy();
    await fixture.whenStable();
    expect(navigate).not.toHaveBeenCalled();
  });
});
