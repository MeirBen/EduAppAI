# Content-first activities and structured templates

Status: accepted design for implementation planning, 30 September 2026; **not
implemented**. This replaces the earlier lifecycle at this stable path. The
[implementation plan](../plans/2026-09-30-structured-templates.md) is the
execution guide. README, product specification and architecture continue to
describe the current application until implementation lands.

## 1. Product outcome and scope

A parent describes an activity or opens a saved template, adjusts settings,
generates an **editable activity**, repairs its material/questions/answer key,
and explicitly marks it ready to create an **immutable task snapshot**. Saving a
reusable template is independent. A valid unsaved plan can generate an activity;
no mandatory template publication, separate blueprint page or acceptance dialog
between successful generation stages is introduced.

Keep .NET 8, Angular, SQLite, OpenRouter, the existing provider configuration
and evaluation tools. All educational generation follows the generic AI path:
plain text materials and numeric, short-text or single-choice questions. No
static or subject-specific generator, arbitrary widget, subjective essay grader,
model code, tool execution, autonomous agent or production repair/retry loop.

This slice ends at parent release/preview. It does not implement child delivery.
The button says **סימון כמוכנה** (mark ready), never promises an available child
assignment. Separate statuses describe generated content, deterministic checks,
needs attention and parent review; none means “AI-certified correct.” Natural
Hebrew, source entailment, factuality and age/pedagogical suitability require
human judgment. A clean model-judge result is advisory only.

## 2. One workspace and explicit persistence

The same route contains chat for the learning plan, native settings, materials,
questions/options/answers, diagnostics and contextual replacement actions.
Narrow screens use stacked regions or accessible tabs. Parents never edit keys,
JSON, placeholders, internal paths or provider prompts. Hide inapplicable
controls; a question-only math or logic activity has no mandatory passage
fields.

1. Describe the activity. AI proposes a plan or asks one focused clarification.
   Valid proposals apply locally with actual computed changes and Undo.
2. Adjust the plan/settings and confirm any source extracted from chat. Direct
   editing costs no AI call. Select **יצירת פעילות** to save an ActivityDraft
   before starting generation.
3. Generate missing materials in one batch when necessary, then all questions in
   one batch against the accepted material. Normal successful stages proceed
   automatically; a real blocker stops the operation and keeps accepted work.
4. Inspect/edit content directly or explicitly replace one generated material or
   question with AI. Use **שמירת טיוטה** for a durable content checkpoint.
5. Use **שמירת תבנית** independently to publish the current plan for reuse. This
   never modifies an activity or starts content generation implicitly.
6. After resolving blocking checks, review the current saved revision and mark
   ready. Viewing the frozen result remains read-only; **Edit as new draft**
   explicitly copies it into a new working activity.

Local fields show unsaved/saving/saved/error states. Raw invalid keystrokes stay
local, with a navigation warning; saving a bounded but incomplete document is
allowed. Do not promise every keystroke is durable. Generation, repair, adoption
and release first flush a valid Save draft; a failed save starts no operation.
An activity reload restores the saved draft and operation, not an unpersisted
conversation or Undo stack. Editing/viewing/saving works without AI.

```text
Describe/open plan → choose inputs → saved ActivityDraft → bounded generation
→ direct/scoped edits → current-revision parent review → immutable TaskSnapshot
                         └─ independently save reusable TemplateVersion
```

For Hebrew reading, chat establishes the grade and requested story-type/length
choices; native controls change those choices without AI, and the parent edits a
generated paragraph or question in the activity. A no-passage math request uses
the same controls and one question batch, with no reading fields or static math
generator. For a supplied bilingual source, the parent confirms the exact
source, chat records the intended direction/answer languages, and generation
writes questions while the application preserves the source string.

Template chat remains a synchronous proposal call. Publication keeps its
existing expectedVersion guard and immutable versions. Content edits never
republish a template; template publication never silently changes the copied
plan/input/content of an activity. Both remain visibly separate actions.

## 3. One application-owned contract

Use a new LearningPlan contract. Retain embedded JSON schemas, typed C# and
TypeScript models and explicit domain validation. The model produces instances
of that contract, never executable schemas or UI code. Manual changes and
normalized AI proposals pass the same plan validator before publication.

