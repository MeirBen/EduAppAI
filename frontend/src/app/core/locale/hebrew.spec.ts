import { parameterLabel, parameterOption, taskInstructions } from './hebrew';
import { ParameterDefinition } from '../api/models';

describe('Hebrew snapshot presentation', () => {
  it('preserves custom labels and non-difficulty options', () => {
    const definition: ParameterDefinition = {
      key: 'difficulty',
      label: 'האתגר שלי',
      type: 'select',
    };
    expect(parameterLabel(definition)).toBe('האתגר שלי');
    expect(parameterOption({ ...definition, key: 'theme' }, 'easy')).toBe('easy');
  });

  it('translates only the exact legacy instruction and preserves authored content', () => {
    expect(taskInstructions('Multiply the two numbers.')).toBe('מהי המכפלה של שני המספרים?');
    expect(taskInstructions('Read chapter 2')).toBe('Read chapter 2');
    expect(taskInstructions(null)).toBeNull();
  });
});
