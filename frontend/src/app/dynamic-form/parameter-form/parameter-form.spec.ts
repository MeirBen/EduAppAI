import { TestBed } from '@angular/core/testing';
import { ParameterForm } from './parameter-form';
import { ParameterDefinition, ParameterValues } from '../../core/api/models';

const definitions: ParameterDefinition[] = [
  { key: 'theme', label: 'Theme', type: 'text', required: true, maxLength: 10 },
  {
    key: 'count',
    label: 'Questions',
    type: 'integer',
    required: true,
    default: 5,
    min: 1,
    max: 20,
  },
  {
    key: 'difficulty',
    label: 'Difficulty',
    type: 'select',
    required: true,
    default: 'easy',
    options: ['easy', 'hard'],
  },
  { key: 'retry', label: 'Retry', type: 'boolean', default: false },
];

describe('ParameterForm', () => {
  async function render() {
    const fixture = TestBed.createComponent(ParameterForm);
    fixture.componentRef.setInput('definitions', definitions);
    let submitted: ParameterValues | undefined;
    fixture.componentInstance.generated.subscribe((value) => (submitted = value));
    await fixture.whenStable();
    return { fixture, submitted: () => submitted };
  }

  it('submits typed defaults and an explicit false value', async () => {
    const view = await render();
    const input = view.fixture.nativeElement.querySelector('#parameter-theme') as HTMLInputElement;
    input.value = 'Space';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await view.fixture.whenStable();
    view.fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()).toEqual({
      theme: 'Space',
      count: 5,
      difficulty: 'easy',
      retry: false,
    });
  });

  it('blocks missing required text and out-of-range numbers', async () => {
    const view = await render();
    const count = view.fixture.nativeElement.querySelector('#parameter-count') as HTMLInputElement;
    count.value = '21';
    count.dispatchEvent(new Event('input', { bubbles: true }));
    view.fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()).toBeUndefined();
    expect(view.fixture.nativeElement.textContent).toContain('Theme is required');
    expect(view.fixture.nativeElement.textContent).toContain('Questions must be at most 20');
  });
});
