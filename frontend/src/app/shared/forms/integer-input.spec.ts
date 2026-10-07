import { isIntegerInput } from './integer-input';

describe('isIntegerInput', () => {
  it('accepts whole numbers, ignoring the surrounding spaces a keyboard adds', () => {
    for (const value of ['0', '3', '-12', ' 3 ', '\t7\n', '2147483647'])
      expect(isIntegerInput(value)).toBe(true);
  });

  it('rejects blanks, decimals, exponents, separators and values beyond 32 bits', () => {
    for (const value of ['', '  ', '1.5', '1e2', '1,000', '1 2', '+3', '2147483648'])
      expect(isIntegerInput(value)).toBe(false);
  });
});
