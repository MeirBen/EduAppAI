const dimensions = {
  hebrew: { label: 'Hebrew', hint: 'Natural wording, real words, spelling and grammar.' },
  correctness: {
    label: 'Correctness',
    hint: 'Facts, calculations and the answer key are correct.',
  },
  ageFit: { label: 'Age fit', hint: 'Vocabulary and difficulty suit the requested learners.' },
  adherence: { label: 'Adherence', hint: 'Follows the parent request and selected parameters.' },
  answerClarity: {
    label: 'Answer clarity',
    hint: 'Each question has a clear, objectively checkable answer.',
  },
  consistency: {
    label: 'Consistency',
    hint: 'Terminology, formatting and instructions agree throughout.',
  },
};
const profileFields = {
  Model: 'Configured model',
  FallbackModel: 'Fallback model',
  ResponseFormat: 'Response format',
  ReasoningEnabled: 'Reasoning enabled',
  ReasoningEffort: 'Reasoning effort',
  ReasoningMaxTokens: 'Reasoning max tokens',
  Temperature: 'Temperature',
  TopP: 'Top-p',
  TopK: 'Top-k',
  MaxOutputTokens: 'Max output tokens',
  RequestTimeoutSeconds: 'Request timeout (seconds)',
};
const incompatibilities = {
  'case-suite-hash': 'Different case suites',
  'selected-cases-or-order': 'Different selected cases or order',
  'scenario-inputs': 'Different case inputs',
  'repeat-count': 'Different repeat counts',
  'automatic-checks-version': 'Automatic checks changed; overall pass counts use different rules',
  'incomplete-run': 'At least one run is incomplete',
  'judge-mode': 'Different judge modes',
  'calibration-suite-hash': 'Different calibration suites',
  'calibration-inputs': 'Different calibration inputs',
  'judge-prompt': 'Different judge prompts or versions',
};
const stages = [
  ['Template authoring', 'authoring'],
  ['Task generation', 'generation'],
  ['Hebrew review', 'judge'],
];
// Completed means finished, not passed, so it stays neutral.
const statusTones = {
  starting: 'running',
  running: 'running',
  cancelling: 'running',
  cancelled: 'warning',
  'rate-limited': 'warning',
  'call-limit': 'warning',
  failed: 'danger',
};
const dateTime = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'medium' });

/** Scoped DOM helpers: all supplied strings remain text, never interpreted markup. */
function createDom(document) {
  function node(tag, text, className) {
    const element = document.createElement(tag);
    if (text !== undefined && text !== null) element.textContent = String(text);
    if (className) element.className = className;
    return element;
  }

  function content(tag, text) {
    const element = node(tag, text, 'content');
    element.dir = 'auto';
    return element;
  }

  /** Values may be nodes; each pair gets a row so layouts can style it as one unit. */
  function pairs(entries, className) {
    const list = node('dl', null, className);
    for (const [key, value] of entries) {
      const row = node('div');
      const detail = content('dd', value?.nodeType ? null : display(value));
      if (value?.nodeType) detail.append(value);
      row.append(node('dt', key), detail);
      list.append(row);
    }
    return list;
  }

  function pill(status, tone = statusTones[status]) {
    return node('span', display(status), tone ? `pill ${tone}` : 'pill');
  }

  /** A native meter: the numbers stay in adjacent text, so it only reinforces them. */
  function gauge(value, max, label, className) {
    const meter = node('meter', null, className);
    meter.setAttribute('min', '0');
    meter.setAttribute('max', String(Math.max(max || 0, 1)));
    meter.setAttribute('value', String(value || 0));
    meter.setAttribute('aria-label', label);
    return meter;
  }

  function table(headings, rows, className) {
    const wrapper = node('div', null, 'table-scroll');
    const tableElement = node('table', null, className);
    const head = node('thead');
    const headerRow = node('tr');
    for (const heading of headings) {
      const cell = node('th', heading);
      cell.scope = 'col';
      headerRow.append(cell);
    }
    head.append(headerRow);
    const body = node('tbody');
    for (const row of rows) {
      const tr = node('tr');
      for (const value of row) {
        const td = node('td');
        if (value?.nodeType) td.append(value);
        else td.textContent = display(value);
        tr.append(td);
      }
      body.append(tr);
    }
    tableElement.append(head, body);
    wrapper.append(tableElement);
    return wrapper;
  }

  /** Copies the current text, so edited blocks copy their latest content. */
  function copyButton(read, label, idle = 'Copy') {
    const button = node('button', null, 'copy');
    button.type = 'button';
    button.setAttribute('aria-live', 'polite');
    const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    icon.setAttribute('viewBox', '0 0 24 24');
    icon.setAttribute('aria-hidden', 'true');
    path.setAttribute('d', 'M9 9h11v11H9zM5 15H4V4h11v1');
    icon.append(path);
    const caption = node('span', idle);
    button.append(icon, caption);
    if (label) button.append(node('span', ` ${label}`, 'sr-only'));
    button.addEventListener('click', async () => {
      caption.textContent = 'Copying…';
      try {
        await document.defaultView.navigator.clipboard.writeText(read());
        caption.textContent = 'Copied';
      } catch {
        caption.textContent = 'Copy failed';
      }
      setTimeout(() => (caption.textContent = idle), 1500);
    });
    return button;
  }

  function raw(heading, value) {
    const details = node('details');
    const text = typeof value === 'string' ? readable(value) : JSON.stringify(value, null, 2);
    const block = content('pre', text ?? 'Not recorded');
    const copyable = node('div', null, 'copyable');
    copyable.append(
      copyButton(() => block.textContent, heading),
      block,
    );
    details.append(node('summary', heading), copyable);
    return details;
  }

  return { node, content, pairs, pill, gauge, copyButton, table, raw };
}

function display(value) {
  return value === null || value === undefined || value === '' ? 'Not reported' : String(value);
}

function number(value, suffix = '') {
  return value === null || value === undefined
    ? 'Not reported'
    : `${new Intl.NumberFormat('en', { maximumFractionDigits: 6 }).format(value)}${suffix}`;
}

function delta(value, available = true) {
  return !available || value === null || value === undefined
    ? 'Unavailable'
    : `${value > 0 ? '+' : ''}${number(value)}`;
}

/** Pretty-prints JSON text; anything else, such as rejected output, stays exactly as stored. */
function readable(text) {
  try {
    const parsed = JSON.parse(text);
    return parsed && typeof parsed === 'object' ? JSON.stringify(parsed, null, 2) : text;
  } catch {
    return text;
  }
}

function transcript(messages) {
  return messages?.map((message) => `[${message.role}]\n${message.text}`).join('\n\n');
}

