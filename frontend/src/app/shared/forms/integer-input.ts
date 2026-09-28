/** Checks the signed 32-bit integer contract without coercing blanks, decimals or exponent notation. */
export function isIntegerInput(value: string): boolean {
  const number = Number(value);
  return /^-?\d+$/.test(value) && number >= -2147483648 && number <= 2147483647;
}
