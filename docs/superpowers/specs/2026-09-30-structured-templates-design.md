# Structured templates and conversational editing

Status: implementation specification; application changes are not implemented.
Finalized for planning on 30 September 2026. This supersedes the research draft.
The [implementation plan](../plans/2026-09-30-structured-templates.md) defines
the delivery sequence. Existing product and architecture documents describe the
current application until the implementation replaces that behavior.

## 1. Product outcome

A parent describes an educational activity, adjusts it through conversation or
ordinary controls, saves a reusable template and creates tasks with selected
values. There is **no separate generated-blueprint screen or mandatory
blueprint-review step**. Creation and editing happen in one workspace.

The app owns the layout, supported field types, input resolution, request
construction and validation. AI interprets learning requests and writes content.
Parents never maintain field keys, JSON, placeholders or technical prompts.

Generic means any subject expressed through supported text materials and
numeric, short-text or single-choice questions. It does not promise arbitrary
widgets, audio, drawing evaluation, subjective essay grading or automatic fact
checking. Question count and known output properties are checkable. Natural
Hebrew, pedagogy and arbitrary custom meanings remain model-dependent.

Keep four kinds of evidence separate: valid inputs, structurally valid output,
measured expectations and reviewed semantic quality. A saved Draft is not
approved content; an empty judge result is not proof of correct Hebrew.
Resolution is deterministic for the same accepted plan, input and policy
versions. Assembly and measurement are deterministic for the same resolved
request and provider payload. Generated IDs/timestamps are boundary metadata;
generated prose is not promised to repeat.

## 2. First-release scope and experience

Keep the existing .NET 8, Angular, OpenRouter and evaluation infrastructure.
Conversation edits reusable templates only. Task settings are adjustable before
generation; existing generated tasks remain immutable. Drafts and conversational
context remain client-owned for this release, with an unsaved-changes warning on
navigation. No persistent chat, cross-device draft sync or agent runtime.

The workspace has a chat area and a consistent settings area. On narrow screens,
use accessible tabs or stacked regions in the same route. App-owned sections are
purpose/defaults, materials, questions and additional choices. Hide inapplicable
sections. Display educational requirements in concise, editable language; do not
show a generated system prompt or a generic schema-field builder.

1. Parent describes the activity and explicitly sends the request.
2. AI returns a valid plan or one focused clarification. An initial plan opens
   as the unsaved working draft in the same workspace.
3. Direct edits require no AI. Subsequent chat requests propose a complete plan.
   After validation and a revision check, apply it to the unsaved draft, show
   the app-computed changes inline and offer Undo. No separate Accept screen.
4. The parent can continue chatting or editing, then select **שמירת תבנית** or
   **שמירת השינויים**. This is the explicit publication boundary.
5. **יצירת משימה** opens the normal per-task controls from the saved revision.
   Generating content is a separate explicit AI action; saving a template never
   starts it automatically.

The inline change display must include removals and unintended changes, not only
the model's claimed summary. Saving is disabled during an active authoring call
or while form validation fails. Errors preserve the draft and entered message.
Do not label an AI-generated artifact as educationally approved.

### Examples

- Reading: grade-three Hebrew, four choice questions and an explicitly requested
  adjustable passage length of about 350 words. Show that control without
  inventing genre or other menus. A later chat request can add fiction/facts.
- Math: ten numerical-answer exercises with a requested maximum-operand input.
  No passage controls. That custom input is delivered reliably; arbitrary
  operands in prose are not automatically verified by an arithmetic parser.
- Logic: plain-text puzzles with supported answer formats and adjustable shared
  settings. Unsupported interactive puzzle mechanics require clarification.
- Open answers: short, objectively checkable answers. A request for essays must
  explain the capability limit instead of pretending subjective grading exists.
- Source activity: parent supplies an English passage, requests Hebrew
  directions and English answers. Preserve the source and intended language
  differences.
- Per-task format: parent asks to choose choice versus short-text questions. The
  template exposes that supported choice, without adding a generic key field.

## 3. One application-owned contract

Use a new LearningPlan contract, schemaVersion 5. Retain embedded JSON schemas,
typed C# and TypeScript models and explicit domain validation. The model
produces instances of that contract, never executable schemas or UI code. Manual
changes and normalized AI proposals pass the same plan validator before
publication.

