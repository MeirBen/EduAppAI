const dimensions = {
  hebrew: 'Hebrew',
  correctness: 'Correctness',
  ageFit: 'Age fit',
  adherence: 'Adherence',
  answerClarity: 'Answer clarity',
  consistency: 'Consistency',
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
  'incomplete-run': 'At least one run is incomplete',
  'judge-mode': 'Different judge modes',
  'calibration-suite-hash': 'Different calibration suites',
  'calibration-inputs': 'Different calibration inputs',
  'judge-prompt': 'Different judge prompts or versions',
};

function node(document, tag, text, className) {
  const element = document.createElement(tag);
  if (text !== undefined && text !== null) element.textContent = String(text);
  if (className) element.className = className;
  return element;
}

function content(document, tag, text) {
  const element = node(document, tag, text, 'content');
  element.dir = 'auto';
  return element;
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

function timestamp(value) {
  return value ? new Date(value).toLocaleString() : 'Not finished';
}

function pairs(document, entries) {
  const list = node(document, 'dl');
  for (const [key, value] of entries)
    list.append(node(document, 'dt', key), content(document, 'dd', display(value)));
  return list;
}

function table(document, headings, rows, className) {
  const wrapper = node(document, 'div', null, 'table-scroll');
  const tableElement = node(document, 'table', null, className);
  const head = node(document, 'thead');
  const headerRow = node(document, 'tr');
  for (const heading of headings) {
    const cell = node(document, 'th', heading);
    cell.scope = 'col';
    headerRow.append(cell);
  }
  head.append(headerRow);
  const body = node(document, 'tbody');
  for (const row of rows) {
    const tr = node(document, 'tr');
    for (const value of row) {
      const td = node(document, 'td');
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

function raw(document, heading, value) {
  const details = node(document, 'details');
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
  details.append(
    node(document, 'summary', heading),
    content(document, 'pre', text ?? 'Not recorded'),
  );
  return details;
}

function cost(total) {
  if (!total) return 'Unknown cost';
  const subtotal =
    total.knownTotal == null ? 'Unknown subtotal' : `${number(total.knownTotal)} credits reported`;
  return `${subtotal}; ${total.missingCalls ?? 0} calls with unknown cost`;
}

function calibration(summary) {
  if (!summary.judgeEnabled) return 'Disabled';
  return summary.judgeCalibrationPassed === true
    ? 'Passed'
    : summary.judgeCalibrationPassed === false
      ? 'Failed'
      : 'Unfinished';
}

/** Renders saved content as text, including answer keys intended for this developer tool. */
export function renderTask(document, task) {
  const section = node(document, 'section', null, 'task');
  section.append(content(document, 'h4', task.title));
  if (task.instructions) section.append(content(document, 'p', task.instructions));
  for (const block of task.contentBlocks ?? []) section.append(content(document, 'p', block.text));
  for (const [index, question] of (task.questions ?? []).entries()) {
    const item = node(document, 'section', null, 'question');
    item.append(
      node(
        document,
        'p',
        `Question ${index + 1} · ${question.id} · ${question.interaction?.type} · ${question.points} points`,
        'muted',
      ),
    );
    item.append(content(document, 'p', question.prompt));
    if (question.interaction?.options?.length) {
      const options = node(document, 'ol');
      options.dir = 'auto';
      for (const option of question.interaction.options)
        options.append(content(document, 'li', option));
      item.append(options);
    }
    const answer = node(document, 'div', null, 'answer');
    answer.append(
      node(document, 'strong', 'Answer key'),
      content(document, 'p', question.answer?.value),
    );
    item.append(answer);
    section.append(item);
  }
  return section;
}

/** Uses only server-computed deltas and comparability flags; missing evidence stays unavailable. */
export function renderComparison(document, comparison) {
  const root = node(document, 'div');
  const compatible = comparison.directlyComparable;
  const compatibility = node(document, 'section', null, `card${compatible ? '' : ' warning'}`);
  compatibility.append(
    node(
      document,
      'h3',
      compatible ? 'Runs are directly comparable' : 'Runs are not directly comparable',
    ),
  );
  if (!compatible) {
    const reasons = node(document, 'ul');
    for (const reason of comparison.incompatibilities ?? [])
      reasons.append(node(document, 'li', incompatibilities[reason] ?? reason));
    compatibility.append(reasons);
  }
  compatibility.append(
    node(
      document,
      'p',
      'Changes are descriptive evidence. Positive deltas mean candidate minus baseline.',
    ),
  );
  root.append(compatibility);
  const section = (name, heading) => {
    const element = node(document, 'section', null, 'card');
    element.dataset.section = name;
    element.append(node(document, 'h3', heading));
    root.append(element);
    return element;
  };
  const profiles = section('profile', 'Profile changes');
  const changes = Object.entries(comparison.profileChanges ?? {});
  profiles.append(
    changes.length
      ? table(
          document,
          ['Setting', 'Baseline', 'Candidate'],
          changes.map(([key, value]) => [
            profileFields[key] ?? key,
            display(value.baseline),
            display(value.candidate),
          ]),
        )
      : node(document, 'p', 'No profile changes.'),
  );
  const deltas = comparison.deltas ?? {};
  const structural = section('structural', 'Structural and adherence changes');
  if (compatible) {
    structural.append(
      pairs(document, [
        ['Automatic-pass delta', delta(deltas.scenarioAutomaticPasses)],
        ['Authoring success delta', delta(deltas.authoringSuccesses)],
        ['Generation success delta', delta(deltas.generationSuccesses)],
      ]),
    );
    const failures = Object.entries(comparison.checkFailureDeltas ?? {});
    structural.append(
      failures.length
        ? table(
            document,
            ['Check', 'Failure delta'],
            failures.map(([key, value]) => [key, delta(value)]),
          )
        : node(document, 'p', 'No automatic check failures in either run.'),
    );
  } else
    structural.append(
      node(document, 'p', 'Structural deltas are unavailable for incompatible runs.'),
    );
  const hebrew = section('hebrew', 'Hebrew quality');
  if (compatible && comparison.hebrewFindingsComparable) {
    hebrew.append(
      pairs(document, [['Generated Hebrew issue delta', delta(deltas.generatedHebrewIssues)]]),
    );
    const kinds = Object.entries(comparison.hebrewKindDeltas ?? {});
    hebrew.append(
      kinds.length
        ? table(
            document,
            ['Issue kind', 'Issue delta'],
            kinds.map(([key, value]) => [key, delta(value)]),
          )
        : node(document, 'p', 'No Hebrew findings in either run.'),
    );
  } else
    hebrew.append(
      node(
        document,
        'p',
        'Hebrew findings are not directly comparable for these runs.',
        'notice warning',
      ),
    );
  const performance = section('performance', 'Performance and cost');
  const before = comparison.baseline ?? {};
  const after = comparison.candidate ?? {};
  const measurement = (total) =>
    total ? `${number(total.knownTotal)} (${total.missingCalls} missing calls)` : 'Not reported';
  performance.append(
    table(
      document,
      ['Measurement', 'Baseline', 'Candidate', 'Delta'],
      [
        [
          'Average latency (ms)',
          number(before.averageLatencyMilliseconds),
          number(after.averageLatencyMilliseconds),
          delta(deltas.averageLatencyMilliseconds, compatible),
        ],
        ...[
          ['inputTokens', 'Input tokens'],
          ['outputTokens', 'Output tokens'],
          ['reasoningTokens', 'Reasoning tokens'],
        ].map(([key, label]) => [
          label,
          measurement(before[key]),
          measurement(after[key]),
          delta(deltas[key], compatible),
        ]),
        [
          'Reported cost (credits)',
          cost(before.costCredits),
          cost(after.costCredits),
          delta(deltas.costCredits, compatible),
        ],
      ],
    ),
  );
  performance.append(
    node(
      document,
      'p',
      'Missing measurements remain unknown. Cost and token deltas require complete coverage in both runs.',
      'muted',
    ),
  );
  const human = section('human', 'Human review');
  human.append(
    table(
      document,
      ['Dimension', 'Average score delta'],
      Object.entries(dimensions).map(([key, label]) => [
        label,
        delta(
          deltas[`humanReview.${key}.average`],
          compatible && comparison.humanReviewComparable?.[key] === true,
        ),
      ]),
    ),
  );
  human.append(
    node(
      document,
      'p',
      'A dimension is comparable only when the same results were reviewed in both compatible runs.',
      'muted',
    ),
  );
  root.append(raw(document, 'Raw comparison JSON (includes diagnostic evidence)', comparison));
  return root;
}

/** Starts read-only loading and serialized one-second polling. Mutations are never retried. */
export function createDashboard(document, fetchRequest = globalThis.fetch.bind(globalThis)) {
  const get = (id) => document.getElementById(id);
  const state = {
    setup: null,
    active: null,
    activeKnown: false,
    pending: false,
    history: [],
    reportId: null,
    report: null,
    disposed: false,
    timer: null,
    confirmation: null,
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
  }

  function showView(view) {
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
    const valid =
      count > 0 &&
      Number.isInteger(repeat) &&
      repeat >= 1 &&
      repeat <= 5 &&
      Number.isInteger(maxCalls) &&
      maxCalls >= 1 &&
      maxCalls <= 100 &&
      planned <= maxCalls;
    get('run-estimate').replaceChildren(
      pairs(document, [
        ['Selected cases', count],
        ['Repeats', Number.isInteger(repeat) ? repeat : 'Invalid'],
        ['Judge calibration calls', calibrationCount],
        ['Maximum billable application calls', planned],
        ['Configured model', state.setup?.profile.Model],
      ]),
    );
    get('budget-message').textContent = !count
      ? 'Select at least one case.'
      : planned > maxCalls
        ? `This selection needs a budget of at least ${planned} calls (limit: 100).`
        : 'The server independently checks the call budget before starting.';
    get('start-run').disabled =
      !valid ||
      !state.setup?.configured ||
      !state.activeKnown ||
      state.active?.running === true ||
      state.pending ||
      (judge && !state.setup.judgeAvailable);
    return { valid, count, repeat, judge, planned, maxCalls };
  }

  function renderSetup(setup) {
    state.setup = setup;
    get('profile').replaceChildren(
      pairs(
        document,
        Object.entries(profileFields).map(([key, label]) => [label, setup.profile[key]]),
      ),
    );
    get('fallback-warning').hidden = !setup.profile.FallbackModel;
    get('configuration-message').hidden = setup.configured;
    get('configuration-message').textContent =
      'AI configuration is unavailable. Saved runs can still be browsed and reviewed.';
    get('run-controls').disabled = false;
    get('judge').disabled = !setup.judgeAvailable;
    get('judge-message').textContent = setup.judgeAvailable
      ? `${setup.calibrationCount} calibration calls are included when enabled.`
      : setup.judgeError || 'Hebrew judge calibration is unavailable.';
    get('cases').replaceChildren();
    if (setup.caseError)
      get('cases').append(node(document, 'p', setup.caseError, 'notice warning'));
    setup.cases.forEach((item, index) => {
      const entry = node(document, 'div', null, 'case');
      const label = node(document, 'label');
      const checkbox = node(document, 'input');
      checkbox.type = 'checkbox';
      checkbox.name = 'caseId';
      checkbox.value = item.id;
      checkbox.id = `case-${index}`;
      label.append(checkbox, node(document, 'strong', item.id));
      entry.append(label, content(document, 'p', item.reviewFocus));
      const expectations = [`${item.questionCount} questions`, item.interaction];
      if (item.choiceCount != null) expectations.push(`${item.choiceCount} choices`);
      if (item.minPassageWords != null || item.maxPassageWords != null)
        expectations.push(
          `passage words: ${item.minPassageWords ?? 0}–${item.maxPassageWords ?? 'unbounded'}`,
        );
      if (item.useMaximumQuestionCount) expectations.push('maximum question count');
      entry.append(
        node(document, 'p', expectations.join(' · '), 'expectations'),
        raw(document, `Prompt · ${item.id}`, item.prompt),
      );
      get('cases').append(entry);
    });
    estimate();
  }

  function renderActive() {
    const active = state.active;
    get('active-run').hidden = !active;
    if (active) {
      const progress = active.progress;
      get('active-heading').textContent = active.running ? 'Active run' : 'Latest run';
      get('cancel-run').hidden = !active.running;
      get('cancel-run').disabled = !active.running || state.pending;
      get('active-progress').replaceChildren(
        pairs(document, [
          ['Run status', active.status],
          ['Current case', progress?.caseId],
          ['Repetition', progress?.repetition],
          ['Stage', progress?.stage],
          [
            'Completed / planned calls',
            progress ? `${progress.completedCalls} / ${progress.plannedCalls}` : 'Preparing',
          ],
          ['Latest status', progress?.status],
          ['Returned model', progress?.model],
          [
            'Reported cost subtotal',
            progress?.reportedCostCredits == null
              ? 'Unknown'
              : `${number(progress.reportedCostCredits)} credits`,
          ],
          ['Calls with unknown cost', progress?.missingCostCalls],
        ]),
      );
      if (active.error)
        get('active-progress').append(node(document, 'p', active.error, 'notice warning'));
    }
    if (state.setup) estimate();
  }

  async function poll() {
    try {
      const prior = state.active;
      state.active = await request('/api/active');
      state.activeKnown = true;
      if (state.disposed) return;
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
    state.history = history;
    const rows = history.map((run) => {
      const identity = node(document, 'div');
      const open = node(document, 'button', run.label || run.id, 'secondary');
      open.type = 'button';
      open.addEventListener('click', () => openReport(run.id).catch(showError));
      identity.append(open, node(document, 'p', timestamp(run.startedAtUtc), 'muted'));
      if (!run.summary) return [identity, run.error || 'Report unavailable', '', '', '', '', ''];
      const summary = run.summary;
      const models = node(document, 'div');
      models.append(
        node(document, 'p', `Configured: ${display(run.configuredModel)}`),
        node(document, 'p', `Returned: ${summary.actualModels?.join(', ') || 'Not reported'}`),
      );
      return [
        identity,
        summary.status,
        models,
        `${summary.caseCount} cases × ${summary.repeat}; ${summary.scenarioAutomaticPasses}/${summary.plannedCaseRuns} automatic passes`,
        `Calibration: ${calibration(summary)}; ${summary.generatedHebrewIssueCount} Hebrew issues`,
        cost(summary.costCredits),
        number(summary.averageLatencyMilliseconds, ' ms'),
      ];
    });
    get('history-list').replaceChildren(
      history.length
        ? table(
            document,
            [
              'Run',
              'Status',
              'Models',
              'Cases / checks',
              'Judge',
              'Reported cost',
              'Average latency',
            ],
            rows,
            'history-table',
          )
        : node(document, 'p', 'No saved runs yet.', 'card'),
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
    return pairs(document, [
      ['Status', summary.status],
      ['Cases / repeats', `${summary.caseCount} / ${summary.repeat}`],
      ['Automatic passes', `${summary.scenarioAutomaticPasses} / ${summary.plannedCaseRuns}`],
      ['Judge calibration', calibration(summary)],
      ['Generated Hebrew issues', summary.generatedHebrewIssueCount],
      ['Actual returned models', summary.actualModels?.join(', ')],
      ['Reported cost subtotal', cost(summary.costCredits)],
      ['Average response latency', number(summary.averageLatencyMilliseconds, ' ms')],
    ]);
  }

  function renderStep(name, step) {
    const section = node(document, 'section', null, 'well');
    section.append(node(document, 'h4', name));
    const status = !step
      ? 'Not attempted'
      : !step.finishedAtUtc
        ? 'In progress / unfinished'
        : step.contractValid
          ? 'Contract passed'
          : 'Failed / rejected';
    section.append(
      pairs(document, [
        ['Status', status],
        ['Model', step?.model],
        ['Latency', number(step?.elapsedMilliseconds, ' ms')],
        ['Reported cost', number(step?.costCredits, ' credits')],
      ]),
    );
    if (step?.failure) section.append(node(document, 'p', step.failure, 'fail'));
    return section;
  }

  function renderReview(runId, report, result) {
    const form = node(document, 'form', null, 'review');
    const fieldset = node(document, 'fieldset');
    fieldset.append(node(document, 'legend', 'Human review'));
    const eligible =
      result.generation?.contractValid &&
      result.generation?.finishedAtUtc &&
      report.status !== 'running';
    fieldset.disabled = !eligible || (state.active?.running && state.active.id === runId);
    const fields = node(document, 'div', null, 'review-fields');
    const controls = {};
    for (const [key, labelText] of Object.entries(dimensions)) {
      const label = node(document, 'label', labelText);
      const select = node(document, 'select');
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
      fields.append(label);
    }
    const notesLabel = node(document, 'label', 'Review notes (up to 4,000 characters)');
    const notes = node(document, 'textarea');
    notes.maxLength = 4000;
    notes.rows = 3;
    notes.dir = 'auto';
    notes.value = result.review?.notes ?? '';
    notesLabel.append(notes);
    const save = node(document, 'button', 'Save review');
    save.type = 'submit';
    const feedback = node(document, 'span', '', 'save-message');
    feedback.setAttribute('role', 'status');
    fieldset.append(fields, notesLabel, save, feedback);
    form.append(fieldset);
    if (!eligible)
      form.append(
        node(
          document,
          'p',
          'Review editing is available after a valid generated result has finished and the run is no longer active.',
          'muted',
        ),
      );
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (fieldset.disabled || !form.reportValidity()) return;
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
        await request(`/api/runs/${encodeURIComponent(runId)}/review`, 'PUT', {
          caseId: result.caseId,
          repetition: result.repetition,
          review,
        });
        result.review = review;
        if (state.reportId === runId) {
          const saved = get('report').querySelector('[data-saved-report] pre');
          if (saved) saved.textContent = JSON.stringify(report, null, 2);
        }
        feedback.textContent = 'Review saved.';
        await loadHistory().catch(showError);
      } catch (error) {
        feedback.textContent = 'Review could not be saved.';
        showError(error);
      } finally {
        fieldset.disabled = !eligible || (state.active?.running && state.active.id === runId);
      }
    });
    return form;
  }

  async function openReport(id, focus = true) {
    const result = await request(`/api/runs/${encodeURIComponent(id)}`);
    if (state.disposed) return;
    state.reportId = id;
    state.report = result.report;
    const report = result.report;
    const target = get('report');
    target.replaceChildren();
    target.hidden = false;
    const heading = node(document, 'section', null, 'card');
    heading.append(
      content(document, 'h2', report.label || id),
      node(
        document,
        'p',
        `${timestamp(report.startedAtUtc)} · Finished: ${timestamp(report.finishedAtUtc)}`,
        'muted',
      ),
    );
    if (report.runNotes) heading.append(content(document, 'p', report.runNotes));
    heading.append(renderSummary(result.summary));
    heading.append(raw(document, 'Captured AI profile', report.profile));
    target.append(heading);
    if (report.judgeEnabled) {
      const calibrationDetails = node(document, 'details', null, 'card');
      calibrationDetails.append(
        node(document, 'summary', `Judge calibration · ${calibration(result.summary)}`),
      );
      for (const sample of report.calibration ?? []) {
        const entry = node(document, 'section');
        entry.append(
          node(
            document,
            'h4',
            `${sample.sample.id} · ${sample.passed ? 'Passed' : 'Failed / unfinished'}`,
          ),
        );
        entry.append(raw(document, 'Calibration request, findings and call', sample));
        calibrationDetails.append(entry);
      }
      target.append(calibrationDetails);
    }
    for (const evaluation of report.results ?? []) {
      const card = node(document, 'article', null, 'card');
      card.append(
        node(document, 'h3', `${evaluation.caseId} · repetition ${evaluation.repetition}`),
      );
      const selected = report.cases.find((item) => item.id === evaluation.caseId);
      if (selected) card.append(content(document, 'p', selected.reviewFocus));
      const stages = node(document, 'div', null, 'stage-grid');
      stages.append(
        renderStep('Template authoring', evaluation.authoring),
        renderStep('Task generation', evaluation.generation),
        renderStep('Hebrew review', evaluation.judge),
      );
      card.append(stages, node(document, 'h4', 'Automatic checks'));
      const checks = Object.entries(evaluation.checks ?? {});
      const checkList = node(document, 'ul', null, 'checks');
      for (const [name, passed] of checks)
        checkList.append(
          node(
            document,
            'li',
            `${passed ? 'Passed' : 'Failed'} · ${name}`,
            passed ? 'pass' : 'fail',
          ),
        );
      card.append(checks.length ? checkList : node(document, 'p', 'No automatic checks recorded.'));
      if (evaluation.generation?.contractValid && evaluation.generation.output) {
        try {
          card.append(renderTask(document, JSON.parse(evaluation.generation.output)));
        } catch {
          card.append(
            node(
              document,
              'p',
              'Saved content could not be displayed. Inspect the retained raw output.',
              'notice warning',
            ),
          );
        }
      } else
        card.append(
          node(
            document,
            'p',
            'No valid generated task. Retained output is available below.',
            'muted',
          ),
        );
      card.append(node(document, 'h4', 'Hebrew findings'));
      if (evaluation.judge?.contractValid) {
        if (evaluation.issues?.length)
          card.append(
            table(
              document,
              ['Kind / path', 'Quoted text', 'Suggested text', 'Reason'],
              evaluation.issues.map((issue) => [
                `${issue.kind} · ${issue.path}`,
                content(document, 'span', issue.quote),
                content(document, 'span', issue.suggestion),
                content(document, 'span', issue.reason),
              ]),
            ),
          );
        else
          card.append(node(document, 'p', 'No findings reported. Human review is still needed.'));
      } else
        card.append(
          node(
            document,
            'p',
            report.judgeEnabled
              ? 'No valid Hebrew review available.'
              : 'Hebrew judge was disabled.',
          ),
        );
      for (const [name, step] of [
        ['Template authoring', evaluation.authoring],
        ['Task generation', evaluation.generation],
        ['Hebrew review', evaluation.judge],
      ]) {
        if (!step) continue;
        const details = node(document, 'details');
        details.append(node(document, 'summary', `${name} · raw request / output JSON`));
        details.append(
          raw(document, 'Request messages', step.request),
          raw(document, 'Retained output', step.output),
          raw(document, 'Whitelisted call diagnostics', {
            ...step,
            request: undefined,
            output: undefined,
          }),
        );
        card.append(details);
      }
      card.append(renderReview(id, report, evaluation));
      target.append(card);
    }
    const savedReport = raw(document, 'Raw saved report JSON', report);
    savedReport.dataset.savedReport = '';
    target.append(savedReport);
    showView('history');
    if (focus) target.focus();
  }

  for (const button of document.querySelectorAll('[data-view]'))
    button.addEventListener('click', () => showView(button.dataset.view));
  get('select-all').addEventListener('click', () => {
    for (const checkbox of document.querySelectorAll('[name="caseId"]')) checkbox.checked = true;
    estimate();
  });
  get('select-none').addEventListener('click', () => {
    for (const checkbox of document.querySelectorAll('[name="caseId"]')) checkbox.checked = false;
    estimate();
  });
  get('run-form').addEventListener('input', estimate);
  get('run-form').addEventListener('change', estimate);
  get('refresh-history').addEventListener('click', async () => {
    get('refresh-history').disabled = true;
    try {
      await loadHistory();
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
      label: get('label').value || null,
      runNotes: get('run-notes').value || null,
      confirmed: true,
    };
    get('confirm-message').textContent =
      `This evaluation can make up to ${current.planned} OpenRouter calls.`;
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
      const created = await request('/api/runs', 'POST', body);
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
      await request(`/api/runs/${encodeURIComponent(state.active.id)}/cancel`, 'POST', {});
      get('active-progress').append(
        node(document, 'p', 'Cancellation requested. Waiting for the partial report to finish.'),
      );
    } catch (error) {
      showError(error);
    } finally {
      state.pending = false;
    }
  });
  get('open-active').addEventListener('click', () => {
    if (state.active) openReport(state.active.id).catch(showError);
  });
  get('compare-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    if (!get('compare-form').reportValidity()) return;
    get('compare-runs').disabled = true;
    clearError();
    try {
      const comparison = await request(
        `/api/compare?baseline=${encodeURIComponent(get('baseline').value)}&candidate=${encodeURIComponent(get('candidate').value)}`,
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