function plural(count, noun) {
  return `${count} ${count === 1 ? noun : `${noun}s`}`;
}

function timestamp(value) {
  return value ? dateTime.format(new Date(value)) : 'Not finished';
}

function duration(milliseconds) {
  if (milliseconds === null || milliseconds === undefined) return 'Not reported';
  return milliseconds < 1000
    ? `${Math.round(milliseconds)} ms`
    : number(Math.round(milliseconds / 100) / 10, ' s');
}

/** Known subtotal plus the calls it misses; unknown never reads as zero. */
function measured(total, unit = '') {
  const missing = total?.missingCalls ?? 0;
  if (total?.knownTotal == null)
    return missing ? `Not reported for ${plural(missing, 'call')}` : 'Not reported';
  const known = number(total.knownTotal, unit);
  return missing ? `${known} reported; data missing for ${plural(missing, 'call')}` : known;
}

function calibration(summary) {
  if (!summary.judgeEnabled) return 'Disabled';
  return summary.judgeCalibrationPassed === true
    ? 'Passed'
    : summary.judgeCalibrationPassed === false
      ? 'Failed'
      : 'Unfinished';
}

function calibrationOutcome(item) {
  if (!item.call?.contractValid || !Array.isArray(item.issues))
    return item.call?.responseReceived
      ? 'Invalid judge response; detection counts unavailable'
      : 'Review unavailable; detection counts unavailable';
  return item.passed
    ? 'Passed'
    : `Missed ${item.missingExpectedIssueCount} expected; ${item.unexpectedFindingCount} unexpected findings`;
}

/** Resolve evidence from the exact judge input, without interpreting model paths as object access. */
function findingsWithContext(evaluation) {
  let texts = [];
  try {
    const input = JSON.parse(
      evaluation.judge?.request?.find((message) => message.role === 'user')?.text,
    );
    if (Array.isArray(input.texts)) texts = input.texts;
  } catch {
    // Older or incomplete reports may not retain a readable judge request.
  }
  return (evaluation.issues ?? []).map((issue) => {
    const text = texts.find((item) => item?.path === issue.path)?.text;
    const index = typeof text === 'string' ? text.indexOf(issue.quote) : -1;
    const start = Math.max(0, index - 100);
    const end = index + issue.quote.length + 100;
    return {
      ...issue,
      source:
        issue.path === 'template.instructions'
          ? 'Template instructions'
          : issue.path.startsWith('template.')
            ? 'Reusable template'
            : issue.path.startsWith('task.')
              ? 'Generated task'
              : 'Review text',
      context:
        index < 0
          ? null
          : `${start ? '…' : ''}${text.slice(start, end)}${end < text.length ? '…' : ''}`,
    };
  });
}

function answerPositionWarning(evaluation) {
  return `At least three choice answers share the same option position (${evaluation.repeatedAnswerPosition}). Check for intentional ordering before judging this pattern. Advisory only; answers and scores are unchanged.`;
}

function hebrewFindings(summary) {
  if (!summary.judgeEnabled) return 'Not reviewed (judge disabled)';
  const reviewed = summary.generatedContentReviews?.succeeded ?? 0;
  return reviewed
    ? `${plural(summary.generatedHebrewIssueCount, 'finding')} across ${plural(reviewed, 'reviewed result')}`
    : 'No completed content reviews';
}

/** Renders saved content as text, including answer keys intended for this developer tool. */
export function renderTask(document, task) {
  const { node, content } = createDom(document);
  const section = node('section', null, 'task');
  section.append(content('h4', task.title));
  if (task.instructions) section.append(content('p', task.instructions));
  for (const block of task.contentBlocks ?? []) section.append(content('p', block.text));
  for (const [index, question] of (task.questions ?? []).entries()) {
    const item = node('section', null, 'question');
    item.append(
      node(
        'p',
        `Question ${index + 1} · ${question.id} · ${question.interaction?.type} · ${plural(question.points, 'point')}`,
        'hint',
      ),
    );
    item.append(content('p', question.prompt));
    if (question.interaction?.options?.length) {
      const options = node('ol');
      options.dir = 'auto';
      // Items stay without dir, so the list takes its direction (and marker side) from them.
      for (const option of question.interaction.options)
        options.append(node('li', option, 'content'));
      item.append(options);
    }
    const answer = node('div', null, 'answer');
    answer.append(node('strong', 'Answer key'), content('p', question.answer?.value));
    item.append(answer);
    section.append(item);
  }
  return section;
}