Distinguish untrusted provider proposals, canonical plans and resolved requests
without duplicating their shared records. Only proposals allow new null IDs;
canonical plans have validated identities. Normalization assigns IDs and handles
documented equivalent optional representations. It never rewrites language,
clamps values, invents semantics or guesses an old identity from a label.

The following are logical field names and invariants, not a second handwritten
schema implementation. Concrete provider schemas must stay within the selected
endpoint's capabilities and be covered by request-wire tests.

### Plan and shared settings

LearningPlan contains schemaVersion, name, goal, guidance, defaults, materials,
questions, controls and optional totalLength.

- defaults retains TaskSettings: topic, audience, difficulty and questionCount.
  These remain ordinary per-task controls. Difficulty is easy/medium/hard
  relative to the audience. Preserve requested defaults; missing values may be
  proposed for review, with medium as the existing unspecified difficulty
  default.
- goal describes the activity. guidance contains shared semantic requirements,
  including requested language distinctions. Typed values and custom meanings
  are not repeated there. Arbitrary prose contradictions cannot be detected
  perfectly; prompts, evaluation and visible editing address that limitation.
- A summary is rendered from the plan. Do not store another AI-authored summary
  as a competing source of task requirements.

### Materials

MaterialDefinition contains id, label, source, guidance, optional text, optional
length and controls. Source is generated, fixed or per-task.

- generated: AI creates title/body. text is absent. Optional length belongs
  here.
- fixed: text is required and copied unchanged from the published plan.
- per-task: the parent supplies required source text when creating each task. No
  generated length expectation may be attached to supplied source material.
- Transformation of a source is a separate generated material whose guidance
  states the transformation. It must not silently replace a verbatim source.

Material IDs are internal associations, not prompt placeholders. Generated
output supplies only the expected generated IDs, once each; application assembly
restores the published material order and inserts original supplied text.
Unexpected, missing or duplicate generated IDs reject the response.

The parent's accepted source string is authoritative from capture onward. A
dedicated source field needs no AI. If AI extracts a proposed source from chat,
show it inline as unverified until the parent confirms or directly edits it;
block publication while that verification is pending. This is a source-specific
check inside the workspace, not another blueprint screen. Browser controls may
normalize line endings before acceptance; preserve the accepted string, not
claimed external-file bytes.

For a retained fixed-source material, authoring rejects a changed text or source
kind against the submitted base. Replacing it is a direct source edit. Removal
is visible in the computed changes. Generation rejects supplied-source IDs in
the model's materials and always inserts the accepted originals itself.

### Questions

QuestionPlan contains formats, selectableFormat, defaultFormat, optional
choiceCount, optional countBounds, guidance and controls.

- formats is a distinct nonempty subset of numeric-input, text-input and
  single-choice. With selectableFormat=false, one format means uniform
  questions; several mean a mixture containing each format at least once. The
  chosen total must be large enough for that mixture. defaultFormat is null in
  this mode. The model chooses distribution/order beyond required coverage; the
  application does not promise balanced question slots.
- With selectableFormat=true, defaultFormat is one allowed format. The parent
  chooses one format for the whole task. No task-time AI interpretation is
  needed to decide which input controls to render.
- choiceCount is an IntegerChoice, required when single-choice is allowed and
  absent otherwise. Values are 2–6, matching existing renderer capabilities.
  Ignore it in resolved generation input when the selected format is not choice.
- Shared questionCount is a positive integer. countBounds may contain explicit
  parent-requested input bounds; default and chosen counts must satisfy them. Do
  not reintroduce a universal educational ceiling of 20.
- Exact per-format quotas in a mixed task are outside this first contract.
  Clarify that limitation and offer a flexible mixture or a uniform format;
  never silently discard a requested distribution. Adding typed quotas later
  must define how they interact with the shared count.

Questions retain current answer/points safety contracts and plain-text
rendering. App numbering owns visible numbering. Models supply bare
question/option text; do not strip meaningful punctuation through general
normalization rules.

### Known numeric choices and length

