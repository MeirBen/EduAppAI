import { createServer } from 'node:http';
import { once } from 'node:events';
import assert from 'node:assert/strict';

/** Test-only OpenRouter transport: scenarios exercise the real resolver, worker and validators. */
export async function startAiProvider(port = 0) {
  let calls = 0;
  let sequence = 0;
  const server = createServer(async (request, response) => {
    if (request.url === '/__stats' && request.method === 'GET') {
      response
        .writeHead(200, { 'content-type': 'application/json' })
        .end(JSON.stringify({ calls }));
      return;
    }
    if (request.url !== '/chat/completions' || request.method !== 'POST') {
      response.writeHead(404).end();
      return;
    }
    calls++;
    let body = '';
    for await (const chunk of request) body += chunk;
    try {
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
      assert.equal(input.tools, undefined);
      assert.equal(input.messages.length, 2);
      // The default strict profile sends the schema natively only (Ai:SchemaInPrompt is false).
      assert.ok(!input.messages[0].content.includes('Output JSON schema'));
      const schema = input.response_format.json_schema.schema;
      assert.equal(schema.additionalProperties, false);
      const stage = input.response_format.json_schema.name.match(
        /^content_first_(author|material_ideas|materials|material_polish|questions|replace_material|replace_question)_v[1-9]\d*$/,
      )?.[1];
      assert.ok(stage, 'Only supported content-first stages may reach the provider');
      const user = JSON.parse(input.messages[1].content);
      assert.ok(!input.messages[1].content.includes('@example.test'));
      const effective = user.request ?? user;
      const scenario = stage === 'author' ? user.message : effective.goal;
      const id = ++sequence;
      if (scenario.includes('בדיקת מכסה')) {
        response.writeHead(scenario.includes('בגוף התשובה') ? 200 : 429, {
          'content-type': 'application/json',
        });
        response.end(JSON.stringify({ error: { code: 429, message: 'private provider quota' } }));
        return;
      }
      // A bounded delay makes reload/cancel/typing races observable through the real worker.
      if (stage !== 'author' && scenario.includes('המתנה')) {
        await new Promise((resolve) => setTimeout(resolve, 2500));
      }
      const result =
        stage === 'author'
          ? {
              result: {
                proposal: plan(user.message, schema.$defs.plan.properties.schemaVersion.minimum),
                clarification: null,
              },
              assumptions: [],
            }
          : stage === 'material_ideas'
            ? {
                ideas: Array.from({ length: 5 }, (_, index) => ({
                  idea: {
                    premise: 'גילוי מאובנים במסע ' + (index + 1),
                    structure: 'פתיחה בגילוי, הסבר ושאלה למסע ' + (index + 1),
                  },
                  recentOverlap: 0,
                })),
              }
            : stage === 'materials'
              ? {
                  materials: effective.materials
                    .filter((/** @type {{source: string}} */ m) => m.source === 'generated')
                    .map(material),
                }
              : stage === 'material_polish'
                ? { materials: user.materials }
                : stage === 'replace_material'
                  ? material(user.target)
                  : stage === 'replace_question'
                    ? question(effective, 0, true)
                    : {
                        title: 'לומדים על ' + effective.settings.topic,
                        instructions: 'קראו ובדקו את תשובותיכם.',
                        questions: Array.from(
                          { length: effective.settings.questionCount },
                          (_, index) => question(effective, index),
                        ),
                      };
      if (stage === 'material_ideas') {
        assert.equal(schema.properties.ideas.minItems, 5);
        assert.equal(schema.properties.ideas.maxItems, 5);
        assert.ok(Array.isArray(user.history));
      }
      if (stage === 'materials') {
        assert.ok(user.request);
        assert.equal(typeof user.idea.premise, 'string');
        assert.equal(typeof user.idea.structure, 'string');
      }
      if (stage === 'material_polish') {
        assert.equal(user.history, undefined);
        assert.ok(user.materials.length > 0);
      }
      if (stage === 'questions') {
        assert.ok(Array.isArray(user.history));
        assert.equal(schema.properties.questions.minItems, effective.settings.questionCount);
        assert.equal(schema.properties.questions.maxItems, effective.settings.questionCount);
        assert.deepEqual(
          schema.properties.questions.items.properties.interaction.properties.type.enum,
          effective.questions.formats,
        );
      }
      const invalid =
        scenario.includes('בדיקת כשל') ||
        (stage === 'questions' && scenario.includes('כשל בשאלות'));
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(
        JSON.stringify({
          id: 'test-' + id,
          object: 'chat.completion',
          created: Math.floor(Date.now() / 1000),
          model: 'isolated-test:free',
          choices: [
            {
              index: 0,
              message: {
                role: 'assistant',
                content: invalid ? '{invalid' : JSON.stringify(result),
              },
              finish_reason: scenario.includes('בדיקת מגבלת פלט') ? 'length' : 'stop',
            },
          ],
          usage: { prompt_tokens: 100, completion_tokens: 100, total_tokens: 200 },
        }),
      );
    } catch (error) {
      // A transport-contract mismatch fails the test promptly instead of hanging a browser request.
      response.writeHead(500, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ error: { message: String(error) } }));
      console.error(error);
    }
  });
  await once(server.listen(port, '127.0.0.1'), 'listening');
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  return { endpoint: 'http://127.0.0.1:' + address.port, close: () => server.close() };
}

/** @param {string} message @param {number} schemaVersion */
function plan(message, schemaVersion) {
  const numeric = message.includes('ללא קטע');
  const supplied = message.includes('מקור דו לשוני');
  return {
    schemaVersion,
    name: numeric ? 'תרגול מספרים' : 'חוקרים וקוראים',
    goal: message,
    guidance: '',
    defaults: {
      topic: numeric ? 'חשבון' : 'דינוזאורים',
      audience: 'כיתה ג׳',
      difficulty: 'easy',
      questionCount: 2,
    },
    materials: numeric
      ? []
      : [
          {
            id: null,
            label: 'קטע קריאה',
            source: supplied ? 'fixed' : 'generated',
            text: supplied ? '"שָׁלוֹם" — Hello!\nDon\'t change בעלי־חיים.\n' : null,
            guidance: '',
            length: supplied
              ? null
              : message.includes('אורך קשיח')
                ? { mode: 'range', count: null, lower: 100, upper: 120 }
                : {
                    mode: 'target',
                    count: { value: 20, adjustable: true },
                    lower: null,
                    upper: null,
                  },
            controls: [],
          },
        ],
    questions: {
      formats: [numeric ? 'numeric-input' : 'text-input'],
      selectableFormat: false,
      defaultFormat: null,
      choiceCount: null,
      guidance: '',
      controls: [],
    },
    controls: message.includes('סגנון לבחירה')
      ? [
          {
            id: null,
            label: 'סגנון',
            type: 'select',
            meaning: 'סגנון הקטע',
            required: true,
            default: 'מידעי',
            unit: null,
            min: null,
            max: null,
            maxLength: null,
            options: [
              { value: 'מידעי', meaning: null },
              { value: 'סיפורי', meaning: null },
            ],
          },
        ]
      : [],
    totalLength: null,
  };
}

/** @param {{id: string}} value */
function material(value) {
  return {
    id: value.id,
    title: 'מגלים יחד',
    body: 'דינוזאורים חיו לפני שנים רבות. חוקרים לומדים עליהם בעזרת מאובנים.',
  };
}

/**
 * @param {{questions: {formats: string[], choiceCount: number}}} effective
 * @param {number} index
 */
function question(effective, index, replaced = false) {
  const type = effective.questions.formats[index % effective.questions.formats.length];
  const answer = type === 'numeric-input' ? '2' : 'דינוזאורים';
  return {
    prompt: replaced
      ? 'שאלה חלופית: מה לומדים?'
      : type === 'numeric-input'
        ? 'כמה הם 1 ועוד 1?'
        : 'על מה לומדים בקטע? ' + (index + 1),
    interaction: {
      type,
      options:
        type === 'single-choice'
          ? [
              answer,
              ...Array.from(
                { length: effective.questions.choiceCount - 1 },
                (_, i) => 'אפשרות אחרת ' + i,
              ),
            ]
          : null,
    },
    answer: { value: answer },
    points: 1,
  };
}
