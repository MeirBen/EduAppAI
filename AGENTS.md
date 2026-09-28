# Repository guidance

- Do not commit, stage, branch, merge, or perform other Git mutations. The user handles Git.
- Prompt-first AI template authoring and generic AI task generation are the primary product flow.
  Do not add static or subject-specific deterministic generators; all task creation uses the generic AI path.

- Read README.md and docs/architecture.md before making structural changes.
- Follow docs/commenting-guide.md: document meaningful C# contracts with XML docs and
  TypeScript contracts with JSDoc; explain non-obvious decisions inline.
- Keep comments accurate in the same change as code. Avoid narrating obvious code,
  duplicating types, or documenting generated files; preserve XML syntax/reference checks.
- Keep one production backend project and feature-oriented folders.
- Use the installed .NET 8 SDK through standard `dotnet` commands; keep SDK installations outside the repo.
- Use DbContext directly. Do not add repository, unit-of-work or mediator wrappers.
- Template versions and task content are immutable snapshots; edits publish a version.
- Enforce family/child ownership on the server. Parent answer keys must never enter child DTOs.
- Use ProblemDetails, bounded validation, cancellation tokens and UTC timestamps.
- Keep Angular standalone, strict and signal-based. Prefer native accessible controls.
- Follow docs/ui-guide.md: Hebrew UI, logical RTL spacing, isolated LTR math/email,
  Tailwind theme tokens and unchanged API values/immutable snapshots.
- Do not seed educational data, add fake working features or introduce paid AI calls.
- Run scripts/verify.sh for changes; use the isolated browser test for workflow changes.
- Never commit databases, Data Protection keys, credentials, node_modules or build outputs.