Distinguish untrusted provider proposals, canonical plans and resolved requests
without duplicating their shared records. Only proposals allow new null IDs;
canonical plans have validated identities. Normalization assigns IDs and handles
documented equivalent optional representations. It never rewrites language,
clamps values, invents semantics or guesses an old identity from a label.

The following are logical field names and invariants, not a second handwritten
schema implementation. Concrete provider schemas must stay within the selected
endpoint's capabilities and be covered by request-wire tests.

### Version ownership

Keep two small constants files, each owned by the code it describes:

- **TaskEngine/EngineVersions.cs** owns `SchemaVersion` and `Revision`, both
  starting at 1 for this new contract. SchemaVersion identifies the LearningPlan
  JSON shape. Revision covers engine behavior, including prompts, resolution,
  assembly, validation and word measurement; these do not need separate
  counters.
- **Evaluation/EvaluationVersions.cs** owns report-format, automatic-check and
  Hebrew-review versions. Consolidation preserves their existing values; advance
  only the affected value when its format or semantics actually change. These
  remain separate so a judge change does not invalidate independent automatic
  comparisons. The application never depends on evaluation metadata.

All consumers reference their owner: model defaults, validators, schemas,
metadata, report readers and test builders. AiSchemas applies SchemaVersion to
the embedded plan schema once when loading it; do not repeat its numeric enum in
JSON or prose prompts. Reuse that finalized schema in the prompt and provider
format. Angular preserves the server-supplied version without its own counter.
Fixed regression artifacts retain their recorded versions; do not rewrite
history or introduce compatibility readers.

Use stable, distinct stage names under the new content-first identity and derive
their prompt labels from the shared engine Revision. The temporary one-shot
experiment is another stage name, not another version counter. Record the exact
prompts and schemas alongside these labels. Bump Revision for changed engine
behavior, not formatting or behavior-preserving refactors. Engine revisions may
differ in a comparison experiment; report that difference and retain the matched
input/check/judge rules in section 9. If shared validation or measurement
changes the meaning of evaluation checks, advance the automatic-check version
too.

Documentation describes this policy and links to the owning files after they
exist; it does not repeat current numeric values. Keep these as ordinary
constants, without a registry, configuration setting or versioning framework.
Parent template versions and draft concurrency revisions remain per-record
values, independent of these software revisions. Engine revision is provenance,
not a learning requirement: changing it alone does not mark accepted content
stale. Current validation still applies before generation or release, and queued
work keeps the configuration-change guard in section 6.

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
- fixed: text is required and copied unchanged from the accepted plan.
- per-task: the parent supplies required source text when creating each task. No
  generated length expectation may be attached to supplied source material.
- Transformation of a source is a separate generated material whose guidance
  states the transformation. It must not silently replace a verbatim source.

Material IDs are internal associations, not prompt placeholders. Generated
output supplies only the expected generated IDs, once each; application assembly
restores the accepted material order and inserts original supplied text.
Unexpected, missing or duplicate generated IDs reject the response.

The parent's accepted source string is authoritative from capture onward. A
dedicated source field needs no AI. If AI extracts a proposed source from chat,
show it inline as unverified until the parent confirms or directly edits it;
block publication and content generation while that verification is pending.
This is a source-specific check inside the workspace, not another blueprint
screen. Browser controls may normalize line endings before acceptance; preserve
the accepted string, not claimed external-file bytes.

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

Limit each compiled provider HTTP body to 512 KiB and each output schema to 64
KiB, measured as serialized UTF-8 bytes; count schemas everywhere they occur in
the actual body. Include the authoring reply envelope/assumptions in the
32,000-character response limit. These are application policies, not provider
capacity claims. Maximum-size wire fixtures must prove the intended supported
plans fit before cutover; tighten the supported limits explicitly if they do
not.

## 4. Resolution and document validation

TaskRequest contains complete settings, optional questionFormat/choiceCount,
materialInputs by material ID, optional totalWordCount, and controlValues by
control ID. Material inputs permit wordCount or sourceText only where allowed.
Reject unknown IDs, inapplicable properties, fixed overrides and range endpoint
overrides. Required per-task source text has no implicit default.

- **Omitted override/map**: Resolve its default once; missing required input
  fails.
