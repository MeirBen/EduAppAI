import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { CreateInstance } from './create-instance';
import { readingDefinition } from '../../templates/ai-template-form/ai-template.fixture';

describe('AI task request lifetime', () => {
  it('cancels generation on navigation away and cannot redirect the destroyed page', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(CreateInstance);
    fixture.componentRef.setInput('templateId', 'template');
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/templates/template').flush({
      id: 'template',
      currentVersion: 1,
      versionId: 'version',
      definition: readingDefinition,
    });
    await fixture.whenStable();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
    const generation = http.expectOne('/api/templates/template/instances');
    fixture.destroy();
    expect(generation.cancelled).toBe(true);
    await Promise.resolve();
    expect(navigate).not.toHaveBeenCalled();
    http.verify();
  });
});
