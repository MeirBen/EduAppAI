/**
 * Checks the signed 32-bit integer contract without coercing blanks, decimals or exponent notation.
 * Surrounding spaces, which mobile keyboards add, do not count; `Number` ignores them the same way.
 */
export function isIntegerInput(value: string): boolean {
  const text = value.trim();
  const number = Number(text);
  return /^-?\d+$/.test(text) && number >= -2147483648 && number <= 2147483647;
}
