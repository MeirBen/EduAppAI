# Prompt-first AI authoring implementation plan

> Use superpowers:executing-plans. No Git mutations; the user owns Git.

**Goal:** Parent prompt → editable reusable blueprint → explicit save → dynamic parameters → AI-generated frozen parent preview.

**Spec:** [AI-only product specification](../../product-specification.md): template authoring, two AI operations, immutable content and acceptance criteria.

**Architecture:** Keep the existing immutable models and one backend project. Use schema version 2 with instructions and an optional question-count parameter binding. One generic AI service uses Microsoft.Extensions.AI.IChatClient for two schema-constrained operations. OpenRouter configuration stays outside the domain; only free models are allowed. Remove deterministic/static implementation and incompatible learning data; retain parent accounts.

**Constraints:** No paid calls, seed data, executable AI output, automatic publication, child data sent to the provider, or Git mutations. Hebrew/RTL UI, standalone Angular signals, server ownership and validation remain mandatory. No provider key is needed for tests.

## Tasks

- [x] Backend: Add tests for AI blueprints, unsaved draft authoring, schema requests, failure rejection, ownership before generation, count matching and frozen snapshots. Run red. Add the provider boundary, versioned prompts, owned schemas, generic validation and endpoints; run green.
- [x] Frontend: Make the new-template route prompt-first with explicit review/edit/regenerate/save. Edit AI instructions and all four parameter types using a focused component; remove manual/static/math authoring. Reuse the generic parameter form and preview. Test preservation of edits and explicit save.
- [x] Workflow: Exercise authoring, edited fields, two different instances from the same blueprint, reload without regeneration and provider failure using an isolated local HTTP provider in browser tests. No live model calls in CI.
- [x] Finish: Update architecture, setup and roadmap; run scripts/verify.sh, publish and isolated browser tests; perform a fresh-context review and fix material findings.

## Review focus

- Missing API configuration must be visible; saved content remains usable.
- Malformed/refused/truncated AI data must not be saved or silently repaired.
- Dynamic parameter defaults/types and count binding must survive editing and version publication.
- Provider requests contain only authoring instructions and selected parameters, never identity or auth data.
- Slow or failed requests must release concurrency capacity, preserve user input and allow explicit retry.

## Decisions and progress

- User chose OpenRouter with a free model. Default to openrouter/free; reject paid model IDs.
- User explicitly requested AI-only creation and authorized removing existing data and schemas. Remove all legacy generation modes, authoring controls and subject-specific branches.
- Draft authoring is transient and returns diagnostic metadata; validated generation logs contain metadata only. Persist AI instance metadata beside frozen content.
- Inline implementation follows the user's instruction to continue; no additional plan-approval or Git steps.

- Final verification: `scripts/verify.sh` passes with 52 backend tests, 19 Angular tests, clean formatting and successful builds.
- `scripts/publish.sh` succeeds; all three isolated Playwright workflows pass against the published app and local provider.
- Independent review found credential leakage into tests and stale navigation after long AI calls. Both were fixed and verified with environment-isolation and request-cancellation regressions.
- No live AI calls were made. `scripts/configure-ai.sh` configures the user's OpenRouter key outside the repository.
- No Git mutations were performed. The AI-only migration removes incompatible educational data and retains accounts; its behavior is covered by an isolated migration test.