IntegerChoice contains value, adjustable, min and max. A fixed choice has no
input bounds. An adjustable choice stores its default and optional requested
bounds; no field-specific bounds are invented. It is embedded in its owning
requirement, not connected through a string expression or binding graph.

LengthExpectation has mode target, exact or range. Target/exact uses one
IntegerChoice; range uses positive inclusive lower/upper endpoints and is fixed
in this release. The ordinary UI offers one approximate desired word count.
Exact/range modes appear only when expressly requested or selected by the
parent. Changing a fixed range per task is not silently approximated by a
midpoint.

Material length concerns that generated body. totalLength concerns all generated
material bodies together, excluding supplied sources. Do not combine totalLength
with per-material lengths in the first release; explain the overlap and let the
parent select one scope. Arbitrary subsets and nested groups are not supported.
Preserve requests for two passages with a combined length.

Allowed input bounds and generated-output expectations are distinct. A target
350 with allowed inputs 300–400 still requests approximately 350 words; the
input bounds do not become an output acceptance interval.

### Custom controls

ControlDefinition contains id, label, type, meaning, required, default, optional
unit, min, max, maxLength and options. Use only applicable metadata for text,
integer, select or boolean. Options have a visible value and optional meaning;
the value is also the submitted selection. Avoid a second option-key system.

Controls are embedded at plan, material or question scope. Their meaning and
selected value travel together. A fiction/facts selection describes what to
create; it is not a claim that factual truth has been verified.

Add custom controls only for explicitly requested per-task choices. Keep fixed
requirements as requirements; do not invent menus for genre, tone or length. If
the request leaves a necessary teaching choice open, propose a visible
assumption or ask a clarification instead of adding an unwanted field.

Defaults resolve only omitted values. Preserve valid false, zero and explicit
empty optional text. Required text must be nonblank after validation, without
rewriting the retained original. Missing optional values without defaults mean
no additional instruction from that control. Other fallback behavior belongs in
the reviewed meaning and remains semantic, not executable logic.

Control label changes keep the same ID. Changing an option value is an explicit
choice change and must update an invalidated default before publication.
Renaming an option never rewrites a previously saved task's selected value.

### Identity and operational bounds

For new AI-proposed materials/controls, require null IDs. The app assigns unique
IDs before common validation; existing objects retain their IDs. Non-null IDs
unknown to the submitted base plan are rejected in AI replies. Direct editor
additions obtain IDs from application code. IDs are local plan identity, never
authorization or database resource references. No provider-generated family,
revision or request identities are trusted.

Use opaque IDs containing 32 lowercase hexadecimal characters, generated from
UUIDs. Require uniqueness across the plan's materials and controls. Moving a
control preserves its ID but changes its scope and appears in the change
display. Retained IDs must keep their entity category: a material ID cannot
become a control ID or vice versa. Never recover identities by matching labels.

Retain current limits: name/labels 100 characters, shared topic/audience 200,
four materials, sixteen custom controls across all scopes, twenty options per
select, option value 100, custom text at most 500, supplied body at most 4,000.
Use goal at most 500, shared guidance 4,000, scoped guidance 1,000, custom
meaning 500 and option meaning 200. Numeric values stay in the signed 32-bit
range; known counts/lengths must also be positive. These are product/resource
limits, not proof of output feasibility.

Bound the canonical compact plan JSON to 24,000 characters using the same
serializer as provider input. Retain the 256 KiB HTTP body limit,
32,000-character provider-response limit, provider token/deadline caps and
current rate limits. Reject invalid or detectably impossible inputs before
calling AI. Never silently reduce counts or alter requirements to fit a budget.

Validate aggregate feasibility before count-sized allocation or provider use.
Use checked arithmetic and the shared content limits: required sources plus the
minimum required output text must fit 8,000 characters. At least one title
character and two characters per question are required, before materials and
choice options. This gives a conservative operational question-count bound
without another arbitrary educational limit; refine the lower bound for known
formats. Four supplied sources of 4,000 characters each must fail before AI. Do
not mistake this proof of impossibility for a token estimate or quality test.