- **Explicit null, including a map**: Reject; it does not mean omission.
- **Valid false or zero**: Preserve exactly.
- **Empty optional text**: Preserve; do not replace with a default.
- **Blank required text**: Reject without rewriting the accepted source.
- **Numeric string**: Reject; never coerce.

Preserve presence at the .NET 8 HTTP boundary with a localized request reader.
Nullable CLR declarations alone do not preserve omission versus null. Precedence
is capabilities/ownership/resource limits, typed requirements and permitted
overrides, explicit choices then omitted defaults, and finally semantic
guidance. Do not parse prose to invent executable requirements.

TaskRequestResolver.Resolve(plan, input) is pure and returns either errors or
one ResolvedTaskRequest. This is the sole effective generation contract:
settings, scoped meanings/values, exact accepted sources, material identities,
allowed formats/counts and lengths. Exclude competing defaults, adjustable
flags, chat, family identity and database or engine revision fields. Stage
preparation adds only its target and source revisions; it does not reinterpret
defaults. The effective request drives payload, schema, validation, assembly,
measurements and evidence. Copy mutable collections at boundaries;
request-specific schema changes must not mutate shared state.

### One TaskDocument, three validation boundaries

TaskDocument contains title, learner instructions, ordered materials and ordered
questions. A material has an app-owned ID/revision, optional title, body and
origin. A question has an app-owned ID, prompt, interaction/options,
answer.value and points. Generated materials and questions record the effective
input fingerprint under which they were accepted; questions also record source
material ID/revisions. Derive staleness from these values and the current input
and sources; do not persist a competing stale flag. Provenance is server-owned
at material/question or step level. Preserve current plain-text and answer
contracts: numeric answers are invariant decimal strings; single-choice answers
match exactly one distinct option; choice counts are 2–6 and points are integers
0–100. There is no option-ID refactor.

MaterialCandidateBatch contains only generated material {id, title?, body}.
QuestionCandidateBatch contains the task title, learner instructions and
complete questions, so the question stage owns those task-level fields.
ReplaceMaterial returns one generated material; ReplaceQuestion returns one
complete question. Both replacements preserve unrelated title/instructions, IDs
and content. Strict target checks plus assembled-document safety/aggregate
bounds apply to a scoped replacement; unrelated incomplete manual fields can
remain draft diagnostics. Full release checks still validate the entire
document. There is no public stage-number input or generic step scheduler.

AI candidate validation is strict for structure, expected generated-material
IDs, question count, allowed formats, mixed coverage, choice count and answer
associations. Validate an entire question batch before assigning stable app IDs
in returned order. A replacement keeps the target question ID. No question-slot
scheduler or per-question call swarm is required.

Draft-save validation enforces bounded JSON/types/IDs, owned references and safe
content, but retains missing answers, deleted questions or invalid answer-option
associations as visible diagnostics. A parent can therefore fix one part at a
time. Changing/deleting an option leaves the old answer invalid until explicitly
chosen again unless its exact value still exists uniquely; never auto-select a
replacement. Release validation requires a fully consistent document and all
requirements in section 8. A strict AI candidate cannot bypass its checks by
being treated as a lenient draft save.

Keep the total content limit at 8,000 characters including titles, instructions,
materials, question prompts, options and answers. Per-field limits remain title
100, instructions 1,000, material body 4,000, prompt 500 and answer/option 200.
Draft diagnostics are app-owned (at most 100, each message at most 500
characters); collect remaining failures into one bounded summary. Retain
supplied source strings exactly during application assembly, including accepted
line breaks and punctuation. The model returns generated material only, never an
authoritative echo of a supplied source.

### Source changes and dependencies

All materials actually sent in a question call become every returned question's
conservative dependency set. Only an app-selected narrower call can narrow it;
model claims cannot. Editing a generated material increments its revision and
marks dependent questions stale. Changing effective plan/settings marks affected
generated content stale conservatively (all generated content initially).

Supplied originals are authoritative. **Replace source** updates the draft's
copied plan.text for a fixed source or task input for a per-task source,
resolves again, increments material/draft revisions and invalidates dependents.
It never changes the published template. Ordinary material editing and AI
ReplaceMaterial target generated materials only. A transformation of supplied
text is a separate derivative generated material added through the plan.

