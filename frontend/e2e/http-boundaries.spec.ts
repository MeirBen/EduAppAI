import { expect, test } from '@playwright/test';
import type { TemplateDefinition } from '../src/app/core/api/models';

test('Kestrel accepts bounded Hebrew templates and rejects oversized bodies', async ({
  request,
}) => {
  let csrf = await (await request.get('/api/auth/csrf')).json();
  const login = await request.post('/api/auth/login', {
    headers: { 'X-XSRF-TOKEN': csrf.token },
    data: { email: 'cleanup@example.test', password: 'TestOnly!Parent12345' },
  });
  expect(login.status()).toBe(204);
  csrf = await (await request.get('/api/auth/csrf')).json();
  const headers = { 'X-XSRF-TOKEN': csrf.token, 'Content-Type': 'application/json' };
  const definition: TemplateDefinition = {
    schemaVersion: 3,
    name: 'א'.repeat(100),
    generation: { instructions: 'א'.repeat(4000), questionCount: 4 },
    instanceParameters: Array.from({ length: 16 }, (_, index) => ({
      key: `field${index}`,
      label: 'א'.repeat(100),
      type: 'select',
      required: true,
      default: 'א'.repeat(100),
      options: Array.from({ length: 20 }, (_, option) =>
        String.fromCharCode(0x5d0 + option).repeat(100),
      ),
    })),
  };
  const created = await request.post('/api/templates', { headers, data: definition });
  expect(created.status()).toBe(201);
  const template = await created.json();
  try {
    // Both native UTF-8 and JSON Unicode escapes must fit the same validated contract.
    const escaped = JSON.stringify({ expectedVersion: 1, definition }).replace(
      /[^\x00-\x7f]/g,
      (character) => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`,
    );
    const published = await request.post(`/api/templates/${template.id}/versions`, {
      headers,
      data: escaped,
    });
    expect(published.status()).toBe(201);
    expect((await published.json()).definition).toEqual(template.definition);
    const oversized = await request.post('/api/templates', {
      headers,
      data: JSON.stringify(definition) + ' '.repeat(256 * 1024),
    });
    expect(oversized.status()).toBe(413);
  } finally {
    expect((await request.delete(`/api/templates/${template.id}`, { headers })).status()).toBe(204);
  }
});