Name and enforce byte limits for compiled provider input and output schema
before dispatch; count the schema wherever it appears in the actual request.
Include the authoring reply envelope/assumptions when testing the
32,000-character response limit. Choose concrete request/schema byte limits in
implementation using bounded maximum-size wire/load fixtures and record them in
configuration and tests before cutover. Do not assert unmeasured provider
capacity here.

## 4. Resolution, generation and saved output

TaskRequest contains complete settings, optional questionFormat/choiceCount,
materialInputs keyed by material ID, optional totalWordCount, and controlValues
keyed by control ID. Material input permits wordCount or sourceText only where
its material explicitly allows that override. Range endpoints are not overrides.
Unknown IDs, inapplicable properties and overrides of fixed requirements fail
with field-level validation errors. Missing adjustable numeric/format overrides
use the published default; required per-task source text has no implicit
default.

Input resolution has this matrix:

| Submitted value     | Meaning                                            |
| ------------------- | -------------------------------------------------- |
| Omitted override    | Use its default; fail if required and unavailable. |
| Explicit null       | Reject; it is not an instruction to use a default. |
| Valid false/zero    | Preserve the value.                                |
| Empty optional text | Preserve it; do not replace it with a default.     |
| Blank required text | Reject without rewriting source text.              |
| Numeric string      | Reject; do not coerce it to a number.              |

Required settings remain complete. Omitted override maps mean no overrides; null
maps are invalid. Null in irrelevant plan metadata or a missing plan default is
separate from null in submitted task choices. Preserve member presence at the
HTTP boundary; nullable CLR properties alone lose that distinction. Keep this
parsing localized, without a general optional-type framework.

Precedence is application capabilities/ownership/limits, published typed
requirements and allowed overrides, explicit valid choices then omitted
defaults, and finally semantic guidance. Scoped guidance may specialize shared
prose but cannot override typed requirements. Arbitrary prose conflicts remain
visible review concerns, not a solved semantic-validation problem.

The HTTP envelope is CreateInstanceRequest(expectedVersion, input: TaskRequest).
expectedVersion is the published revision shown in the form; it is checked by
the endpoint and never sent to the model.

The endpoint loads and pins one owned revision. TaskRequestResolver validates
the plan and input, resolves values exactly once and produces
ResolvedTaskRequest. It owns input bounds/defaults; it does not call the model
or access persistence. Generation receives the resolved requirements, scoped
values and source data. It receives no transcript, unresolved placeholders or
competing default values.

ResolvedTaskRequest is also the one immutable per-request generation contract;
do not add a second compiler model or reread LearningPlan downstream. The same
instance supplies provider input, specialized schema, structural validation,
source assembly, measurements and recorded evidence. Include effective rules and
expected generated IDs, with policyVersion 1 recorded in the saved resolved
input; exclude adjustable flags and alternate defaults. Advance that version
when resolution/assembly semantics change independently of the plan schema.
Consumer-owned schema copies cannot mutate these requirements or another call.

Generate through the existing IChatClient/provider registration. Each explicit
authoring or task-generation operation makes one application AI call. Prompts
separate learning data from application rules; delimiters alone are not a
security guarantee. No model tools, automatic translation, proofreader or repair
loop. Provider errors, refusal, truncation and invalid output fail safely.

TaskDocument replaces unassociated content blocks with materials containing id,
optional title and body. Title/body separation makes counting structural rather
than a guess about the first line. It retains task title, learner instructions,
questions and parent-only answer keys. The provider produces generated materials
only; the app inserts fixed/per-task sources and validates the assembled
document. Keep the existing 8,000-character total content limit, including
titles, body, directions, prompts, options and answers. Source material counts
toward it.

Per-request schemas constrain exact question count, permitted formats, choice
count and expected generated material identities where the provider supports it.
Server validation repeats the relevant checks, including mixed-format coverage.
Schema constraints are never the only validation boundary.

Save TaskSnapshot containing content, lengthMeasurements and measurementVersion
1 in the existing ContentJson column; use InputJson for resolved selections.
GenerationMetadataJson continues to hold provider/model/prompt/UTC metadata. No
measurement table or separate provider client. Parent preview exposes content
and measurements, never regenerates and never silently recalculates historical
measurements under a newer policy.

## 5. Length measurement and quality policy

