import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideLimits, limits } from '../../core/api/limits.fixture';
import { ChildrenPage } from './children-page';

const child = {
  id: 'child',
  name: 'נועה',
  enabled: true,
  revision: 1,
  createdAtUtc: '2026-10-01T00:00:00Z',
};
const page = (items: unknown[], hasMore = false, number = 1) => ({
  items,
  page: number,
  pageSize: 25,
  hasMore,
});

describe('Parent child management', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideLimits(),
      ],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });
  async function open() {
    const fixture = TestBed.createComponent(ChildrenPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/children?page=1').flush(page([child], true));
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const type = (id: string, value: string) => {
      const field = root.querySelector<HTMLInputElement>('#' + id)!;
      field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true }));
    };
    const submit = () =>
      root.querySelector('#profile-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const edit = async () => {
      root.querySelector<HTMLButtonElement>('[data-edit-child]')!.click();
      fixture.detectChanges();
      http.expectOne('/api/children/child/devices?page=1').flush(page([]));
      await fixture.whenStable();
    };
    return { fixture, http, root, type, submit, edit };
  }
  it('validates the server name bound and blocks duplicate creates', async () => {
    const { fixture, http, root, type, submit } = await open();
    type('child-name', '   ');
    submit();
    await fixture.whenStable();
    http.expectNone((r) => r.method === 'POST');
    expect(root.querySelector<HTMLInputElement>('#child-name')!.maxLength).toBe(limits.nameLength);
    type('child-name', 'יעל');
    submit();
    TestBed.tick();
    const create = http.expectOne('/api/children');
    expect(create.request.body).toEqual({ name: 'יעל' });
    submit();
    http.expectNone((r) => r.method === 'POST');
    create.flush({ ...child, name: 'יעל' });
    (await vi.waitFor(() => http.expectOne('/api/children?page=1'))).flush(
      page([{ ...child, name: 'יעל' }]),
    );
    http.expectOne('/api/children/child/devices?page=1').flush(page([]));
  });
  it('keeps profile edits after conflict and refreshes the list without replacing them', async () => {
    const { fixture, http, root, type, submit, edit } = await open();
    await edit();
    type('child-name', 'שם מקומי');
    submit();
    TestBed.tick();
    const update = http.expectOne('/api/children/child');
    expect(update.request.body).toEqual({ name: 'שם מקומי', enabled: true, expectedRevision: 1 });
    update.flush({}, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    await vi.waitFor(() => expect(root.textContent).toContain('הפרופיל השתנה'));
    expect(root.textContent).not.toContain('התבנית השתנתה');
    root.querySelector<HTMLButtonElement>('#refresh-children')!.click();
    TestBed.tick();
    http
      .expectOne('/api/children?page=1')
      .flush(page([{ ...child, name: 'שם בשרת', revision: 2 }]));
    await fixture.whenStable();
    expect(root.querySelector<HTMLInputElement>('#child-name')!.value).toBe('שם מקומי');
  });
  it('displays a transient activation code, clears it on selection and cancels outstanding issuance', async () => {
    const { fixture, http, root, type, edit } = await open();
    const persisted = vi.spyOn(Storage.prototype, 'setItem');
    await edit();
    type('device-label', 'טאבלט');
    const issue = () =>
      root
        .querySelector('#activation-form')!
        .dispatchEvent(new Event('submit', { cancelable: true }));
    issue();
    TestBed.tick();
    http
      .expectOne('/api/children/child/activation')
      .flush({ code: 'one-time-secret', expiresAtUtc: '2026-10-06T12:10:00Z' });
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(root.querySelector('[data-activation-code]')?.textContent).toBe('one-time-secret'),
    );
    expect(persisted).not.toHaveBeenCalled();
    root.querySelector<HTMLButtonElement>('#new-child')!.click();
    await fixture.whenStable();
    expect(root.textContent).not.toContain('one-time-secret');
    await edit();
    type('device-label', 'טלפון');
    issue();
    TestBed.tick();
    const request = http.expectOne('/api/children/child/activation');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
  it('restores keyboard focus after the revocation refresh removes its button', async () => {
    const { fixture, http, root, edit } = await open();
    await edit();
    const device = {
      id: 'grant',
      deviceLabel: 'טאבלט',
      createdAtUtc: child.createdAtUtc,
      expiresAtUtc: '2026-11-01T00:00:00Z',
      revokedAtUtc: null,
    };
    const refreshButton = Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'רענון המכשירים',
    )!;
    refreshButton.click();
    TestBed.tick();
    http.expectOne('/api/children/child/devices?page=1').flush(page([device]));
    await fixture.whenStable();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const revoke = root.querySelector<HTMLButtonElement>('[data-revoke]')!;
    revoke.focus();
    revoke.click();
    http.expectOne('/api/children/child/devices/grant').flush(null);
    const refresh = await vi.waitFor(() => http.expectOne('/api/children/child/devices?page=1'));
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    refresh.flush(page([{ ...device, revokedAtUtc: '2026-10-06T12:00:00Z' }]));
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(document.activeElement).toBe(root.querySelector('#device-grant')),
    );
  });

  it('requires confirmation for disabling and revoking, and pages devices and profiles', async () => {
    const { fixture, http, root, edit, submit } = await open();
    root.querySelector<HTMLButtonElement>('#children-next')!.click();
    TestBed.tick();
    http.expectOne('/api/children?page=2').flush(page([child], false, 2));
    await fixture.whenStable();
    await edit();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    root.querySelector<HTMLInputElement>('#child-enabled')!.click();
    submit();
    await fixture.whenStable();
    expect(confirm).toHaveBeenCalled();
    http.expectNone((r) => r.method === 'PUT');
    confirm.mockReturnValue(true);
    submit();
    TestBed.tick();
    http.expectOne('/api/children/child').flush({ ...child, enabled: false, revision: 2 });
    (await vi.waitFor(() => http.expectOne('/api/children?page=2'))).flush(
      page([{ ...child, enabled: false, revision: 2 }], false, 2),
    );
    http.expectOne('/api/children/child/devices?page=1').flush(
      page(
        [
          {
            id: 'grant',
            deviceLabel: 'טאבלט',
            createdAtUtc: child.createdAtUtc,
            expiresAtUtc: '2026-11-01T00:00:00Z',
            revokedAtUtc: null,
          },
        ],
        true,
      ),
    );
    await fixture.whenStable();
    confirm.mockReturnValue(false);
    root.querySelector<HTMLButtonElement>('[data-revoke]')!.click();
    http.expectNone((r) => r.method === 'DELETE');
    confirm.mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('[data-revoke]')!.click();
    http.expectOne('/api/children/child/devices/grant').flush(null);
    (await vi.waitFor(() => http.expectOne('/api/children/child/devices?page=1'))).flush(
      page([], true),
    );
    await fixture.whenStable();
    root.querySelector<HTMLButtonElement>('#devices-next')!.click();
    TestBed.tick();
    http.expectOne('/api/children/child/devices?page=2').flush(page([], false, 2));
  });
});