A parent can regenerate stale content or explicitly adopt it under the current
requirements after inspecting it. Adoption updates current dependency/input
associations and records that human action, without rewriting original
generation provenance. It cannot waive strict measurements or structural/answer
failures. Final parent review is still required for release.

## 5. Persistence, API and ownership

Use three small feature-owned entities plus existing immutable template
versions:

- **ActivityDraft**: Family-owned editable JSON aggregate, copied canonical
  plan, accepted TaskRequest, document, revision, activeOperationId, UTC times
  and optional template provenance.
- **TaskSnapshot**: Immutable released plan/input/document/keys, measured
  expectations, engine revision, provenance and parent review at
  sourceDraftId/sourceDraftRevision.
- **GenerationOperation**: Bounded immutable request/effective-input evidence,
  steps, candidates, status, timestamps, safe diagnostics and known provider
  usage for one draft.

Draft plan/input/document are the authoritative saved state. Resolve effective
input and derive content diagnostics on validated save/start; do not maintain
independently mutable resolved-input/readiness caches. Operations pin the exact
resolved request, and snapshots store it with release measurements. Persist
operation failure evidence separately from ordinary derived draft diagnostics.

Create the draft before any content call. Optional templateVersionId is
provenance, never required for generation or interpretation; opening a template
pins/copies the version shown and rejects a stale expectedVersion before draft
creation. Later template publication/deletion cannot change or cascade-delete
self-contained drafts/snapshots. Store provenance IDs without a required live
foreign key. Draft deletion cascades operations/key tombstones, but never its
released snapshot. Keep snapshot sourceDraftId as provenance with a unique
index, not a cascading foreign key. An unknown/deleted draft route cannot
recreate it through an operation request. Family reset explicitly deletes all
owned learning records in one transaction while preserving
accounts/configuration.

Use application-managed monotonic long Revision as an EF concurrency token. Each
manual save, adoption, successful stage apply and cancellation uses a
conditional update and returns the new revision. Released drafts are terminal.
Editing a snapshot copies it into a new editable draft, with no prior review or
active operation. Historical TaskInstances are never made mutable.

All routes inherit parent authorization, family ownership, CSRF, no-store and
safe ProblemDetails. Missing/foreign IDs return 404 before AI. Resource
admission returns 429; invalid input returns field errors; stale revisions/key
conflicts return 409. The planned API replaces POST /templates/{id}/instances:

- `POST /api/activity-drafts`: Create from valid plan + input, or owned
  snapshotId; optional templateId/expectedVersion provenance is checked and
  copied. No AI.
- `GET /api/activity-drafts/{id}`: Saved plan/input/document, revision,
  diagnostics and active operation.
- `PUT /api/activity-drafts/{id}`: SaveDraft(expectedRevision, plan, input,
  editable content); server owns IDs/revisions/provenance and recomputes
  diagnostics.
- `POST /api/activity-drafts/{id}/adopt-content`: expectedRevision +
  material/question IDs explicitly inspected under current inputs; checks then
  records adoption.
- `POST /api/activity-drafts/{id}/operations`: StartOperation(operationKey,
  expectedRevision, kind, optional targetId/instruction). Returns 202 +
  operation URL, or existing operation.
- `GET /api/activity-drafts/{id}/operations/{operationId}`: Read
  status/checkpoints/diagnostics; never initiates work.
- `POST /api/activity-drafts/{id}/operations/{operationId}/cancel`: Cancel the
  named operation; repeated cancellation is harmless.
- `POST /api/activity-drafts/{id}/release`: Release(expectedRevision) is the
  explicit parent review action for that saved revision. Returns an immutable
  snapshot; no second identical review-revision field is needed.
- `DELETE /api/activity-drafts/{id}`: Delete owned draft and operation records;
  fence late apply.
- `GET /api/instances/{id}`: Existing parent preview route reads the new
  TaskSnapshot.

Keep existing template publication/list/delete and snapshot delete routes,
updating their semantics to the independent lifecycle. Template publication uses
expectedVersion; operation idempotency does not make template Save idempotent. A
lost publication response offers checking the library, without claiming rollback
or automatically retrying. Lists distinguish editable drafts and ready snapshots
using existing bounded list conventions.

## 6. Bounded durable generation

One BackgroundService in the existing API project executes content operations
sequentially. SQLite is the queue/source of truth; an optional in-memory wake
signal only reduces polling. No broker, leases, event log or distributed
workflow engine. This deployment runs one API process; multiple replicas require
a new claim/lease design before deployment, not accidental concurrent workers.

