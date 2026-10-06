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
  updatedAtUtc: null,
  grade: null,
  age: null,
  ageConfirmedAtUtc: null,
  hasAssignments: false,
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
  async function open(profile: object = child) {
    const fixture = TestBed.createComponent(ChildrenPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/children?page=1').flush(page([profile], true));
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
    expect(create.request.body).toEqual({
      name: 'יעל',
      details: { grade: null, age: null },
    });
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
    expect(update.request.body).toEqual({
      name: 'שם מקומי',
      enabled: true,
      expectedRevision: 1,
      details: { grade: null, age: null },
    });
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
  it.each(['-1', '121', '9.5', '1e1', 'abc'])(
    'preserves and rejects invalid optional age %s',
    async (value) => {
      const { fixture, http, root, type, submit } = await open();
      type('child-name', 'ילד');
      type('child-age', value);
      submit();
      await fixture.whenStable();
      expect(root.querySelector<HTMLInputElement>('#child-age')!.value).toBe(value);
      expect(root.querySelector<HTMLInputElement>('#child-age')!.dir).toBe('ltr');
      http.expectNone((r) => r.method === 'POST');
    },
  );
  it('clears optional details independently, protects unsaved edits and keeps them after conflicts', async () => {
    const { fixture, http, root, type, submit, edit } = await open({
      ...child,
      grade: 'כיתה ד׳',
      age: 9,
      ageConfirmedAtUtc: child.createdAtUtc,
    });
    await edit();
    expect(root.querySelector<HTMLInputElement>('#child-grade')!.value).toBe('כיתה ד׳');
    expect(root.querySelector<HTMLInputElement>('#child-grade')!.maxLength).toBe(limits.nameLength);
    expect(root.textContent).toContain('הגיל עודכן');
    expect(fixture.componentInstance.canLeave()).toBe(true);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    expect(root.querySelector('#confirm-age')).toBeNull();
    type('child-grade', '  ');
    expect(fixture.componentInstance.canLeave()).toBe(false);
    submit();
    TestBed.tick();
    const update = http.expectOne('/api/children/child');
    expect(update.request.body.details).toEqual({ grade: null, age: 9 });
    update.flush({}, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    await vi.waitFor(() => expect(root.textContent).toContain('הפרופיל השתנה'));
    expect(root.querySelector<HTMLInputElement>('#child-age')!.value).toBe('9');
    expect(root.querySelector<HTMLInputElement>('#child-grade')!.value).toBe('  ');
    type('child-grade', 'גן חובה');
    type('child-age', '');
    submit();
    TestBed.tick();
    const cleared = http.expectOne('/api/children/child');
    expect(cleared.request.body.details).toEqual({
      grade: 'גן חובה',
      age: null,
    });
    cleared.flush({ ...child, grade: 'גן חובה', revision: 2 });
    (await vi.waitFor(() => http.expectOne('/api/children?page=1'))).flush(
      page([{ ...child, grade: 'גן חובה', revision: 2 }]),
    );
    http.expectOne('/api/children/child/devices?page=1').flush(page([]));
    await fixture.whenStable();
    expect(root.textContent).not.toContain('הגיל עודכן');
    expect(fixture.componentInstance.canLeave()).toBe(true);
    expect(confirm).toHaveBeenCalledTimes(1);
  });
  it('confirms deleting an unused profile and clears its editor after success', async () => {
    const { fixture, http, root, edit } = await open();
    await edit();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    root.querySelector<HTMLButtonElement>('#delete-child')!.click();
    http.expectNone((r) => r.method === 'DELETE');
    confirm.mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('#delete-child')!.click();
    http.expectOne('/api/children/child?expectedRevision=1').flush(null);
    (await vi.waitFor(() => http.expectOne('/api/children?page=1'))).flush(page([]));
    await fixture.whenStable();
    expect(root.querySelector('#delete-child')).toBeNull();
    expect(root.querySelector<HTMLInputElement>('#child-name')!.value).toBe('');
    expect(root.textContent).toContain('הפרופיל נמחק');
    expect(fixture.componentInstance.canLeave()).toBe(true);
  });
  it('offers disabling instead of deletion for profiles with assignments', async () => {
    const { root, edit } = await open({ ...child, hasAssignments: true });
    await edit();
    expect(root.querySelector('#delete-child')).toBeNull();
    expect(root.querySelector('#child-enabled')).not.toBeNull();
    expect(root.textContent).toContain('לפרופיל יש פעילויות שמורות');
  });
  it('keeps profile dates in a quiet disclosure and shows only known update times', async () => {
    const { root, edit } = await open({ ...child, updatedAtUtc: '2026-10-02T10:30:00Z' });
    await edit();
    const metadata = root.querySelector<HTMLDetailsElement>('#profile-dates')!;
    expect(metadata.open).toBe(false);
    expect(metadata.textContent).toContain('נוצר ב־');
    expect(metadata.textContent).toContain('עודכן ב־');
  });
  it('removes an inactive device record after confirmation and restores focus to the device section', async () => {
    const { fixture, http, root, edit } = await open();
    await edit();
    Array.from(root.querySelectorAll('button'))
      .find((button) => button.textContent?.trim() === 'רענון המכשירים')!
      .click();
    TestBed.tick();
    http.expectOne('/api/children/child/devices?page=1').flush(
      page([
        {
          id: 'old-device',
          deviceLabel: 'טאבלט ישן',
          createdAtUtc: child.createdAtUtc,
          expiresAtUtc: child.createdAtUtc,
          revokedAtUtc: null,
          canRemove: true,
        },
      ]),
    );
    await fixture.whenStable();
    expect(root.querySelector('[data-revoke]')).toBeNull();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const remove = root.querySelector<HTMLButtonElement>('[data-remove-device]')!;
    remove.focus();
    remove.click();
    http.expectNone((r) => r.method === 'DELETE');
    confirm.mockReturnValue(true);
    remove.click();
    http.expectOne('/api/children/child/devices/old-device/record').flush(null);
    (await vi.waitFor(() => http.expectOne('/api/children/child/devices?page=1'))).flush(page([]));
    await fixture.whenStable();
    await vi.waitFor(() =>
      expect(document.activeElement).toBe(root.querySelector('#devices-heading')),
    );
    expect(root.textContent).toContain('המכשיר הוסר מהרשימה');
  });
  it('keeps local edits when a deletion loses a race with another parent', async () => {
    const { fixture, http, root, type, edit } = await open();
    await edit();
    type('child-grade', 'כיתה ה׳');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    root.querySelector<HTMLButtonElement>('#delete-child')!.click();
    http
      .expectOne('/api/children/child?expectedRevision=1')
      .flush({}, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    await vi.waitFor(() => expect(root.textContent).toContain('רעננו את הרשימה'));
    expect(root.querySelector<HTMLInputElement>('#child-grade')!.value).toBe('כיתה ה׳');
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
    refresh.flush(page([{ ...device, revokedAtUtc: '2026-10-06T12:00:00Z', canRemove: true }]));
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