Measurement version 1 counts whitespace-separated tokens containing at least one
Unicode letter or number. Ignore punctuation-only tokens. Hebrew maqaf inside a
token stays within one token; vowel points do not create another word. Count
bodies only and sum body counts for a total. Do not strip arbitrary headings or
normalize the saved text. This is an explicit product counting convention, not a
claim to universal linguistic segmentation.

Use one TextLength implementation for runtime display and evaluation. Examples:
“שלום עולם” is 2; “שלום — עולם” is 2; “בעלי־חיים” is 1; “don't” is 1; a
standalone emoji is 0. Cover newlines, niqqud, mixed languages and supplementary
Unicode letters in tests. Supplied source preservation uses exact accepted
string values.

A LengthMeasurement records scope, requested expectation, actual words and
nullable satisfied. Target has no pass/fail tolerance and satisfied is null.
Exact/range compares to the explicit count/endpoints. Save structurally valid
content as a parent-review Draft even when those expectations are unmet; display
the unmet condition prominently. No automatic trimming or correction call. An
explicit demand to return nothing unless exact must be clarified as unsupported
strict enforcement, not silently weakened. This is the chosen first-release
policy; question/choice structure violations still reject without saving.

Language, factuality, grounding and custom-control adherence remain semantic
quality checks. Existing human review and the developer Hebrew judge assess
these separately. A successful schema check is not a quality endorsement. The UI
derives execution/structural status and expectation warnings separately from the
saved snapshot. Use a needs-review message with any unmet expectations, not a
generic educational pass or a new persisted Approved state.

## 6. Authoring API and workspace state

Evolve POST /api/ai/template-drafts instead of adding an agent/chat service. The
request carries message, optional baseDefinition, baseRevision, requestId and
bounded context. A message is at most 4,000 characters. Context contains only
user/assistant turns from the current unresolved request: at most six turns and
12,000 text characters. Keep the original request until its clarification is
resolved. Do not silently discard required context at the limit; ask the parent
to consolidate the request. Completed old conversations are not model state.

TemplateAuthoringInput excludes request/revision metadata before provider input.
The model returns AuthoringReply: kind proposal or clarification, a definition
or one question of at most 1,000 characters, and up to eight short assumption
notes of 200 characters each. Exactly one outcome is populated. Assumptions are
transient review aids, not another persisted plan or authoritative model change
log. Any assumption affecting later generation must also exist in the plan
itself; transient notes alone cannot carry an operative requirement.

Normalize new identities, validate the proposed plan and compute PlanChange[]
against the submitted base. The endpoint returns definition/question,
assumptions, changes, metadata and application-echoed requestId/baseRevision.
Domain comparison includes ordered material/control changes, defaults, removals,
meanings and source changes. UI labels changes in Hebrew; raw paths/IDs are not
parent copy. An identical proposal is a no-op: show that nothing changed,
without adding an Undo snapshot or marking a saved template dirty. First-release
chat edits the whole plan. A prompt to change only one section is not a
preservation guarantee. Do not offer section-scoped AI actions until an
application-owned edit target can reject out-of-scope changes. No JSON Patch,
stale-response merging or expression language is introduced.

The workspace owns the raw form draft, monotonically increasing revision, active
request, current clarification and Undo history. Revision advances on every
direct edit, applied proposal and Undo, including invalid transient edits. Only
a valid draft can be submitted or published. Keep at most twenty in-memory Undo
snapshots; coalesce typing in a field into one history action, while every
keystroke still advances the stale-response revision. No history persistence.

An AI response applies only if requestId is active and baseRevision still
matches. Apply atomically, push the previous draft into Undo and show actual
changes. Otherwise mark it stale without overwriting anything; retry is explicit
against the current draft. Cancelling invalidates the request identity before
awaiting transport cancellation. A changed draft invalidates an old
clarification too. Do not stream partial JSON into the form. Direct edits remain
usable during a call; the revision rule protects them. Undo cannot mutate
published revisions.

Dirty state compares draft content against the last saved baseline, including
invalid transient edits; it is not inferred from revision counters. Undo can
restore saved content while increasing draftRevision. Keep pending source
verification in the same route-owned state and restore it with Undo.