TaskAssembly owns the small in-memory stage-selection rules: which generated
materials need work and whether questions can run. Runtime and evaluation use
those same rules and validators. The worker owns scheduling, checkpoints and
database transitions; the evaluator owns experiments and its call budget.
Neither reimplements content acceptance or defaults. This needs ordinary methods
over the four actions below, not a workflow interface, registry or configurable
graph.

- **GenerateActivity**: One batch for absent or stale required generated
  materials if needed, then one question batch: at most 2.
- **GenerateQuestions**: One full question batch against current accepted
  material, or no material: 1.
- **ReplaceMaterial**: One generated material at an app-owned target: 1.
  Dependent questions become stale; no automatic question call.
- **ReplaceQuestion**: One complete prompt/interaction/options/answer
  replacement at an app-owned target: 1.

Existing accepted generated materials can be reused only when current under the
effective input. GenerateActivity always generates a fresh full question batch;
label reuse of current material explicitly. **New activity** creates a new draft
from the plan/inputs without generated content; its GenerateActivity therefore
requests fresh material too. Never interpret a request for fresh content as
silent reuse. Supplied sources skip material generation. Question-only tasks use
one question call. Reject question work until all required material is
accepted/current and satisfies strict requirements. A replacement returns a
complete bounded target, never arbitrary paths, JSON Patch or a whole activity.
Author/refine interprets plans; material calls write generated materials;
questions receive exact accepted material and resolved settings; replacements
receive an app-selected target. Keep typed constraints in schemas/effective
inputs, rather than duplicating defaults in semantic prose. Never request
chain-of-thought. A judge is evaluation-only. All calls use the existing
IChatClient and provider adapter/profile; production has no application retry or
automatic repair. OpenRouter fallback may route a single application call; the
application budget is not a billing guarantee.

AI failures describe the failed call, never whether application data was saved.
The worker records safe failure categories and preserves accepted checkpoints;
the parent UI derives recovery wording from that state. Replace today's blanket
"nothing was saved" AI messages rather than masking them in the UI. Handle
expected provider, validation and cancellation outcomes per operation so they do
not terminate the host; clear only that operation's active reference. Unexpected
worker/database defects retain the host's normal failure behavior and restart
recovery, rather than being swallowed or globally ignored.

### Admission, retention and idempotency

Initial **application policies**, measured with isolated fixtures before
cutover: 32 queued/running operations globally, four per family, one active
operation per draft, and at most 128 operation records per draft. The existing
global capacity of two provider calls includes synchronous template authoring.
One worker permits at most one content call at a time. Keep the existing ten AI
starts per family per minute; same-key replay consumes no additional start/call
budget. Queue admission and unique active-operation assignment occur atomically.
Explicitly copy into a new draft if the lifetime operation-record limit is
reached; this copy is explicit and makes no AI call. The limit blocks only new
operations: viewing, manual edits, release and existing-key replay remain
available. Never silently recycle a key.

Each operation has at most two steps/call attempts, a 2 MiB serialized payload
ceiling including request/schema/candidates, and the request/output limits in
section 3. Retain bulky request/schema/raw candidate evidence for seven days
after terminal status; retain accepted content and minimal operation ID/key,
original-request/effective-input fingerprints, status, stage outcome and known
usage metadata for the draft lifetime. Purge artifacts in bounded batches of 32
on startup and hourly, outside provider calls. Tell the UI when diagnostics have
expired. The 128-record limit bounds idempotency storage per draft. Never drop a
key while the draft route can still accept a replay. Deleting the draft removes
its tombstones; subsequent draft requests return 404.

A unique (familyId, operationKey) index prevents duplicate starts. Fingerprint
canonical original request fields (draft ID, expectedRevision, kind, target,
instruction) and capture the accepted effective-input hash separately. Authorize
the family/draft, then look up the key **before** testing today's revision or
active-operation state. The same original request returns the saved operation
even after progress; changed original inputs return 409. Never recompute the
original fingerprint against a now-edited draft. For a new key, check current
revision/state, admission and targets, capture effective input, then create the
operation and set activeOperationId in one transaction. Keys are client UUIDs;
IDs/fingerprints are technical values, not parent-facing copy.

