import { applyEach, applyWhen, maxLength, schema, validate } from '@angular/forms/signals';
import { authoredTextLength, choiceOptions, TemplateDraft } from './template-draft';

const error = (message: string) => ({ kind: 'content', message });
const hasText = (value: string, limit: number) => !!value.trim() && value.length <= limit;

/** Client feedback for the authoring contract; the server remains authoritative. */
export const templateSchema = schema<TemplateDraft>((path) => {
  maxLength(path.name, 100, { message: 'שם התבנית מוגבל ל־100 תווים.' });
  validate(path.name, ({ value }) =>
    hasText(value(), 100) ? undefined : error('יש להזין שם באורך של 1 עד 100 תווים.'),
  );
  applyWhen(
    path,
    ({ value }) => value().mode === 'deterministic',
    (math) => {
      validate(math.questionCount, ({ value }) =>
        Number.isInteger(value()) && value() >= 1 && value() <= 20
          ? undefined
          : error('יש לבחור מספר שאלות שלם מ־1 עד 20.'),
      );
    },
  );
  applyWhen(
    path,
    ({ value }) => value().mode === 'static',
    (content) => {
      maxLength(content.contentTitle, 100, { message: 'כותרת התרגול מוגבלת ל־100 תווים.' });
      maxLength(content.instructions, 1000, { message: 'ההנחיות מוגבלות ל־1,000 תווים.' });
      validate(content, ({ value }) =>
        authoredTextLength(value()) <= 8000
          ? undefined
          : error('התוכן כולו מוגבל ל־8,000 תווים, כולל שאלות ותשובות.'),
      );
      validate(content.instructions, ({ value }) =>
        value().length <= 1000 ? undefined : error('ההנחיות מוגבלות ל־1,000 תווים.'),
      );
      validate(content.passages, ({ value }) =>
        value().length <= 4 ? undefined : error('אפשר להוסיף עד ארבעה קטעי קריאה.'),
      );
      applyEach(content.passages, (passage) => {
        maxLength(passage.text, 4000, { message: 'קטע קריאה מוגבל ל־4,000 תווים.' });
        validate(passage.text, ({ value }) =>
          hasText(value(), 4000)
            ? undefined
            : error('יש להזין קטע קריאה באורך של 1 עד 4,000 תווים או להסיר אותו.'),
        );
      });
      validate(content.questions, ({ value }) =>
        value().length >= 1 && value().length <= 20
          ? undefined
          : error('יש להוסיף בין שאלה אחת ל־20 שאלות.'),
      );
      applyEach(content.questions, (question) => {
        maxLength(question.prompt, 500, { message: 'נוסח השאלה מוגבל ל־500 תווים.' });
        maxLength(question.answer, 200, { message: 'התשובה מוגבלת ל־200 תווים.' });
        validate(question.prompt, ({ value }) =>
          hasText(value(), 500) ? undefined : error('יש להזין שאלה באורך של 1 עד 500 תווים.'),
        );
        validate(question.points, ({ value }) =>
          /^\d+$/.test(value()) && Number(value()) <= 100
            ? undefined
            : error('הניקוד חייב להיות מספר שלם מ־0 עד 100.'),
        );
        validate(question.answer, ({ value, valueOf }) => {
          if (!hasText(value(), 200)) return error('יש להזין תשובה באורך של 1 עד 200 תווים.');
          const type = valueOf(question.type);
          if (
            type === 'single-choice' &&
            !choiceOptions(valueOf(question.choices)).includes(value())
          )
            return error('יש לבחור תשובה נכונה מתוך האפשרויות הנוכחיות.');
          if (
            type === 'numeric-input' &&
            (!/^[+-]?[0-9]+(?:\.[0-9]+)?$/.test(value()) ||
              Math.abs(Number(value())) > 7.922816251426433e28)
          )
            return error('יש להזין מספר רגיל, עם נקודה עשרונית לפי הצורך וללא מפרידי אלפים.');
          return undefined;
        });
        validate(question.choices, ({ value, valueOf }) => {
          if (valueOf(question.type) !== 'single-choice') return undefined;
          const options = choiceOptions(value());
          return options.length >= 2 &&
            options.length <= 6 &&
            options.every((option) => option.length <= 200) &&
            new Set(options).size === options.length
            ? undefined
            : error('יש להזין בין שתיים לשש אפשרויות שונות, עד 200 תווים בכל אפשרות.');
        });
      });
    },
  );
});