/** Uses only server-computed deltas and comparability flags; missing evidence stays unavailable. */
export function renderComparison(document, comparison) {
  const { node, pairs, table, raw } = createDom(document);
  const root = node('div');
  const compatible = comparison.directlyComparable;
  const compatibility = node('section', null, `card${compatible ? '' : ' warning'}`);
  compatibility.append(
    node('h3', compatible ? 'Runs are directly comparable' : 'Runs are not directly comparable'),
  );
  if (!compatible) {
    const reasons = node('ul');
    for (const reason of comparison.incompatibilities ?? [])
      reasons.append(node('li', incompatibilities[reason] ?? reason));
    compatibility.append(reasons);
  }
  compatibility.append(
    node('p', 'Changes are descriptive evidence. Positive deltas mean candidate minus baseline.'),
  );
  root.append(compatibility);
  const section = (name, heading) => {
    const element = node('section', null, 'card');
    element.dataset.section = name;
    element.append(node('h3', heading));
    root.append(element);
    return element;
  };
  const profiles = section('profile', 'Profile changes');
  const changes = Object.entries(comparison.profileChanges ?? {});
  profiles.append(
    changes.length
      ? table(
          ['Setting', 'Baseline', 'Candidate'],
          changes.map(([key, value]) => [
            profileFields[key] ?? key,
            display(value.baseline),
            display(value.candidate),
          ]),
        )
      : node('p', 'No profile changes.'),
  );
  const deltas = comparison.deltas ?? {};
  const structural = section('structural', 'Structural and adherence changes');
  if (compatible) {
    structural.append(
      pairs(
        [
          ['Automatic-pass delta', delta(deltas.scenarioAutomaticPasses)],
          ['Authoring success delta', delta(deltas.authoringSuccesses)],
          ['Generation success delta', delta(deltas.generationSuccesses)],
        ],
        'kpis',
      ),
    );
    const failures = Object.entries(comparison.checkFailureDeltas ?? {});
    structural.append(
      failures.length
        ? table(
            ['Check', 'Failure delta'],
            failures.map(([key, value]) => [key, delta(value)]),
            'numbers',
          )
        : node('p', 'No automatic check failures in either run.'),
    );
  } else structural.append(node('p', 'Structural deltas are unavailable for incompatible runs.'));
  const hebrew = section('hebrew', 'Hebrew quality');
  if (compatible && comparison.hebrewFindingsComparable) {
    hebrew.append(
      pairs([['Generated Hebrew issue delta', delta(deltas.generatedHebrewIssues)]], 'kpis'),
    );
    const kinds = Object.entries(comparison.hebrewKindDeltas ?? {});
    hebrew.append(
      kinds.length
        ? table(
            ['Issue kind', 'Issue delta'],
            kinds.map(([key, value]) => [key, delta(value)]),
            'numbers',
          )
        : node('p', 'No Hebrew findings in either run.'),
    );
  } else
    hebrew.append(
      node('p', 'Hebrew findings are not directly comparable for these runs.', 'notice warning'),
    );
  const performance = section('performance', 'Performance and cost');
  const before = comparison.baseline ?? {};
  const after = comparison.candidate ?? {};
  performance.append(
    table(
      ['Measurement', 'Baseline', 'Candidate', 'Delta'],
      [
        [
          'Average latency (ms)',
          ...[
            before.averageLatencyMilliseconds,
            after.averageLatencyMilliseconds,
            deltas.averageLatencyMilliseconds,
          ].map((value, column) => {
            // Whole milliseconds; finer digits are measurement noise.
            const rounded = value == null ? value : Math.round(value);
            return column === 2 ? delta(rounded, compatible) : number(rounded);
          }),
        ],
        ...[
          ['inputTokens', 'Input tokens'],
          ['outputTokens', 'Output tokens'],
          ['reasoningTokens', 'Reasoning tokens'],
        ].map(([key, label]) => [
          label,
          measured(before[key]),
          measured(after[key]),
          delta(deltas[key], compatible),
        ]),
        [
          'Reported cost (credits)',
          measured(before.costCredits),
          measured(after.costCredits),
          delta(deltas.costCredits, compatible),
        ],
      ],
      'numbers',
    ),
  );
  performance.append(
    node(
      'p',
      'Missing measurements remain unknown. Cost and token deltas require complete coverage in both runs.',
      'hint',
    ),
  );
  const human = section('human', 'Human review');
  human.append(
    table(
      ['Dimension', 'Average score delta'],
      Object.entries(dimensions).map(([key, { label }]) => [
        label,
        delta(
          deltas[`humanReview.${key}.average`],
          compatible && comparison.humanReviewComparable?.[key] === true,
        ),
      ]),
      'numbers',
    ),
  );
  human.append(
    node(
      'p',
      'A dimension is comparable only when the same results were reviewed in both compatible runs.',
      'hint',
    ),
  );
  root.append(raw('Raw comparison JSON (includes diagnostic evidence)', comparison));
  return root;
}

