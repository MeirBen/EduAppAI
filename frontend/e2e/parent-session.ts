import { resolve } from 'node:path';

/** A parent's signed-in state, saved once per run by the `parent-sessions` setup project. */
export const sessionFile = (email: string) =>
  resolve(__dirname, '../test-results/.auth', `${email}.json`);