### Checkpoint and race rules

Operations have queued, calling, completed, failed, conflict, cancelled or
unknown status; step data distinguishes candidate returned, accepted/applied and
queued next stage. Operation completion and content readiness are separate.

Before a provider call, atomically claim queued → calling only while the
operation is queued/uncancelled, remains the draft's active operation, and
target draft/material revisions still match. A failed claim makes no provider
call; record conflict or preserve cancellation as appropriate. Persist immutable
stage input and target revisions with the claim, then dispose the short-lived
DbContext/transaction. Cancellation after the claim propagates to transport but
cannot guarantee that remote work never began. After the call, open a new scope
and transaction: check family/draft existence, editable state, active operation,
uncancelled status, expected draft revision and source revisions. On success,
save the candidate/metadata, accepted content, new draft revision and accepted
step checkpoint together. Queue the next stage in that same commit. Then normal
execution advances without a parent dialog.

A material candidate failing structure or strict exact/range length is retained
with diagnostics; it does not overwrite accepted material and no question call
starts. Approximate target mismatch is advisory. The parent explicitly edits a
candidate into the draft, changes the requirement, or retries. A question
failure retains the already accepted material for explicit GenerateQuestions. No
hidden relaxation, trimming, count reduction or automatic repair.

If an edit wins during a provider call, retain its returned candidate as
unapplied, mark conflict, clear activeOperationId and stop downstream calls.
Never auto-merge. Only structurally parsed safe content can enter the local
editor through an explicit review/edit action and normal validated Save draft;
malformed/raw provider output remains diagnostic-only. Applying selected content
must not replay old provenance, review or operation fields.

Cancellation atomically marks the operation cancelled, clears its active ID and
advances draft revision. Late output can add known usage evidence, but cannot
apply content, change cancelled status or start another stage. Release requires
no active operation. Deletion/reset similarly fences late results. Propagate
cancellation to transport without claiming remote work or billing stopped.

On startup resume only queued work and the next queued stage of an accepted
checkpoint whose recorded engine/schema versions and nonsecret AI profile still
match the running configuration. Otherwise stop with a safe configuration-change
conflict and preserve accepted work for an explicit new operation; do not load
old engines or silently switch models mid-operation. Credential rotation alone
does not change this fingerprint. A durably calling step without an accepted
checkpoint becomes unknown, with active ID cleared atomically; preserve accepted
earlier material. Explain that the provider may have completed and another
explicit attempt may incur another charge. Never silently replay it. Preserve
any durably recorded completed-call metadata; missing usage/cost stays unknown.
A local database checkpoint is the recovery boundary, not an exactly-once
external promise.

## 7. Editing, chat and client conflict safety

POST /api/ai/template-drafts evolves in place: message (4,000 characters),
optional baseDefinition, baseRevision, requestId and unresolved context (six
turns/12,000 characters). Retain the original request while clarifying; require
consolidation instead of silently truncating it. The provider receives no
request/revision/family metadata. AuthoringReply contains either a proposal or
one clarification (1,000 characters), plus at most eight assumptions of 200
characters. Operative assumptions must also occur in the plan. Normalize IDs,
validate and compute real ordered changes including removals; do not trust the
model's summary. Identical proposals are no-ops.

Plan chat can propose a complete plan; expose all changes and avoid claiming
unrelated content is preserved by prompting. Scoped content actions have
application-enforced targets. No persistent chat, global AI store or parallel
schema editor. One route-owned state manages plan, task input and content; child
components emit edits and LearningApi owns HTTP.

Advance a client edit revision on every keystroke, including invalid edits,
applied proposal and Undo. Authoring applies only when request identity and base
client revision match. Cancel invalidates identity before transport
cancellation. A changed draft invalidates old clarification. Do not stream
partial JSON.

Keep a focused form model with initialized fields and control-appropriate empty
values, separate from the transport/domain records. A boundary mapping omits
applicable unset overrides and preserves explicit empty text, false and zero; it
does not forward form-only nulls or metadata. Reuse native Signal Forms and
existing input helpers, without a form/schema framework or a second editable
copy of the same draft.

