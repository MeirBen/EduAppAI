import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ChildSelector } from './child-selector';

describe('Paged child selection', () => {
  it('keeps the selected profile while paging and disallows disabled profiles for assignment', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(ChildSelector),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/children?page=1').flush({
      items: [
        { id: 'one', name: 'ראשון', enabled: true },
        { id: 'disabled', name: 'מושבת', enabled: false },
      ],
      page: 1,
      hasMore: true,
    });
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement,
      select = root.querySelector('select')!;
    expect(select.options[2].disabled).toBe(true);
    select.value = 'one';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    const next = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('הבא'),
    )!;
    next.click();
    TestBed.tick();
    http
      .expectOne('/api/children?page=2')
      .flush({ items: [{ id: 'two', name: 'שני', enabled: true }], page: 2, hasMore: false });
    await fixture.whenStable();
    expect(select.value).toBe('one');
    expect(fixture.componentInstance.value()).toBe('one');
    expect(select.options.length).toBe(3);
    http.verify();
  });
});
