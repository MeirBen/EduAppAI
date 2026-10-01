# Content-first cutover decision

Decision: **proceed**, explicitly approved by the project owner on 1 October
2026 for editing, source preservation and recovery. This amends the comparative
value gate in the [accepted design](2026-09-30-structured-templates-design.md#9-comparative-prototype-and-implementation-gate).
It does not establish improved Hebrew or educational quality.

The preregistered Task 2 trial used three cases, three repetitions and both
one-shot and split variants. It spent $0.062444323 across 19 of the authorized
21 calls, below the $1 cap, with no retries or repairs. One-shot passed structural
checks in 9/9 trials; split passed in 7/9. For generated reading, split passed
1/3 versus 3/3, with approximately 3× median cost and 3.4× median provider latency.
The two material failures violated the strict word range and prevented question
calls. Supplied bilingual sources were preserved exactly. Isolated fault tests
demonstrate retention of accepted material when questions fail.

These results did not meet the original comparative threshold. Human review
remains unscored; the sample is exploratory and cannot establish general quality.
The owner accepts the measured tradeoff for independently editable content,
scoped replacement and checkpoint recovery, allowing one content-first lifecycle
and removal of the temporary comparison implementation. Strict validation,
explicit parent review and the lack of a quality-improvement claim remain intact.
Ordinary approximate length targets remain advisory; exact/range requirements
remain blocking. No automatic repair, retry or relaxation is introduced.

The original preregistration, authoritative run, blind review and decision remain
unchanged under the local artifact directory
`artifacts/evaluations/task2-structured-2026-10-01/`.
Run ID: `20260930T213012Z-25ad70d9dd6f49128325ef36512f0ad9`.
Authoritative run SHA-256:
`ca3be34a99691af75ef9ca701f3affd17aec4f8e8faef63cfeabb5dfe22007e9`.
Historical evidence is retained as an artifact, without a compatibility reader.
The owner's additional $2 allowance was unused at Task 8 completion: cutover
verification used isolated providers. Subsequent authorized trials are documented
in the [post-cutover tuning report](2026-10-01-post-cutover-ai-tuning.md).
Manual checks still need to assess Hebrew, answer correctness, grounding and
age fit.
