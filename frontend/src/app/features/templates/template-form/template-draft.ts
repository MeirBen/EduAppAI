import { MathOperation, QuestionType, TemplateDefinition } from '../../../core/api/models';

/** Local editor state; numeric points stay text so an empty field cannot become zero. */
export interface QuestionDraft {
  id: string;
  prompt: string;
  type: QuestionType;
  choices: string;
  answer: string;
  points: string;
}

/** Editable values separated from the published wire contract. */
export interface TemplateDraft {
  name: string;
  mode: 'deterministic' | 'static';
  operation: MathOperation;
  difficulty: string;
  questionCount: number;
  contentTitle: string;
  instructions: string;
  passages: { id: string; text: string }[];
  questions: QuestionDraft[];
}

/** Creates a blank question with an identity that survives reordering and publication. */
export function blankQuestion(): QuestionDraft {
  return {
    id: crypto.randomUUID(),
    prompt: '',
    type: 'text-input',
    choices: '',
    answer: '',
    points: '1',
  };
}

/** Splits the explicitly line-based choice editor; blank lines are ignored. */
export function choiceOptions(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((option) => option.trim())
    .filter(Boolean);
}

/** Loads an editable copy without mutating the published definition. */
export function templateDraft(definition?: TemplateDefinition): TemplateDraft {
  const content =
    definition?.generation.mode === 'static' ? definition.generation.content : undefined;
  const count = definition?.instanceParameters.find((field) => field.key === 'questionCount');
  const difficulty = definition?.instanceParameters.find((field) => field.key === 'difficulty');
  return {
    name: definition?.name ?? '',
    mode: definition?.generation.mode ?? 'deterministic',
    operation:
      definition?.generation.mode === 'deterministic'
        ? definition.generation.fixedSettings.operation
        : 'multiplication',
    difficulty: typeof difficulty?.default === 'string' ? difficulty.default : 'medium',
    questionCount:
      typeof count?.default === 'number'
        ? count.default
        : Math.min(count?.max ?? 20, Math.max(count?.min ?? 1, 5)),
    contentTitle: content?.title ?? '',
    instructions: content?.instructions ?? '',
    passages:
      content?.contentBlocks.map((block) => ({ id: crypto.randomUUID(), text: block.text })) ?? [],
    questions: content?.questions.map((question) => ({
      id: question.id,
      prompt: question.prompt,
      type: question.interaction.type,
      choices: question.interaction.options?.join('\n') ?? '',
      answer: question.answer.value,
      points: String(question.points),
    })) ?? [blankQuestion()],
  };
}

/** Builds a replacement definition after form validation, preserving existing math metadata. */
export function templateDefinition(
  draft: TemplateDraft,
  previous?: TemplateDefinition,
): TemplateDefinition {
  const name = draft.name.trim();
  if (draft.mode === 'static') {
    return {
      schemaVersion: 1,
      name,
      instanceParameters: [],
      generation: {
        mode: 'static',
        content: {
          title: draft.contentTitle.trim() || name,
          instructions: draft.instructions || null,
          contentBlocks: draft.passages.map((passage) => ({ type: 'text', text: passage.text })),
          questions: draft.questions.map((question) => ({
            id: question.id,
            prompt: question.prompt,
            interaction: {
              type: question.type,
              options: question.type === 'single-choice' ? choiceOptions(question.choices) : null,
            },
            answer: { value: question.answer },
            points: Number(question.points),
          })),
        },
      },
    };
  }
  const parameters =
    previous?.generation.mode === 'deterministic'
      ? previous.instanceParameters
      : [
          {
            key: 'difficulty',
            label: 'רמת קושי',
            type: 'select' as const,
            required: true,
            options: ['easy', 'medium', 'hard'],
          },
          {
            key: 'questionCount',
            label: 'מספר שאלות',
            type: 'integer' as const,
            required: true,
            min: 1,
            max: 20,
          },
        ];
  return {
    schemaVersion: 1,
    name,
    instanceParameters: parameters.map((parameter) => {
      if (parameter.key === 'difficulty') return { ...parameter, default: draft.difficulty };
      if (parameter.key === 'questionCount') return { ...parameter, default: draft.questionCount };
      return { ...parameter };
    }),
    generation: {
      mode: 'deterministic',
      generator: 'math-v1',
      fixedSettings: { operation: draft.operation },
    },
  };
}

/** Matches the server's aggregate text limit, including each choice and the answer key. */
export function authoredTextLength(draft: TemplateDraft): number {
  return (
    (draft.contentTitle.trim() || draft.name.trim()).length +
    draft.instructions.length +
    draft.passages.reduce((total, passage) => total + passage.text.length, 0) +
    draft.questions.reduce(
      (total, question) =>
        total +
        question.prompt.length +
        question.answer.length +
        (question.type === 'single-choice'
          ? choiceOptions(question.choices).reduce((length, option) => length + option.length, 0)
          : 0),
      0,
    )
  );
}
