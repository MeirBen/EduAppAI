import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ChildSelector } from './child-selector';

describe('Child selection', () => {
  it('offers every profile in one bounded read and disallows disabled profiles for assignment', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(ChildSelector),
      http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/children?pageSize=100').flush({
      items: [
        { id: 'one', name: 'ראשון', enabled: true },
        { id: 'disabled', name: 'מושבת', enabled: false },
      ],
      page: 1,
      hasMore: false,
    });
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement,
      select = root.querySelector('select')!;
    expect(select.options[2].disabled).toBe(true);
    select.value = 'one';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(fixture.componentInstance.value()).toBe('one');
    expect(root.querySelector('nav')).toBeNull();
    http.verify();
  });
});