Publication remains server-authorized, revalidates the full plan and retains
expectedVersion concurrency checks. A 409 preserves local edits. Successful save
updates the baseline/version once and disables duplicate publication until
content changes. While Save is outstanding, briefly disable edits, authoring and
Undo. Preserve the draft on failure; do not discard a successful save response
using the AI stale-reply rule.

Task creation checks the displayed expectedVersion against the owned current
revision before AI; a mismatch returns 409 and requires explicit reload. After
that check, pin the immutable revision. A subsequent publication does not cancel
generation or switch its inputs; save against the pinned revision. Deletion or
reset winning before persistence still saves nothing. Never hold a transaction
across the model call.

requestId correlates authoring only; it is not durable idempotency. No automatic
publication or generation retry is added. An ambiguous network failure must say
that a save may have completed and offer checking the library before retrying;
never claim nothing was saved without evidence. Durable idempotency would need a
separate explicit design before introducing automatic write retries.

## 7. Ownership, errors and tools

Keep one backend project and feature-oriented folders. TaskEngine owns
contracts, resolution, AI requests, output assembly and validation. Features own
HTTP, family authorization and persistence. The provider adapter owns
protocol/config. Angular's route workspace owns draft state; presentation
children receive state and emit edits. LearningApi owns HTTP; there is no global
AI store or mediator.

Keep native Angular controls, Signal Forms, RTL logical spacing, keyboard
access, focus/error association and text-only rendering. New dynamic control
kinds require app support; the model cannot generate HTML, expressions or
executable validators. No UI framework or agent framework is added for this
work.

Retain ownership checks, CSRF, ProblemDetails, UTC, cancellation and bounded
concurrency. Never put parent answer keys into a future child DTO. Do not log
learning text, keys, raw provider errors or reasoning. Progress reports actual
states only. Editing/viewing valid local data must work with AI unavailable.
Production has no application retry loop; evaluation retains its bounded 429
policy. A timeout or cancellation does not guarantee provider billing stopped.

JsonSchema.Net remains a future experiment only if it removes demonstrable
validation duplication. No BAML, form generator, A2UI, orchestration runtime,
retrieval subsystem or additional evaluation platform is required here.
Generation has no cross-task history and cannot promise that a passage will
never recur. Request fresh content without claiming global uniqueness.

Keep the current answer.value contract and prose-based language intent for this
cutover. Application-planned mixed question slots, a typed bilingual language
policy and option-index answer identities are deferred, independent product
changes. None is required to remove the blueprint screen.

## 8. Cutover and evaluation

This is a coordinated schema-5 cutover, not a deployed dual-mode system. Replace
the old field/prose editor, schema-4 contracts and obsolete tests when the new
path is integrated. Do not add compatibility readers or adapters. The user has
allowed discarding development learning data: use an explicit migration clearing
learning templates/revisions/tasks while preserving accounts and configuration.
Do not erase the database, credentials or Data Protection keys. Run migrations
only after stopping development watchers and taking an appropriate backup. This
planning change itself deletes no application data.

Extend the existing harness rather than redesigning it. Evaluate initial
authoring, a bounded sequence of refinements, then generation. Preserve raw
model output separately from normalized plans and application-assembled
snapshots. Capture prompt/schema/profile versions, request counts, latency and
known costs. Clarification is a distinct outcome, not malformed JSON or a
task-generation attempt. Deterministic fixture cases with complete requests
expect proposals.

Also run generation-only cases from hand-authored canonical plans and fixed
inputs. This separates interpretation/editing failures from writing failures; do
not regenerate a plan in trials advertised as generator comparisons. Reuse the
same engine and harness, with one generation call plus an optional judge. Fixed
plans are developer fixtures, never production educational seed data.

Record effective input/schema fingerprints, policy versions and the actual model
and provider when available, alongside raw output, normalized plans and
assembled snapshots. Use stable serialization for hashes. A hash alone cannot
reconstruct input: preserve the corresponding effective request/schema in
authorized local artifacts. Keep source text and provider secrets out of
ordinary logs.

