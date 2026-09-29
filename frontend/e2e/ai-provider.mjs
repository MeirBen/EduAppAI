import { createServer } from 'node:http';
import { once } from 'node:events';
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

/** Test-only OpenRouter-compatible transport. Never imported by production code. */
export async function startAiProvider() {
  const [templateSchema, contentSchema] = await Promise.all(
    ['template', 'content'].map(async (name) =>
      JSON.parse(
        await readFile(
          new URL(
            `../../backend/FamilyLearning.Api/TaskEngine/Ai/${name}.schema.json`,
            import.meta.url,
          ),
          'utf8',
        ),
      ),
    ),
  );
  let sequence = 0;
  const server = createServer(async (request, response) => {
    if (request.url !== '/chat/completions' || request.method !== 'POST') {
      response.writeHead(404).end();
      return;
    }
    let body = '';
    for await (const chunk of request) body += chunk;
    const input = JSON.parse(body);
    assert.equal(input.model, 'test/schema-model');
    assert.equal(input.models, undefined);
    assert.deepEqual(input.reasoning, { max_tokens: 2048, exclude: true });
    assert.equal(input.temperature, 1);
    assert.equal(input.top_p, 0.95);
    assert.equal(input.top_k, 20);
    assert.equal(input.max_completion_tokens ?? input.max_tokens, 8192);
    assert.deepEqual(input.provider, { require_parameters: true });
    assert.equal(input.response_format.type, 'json_schema');
    assert.equal(input.response_format.json_schema.strict, true);
    assert.equal(input.response_format.json_schema.schema.additionalProperties, false);
    const schemaName = input.response_format.json_schema.name;
    assert.match(schemaName, /^(template_authoring|instance_generation)_v[1-9]\d*$/);
    assert.equal(input.tools, undefined);
    assert.equal(input.messages.length, 2);
    const schemaText = input.messages[0].content.split('\nOutput JSON schema:\n')[1];
    assert.ok(schemaText, 'The model must see the full schema in the prompt');
    const schema = JSON.parse(schemaText);
    const isAuthoring = schemaName.startsWith('template_authoring_');
    assert.deepEqual(schema, isAuthoring ? templateSchema : contentSchema);
    const user = input.messages[1].content;
    assert.ok(!user.includes('browser@example.test'));
    if (user.includes('בדיקת מכסה')) {
      response.writeHead(user.includes('בגוף התשובה') ? 200 : 429, {
        'content-type': 'application/json',
      });
      response.end(
        JSON.stringify({ error: { code: 429, message: 'private provider quota details' } }),
      );
      return;
    }
    sequence++;
    let result;
    if (isAuthoring) {
      result = {
        schemaVersion: 2,
        name: 'חוקרים וקוראים',
        instanceParameters: [
          {
            key: 'theme',
            label: 'נושא',
            type: 'text',
            required: true,
            default: 'דינוזאורים',
            min: null,
            max: null,
            maxLength: 100,
            options: null,
          },
          {
            key: 'level',
            label: 'רמה',
            type: 'select',
            required: true,
            default: 'קלה',
            min: null,
            max: null,
            maxLength: null,
            options: ['קלה', 'מאתגרת'],
          },
          {
            key: 'count',
            label: 'מספר שאלות',
            type: 'integer',
            required: true,
            default: 2,
            min: 1,
            max: 20,
            maxLength: null,
            options: null,
          },
        ],
        generation: {
          instructions:
            'יש ליצור קטע קריאה חדש לפי "theme" ברמה "level", עם "count" שאלות הבנה.\n\nיש לשמור כלשונו את המשפט: שָׁלוֹם, Maya! שלום־עולם',
          questionCountParameter: 'count',
          contentWordCount: user.includes('בדיקת אורך') ? { min: 100, max: 150 } : null,
        },
      };
    } else {
      const { parameters, expectedQuestionCount } = JSON.parse(user);
      result = {
        title: `לומדים על ${parameters.theme}`,
        instructions: 'קראו וענו על השאלות.',
        contentBlocks: [
          {
            type: 'text',
            text: `קטע ${sequence}: לומדים על ${parameters.theme} ברמה ${parameters.level}.`,
          },
        ],
        questions: Array.from({ length: expectedQuestionCount }, (_, index) => ({
          id: `q${index + 1}`,
          prompt: index % 3 === 2 ? 'כמה נושאים מופיעים בקטע?' : `מה נושא הקטע? (${index + 1})`,
          interaction: {
            type: ['text-input', 'single-choice', 'numeric-input'][index % 3],
            options: index % 3 === 1 ? [parameters.theme, 'נושא אחר'] : null,
          },
          answer: { value: index % 3 === 2 ? '1' : parameters.theme },
          points: 1,
        })),
      };
    }
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(
      JSON.stringify({
        id: `test-${sequence}`,
        object: 'chat.completion',
        created: Math.floor(Date.now() / 1000),
        model: 'isolated-test:free',
        choices: [
          {
            index: 0,
            message: {
              role: 'assistant',
              content: user.includes('בדיקת כשל') ? '{invalid' : JSON.stringify(result),
            },
            finish_reason: user.includes('בדיקת מגבלת פלט') ? 'length' : 'stop',
          },
        ],
        usage: { prompt_tokens: 100, completion_tokens: 100, total_tokens: 200 },
      }),
    );
  });
  await once(server.listen(0, '127.0.0.1'), 'listening');
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  return { endpoint: `http://127.0.0.1:${address.port}`, close: () => server.close() };
}