/** Wraps text in a fence longer than any backtick run inside it, so content cannot break out. */
function fence(language, text) {
  const longest = Math.max(2, ...(String(text).match(/`+/g) ?? []).map((run) => run.length));
  const marks = '`'.repeat(longest + 1);
  return `${marks}${language}\n${text}\n${marks}`;
}

/**
 * Exports saved evidence, including calibration context and scoring policy.
 * Repeated request messages reference their first occurrence.
 */
export function reportBrief(id, report, summary) {
  const json = (value) => fence('json', JSON.stringify(value, null, 2));
  const output = (text) => {
    try {
      return json(JSON.parse(text));
    } catch {
      return fence('text', text ?? 'Not recorded');
    }
  };
  const seen = new Map();
  const lines = [
    `# Evaluation report: ${report.label || id}`,
    '',
    'A saved run from the Family Learning Hebrew AI evaluation harness. Each result sends a synthetic parent request through AI template authoring, then task generation, and, when enabled, an advisory Hebrew review by the same model.',
    '',
    'Reading rules:',
    '- "completed" means the workflow finished, not that checks passed.',
    '- Automatic checks cover app contracts and case expectations, not language or educational quality.',
    '- Hebrew findings are advisory. Failed or unfinished judge calibration weakens them. No completed review means no language evidence, not zero errors.',
    '- Finding paths identify the source: template.* refers to the reusable blueprint; task.* refers to generated learner content.',
    '- Human scores: 0 unusable, 1 needs edits, 2 ready, null unreviewed.',
    '- Unknown cost or token measurements are unknown, not zero. Latency is in milliseconds.',
    '- Each distinct model request message appears once; repeats name their first occurrence.',
    '- Contains prompts and generated answer keys.',
    '',
    '## Run',
    json({
      id,
      label: report.label,
      status: report.status,
      startedAtUtc: report.startedAtUtc,
      finishedAtUtc: report.finishedAtUtc,
      runNotes: report.runNotes,
      repeat: report.repeat,
      automaticChecksVersion: report.automaticChecksVersion ?? 1,
      callDelaySeconds: report.callDelaySeconds ?? 0,
      judgeEnabled: report.judgeEnabled,
      judgePromptVersion: report.judgePromptVersion,
      maxCalls: report.maxCalls,
      plannedCalls: report.plannedCalls,
      attemptedCalls: report.attemptedCalls,
      suiteSha256: report.suiteSha256,
    }),
    '',
    '## AI profile',
    json(report.profile),
    '',
    '## Summary',
    json(summary),
    '',
  ];
  if (report.retries?.length) {
    lines.push(
      '## Rate-limited attempts before retry',
      json(
        report.retries.map((retry) => ({ ...retry, call: { ...retry.call, request: undefined } })),
      ),
      '',
    );
  }
  if (report.judgeEnabled) {
    lines.push('## Judge prompt', fence('text', report.judgePrompt ?? 'Not recorded'), '');
    lines.push(
      '## Judge calibration',
      json(
        (report.calibration ?? []).map((item) => ({
          ...item.sample,
          passed: item.passed,
          outcome: calibrationOutcome(item),
          missingExpectedIssueCount:
            item.call?.contractValid && Array.isArray(item.issues)
              ? item.missingExpectedIssueCount
              : null,
          unexpectedFindingCount:
            item.call?.contractValid && Array.isArray(item.issues)
              ? item.unexpectedFindingCount
              : null,
          findings: item.issues,
          call: item.call && { ...item.call, request: undefined },
        })),
      ),
      '',
    );
  }
  lines.push('## Results', '');
  (report.results ?? []).forEach((evaluation, index) => {
    const name = `Result ${index + 1} (${evaluation.caseId} · repetition ${evaluation.repetition})`;
    const { prompt, ...expectations } =
      report.cases?.find((item) => item.id === evaluation.caseId) ?? {};
    lines.push(
      `### ${name}`,
      '',
      '#### Parent request',
      fence('text', prompt ?? 'Not recorded'),
      '',
      '#### Case expectations and parameters',
      json({ ...expectations, parameters: evaluation.parameters }),
      '',
      '#### Automatic checks',
      json(evaluation.checks ?? {}),
      ...(evaluation.passageWordCount != null
        ? [json({ passageWordCount: evaluation.passageWordCount })]
        : []),
      ...(evaluation.repeatedAnswerPosition
        ? [
            json({ repeatedAnswerPosition: evaluation.repeatedAnswerPosition }),
            answerPositionWarning(evaluation),
          ]
        : []),
      '',
    );
    for (const [stage, key] of stages) {
      const step = evaluation[key];
      if (!step) {
        lines.push(`#### ${stage}`, 'Not attempted.', '');
        continue;
      }
      lines.push(
        `#### ${stage}`,
        'Call diagnostics:',
        json({ ...step, request: undefined, output: undefined }),
        'Output:',
        output(step.output),
        'Request messages:',
      );
      for (const message of step.request ?? []) {
        const where = `${name} · ${stage} · ${message.role}`;
        const first = seen.get(message.text);
        if (first) lines.push(`- ${message.role}: same as ${first}`);
        else {
          seen.set(message.text, where);
          lines.push(`- ${message.role}:`, fence('text', message.text));
        }
      }
      lines.push('');
    }
    lines.push(
      '#### Hebrew findings',
      evaluation.judge?.contractValid
        ? json(findingsWithContext(evaluation))
        : 'No valid Hebrew review.',
      '',
      '#### Human review',
      json(evaluation.review ?? {}),
      '',
    );
  });
  return lines.join('\n');
}

/** Starts read-only loading and serialized one-second polling. Mutations are never retried. */
export function createDashboard(document, fetchRequest = globalThis.fetch.bind(globalThis)) {
  const { node, content, pairs, pill, gauge, copyButton, table, raw } = createDom(document);
  const get = (id) => document.getElementById(id);
  const state = {
    setup: null,
    active: null,
    activeKnown: false,
    pending: false,
    savingReview: false,
    reportId: null,
    report: null,
    viewVersion: 0,
    disposed: false,
    timer: null,
    confirmation: null,
    loading: 0,
  };
  const showError = (error) => {
    get('message').textContent = error?.message || 'The request could not be completed.';
    get('message').hidden = false;
  };
  const clearError = () => {
    get('message').hidden = true;
    get('message').textContent = '';
  };

  async function request(path, method = 'GET', body) {
    const options = {
      method,
      mode: 'same-origin',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: { Accept: 'application/json' },
    };
    if (method !== 'GET') {
      options.headers['Content-Type'] = 'application/json';
      options.headers['X-Evaluation-CSRF'] = state.setup.csrfToken;
      options.body = JSON.stringify(body);
    }
    // Background status polling never shows the loading bar.
    const foreground = path !== '/api/active';
    if (foreground) trackLoading(1);
    try {
      let response;
      try {
        response = await fetchRequest(path, options);
      } catch {
        throw new Error(
          'The local dashboard could not be reached. Check that the evaluation tool is running.',
        );
      }
      let result = null;
      if (response.status !== 204) {
        try {
          result = await response.json();
        } catch {
          throw new Error('The local dashboard returned an unreadable response.');
        }
      }
      if (!response.ok) throw new Error(result?.title || 'The request could not be completed.');
      return result;
    } finally {
      if (foreground) trackLoading(-1);
    }
  }

  function trackLoading(change) {
    state.loading += change;
    document.body.toggleAttribute('data-loading', state.loading > 0);
  }

  /** Shows a spinner on the control that started the work; the control itself stays usable. */
  async function busy(control, work) {
    control.setAttribute('aria-busy', 'true');
    try {
      return await work();
    } finally {
      control.removeAttribute('aria-busy');
    }
  }

  function showView(view) {
    state.viewVersion++;
    for (const name of ['new', 'history', 'compare']) get(`view-${name}`).hidden = name !== view;
    for (const button of document.querySelectorAll('[data-view]')) {
      if (button.dataset.view === view) button.setAttribute('aria-current', 'page');
      else button.removeAttribute('aria-current');
    }
  }

  function selectedCases() {
    const selected = new Set(
      [...document.querySelectorAll('[name="caseId"]:checked')].map((input) => input.value),
    );
    return state.setup.cases.filter((item) => selected.has(item.id)).map((item) => item.id);
  }

  function estimate() {
    const count = state.setup ? selectedCases().length : 0;
    const repeat = Number(get('repeat').value);
    const judge = get('judge').checked;
    const calibrationCount = judge ? (state.setup?.calibrationCount ?? 0) : 0;
    const planned = count * repeat * (judge ? 3 : 2) + calibrationCount;
    const maxCalls = Number(get('max-calls').value);
    const callDelaySeconds = Number(get('call-delay').value);
    const valid =
      count > 0 &&
      Number.isInteger(repeat) &&
      repeat >= 1 &&
      repeat <= 5 &&
      Number.isInteger(maxCalls) &&
      maxCalls >= 1 &&
      maxCalls <= 100 &&
      get('call-delay').value !== '' &&
      Number.isInteger(callDelaySeconds) &&
      callDelaySeconds >= 0 &&
      callDelaySeconds <= 60 &&
      planned <= maxCalls;
    get('case-count').textContent = state.setup
      ? `${count} of ${state.setup.cases.length} selected`
      : '';
    get('run-estimate').replaceChildren(
      pairs([
        ['Configured model', state.setup?.profile.Model],
        ['Pause between calls (seconds)', callDelaySeconds],
        ['Selected cases', count],
        ['Repeats', Number.isInteger(repeat) ? repeat : 'Invalid'],
        ['Template + task calls', count * repeat * 2],
        ['Additional Hebrew review calls', judge ? count * repeat : 0],
        ['Calibration calls (once per run)', calibrationCount],
        ['Base calls (without retries)', planned],
        ['Maximum application calls including retries', Math.min(maxCalls, planned * 4)],
      ]),
      gauge(planned, maxCalls, 'Planned calls within max calls', planned > maxCalls ? 'over' : ''),
    );
    get('budget-message').textContent = !count
      ? 'Select at least one case.'
      : planned > maxCalls
        ? `This selection needs a budget of at least ${planned} calls (limit: 100).`
        : `${maxCalls - planned} calls above the base plan are available for retries. The server enforces the total budget.`;
    get('start-run').disabled =
      !valid ||
      !state.setup?.configured ||
      !state.activeKnown ||
      state.active?.running === true ||
      state.pending ||
      (judge && !state.setup.judgeAvailable);
    return { valid, count, repeat, judge, planned, maxCalls, callDelaySeconds };
  }

  function renderSetup(setup) {
    state.setup = setup;
    get('profile').replaceChildren(
      pairs(
        Object.entries(profileFields).map(([key, label]) => [
          label,
          setup.profile[key] ?? 'Not set',
        ]),
      ),
    );
    get('fallback-warning').hidden = !setup.profile.FallbackModel;
    get('configuration-message').hidden = setup.configured;
    get('configuration-message').textContent =
      'AI configuration is missing or invalid. Saved runs remain available. Fix the configuration and restart the dashboard to enable real evaluations.';
    get('run-controls').disabled = false;
    get('judge').disabled = !setup.judgeAvailable;
    get('judge-message').textContent = setup.judgeAvailable
      ? `When enabled: ${setup.calibrationCount} calibration calls once per run, plus one review call for each successful template/task pair. All are included in the estimate.`
      : setup.judgeError || 'Hebrew judge calibration is unavailable.';
    get('cases').replaceChildren();
    if (setup.caseError) get('cases').append(node('p', setup.caseError, 'notice warning'));
    setup.cases.forEach((item, index) => {
      const entry = node('div', null, 'case');
      const label = node('label');
      const checkbox = node('input');
      checkbox.type = 'checkbox';
      checkbox.name = 'caseId';
      checkbox.value = item.id;
      checkbox.id = `case-${index}`;
      label.append(checkbox, node('span', item.id));
      entry.append(label, content('p', item.reviewFocus));
      const expectations = [`${item.questionCount} questions`, item.interaction];
      if (item.choiceCount != null) expectations.push(`${item.choiceCount} choices`);
      if (item.minPassageWords != null || item.maxPassageWords != null)
        expectations.push(
          `passage words: ${item.minPassageWords ?? 0}–${item.maxPassageWords ?? 'unbounded'}`,
        );
      if (item.useMaximumQuestionCount) expectations.push('maximum question count');
      if (item.requireWordCountConstraint)
        expectations.push('template must retain word-count bounds');
      const chips = node('ul', null, 'chips');
      for (const expectation of expectations) chips.append(node('li', expectation, 'chip'));
      entry.append(chips, raw(`Prompt · ${item.id}`, item.prompt));
      get('cases').append(entry);
    });
    filterCases();
    estimate();
  }

  function filterCases() {
    const query = get('case-filter').value.trim().toLowerCase();
    let shown = 0;
    for (const entry of get('cases').querySelectorAll('.case')) {
      entry.hidden = Boolean(query) && !entry.textContent.toLowerCase().includes(query);
      if (!entry.hidden) shown++;
    }
    get('cases-empty').hidden = shown > 0 || !state.setup?.cases.length;
  }

  function renderActive() {
    const active = state.active;
    const progress = active?.progress;
    const indicator = get('run-indicator');
    indicator.hidden = !active?.running;
    indicator.textContent = progress
      ? `Run in progress · ${progress.completedCalls}/${progress.plannedCalls} calls`
      : 'Run starting';
    get('active-run').hidden = !active;
    if (active) {
      get('active-heading').textContent = active.running ? 'Active run' : 'Latest run';
      get('cancel-run').hidden = !active.running;
      get('cancel-run').disabled = !active.running || state.pending;
      // Without progress the bar stays indeterminate while the run prepares.
      const bar = node('progress');
      bar.setAttribute('aria-label', 'Completed calls');
      if (progress) {
        bar.setAttribute('max', String(Math.max(progress.plannedCalls, 1)));
        bar.setAttribute('value', String(progress.completedCalls));
      }
      get('active-progress').replaceChildren(
        pill(active.status),
        bar,
        pairs(
          [
            [
              'Completed / planned calls',
              progress ? `${progress.completedCalls} / ${progress.plannedCalls}` : 'Preparing',
            ],
            ['Current case', progress?.caseId],
            ['Repetition', progress?.repetition],
            [
              'Stage',
              progress?.stage === 'waiting'
                ? 'Waiting between calls'
                : progress?.stage === 'retry-wait'
                  ? 'Waiting to retry a 429 response'
                  : progress?.stage,
            ],
            ['Latest status', progress?.status],
            ['Returned model', progress?.model],
            [
              'Reported cost',
              measured(
                {
                  knownTotal: progress?.reportedCostCredits,
                  missingCalls: progress?.missingCostCalls,
                },
                ' credits',
              ),
            ],
          ],
          'kpis',
        ),
      );
      if (active.error) get('active-progress').append(node('p', active.error, 'notice warning'));
    }
    if (state.setup) estimate();
  }

  async function poll() {
    try {
      const prior = state.active;
      const active = await request('/api/active');
      // A run started while this read was pending owns the newer state.
      if (state.disposed || state.active !== prior) return;
      state.active = active;
      state.activeKnown = true;
      renderActive();
      if (prior?.running && !state.active?.running) {
        await loadHistory();
        if (state.reportId === prior.id && state.report?.status === 'running')
          await openReport(prior.id, false);
      }
    } catch (error) {
      state.activeKnown = false;
      if (!state.disposed) {
        showError(error);
        if (state.setup) estimate();
      }
    } finally {
      if (!state.disposed) state.timer = setTimeout(poll, 1000);
    }
  }

  function runName(run) {
    return `${run.label || run.id} · ${timestamp(run.startedAtUtc)}`;
  }

  async function loadHistory() {
    const history = await request('/api/runs');
    if (state.disposed) return;
    const rows = history.map((run) => {
      const identity = node('div');
      const open = node('button', null, 'run-link');
      open.append(node('span', run.label || run.id));
      open.type = 'button';
      open.title = run.id;
      open.addEventListener('click', () => busy(open, () => openReport(run.id)).catch(showError));
      identity.append(open, node('p', timestamp(run.startedAtUtc), 'hint'));
      if (!run.summary)
        return [identity, node('p', run.error || 'Report unavailable', 'fail'), '', '', '', '', ''];
      const summary = run.summary;
      const returned = summary.actualModels?.join(', ') || 'Not reported';
      const models = node('div');
      if (returned === run.configuredModel) models.append(node('p', returned));
      else
        models.append(
          node('p', `Configured: ${display(run.configuredModel)}`),
          node('p', `Returned: ${returned}`, 'hint'),
        );
      const checks = node('div', null, 'rate');
      checks.append(
        node(
          'span',
          `${summary.scenarioAutomaticPasses}/${summary.plannedCaseRuns} passed · ${plural(summary.caseCount, 'case')} × ${summary.repeat}`,
        ),
        gauge(summary.scenarioAutomaticPasses, summary.plannedCaseRuns, 'Automatic passes'),
      );
      const judge = node('div');
      judge.append(
        node('p', `Calibration: ${calibration(summary)}`),
        node('p', hebrewFindings(summary), 'hint'),
      );
      return [
        identity,
        pill(summary.status),
        models,
        checks,
        judge,
        measured(summary.costCredits, ' credits'),
        duration(summary.averageLatencyMilliseconds),
      ];
    });
    get('history-count').textContent = history.length || '';
    get('history-list').replaceChildren(
      history.length
        ? table(
            ['Run', 'Status', 'Models', 'Automatic checks', 'Judge', 'Reported cost', 'Latency'],
            rows,
            'history-table',
          )
        : node('p', 'No saved runs yet.', 'card'),
    );
    for (const id of ['baseline', 'candidate']) {
      const select = get(id);
      const selected = select.value;
      select.replaceChildren(new document.defaultView.Option('Choose a run', ''));
      for (const run of history.filter((item) => item.summary))
        select.add(new document.defaultView.Option(runName(run), run.id));
      if ([...select.options].some((option) => option.value === selected)) select.value = selected;
    }
  }

  function renderSummary(summary) {
    const passes = node('div', null, 'rate');
    passes.append(
      node('span', `${summary.scenarioAutomaticPasses} / ${summary.plannedCaseRuns}`),
      gauge(summary.scenarioAutomaticPasses, summary.plannedCaseRuns, 'Automatic passes'),
    );
    return pairs(
      [
        ['Status', pill(summary.status)],
        ['Automatic passes', passes],
        ['Cases / repeats', `${summary.caseCount} / ${summary.repeat}`],
        ['Judge calibration', calibration(summary)],
        ['Generated Hebrew findings', hebrewFindings(summary)],
        ['Actual returned models', summary.actualModels?.join(', ')],
        ['Reported cost', measured(summary.costCredits, ' credits')],
        ['Average latency', duration(summary.averageLatencyMilliseconds)],
      ],
      'kpis',
    );
  }

  function renderStep(name, step) {
    const section = node('section', null, 'stage');
    const [status, tone] = !step
      ? ['Not attempted']
      : !step.finishedAtUtc
        ? ['In progress / unfinished', 'warning']
        : step.contractValid
          ? ['Contract passed', 'pass']
          : step.statusCode === 504
            ? ['Timed out', 'danger']
            : ['Failed / rejected', 'danger'];
    section.append(
      node('h4', name),
      pill(status, tone),
      pairs([
        ['Model', step?.model],
        ['Latency', duration(step?.elapsedMilliseconds)],
        ['Reported cost', number(step?.costCredits, ' credits')],
      ]),
    );
    if (step?.failure && !step.validationErrors) section.append(node('p', step.failure, 'fail'));
    if (step?.validationErrors)
      section.append(
        block(
          'Validation errors',
          ...Object.entries(step.validationErrors).map(([path, messages]) =>
            content('p', `${path}\n${messages.join('\n')}`),
          ),
        ),
      );
    return section;
  }

  function renderReview(runId, report, result, onSaved) {
    const form = node('form', null, 'review');
    const fieldset = node('fieldset');
    fieldset.append(node('legend', 'Human review'));
    fieldset.append(
      node(
        'p',
        'Score what you checked: 0 unusable, 1 needs edits, 2 ready. Leave other dimensions unreviewed. Saving changes only your local scores and notes.',
        'hint',
      ),
    );
    const eligible =
      result.generation?.contractValid &&
      result.generation?.finishedAtUtc &&
      report.status !== 'running';
    fieldset.disabled = !eligible || (state.active?.running && state.active.id === runId);
    const fields = node('div', null, 'review-fields');
    const controls = {};
    for (const [key, { label: labelText, hint }] of Object.entries(dimensions)) {
      const field = node('div');
      const label = node('label', labelText);
      const select = node('select');
      const help = node('p', hint, 'hint');
      help.id = `review-${runId}-${result.caseId}-${result.repetition}-${key}`;
      select.setAttribute('aria-describedby', help.id);
      for (const [value, caption] of [
        ['', 'Unreviewed'],
        ['0', '0 — unusable'],
        ['1', '1 — needs edits'],
        ['2', '2 — ready'],
      ])
        select.add(new document.defaultView.Option(caption, value));
      select.value = result.review?.[key] == null ? '' : String(result.review[key]);
      controls[key] = select;
      label.append(select);
      field.append(label, help);
      fields.append(field);
    }
    const notesLabel = node('label', 'Review notes (up to 4,000 characters)');
    const notes = node('textarea');
    notes.maxLength = 4000;
    notes.rows = 3;
    notes.dir = 'auto';
    notes.value = result.review?.notes ?? '';
    notesLabel.append(notes);
    const save = node('button', 'Save review');
    save.type = 'submit';
    save.disabled = state.savingReview;
    const feedback = node('span', '', 'save-message');
    feedback.setAttribute('role', 'status');
    fieldset.append(fields, notesLabel, save, feedback);
    form.append(fieldset);
    if (!eligible)
      form.append(
        node(
          'p',
          'Review editing is available after a valid generated result has finished and the run is no longer active.',
          'hint',
        ),
      );
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (state.savingReview || fieldset.disabled || !form.reportValidity()) return;
      // Serialize saves so a delayed response cannot restore an older aggregate summary.
      state.savingReview = true;
      for (const button of document.querySelectorAll('.review button')) button.disabled = true;
      fieldset.disabled = true;
      feedback.textContent = 'Saving…';
      const review = Object.fromEntries(
        Object.entries(controls).map(([key, select]) => [
          key,
          select.value === '' ? null : Number(select.value),
        ]),
      );
      review.notes = notes.value || null;
      try {
        const summary = await busy(save, () =>
          request(`/api/runs/${encodeURIComponent(runId)}/review`, 'PUT', {
            caseId: result.caseId,
            repetition: result.repetition,
            review,
          }),
        );
        result.review = review;
        onSaved(summary);
        feedback.textContent = 'Review saved.';
        await loadHistory().catch(showError);
      } catch (error) {
        feedback.textContent = 'Review could not be saved.';
        showError(error);
      } finally {
        state.savingReview = false;
        for (const button of document.querySelectorAll('.review button')) button.disabled = false;
        fieldset.disabled = !eligible || (state.active?.running && state.active.id === runId);
      }
    });
    return form;
  }

  function resultName(evaluation) {
    return `${evaluation.caseId} · repetition ${evaluation.repetition}`;
  }

  /** One-glance status shared by the results index and each result header. */
  function resultChips(evaluation, position) {
    const checks = Object.values(evaluation.checks ?? {});
    const passed = checks.filter(Boolean).length;
    const generated = evaluation.generation?.contractValid;
    const reviewed = Object.keys(dimensions).filter((key) => evaluation.review?.[key] != null);
    const chips = node('ul', null, 'chips');
    chips.dataset.result = position;
    chips.append(
      node(
        'li',
        `checks ${passed}/${checks.length}`,
        `chip ${passed === checks.length ? 'pass' : 'fail'}`,
      ),
      node(
        'li',
        generated ? 'task generated' : 'no valid task',
        `chip ${generated ? 'pass' : 'fail'}`,
      ),
    );
    if (evaluation.judge?.contractValid)
      chips.append(node('li', plural(evaluation.issues?.length ?? 0, 'finding'), 'chip'));
    chips.append(
      node('li', `reviewed ${reviewed.length}/${Object.keys(dimensions).length}`, 'chip'),
    );
    return chips;
  }

  function block(title, ...children) {
    const section = node('section', null, 'block');
    section.append(node('h4', title), ...children);
    return section;
  }

  async function openReport(id, focus = true) {
    // Explicit selections and navigation own the view; background refreshes never take it over.
    const version = focus ? ++state.viewVersion : state.viewVersion;
    let result;
    try {
      result = await request(`/api/runs/${encodeURIComponent(id)}`);
    } catch (error) {
      if (!state.disposed && version === state.viewVersion) throw error;
      return;
    }
    if (state.disposed || version !== state.viewVersion) return;
    state.reportId = id;
    state.report = result.report;
    const report = result.report;
    const target = get('report');
    target.replaceChildren();
    target.hidden = false;
    const heading = node('section', null, 'card report-header');
    const title = node('div', null, 'card-header');
    const brief = copyButton(() => reportBrief(id, report, result.summary), '', 'Copy for AI');
    brief.classList.add('prominent');
    brief.title =
      'Copies context, requests, outputs, findings and reviews as Markdown, including answer keys.';
    title.append(content('h2', report.label || id), brief);
    heading.append(
      title,
      node(
        'p',
        `${timestamp(report.startedAtUtc)} · Finished: ${timestamp(report.finishedAtUtc)} · Pause between calls: ${report.callDelaySeconds ?? 0}s`,
        'hint',
      ),
    );
    const runId = node('p', null, 'run-id hint');
    runId.append(
      'Run ID ',
      node('code', id),
      copyButton(() => id, 'run ID'),
    );
    heading.append(runId);
    if (report.status === 'running')
      heading.append(
        node(
          'p',
          'This run is still in progress. The report refreshes when it finishes.',
          'notice',
        ),
      );
    if (report.runNotes) heading.append(content('p', report.runNotes));
    heading.append(renderSummary(result.summary));
    heading.append(raw('Captured AI profile', report.profile));
    if (report.retries?.length)
      heading.append(raw('Rate-limited attempts before retry', report.retries));
    target.append(heading);
    if (report.judgeEnabled) {
      const calibrationDetails = node('details', null, 'card');
      const passed = result.summary.judgeCalibrationPassed;
      calibrationDetails.append(
        node('summary', `Judge calibration · ${calibration(result.summary)}`),
        node(
          'p',
          passed === true
            ? 'The judge passed the known examples. This does not prove it will catch every error; inspect content findings and complete your own review.'
            : passed === false
              ? 'Calibration failed: a known-example check failed or could not be evaluated. Inspect its call and findings before trusting the content reviews.'
              : 'Calibration is unfinished. The judge has not completed the known-example checks, so its reliability has not been established for this run.',
          passed === true ? 'hint' : 'notice warning',
        ),
      );
      for (const sample of report.calibration ?? [])
        calibrationDetails.append(
          raw(`${sample.sample.id} · ${calibrationOutcome(sample)}`, sample),
        );
      target.append(calibrationDetails);
    }
    const results = report.results ?? [];
    if (results.length > 1) {
      const index = node('nav', null, 'card');
      index.setAttribute('aria-labelledby', 'results-heading');
      const title = node('h3', 'Results');
      title.id = 'results-heading';
      const list = node('ul', null, 'results-index');
      results.forEach((evaluation, position) => {
        const link = node('a');
        link.href = `#result-${position}`;
        const name = node('span', null, 'result-name');
        const caseName = node('strong', evaluation.caseId);
        caseName.title = evaluation.caseId;
        name.append(caseName, node('span', `Repetition ${evaluation.repetition}`, 'hint'));
        link.append(name, resultChips(evaluation, position));
        const item = node('li');
        item.append(link);
        list.append(item);
      });
      index.append(title, list);
      target.append(index);
    }
    results.forEach((evaluation, position) => {
      const card = node('article', null, 'card result');
      card.id = `result-${position}`;
      const header = node('header', null, 'card-header');
      header.append(node('h3', resultName(evaluation)), resultChips(evaluation, position));
      card.append(header);
      const selected = report.cases.find((item) => item.id === evaluation.caseId);
      if (selected) card.append(block('Review focus', content('p', selected.reviewFocus)));
      const steps = stages.map(([name, key]) => [name, evaluation[key]]);
      const stageGrid = node('div', null, 'stage-grid');
      stageGrid.append(...steps.map(([name, step]) => renderStep(name, step)));
      card.append(block('Stages', stageGrid));

      const checks = Object.entries(evaluation.checks ?? {});
      const checkList = node('ul', null, 'chips');
      for (const [name, passed] of checks)
        checkList.append(
          node(
            'li',
            `${name} · ${passed ? 'passed' : 'failed'}`,
            `chip ${passed ? 'pass' : 'fail'}`,
          ),
        );
      const automaticChecks = block(
        'Automatic checks',
        checks.length ? checkList : node('p', 'No automatic checks recorded.', 'hint'),
      );
      if (evaluation.passageWordCount != null)
        automaticChecks.append(
          node(
            'p',
            `Passage words: ${number(evaluation.passageWordCount)} · whitespace count across passage blocks; an exact leading task title is excluded since checks v3. Other headings remain included.`,
            'hint',
          ),
        );
      card.append(automaticChecks);
      if (evaluation.repeatedAnswerPosition)
        card.append(
          block('Advisory', node('p', answerPositionWarning(evaluation), 'notice warning')),
        );

      let task;
      if (evaluation.generation?.contractValid && evaluation.generation.output) {
        try {
          task = renderTask(document, JSON.parse(evaluation.generation.output));
        } catch {
          task = node(
            'p',
            'Saved content could not be displayed. Inspect the retained raw output.',
            'notice warning',
          );
        }
      } else
        task = node('p', 'No valid generated task. Retained output is under Raw data.', 'hint');
      card.append(block('Generated task', task));

      card.append(
        block(
          'Hebrew findings',
          node(
            'p',
            'The judge reviews both the template and the task. Paths starting with template. refer to the reusable blueprint under Raw data → Template authoring; task. refers to the task above. Suggested corrections can also be wrong.',
            'hint',
          ),
          !evaluation.judge?.contractValid
            ? node(
                'p',
                report.judgeEnabled
                  ? 'No valid Hebrew review available.'
                  : 'Hebrew judge was disabled.',
                'hint',
              )
            : evaluation.issues?.length
              ? table(
                  ['Source / kind', 'Quoted text', 'Suggested text', 'Reason'],
                  findingsWithContext(evaluation).map((issue) => {
                    const source = node('div');
                    source.append(
                      node('strong', issue.source),
                      node('p', `${issue.kind} · ${issue.path}`, 'hint'),
                    );
                    const context = node('details');
                    context.append(
                      node('summary', 'Source context'),
                      content(
                        'p',
                        issue.context ??
                          'Source context unavailable. Inspect the retained request.',
                      ),
                    );
                    source.append(context);
                    return [
                      source,
                      content('span', issue.quote),
                      content('span', issue.suggestion),
                      content('span', issue.reason),
                    ];
                  }),
                )
              : node('p', 'No findings reported. Human review is still needed.', 'hint'),
        ),
      );

      const rawData = steps
        .filter(([, step]) => step)
        .map(([name, step]) => {
          const details = node('details');
          details.append(
            node('summary', `${name} · request / output JSON`),
            raw('Request messages', transcript(step.request)),
            raw('Retained output', step.output),
            raw('Whitelisted call diagnostics', { ...step, request: undefined, output: undefined }),
          );
          return details;
        });
      if (rawData.length) card.append(block('Raw data', ...rawData));
      card.append(
        renderReview(id, report, evaluation, (summary) => {
          result.summary = summary;
          if (state.report !== report) return;
          for (const chips of target.querySelectorAll(`[data-result="${position}"]`))
            chips.replaceWith(resultChips(evaluation, position));
          savedReport.querySelector('pre').textContent = JSON.stringify(report, null, 2);
        }),
      );
      target.append(card);
    });
    const savedReport = raw('Raw saved report JSON', report);
    savedReport.dataset.savedReport = '';
    target.append(savedReport);
    if (focus) {
      showView('history');
      target.focus();
    }
  }

  for (const button of document.querySelectorAll('[data-view]'))
    button.addEventListener('click', () => showView(button.dataset.view));
  // Selecting follows the filter; clearing always empties the whole selection.
  for (const [id, selector, checked] of [
    ['select-all', '.case:not([hidden]) [name="caseId"]', true],
    ['select-none', '[name="caseId"]', false],
  ])
    get(id).addEventListener('click', () => {
      for (const checkbox of document.querySelectorAll(selector)) checkbox.checked = checked;
      estimate();
    });
  get('case-filter').addEventListener('input', filterCases);
  get('run-form').addEventListener('input', estimate);
  get('refresh-history').addEventListener('click', async () => {
    get('refresh-history').disabled = true;
    try {
      await busy(get('refresh-history'), loadHistory);
    } catch (error) {
      showError(error);
    } finally {
      get('refresh-history').disabled = false;
    }
  });

  const dialog = get('run-confirmation');
  function closeConfirmation() {
    state.confirmation = null;
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute('open');
  }
  dialog.addEventListener('cancel', () => {
    state.confirmation = null;
  });
  get('dismiss-confirmation').addEventListener('click', closeConfirmation);
  get('run-form').addEventListener('submit', (event) => {
    event.preventDefault();
    const current = estimate();
    if (!current.valid || get('start-run').disabled || !get('run-form').reportValidity()) return;
    state.confirmation = {
      caseIds: selectedCases(),
      repeat: current.repeat,
      judge: current.judge,
      maxCalls: current.maxCalls,
      callDelaySeconds: current.callDelaySeconds,
      label: get('label').value || null,
      runNotes: get('run-notes').value || null,
      confirmed: true,
    };
    get('confirm-message').textContent =
      `This evaluation can make up to ${Math.min(current.maxCalls, current.planned * 4)} OpenRouter calls including retries with ${display(state.setup.profile.Model)}, with a ${current.callDelaySeconds}-second pause between calls. Each 429 allows at most three retries within that total budget.`;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
  });
  get('confirm-run').addEventListener('click', async () => {
    if (!state.confirmation || state.pending) return;
    const body = state.confirmation;
    closeConfirmation();
    state.pending = true;
    clearError();
    estimate();
    try {
      const created = await busy(get('start-run'), () => request('/api/runs', 'POST', body));
      state.active = { id: created.id, running: true, status: 'starting', progress: null };
      renderActive();
    } catch (error) {
      showError(error);
    } finally {
      state.pending = false;
      estimate();
    }
  });
  get('cancel-run').addEventListener('click', async () => {
    if (!state.active?.running || state.pending) return;
    state.pending = true;
    get('cancel-run').disabled = true;
    try {
      await busy(get('cancel-run'), () =>
        request(`/api/runs/${encodeURIComponent(state.active.id)}/cancel`, 'POST', {}),
      );
      get('active-progress').append(
        node('p', 'Cancellation requested. Waiting for the partial report to finish.'),
      );
    } catch (error) {
      showError(error);
    } finally {
      state.pending = false;
    }
  });
  get('open-active').addEventListener('click', () => {
    if (state.active) busy(get('open-active'), () => openReport(state.active.id)).catch(showError);
  });
  get('swap-runs').addEventListener('click', () => {
    const [baseline, candidate] = [get('baseline'), get('candidate')];
    [baseline.value, candidate.value] = [candidate.value, baseline.value];
  });
  get('compare-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    if (!get('compare-form').reportValidity()) return;
    get('compare-runs').disabled = true;
    clearError();
    try {
      const comparison = await busy(get('compare-runs'), () =>
        request(
          `/api/compare?baseline=${encodeURIComponent(get('baseline').value)}&candidate=${encodeURIComponent(get('candidate').value)}`,
        ),
      );
      get('comparison').replaceChildren(renderComparison(document, comparison));
    } catch (error) {
      showError(error);
    } finally {
      get('compare-runs').disabled = false;
    }
  });

  const ready = (async () => {
    try {
      renderSetup(await request('/api/setup'));
      await Promise.all([loadHistory(), poll()]);
    } catch (error) {
      showError(error);
    }
  })();
  return {
    ready,
    dispose() {
      state.disposed = true;
      clearTimeout(state.timer);
    },
  };
}

if (typeof document !== 'undefined' && document.querySelector('[data-dashboard]'))
  createDashboard(document);