At cutover use authoring v23, generation v20, Hebrew review v9, report format 4
and automatic checks version 9; advance further if implementation later changes
those semantics. Update calibration paths and source context without correcting
planted defects. All refinement and retry attempts count toward the run budget;
checkpoint each. Comparison requires matching edit sequences and check versions.
Older report formats receive a clear unsupported-format message, not conversion.

Compare judge-derived findings only when judge instructions, configuration,
actual model identities and comparable review coverage match and both
calibrations pass. Record missing provider/model evidence; do not invent it.
Changing the application model also changes the current judge, so suppress
judge-derived quality deltas for that comparison without adding another judge
provider. Structural comparisons may remain valid on matched inputs/policies.
Human review checks calibration labels; fixtures are not linguistic authority.

Keep the historical 100–150-word case as a range adherence regression with the
new counting version. Relabeling it as an approximate target cannot count as an
improvement. New format/check versions require a new baseline.

Required checks cover source copy, typed length scope, input resolution, dynamic
meaning delivery, change preservation, Undo, late replies, publication/version
races, known output constraints and no passage controls for math. Live repeated
evaluation remains explicit and paid-call-budgeted; CI uses isolated providers.
The Hebrew judge stays advisory and outside production generation.

## 9. Completion criteria

- The whole parent author/refine/save flow fits one workspace, without a
  blueprint step, raw keys or an extra acceptance screen after each message.
- Every advertised control has an owner, input semantics and a tested request
  mapping. Custom semantic effects are not claimed as deterministic guarantees.
- Reading, math, logic, short answers, adjustable formats and supplied bilingual
  source scenarios use one generation path, with unsupported requests clarified.
- No stale response, Undo, chat instruction or foreign ID can overwrite a newer
  edit, publish implicitly, access another family or alter old task snapshots.
- All isolated checks, production build and browser workflows pass, including
  360px RTL, 200% text, keyboard editing, errors and cancellation.
- Aggregate resource checks, concrete wire-size limits, explicit-null handling,
  accepted-source preservation and fixed-plan generation trials are covered
  before cutover. No ordinary edit, Undo, Save or preview makes an AI call.
- A later authorized repeated model evaluation measures adherence and Hebrew
  quality. No release claim treats this document or mocked tests as that
  evidence.

## 10. Research basis

The decisions above are project design choices informed by these sources; they
are not claims of benchmarked quality improvement:

- [OpenRouter structured outputs][structured] documents endpoint-specific
  capabilities; [Google's guidance][semantic] distinguishes schema conformance
  from semantic correctness. Retain server checks and wire-level schema tests.
- [Angular dynamic forms][forms] supports runtime-configured native forms;
  [Microsoft IChatClient][chat] already supplies the required model boundary.
- [Anthropic workflow guidance][workflow] supports a small
  application-controlled flow. [Hex's firsthand product example][hex]
  demonstrates chat beside editable work. This app chooses reversible draft
  updates instead of another review step.
- [EF concurrency][concurrency] supports application-managed version protection;
  [evaluation guidance][evals] supports deterministic tests, calibrated judges
  and human review as separate evidence.
- [System.Text.Json required properties][required] and [nullability
  guidance][nullable] distinguish presence from null. Newer nullable enforcement
  starts in .NET 9; this .NET 8 app needs explicit boundary checks.
- Community reports [MEAI #7249][sdk-issue] and [Angular #66711][forms-issue]
  were closed after fixes. They motivate boundary regression tests, not claims
  that those defects remain in this app. [Formly #4125][formly-issue] was an
  open reported default-value issue at research time, not reproduced here.

[structured]: https://openrouter.ai/docs/guides/features/structured-outputs
[semantic]: https://ai.google.dev/gemini-api/docs/structured-output#best-practices
[forms]: https://angular.dev/guide/forms/signals/dynamic-forms-with-json
[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[workflow]: https://www.anthropic.com/engineering/building-effective-agents
[hex]: https://learn.hex.tech/changelog/2025-06-25
[concurrency]: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
[evals]: https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents
[sdk-issue]: https://github.com/dotnet/extensions/issues/7249
[forms-issue]: https://github.com/angular/angular/issues/66711
[formly-issue]: https://github.com/ngx-formly/ngx-formly/issues/4125
[required]: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties
[nullable]: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations
