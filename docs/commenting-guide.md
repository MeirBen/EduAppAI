# Comments and API documentation

Keep the code understandable to someone learning this application and changing it
later. Names and small functions explain the mechanics. Comments explain contracts,
constraints and decisions that a reader cannot safely infer from those mechanics.
Update comments in the same change as the behavior they describe.

## Where information belongs

| Information | Location |
| --- | --- |
| What a caller must supply, receives, or must not assume | XML docs / JSDoc beside the declaration |
| Why a particular check, ordering or workaround is necessary | A short inline comment beside that code |
| How several components interact and why a boundary exists | [Architecture guide](architecture.md) |
| Setup, commands and verification | [README](../README.md) |
| Product requirements and implemented/deferred scope | Product specification, foundation design and roadmap |

Document public application contracts, reusable services and validators, generators,
and components with meaningful input/output behavior. Add property or method details
where the name and type leave a decision unresolved. Obvious getters, assignments,
constructors, DI fields and template event handlers need no narration. Generated
migrations and tool output are not places for hand-written commentary.

Use plain English and complete, concise sentences. Avoid authorship banners, dates,
change histories, commented-out code, speculative promises and summaries that merely
repeat a symbol name. Git records history; the roadmap records future work. A TODO
needs a concrete condition and a tracked issue or roadmap reference.

## C# XML documentation

Use `///` with `<summary>` for the declaration's purpose. Add tags only when useful:

- `<param>` and `<returns>` describe semantics: accepted nulls, defaults, bounds,
  ownership, partial results or side effects. They do not restate parameter types.
- `<remarks>` records preconditions, lifetime, transaction/concurrency obligations
  and compatibility limits that callers must understand.
- `<exception>` describes deliberate, actionable failures. Do not list every
  framework exception or imply that validation results are thrown exceptions.
- `<see cref="..."/>` and `<paramref name="..."/>` link symbols and parameters with
  compiler-checked names. Use `<c>` for literal keys and values; escape XML characters.
- `<inheritdoc />` is appropriate when an override honors the base contract. Document
  any application-specific behavior separately; do not hide it behind inheritance.

For positional records, put meaningful `<param>` descriptions on the record. Distinguish
JSON presence (`[JsonRequired]`), nullable C# annotations and runtime validation: they
provide different guarantees. A `record` containing arrays is not deeply immutable;
callers must honor the published-snapshot convention.

For example, the parameter validator documents how callers may use its result:

```csharp
/// <summary>Resolves defaults and validates submitted instance parameters.</summary>
/// <param name="definitions">Definitions accepted by the template validator.</param>
/// <param name="supplied">Submitted values; null is invalid, while an empty dictionary requests defaults.</param>
/// <returns>Accepted values and field errors. Use values only when the error dictionary is empty.</returns>
public static ParameterValidationResult Validate(
    IReadOnlyList<ParameterDefinition> definitions,
    IReadOnlyDictionary<string, JsonElement>? supplied)
```

See [ParameterValidator](../backend/FamilyLearning.Api/TaskEngine/Validation/ParameterValidator.cs)
and [MathTaskGenerator](../backend/FamilyLearning.Api/TaskEngine/Generators/MathTaskGenerator.cs)
for complete examples. The generator documents validated-input requirements and why
its seed cannot replace the saved content across implementation changes.

The API project enables `GenerateDocumentationFile`. A build writes
`backend/FamilyLearning.Api/bin/Debug/net8.0/FamilyLearning.Api.xml` alongside the assembly;
Release builds use the corresponding Release directory. Existing warnings-as-errors
also apply to malformed XML, invalid parameter references and unresolved `cref` links.
Only CS1591 (missing public-member comments) is suppressed in the API project, allowing
generated migrations and self-explanatory members to remain uncluttered. Reviewers
still check meaningful documentation coverage and accuracy. Do not suppress XML
syntax/reference diagnostics to make a build pass or commit generated XML output.

## TypeScript and Angular documentation

Use `/** ... */` JSDoc with a concise summary on exported contracts, services, shared
functions and components. Place component/service docs before their decorators so
editor hovers can show them. Keep types in TypeScript; do not repeat `{string}` or
`{Promise<...>}` type annotations in comments.

Use `@param name - meaning` and `@returns` when the semantics need explanation. For
asynchronous calls, state whether failures reject and whether retrying causes another
write. Avoid `@throws` for an HTTP status returned by a rejected promise; explain the
rejection behavior in prose instead.

```typescript
/**
 * Creates a persisted draft from the template's current revision.
 * @param templateId - Stable template ID; the server selects the published revision.
 * @param parameters - An empty object accepts template defaults.
 * @returns The saved preview. Repeating a successful call creates another draft.
 */
```

Document component input/output semantics, reset behavior and side effects when they
matter. The [parameter form](../frontend/src/app/dynamic-form/parameter-form/parameter-form.ts)
explains why numbers stay as text while editing and that its output contains values,
not generated task content. [Auth](../frontend/src/app/core/auth/auth.ts) documents
session and CSRF sequencing. Route guards improve navigation; comments must not imply
they replace server authorization.

Prettier and the Angular build check formatting and code, not the truth of JSDoc.
There is no separate documentation generator or coverage dependency in this skeleton.

## Review and maintenance

For each change, check that:

1. Comments match actual inputs, outputs, failure paths and side effects. They describe
   implemented guarantees; deferred features are explicitly labeled.
2. Non-obvious ownership, answer-key exposure, null/default handling, snapshot lifetime,
   concurrency and retry behavior are explained at the relevant boundary.
3. Shared rules have one detailed home. Specs and architecture link to this guide
   rather than copying its examples; local code comments remain understandable on their own.
4. A changed contract updates both C# and TypeScript descriptions and any affected
   architecture/spec sections. Tests remain the executable proof of behavior.
5. `./scripts/verify.sh` passes. For a comments-only change, no new behavioral tests
   or browser run are needed; use existing tests and review the diff for accidental code edits.

Syntax references: [Microsoft's XML documentation tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags)
and [TypeScript's JSDoc reference](https://www.typescriptlang.org/docs/handbook/jsdoc-supported-types.html).
