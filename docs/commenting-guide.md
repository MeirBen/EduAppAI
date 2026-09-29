# Comments

Document contracts and non-obvious decisions, not what the syntax already says.
Use English for developer comments and Hebrew for application copy.

- Use C# XML docs and TypeScript JSDoc for meaningful public contracts. Explain
  defaults/nulls, ownership, validation, immutable snapshots, cancellation,
  retries and concurrency where callers need them.
- Explain the reason for a workaround or ordering constraint beside the code.
- Keep product rules in the [specification](product-specification.md), system
  boundaries in [architecture](architecture.md), setup in the
  [README](../README.md) and presentation in the [UI guide](ui-guide.md). Link
  instead of repeating.
- Remove stale history, abandoned approaches and obvious restatements. Avoid
  speculative comments and hand-written documentation in generated files.
- Update comments with the code. Preserve important invariants and explain
  future behavior only in the specification's next steps.

For C#, use `<summary>` and add `<param>`, `<returns>`, `<remarks>` or
`<exception>` only when useful. Use compiler-checked `cref`/`paramref`
references and valid XML. Distinguish required JSON members, nullable types and
runtime validation; arrays inside records remain mutable. Keep the XML build
checks; only CS1591 is suppressed. If using `<param>` tags, cover every parameter;
use `<paramref>` in a summary or remark for a focused note instead.

For TypeScript, place JSDoc before decorators. Keep types in TypeScript;
document promise rejection and side effects in prose. Do not imply route guards
authorize server requests or cancelled writes are guaranteed to roll back.

Run `scripts/verify.sh`. Review comment accuracy separately from formatting. A
comments-only change needs no new behavior tests or browser run.
