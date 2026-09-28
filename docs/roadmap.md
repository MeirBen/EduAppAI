# Small next steps

Build one complete behavior at a time. Keep the [product specification](product-specification.md)
as the destination; the current [foundation scope](superpowers/specs/2026-09-28-foundation-design.md)
is intentionally smaller.

Each increment includes accurate contract documentation and updates to affected specs,
following the [commenting guide](commenting-guide.md).

1. **Child access.** Add child profiles, expiring device activation codes, revocable
   persistent child cookies and ownership tests. Never expose a parent session on a
   child device. A family needs an actual child before drafts can be assigned.
2. **One complete learning loop.** Add assignment, the numeric child player, a session,
   answer storage and authoritative C# scoring. Make start/answer/complete transitions
   safe under retries and concurrent requests. Add a browser test from parent to result.
3. **Manual authoring expansion.** Add the template version editor, static content,
   text/single-choice rendering and scoring rules. Validate all question IDs, answer
   choices, lengths and points before storing content.
4. **AI template drafts.** Add `Microsoft.Extensions.AI` and `IChatClient`, an owned
   structured schema, validation and parent review. Keep provider keys outside source.
5. **AI instance generation.** Add one generic generator, preview before assignment,
   provider failure handling, metadata and separate paid evaluations.
6. **Reports.** Start with completed counts and per-question results from stored sessions.
7. **Operations.** Add account recovery, invitation of a second parent to the same family,
   Docker packaging, HTTPS hosting, backup/restore verification and PWA update notices.

Add pagination when the initial 100-item lists become limiting. Keep offline
synchronization and native app packaging out until online family use is dependable.
