import { TestBed } from '@angular/core/testing';
import { ParameterForm } from './parameter-form';
import { ParameterDefinition, ParameterValues } from '../../core/api/models';

const definitions: ParameterDefinition[] = [
  { key: 'theme', label: 'נושא', type: 'text', required: true, maxLength: 10 },
  {
    key: 'count',
    label: 'שאלות',
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
  { key: 'retry', label: 'ניסיון נוסף', type: 'boolean', default: false },
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
    expect(view.fixture.nativeElement.textContent).toContain('יש למלא את השדה „נושא”.');
    expect(view.fixture.nativeElement.textContent).toContain(
      'הערך בשדה „שאלות” חייב להיות לכל היותר 20.',
    );
  });

  it('localizes legacy math metadata without changing saved labels or option values', async () => {
    const view = await render();
    const element: HTMLElement = view.fixture.nativeElement;
    expect(element.querySelector('label[for="parameter-difficulty"]')?.textContent).toContain(
      'רמת קושי',
    );
    const option = element.querySelector<HTMLOptionElement>('#parameter-difficulty option:checked');
    expect(option?.textContent).toContain('קלה');
    expect(option?.value).toBe('easy');
    expect(definitions[2].label).toBe('Difficulty');
    expect(element.querySelector('#parameter-theme')?.getAttribute('dir')).toBe('auto');
    expect(element.querySelector('#parameter-count')?.getAttribute('dir')).toBe('ltr');
  });
});