During content work, direct edits remain available. Server revision protects
saved changes; a captured client revision also protects **unsaved** keystrokes.
Polling/operation completion must not replace a dirty local buffer even if its
server revision still matches the call's base. Show the available server result
and require explicit reload/reconciliation; a later stale Save returns 409 and
preserves local input. Passive refresh/polling never creates another paid call.

Keep at most twenty client Undo entries of editable content, coalescing typing.
Undo submits an ordinary expectedRevision save that creates a new revision and
recalculates dependencies. It never restores parent review, readiness, server
revisions, operation state or historical provenance. Source confirmation state
follows its editable source locally. Published templates and snapshots are never
Undo targets. Dirty state compares content to its saved baseline.

Briefly disable editing/Undo/authoring while a Save request is outstanding;
accept its successful result once, not through the AI stale-response rule.
Preserve input on failure. Use native controls, Hebrew labels/errors, logical
RTL spacing, dir=auto learning text, isolated LTR numeric inputs, focus/error
associations and live status. No UI framework; keep 360px, 200% text and iOS
scaling usable. Render source and generated text as text only.

## 8. Measurement, review and immutable release

TextLength counts whitespace-separated tokens containing at least one Unicode
letter or number, ignoring punctuation-only tokens. Count material bodies only;
total scope sums generated bodies, excluding supplied sources. Never strip
headings or rewrite text. “שלום עולם” and “שלום — עולם” count 2; “בעלי־חיים” and
“don't” count 1; standalone emoji counts 0. Cover niqqud, newlines, mixed
scripts and supplementary Unicode letters in the shared TextLength
implementation used by runtime and evaluation.

LengthMeasurement stores scope, expected, actual and nullable satisfied. Target
has no invented tolerance/pass flag (satisfied=null). Exact/range uses the
stated count/inclusive endpoints. A draft can retain an unmet requirement with
visibility; a generated material cannot pass the stage gate, and release cannot
succeed, while an explicitly strict requirement fails. A parent repairs content
or explicitly changes requirements and re-reviews; no review override.

Release requires supported complete structure; exact question/choice counts and
mixed-format coverage; complete answer associations; all strict deterministic
constraints; no stale content/dependencies; no active operation; and explicit
parent review of the exact current saved revision. Approximate targets and
semantic intent/grammar/age judgments stay advisory, visibly separate from
blocking diagnostics. Review records a human action, not certified truth.

In one short revision-protected transaction, validate those conditions, create
TaskSnapshot and mark the draft released. Copy the canonical plan, resolved
input, final materials/questions/keys, measurements, the shared engine revision
and creation/review provenance. Record the reviewed revision from the successful
release request. Do not store a placeholder scoring policy: scoring belongs to
the later child slice, which must pin its implemented policy on the assignment
and persist it with submitted results.

A unique sourceDraftId allows one release. After authorization, a duplicate
release with expectedRevision equal to the original sourceDraftRevision returns
the same snapshot, even if the successful HTTP response was lost. A
different/stale revision returns 409. The terminal draft retains
releasedSnapshotId and released source revision. If the snapshot was explicitly
deleted, an exact replay returns 410 Gone, never recreates it; a deleted draft
returns 404. A transaction/race test must prove one winner between
edit/release/cancel, with no mixed revisions. The snapshot remains
self-contained if template, draft or diagnostic operation evidence is later
deleted.

A later separate child slice may implement separately activated child access →
snapshot assignment → resumable attempt → immutable submitted result. Use
answer-free child DTOs, deterministic server scoring under the assignment's
pinned policy and persisted reports; no AI when assigning, opening, answering,
scoring or reading reports. Define child ownership/device/CSRF/retention and
submission concurrency then. Do not add placeholder delivery controls in this
parent-only cutover.

## 9. Comparative prototype and implementation gate

Before destructive migration, use the existing evaluator/provider with fixed
canonical plans for generated Hebrew reading, exact supplied bilingual source,
and a question-only control. Compare one-shot versus the conditional split on
matched model/settings, source strings, effective inputs and check policies.
Keep source assembly equally authoritative in both variants. Use repeated,
blinded randomized human review for Hebrew naturalness, age fit, answer-key
correctness, grounding, distractors and requested pedagogy; preserve all
failures, corrections, retries, call counts, usage coverage, actual costs and
total latency. Keep unseen cases for holdout evaluation. A mock proves
mechanics, not language quality. No live run without an explicit user-approved
paid-call/cost budget.

