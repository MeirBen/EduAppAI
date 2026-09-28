import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { startAiProvider } from './ai-provider.mjs';

const root = resolve(import.meta.dirname, '../..');
const dataDirectory = mkdtempSync(resolve(tmpdir(), 'family-learning-e2e-'));
const provider = await startAiProvider();
const environment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: 'http://localhost:5199',
  Storage__Directory: dataDirectory,
  Logging__LogLevel__Default: 'Warning',
  Ai__ApiKey: 'isolated-test-key',
  Ai__Model: 'openrouter/free',
  Ai__ReasoningEnabled: 'false',
  Ai__Endpoint: provider.endpoint,
  Ai__RequestTimeoutSeconds: '180',
};
// Separate families keep cleanup independent of other workflows and their AI rate limits.
for (const email of ['browser@example.test', 'cleanup@example.test']) {
  // This known password belongs only to this disposable test database.
  const account = spawnSync(
    'dotnet',
    ['artifacts/app/FamilyLearning.Api.dll', '--create-parent', email],
    {
      cwd: root,
      env: environment,
      input: 'TestOnly!Parent12345\nTestOnly!Parent12345\n',
      encoding: 'utf8',
    },
  );
  if (account.status !== 0) {
    provider.close();
    rmSync(dataDirectory, { recursive: true, force: true });
    throw new Error(`Test account setup failed: ${account.stdout}\n${account.stderr}`);
  }
}
const server = spawn(
  'dotnet',
  ['artifacts/app/FamilyLearning.Api.dll', '--contentRoot', resolve(root, 'artifacts/app')],
  { cwd: root, env: environment, stdio: 'inherit' },
);
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => server.kill('SIGTERM'));
server.on('exit', (code) => {
  provider.close();
  rmSync(dataDirectory, { recursive: true, force: true });
  process.exit(code ?? 0);
});
