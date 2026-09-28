# Richer template authoring

## Outcome

Extend the existing parent workflow beyond multiplication. Parents create reusable
arithmetic templates or write passages and mixed-question quizzes, publish revisions,
and create exact saved drafts. This implements the richer-authoring direction agreed
with the user; child access, assignment and scoring remain the next milestone.

## Scope and user flow

- The Hebrew template editor offers generated arithmetic or parent-written content.
- Arithmetic supports addition, subtraction, multiplication and exact division.
  Each template fixes its operation; difficulty and question count remain parameters.
- Authored templates contain optional instructions, up to four plain-text passages,
  and 1–20 numeric, short-text or single-choice questions. Parents supply answer keys
  and 0–100 integer points per question. No educational content is seeded.
- An optional task title can differ from the reusable template name. Leaving it
  empty when authoring uses the template name; editing preserves the published title.
- Parents add/remove passages and questions and move questions up/down with native
  buttons. Stable question IDs survive edits and reorder operations.
- Editing loads the current definition and publishes through the existing
  `expectedVersion` API. A conflict keeps the local form and offers an explicit reload.
  A template's generation mode is fixed in the editor while editing; arithmetic
  operations and authored content remain editable.
- A common parent preview shows passages, prompts, choices, points and answer
  disclosures. Only generated arithmetic receives LTR equation formatting.
- Static creation copies the saved content without irrelevant difficulty controls.
  Existing drafts continue to read their own snapshot and original version.

## Backend and compatibility

Keep one .NET 8 API project and direct EF Core access. Retain the current generation
envelope and deterministic JSON fields; add optional `content` for `mode: static`.
Mode-specific validation rejects contradictory settings. This avoids introducing a
JSON metadata discriminator whose property-order requirements could reject existing
clients. Omit absent mode-specific fields on serialization.

`math-v1` retains the exact existing multiplication algorithm and ranges. Addition
and subtraction use positive operands up to 10/50/100 at easy/medium/hard; subtraction
orders them to keep answers nonnegative. Division uses factors up to 5/10/12 and
forms the dividend from their product: no zero divisors or fractional answers.

The small task-generation dispatcher chooses math or copies static content. Generated
content is validated before persistence. Authored templates accept zero parameters;
unused static parameters are rejected. Existing math metadata, including optional
custom fields and narrower count bounds, survives visual edits.

Content validation checks mandatory JSON members and explicit nulls, unique IDs
matching `[a-zA-Z0-9_-]{1,64}`, interaction types, answer validity, choice membership,
and integer point bounds. Limits: title 100, instructions 1000, passage 4000, prompt
500, answer/choice 200 characters; 2–6 distinct nonblank, trimmed, single-line choices
(matching the line-based editor); at most 8000 text
characters in total. Numeric keys use signed invariant decimal notation without
exponents, grouping or nonfinite values. Existing 64 KiB HTTP limits remain.

No database migration, new package, paid AI call, repository wrapper or generic form
framework is needed. Family ownership, CSRF, ProblemDetails and version concurrency
remain enforced at their current boundaries. Answer keys remain parent-only.

## Frontend

Use Angular 22 standalone components, signals and Signal Forms. Extend the current
editor workflow with a reusable form and a focused question-fields component; keep
serialization and form-model conversion in a small feature-local helper. Preserve
the existing Tailwind theme and Hebrew interface. All authored text is plain text,
uses automatic direction, and is never interpreted as HTML. Errors identify fields;
controls support keyboard use, mobile layouts and 200% text scaling.

## Verification

- Arithmetic tests cover every operation/difficulty, seeded repeatability, valid
  answers and unchanged legacy multiplication output.
- SQLite HTTP tests exercise mixed static content, malformed/missing/null fields,
  contradictory generation modes, total-size bounds, ownership, concurrent edits,
  static zero-parameter creation and frozen drafts after publication.
- Angular tests cover draft conversion, preservation of existing metadata, mixed
  questions and validation. Browser tests create arithmetic and authored templates,
  preview/reload drafts, revise templates and verify old drafts remain unchanged.
- Run `scripts/verify.sh`, publish locally and run the isolated Playwright suite;
  inspect screenshots at 360px with enlarged text.

## Framework references

- [Angular Signal Forms](https://angular.dev/guide/forms/signals/overview)
- [Signal Forms validation](https://angular.dev/guide/forms/signals/validation)
- [System.Text.Json required properties](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties)
- [EF Core optimistic concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
