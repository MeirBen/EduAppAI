import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { once } from 'node:events';
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { extname, join, relative, resolve } from 'node:path';

const frontend = resolve(__dirname, '..');
const published = resolve(frontend, '../artifacts/app/wwwroot');
const types: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.webmanifest': 'application/manifest+json',
};

/**
 * Serves the published frontend, then a next build of it, on one origin, so the real service worker
 * meets a deployment. The next build differs by a marker, and Angular's own tool rewrites its
 * manifest. The API answers only anonymous parent/child session checks.
 */
export async function startAppBuilds() {
  const next = mkdtempSync(resolve(tmpdir(), 'family-learning-next-'));
  cpSync(published, next, { recursive: true });
  const index = join(next, 'index.html');
  writeFileSync(
    index,
    readFileSync(index, 'utf8').replace('<head>', '<head><meta name="build" content="next">'),
  );
  // Like the CLI, the tool reads both paths relative to the folder it runs in.
  execFileSync(
    process.execPath,
    [
      'node_modules/@angular/service-worker/ngsw-config.js',
      relative(frontend, next),
      'ngsw-config.json',
      '/',
    ],
    { cwd: frontend },
  );
  let root = published;
  const requests = new Set<string>();
  const server = createServer(async (request, response) => {
    const path = new URL(request.url ?? '/', 'http://127.0.0.1').pathname;
    requests.add(path);
    if (path.startsWith('/api/')) {
      response.writeHead(['/api/auth/me', '/api/child/auth/me'].includes(path) ? 401 : 404).end();
      return;
    }
    // Routes fall back to the app shell; the URL parser has already resolved any `..` segment.
    const file = extname(path) ? join(root, path) : join(root, 'index.html');
    try {
      const body = await readFile(file);
      response.writeHead(200, {
        'content-type': types[extname(file)] ?? 'application/octet-stream',
      });
      response.end(body);
    } catch {
      response.writeHead(404).end();
    }
  });
  await once(server.listen(0, '127.0.0.1'), 'listening');
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  return {
    url: `http://127.0.0.1:${address.port}`,
    requests,
    deploy: () => {
      root = next;
    },
    close: () => {
      server.close();
      rmSync(next, { recursive: true, force: true });
    },
  };
}
