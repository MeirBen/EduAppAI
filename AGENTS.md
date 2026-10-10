# Repository guidance

- Do not commit, stage, branch, merge, or perform other Git mutations. The user
  handles Git.
- The product flow is prompt-first activity creation, saved drafts and
  explicit approval; see docs/product-specification.md. Templates are retired;
  do not reintroduce reusable definitions. All task creation uses the generic
  AI path; do not add static or subject-specific deterministic generators.
- Read README.md and docs/architecture.md before making structural changes.
- Follow docs/commenting-guide.md and keep comments accurate in the same change
  as the code.
- Keep one production backend project and feature-oriented folders.
- Use the installed .NET 8 SDK through standard `dotnet` commands; keep SDK
  installations outside the repo.
- Use DbContext directly. Do not add repository, unit-of-work or mediator
  wrappers.
- Reviewed activity snapshots and assigned content are immutable; later edits
  start a new draft and never change existing assignments or results.
- Enforce family/child ownership on the server. Parent answer keys must never
  enter child DTOs.
- Use ProblemDetails, bounded validation, cancellation tokens and UTC
  timestamps.
- Keep Angular standalone, strict and signal-based. Prefer native accessible
  controls.
- Follow docs/ui-guide.md: Hebrew UI, logical RTL spacing, isolated LTR
  numeric/email inputs, Tailwind theme tokens and immutable content snapshots.
- Do not seed educational data or add fake working features. Use isolated AI
  providers for verification; do not make live paid AI calls unless requested.
- Run scripts/verify.sh for changes; use the isolated browser test for workflow
  changes.
- Never commit databases, Data Protection keys, credentials, node_modules or
  build outputs.