Pre-register sample count, repeats, usable-task rubric, acceptable extra cost
and latency, and recovery/control outcomes in the experiment artifact before
calls. The gate is falsifiable: if the extra call adds cost without useful
control, recovery or quality benefit, reconsider the split before migration. Do
not claim split improves Hebrew on the strength of architecture or outside case
studies. If no paid evidence is authorized, the live-value gate remains unmet;
complete isolated implementation work without destructive cutover.

Reuse the evaluator for interpretation/refinement, fixed-plan generation, scoped
replacement and end-to-end readiness. Distinguish those suites and retain raw
per-stage output, normalized plan, accepted/unapplied candidate, assembled
content, exact effective requests/schemas plus hashes, models/providers when
known, UTC, finish reasons, latency and partial usage/cost coverage. Record
stages skipped because supplied material needs no call. All actual attempts
consume the explicit evaluation budget; production never inherits evaluator 429
retries. Keep the historical 100–150-word range regression unchanged in meaning.
A dropped authoring requirement remains an adherence failure even if the
resulting weaker plan passes its own checks.

Apply the [version ownership](#version-ownership) rules when adapting the
evaluation format, checks and judge. Record stage identity, engine/schema
versions and evaluation versions with each artifact. Remove the temporary
one-shot capability after the comparison decision. Do not add another model
profile, judge provider or configuration source.

Compare judge findings only with matched judge model/profile/prompt/rubric,
review coverage and passing human-audited calibrations. If generator and judge
change together, suppress judge-quality deltas; retain valid deterministic and
blinded human comparisons. Never convert historical reports silently or correct
planted fixture defects to improve results.

## 10. Delivery boundaries and completion

Use one API project, feature folders and DbContext directly. Pure TaskEngine
helpers own contracts/resolution/checks; a small feature worker owns
transitions; the existing adapter owns provider protocol. No repository,
mediator, generic workflow DSL, evidence-span system, content bank, QTI
exchange, event sourcing, option identity redesign or per-subject arithmetic
generator.

The implementation plan stages minimal domain contracts and the comparative
prototype before persisted draft/release, durable worker, workspace/scoped
editing, evaluator and cutover. Remove the old schema-4 authoring/content path
only after isolated unit, fixture, migration and browser checks plus the
evidence gate pass. No deployed dual lifecycle or compatibility reader. Preserve
existing application/evaluator baseline tests during this documentation-only
change.

The user has allowed a development learning-data reset for the eventual verified
cutover. Generate a new migration rather than rewriting migration history; clear
learning rows while preserving accounts/configuration/keys. Stop watchers, take
a backup and apply it only after the prototype gate and all checks. This design
update runs no migration or destructive command. Current README, architecture
and product behavior text remains current until implementation.

Completion requires generation from an unsaved plan; exact supplied sources;
editable reloadable drafts; scoped edits that preserve unrelated content; strict
release blocking; no stale overwrite after save, unsaved typing, Undo, cancel or
restart; replay-safe starts/releases; no live AI in isolated tests; and usable
narrow RTL/keyboard/enlarged-text behavior. Run scripts/verify.sh and the
isolated browser workflow for implementation. Neither code tests nor this design
establish measured educational quality.

## 11. Research references

These sources inform the design; they do not establish improved Hebrew quality:

- [Bounded AI workflows][workflows] and [evaluation guidance][evaluations].
- [Structured output and semantic correctness][structured-output].
- [Hosted background work][hosted-services], [failure behavior][worker-failures]
  and [EF concurrency][concurrency].
- [Angular form-model design][form-model], [route data][routes] and [endpoint
  schema support][schemas].
- [OpenRouter caching and duplicate request billing][caching].

[workflows]: https://www.anthropic.com/engineering/building-effective-agents
[evaluations]: https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents
[structured-output]: https://ai.google.dev/gemini-api/docs/structured-output#best-practices
[hosted-services]: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
[concurrency]: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
[caching]: https://openrouter.ai/docs/guides/features/response-caching
[worker-failures]: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling
[form-model]: https://angular.dev/guide/forms/signals/model-design
[schemas]: https://openrouter.ai/docs/guides/features/structured-outputs
[routes]: https://angular.dev/guide/routing/define-routes#associating-data-with-routes
