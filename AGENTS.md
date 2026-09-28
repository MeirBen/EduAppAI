# Repository guidance

- Read README.md and docs/architecture.md before making structural changes.
- Keep one production backend project and feature-oriented folders.
- Use DbContext directly. Do not add repository, unit-of-work or mediator wrappers.
- Template versions and task content are immutable snapshots; edits publish a version.
- Enforce family/child ownership on the server. Parent answer keys must never enter child DTOs.
- Use ProblemDetails, bounded validation, cancellation tokens and UTC timestamps.
- Keep Angular standalone, strict and signal-based. Prefer native accessible controls.
- Do not seed educational data, add fake working features or introduce paid AI calls.
- Run scripts/verify.sh for changes; use the isolated browser test for workflow changes.
- Never commit databases, Data Protection keys, credentials, node_modules or build outputs.
