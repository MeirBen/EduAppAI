import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { form } from '@angular/forms/signals';
import { TemplateDefinition } from '../../../core/api/models';
import { templateDefinition, templateDraft } from './template-draft';
import { templateSchema } from './template-schema';

describe('Template draft', () => {
  it('preserves a task title distinct from the template name on unrelated edits', () => {
    const previous: TemplateDefinition = {
      schemaVersion: 1,
      name: 'Reading template',
      instanceParameters: [],
      generation: {
        mode: 'static',
        content: {
          title: 'The lost book',
          instructions: null,
          contentBlocks: [],
          questions: [
            {
              id: 'q1',
              prompt: 'What was lost?',
              interaction: { type: 'text-input', options: null },
              answer: { value: 'A book' },
              points: 1,
            },
          ],
        },
      },
    };
    const draft = templateDraft(previous);
    draft.questions[0].points = '2';
    const updated = templateDefinition(draft, previous);
    if (updated.generation.mode !== 'static') throw new Error('Expected static content');
    expect(updated.generation.content.title).toBe('The lost book');
  });

  it('preserves existing math metadata and narrower bounds when editing defaults', () => {
    const previous: TemplateDefinition = {
      schemaVersion: 1,
      name: 'Math',
      instanceParameters: [
        {
          key: 'difficulty',
          label: 'My level',
          type: 'select',
          required: true,
          options: ['easy', 'medium', 'hard'],
          default: 'easy',
        },
        {
          key: 'questionCount',
          label: 'My count',
          type: 'integer',
          required: true,
          min: 1,
          max: 3,
        },
        { key: 'note', label: 'My note', type: 'text', maxLength: 100, default: 'Keep me' },
      ],
      generation: {
        mode: 'deterministic',
        generator: 'math-v1',
        fixedSettings: { operation: 'multiplication' },
      },
    };
    const model = templateDraft(previous);
    expect(model.questionCount).toBe(3);
    model.operation = 'division';
    model.difficulty = 'hard';
    const next = templateDefinition(model, previous);
    expect(next.instanceParameters[0]).toEqual({
      key: 'difficulty',
      label: 'My level',
      type: 'select',
      required: true,
      options: ['easy', 'medium', 'hard'],
      default: 'hard',
    });
    expect(next.instanceParameters[1].max).toBe(3);
    expect(next.instanceParameters[2]).toEqual({
      key: 'note',
      label: 'My note',
      type: 'text',
      maxLength: 100,
      default: 'Keep me',
    });
    expect(previous.instanceParameters[0].default).toBe('easy');
  });

  it('keeps question identities and corresponding answers when reordering authored content', () => {
    const model = templateDraft();
    model.mode = 'static';
    model.name = 'קריאה';
    model.passages = [{ id: 'local', text: 'שורה ראשונה\nSecond line' }];
    model.questions = [
      { id: 'first', prompt: 'מי?', type: 'text-input', answer: 'נועה', choices: '', points: '0' },
      {
        id: 'second',
        prompt: 'מה?',
        type: 'single-choice',
        answer: 'ספר',
        choices: 'ספר\nפרח',
        points: '3',
      },
    ];
    model.questions.reverse();
    const definition = templateDefinition(model);
    expect(definition.instanceParameters).toEqual([]);
    if (definition.generation.mode !== 'static') throw new Error('Expected authored content');
    expect(
      definition.generation.content.questions.map((q) => [q.id, q.answer.value, q.points]),
    ).toEqual([
      ['second', 'ספר', 3],
      ['first', 'נועה', 0],
    ]);
    const reopened = templateDraft(definition);
    expect(reopened.questions[0].id).toBe('second');
    expect(reopened.passages[0].text).toBe('שורה ראשונה\nSecond line');
  });

  it('invalidates a selected answer when the available choices change', () => {
    TestBed.runInInjectionContext(() => {
      const model = signal({
        ...templateDraft(),
        mode: 'static' as const,
        name: 'Quiz',
        questions: [
          {
            id: 'q1',
            prompt: 'Pick',
            type: 'single-choice' as const,
            choices: 'Red\nBlue',
            answer: 'Red',
            points: '1',
          },
        ],
      });
      const fields = form(model, templateSchema);
      expect(fields().valid()).toBe(true);
      fields.questions[0].choices().value.set('Green\nBlue');
      expect(fields.questions[0].answer().invalid()).toBe(true);
      fields.questions[0].answer().value.set('Blue');
      expect(fields().valid()).toBe(true);
      fields.questions[0].points().value.set('');
      expect(fields.questions[0].points().invalid()).toBe(true);
    });
  });
});
