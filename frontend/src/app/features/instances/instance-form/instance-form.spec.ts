import { TestBed } from '@angular/core/testing';
import { InstanceForm } from './instance-form';
import { TaskInput, ParameterDefinition, TaskSettings } from '../../../core/api/models';

const defaults: TaskSettings = {
  topic: 'חלל',
  audience: 'כיתה ג׳',
  difficulty: 'easy',
  questionCount: 4,
};

const definitions: ParameterDefinition[] = [
  { key: 'sourceText', label: 'טקסט מקור', type: 'text', required: true, maxLength: 10 },
  {
    key: 'paragraphs',
    label: 'מספר פסקאות',
    type: 'integer',
    required: true,
    default: 5,
    min: 1,
    max: 20,
  },
  {
    key: 'style',
    label: 'Style',
    type: 'select',
    required: true,
    default: 'story',
    options: ['story', 'article'],
  },
  { key: 'hints', label: 'רמזים', type: 'boolean', default: false },
];

describe('InstanceForm', () => {
  async function render(schema = definitions) {
    const fixture = TestBed.createComponent(InstanceForm);
    fixture.componentRef.setInput('definitions', schema);
    fixture.componentRef.setInput('defaults', defaults);
    let submitted: TaskInput | undefined;
    fixture.componentInstance.generated.subscribe((value) => (submitted = value));
    await fixture.whenStable();
    return { fixture, submitted: () => submitted };
  }

  it.each(['', '0', '-1', '1.5'])(
    'blocks an invalid question count %j even without dynamic parameters',
    async (value) => {
      const view = await render([]);
      const element: HTMLElement = view.fixture.nativeElement;
      const count = element.querySelector<HTMLInputElement>('#task-questionCount')!;
      count.value = value;
      count.dispatchEvent(new Event('input'));
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await view.fixture.whenStable();
      expect(view.submitted()).toBeUndefined();
      expect(element.textContent).toContain('יש להזין מספר שלם גדול מאפס.');
    },
  );

  it('lets the parent override the default count without adding a parameter', async () => {
    const view = await render([]);
    const element: HTMLElement = view.fixture.nativeElement;
    const count = element.querySelector<HTMLInputElement>('#task-questionCount')!;
    expect(count.value).toBe('4');
    count.value = '25';
    count.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()).toEqual({
      settings: { ...defaults, questionCount: 25 },
      parameters: {},
    });
  });

  it('submits changed settings without mutating defaults or creating dynamic fields', async () => {
    const view = await render([]);
    const element: HTMLElement = view.fixture.nativeElement;
    for (const [key, value] of Object.entries({
      topic: 'צמחים',
      audience: 'מבוגרים',
      difficulty: 'hard',
    })) {
      const control = element.querySelector<HTMLInputElement | HTMLSelectElement>('#task-' + key)!;
      control.value = value;
      control.dispatchEvent(new Event('input'));
    }
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()).toEqual({
      settings: { topic: 'צמחים', audience: 'מבוגרים', difficulty: 'hard', questionCount: 4 },
      parameters: {},
    });
    expect(defaults).toEqual({
      topic: 'חלל',
      audience: 'כיתה ג׳',
      difficulty: 'easy',
      questionCount: 4,
    });
  });

  it.each(['topic', 'audience'])('rejects blank or oversized %s', async (key) => {
    const view = await render([]);
    const element: HTMLElement = view.fixture.nativeElement;
    const control = element.querySelector<HTMLInputElement>('#task-' + key)!;
    for (const value of [' ', 'א'.repeat(201)]) {
      control.value = value;
      control.dispatchEvent(new Event('input'));
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await view.fixture.whenStable();
      expect(view.submitted()).toBeUndefined();
      expect(control.getAttribute('aria-invalid')).toBe('true');
    }
  });

  it('submits typed defaults and an explicit false value', async () => {
    const view = await render();
    const input = view.fixture.nativeElement.querySelector(
      '#parameter-sourceText',
    ) as HTMLInputElement;
    input.value = 'Space';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await view.fixture.whenStable();
    view.fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()?.settings.questionCount).toBe(4);
    expect(view.submitted()?.parameters).toEqual({
      sourceText: 'Space',
      paragraphs: 5,
      style: 'story',
      hints: false,
    });
  });

  it('blocks missing required text and out-of-range numbers', async () => {
    const view = await render();
    const count = view.fixture.nativeElement.querySelector(
      '#parameter-paragraphs',
    ) as HTMLInputElement;
    count.value = '21';
    count.dispatchEvent(new Event('input', { bubbles: true }));
    view.fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()).toBeUndefined();
    expect(view.fixture.nativeElement.textContent).toContain('יש למלא את השדה „טקסט מקור”.');
    expect(view.fixture.nativeElement.textContent).toContain(
      'הערך בשדה „מספר פסקאות” חייב להיות לכל היותר 20.',
    );
  });

  it('submits cleared optional text explicitly so the server cannot restore its default', async () => {
    const view = await render([
      {
        key: 'sourceText',
        label: 'טקסט מקור',
        type: 'text',
        required: false,
        default: 'דינוזאורים',
      },
    ]);
    const element: HTMLElement = view.fixture.nativeElement;
    const input = element.querySelector<HTMLInputElement>('#parameter-sourceText')!;
    expect(input.value).toBe('דינוזאורים');
    input.value = '';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await view.fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()?.parameters).toEqual({ sourceText: '' });
  });

  it('renders schema labels and options verbatim with appropriate input direction', async () => {
    const view = await render();
    const element: HTMLElement = view.fixture.nativeElement;
    expect(element.querySelector('label[for="parameter-style"]')?.textContent).toContain('Style');
    const option = element.querySelector<HTMLOptionElement>('#parameter-style option:checked');
    expect(option?.textContent).toContain('story');
    expect(option?.value).toBe('story');
    expect(definitions[2].label).toBe('Style');
    expect(element.querySelector('#parameter-sourceText')?.getAttribute('dir')).toBe('auto');
    expect(element.querySelector('#parameter-paragraphs')?.getAttribute('dir')).toBe('ltr');
  });

  it.each([' ', '2147483648', '-2147483649'])(
    'rejects optional integer input %j instead of emitting an invalid or coerced value',
    async (value) => {
      const view = await render([{ key: 'offset', label: 'היסט', type: 'integer' }]);
      const element: HTMLElement = view.fixture.nativeElement;
      const input = element.querySelector<HTMLInputElement>('#parameter-offset')!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await view.fixture.whenStable();
      expect(view.submitted()).toBeUndefined();
      expect(element.textContent).toContain('יש להזין מספר שלם');
    },
  );

  it.each([
    ['', {}],
    ['-2147483648', { offset: -2147483648 }],
    ['2147483647', { offset: 2147483647 }],
  ])('preserves blank omission and valid integer boundary %j', async (value, expected) => {
    const view = await render([{ key: 'offset', label: 'היסט', type: 'integer' }]);
    const element: HTMLElement = view.fixture.nativeElement;
    const input = element.querySelector<HTMLInputElement>('#parameter-offset')!;
    input.value = value as string;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await view.fixture.whenStable();
    expect(view.submitted()?.parameters).toEqual(expected);
  });
});
