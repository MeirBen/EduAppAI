import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { CreateTemplate } from './create-template';

describe('Template authoring', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('switches from generated arithmetic to authored questions and validates an empty question', async () => {
    const fixture = TestBed.createComponent(CreateTemplate);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const mode = element.querySelector<HTMLSelectElement>('#template-kind');
    expect(mode).not.toBeNull();
    mode!.value = 'static';
    mode!.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(element.querySelector('#difficulty')).toBeNull();
    expect(element.querySelector('[data-question]')).not.toBeNull();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    TestBed.inject(HttpTestingController).expectNone('/api/templates');
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('שאלה');
  });
});
