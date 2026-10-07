# Hebrew UI

The app is **לומדים ביחד**, with Hebrew UI and RTL layout. Keep UI copy beside
its feature; identifiers and developer docs remain English. Learning content and
parent labels keep their original language and values.

## Layout and controls

- Use native HTML, document scrolling and accessible controls. The header stays
  visible on screens at least 64rem wide and 40rem tall; let it scroll away on
  smaller screens. On phones it keeps one row: nav links show only their icons,
  and the signed-in brand only its mark. Reserve scroll padding for focus
  targets and include the skip link.
- Route changes never move the layout: the root keeps a stable scrollbar
  gutter, the footer rests at the window's bottom on short pages, new pages
  open at the top and Back restores the previous position.
- Use the shared light/dark theme tokens, visible focus/error states and locally
  bundled Heebo in three weights: semibold headings with snug leading, medium
  field captions and actions, and regular body, option and help text with the
  theme's 1.7 leading. Underlines share one offset; Hebrew text keeps its
  natural letter spacing.
- Use `panel` for raised surfaces and `button` for primary actions; add
  `button-secondary`, or the quiet `button-danger` for destructive actions
  guarded by a confirmation. Actions size to their content. Keep one prominent
  action per card, with quieter edit links and `text-link-danger` deletions.
  Native disclosures reveal answers; their summary shares the select's chevron.
- A page that edits or acts on one item ends with an `action-bar`: its save
  state and one row of current actions, with their errors and recovery. Forms,
  such as assignment, stay in their own panel so the bar never hides content.
  It pins to the window's bottom while the window is at least 40rem tall at the
  current text size, and scroll padding keeps focus clear of it.
- Pages opened from the library lead back with a link above their heading. A
  `list-row` with a `row-link` opens from anywhere on the row, which shows the
  link's hover and focus; the row's other actions stay separate controls. A
  link inside a sentence stays a plain underlined `a`; standalone actions use
  `text-link`, whose control height would stretch a line of text.
- Icons come from the one set in `icons.css`, scale with their text and lead a
  label only where they add meaning. Repeated actions with a familiar shape
  (remove, move, undo, edit in a row, send and stop) are icon-only
  `icon-button`s whose `tooltip` is their accessible name; where the same action
  repeats per item, an `aria-label` adds the item and hides the tooltip from
  assistive technology. Tooltips show on hover or keyboard focus and Escape
  hides them; anchor positioning keeps them within the window. Actions with
  lasting effects keep their visible label.
- Keep hover/press feedback brief, exclude disabled controls and respect reduced
  motion. Use theme shadows and colors rather than page-specific copies.
- Actions show a 2px offset focus ring. Fields tint their own edge instead, in
  the danger color when invalid. Script focus targets (`tabindex="-1"`) show no
  ring. Selects, checkboxes and fields stay native; supporting browsers open a
  styled select listbox.
- Use rem sizing, generous line height and wrapping for long user content.
  Text areas grow with their text where the browser supports it, so the page
  scrolls once; numeric fields size to a few digits.
  Use the viewport-capped `gutter` spacing for narrow containers so padding does
  not crowd enlarged text. On phones, buttons take the 44px control height, the
  page heading steps down one size and list rows drop their decorative tile;
  body and field text stay 16px. Preserve browser zoom, iOS text scaling and the
  production bundle budgets.
- Label controls; associate errors with fields. Provide keyboard access, visible
  focus, loading status, error alerts and distinguishable repeated
  links/disclosures. Focus moves only to keep it from being lost: a button its
  own action makes unavailable stays focusable through `disabledInteractive`,
  and an action that removes, moves or replaces the focused control hands focus
  through `focusHolder` to the successor its owner names (the same control, a
  neighbour's disclosure or the list's add button), else to the nearest
  surviving region heading; content that replaces the request, such as the
  first plan, takes it at its heading. Results are announced, never focused or
  scrolled to; only full generation brings its progress into view once, without
  motion when reduced motion is preferred.
- Each problem shows on the field or group that can fix it: its edge and
  message appear once the parent leaves it, and on every field once they try
  to save or send. A failed attempt sets one announced line beside its action,
  where problems with the whole form also show; a closed disclosure names
  invalid content in its summary. A problem the field did not just cause shows
  at once: an answer that another field's edit left unmatched, or one the
  saved check found. Field messages state their rule ("יש להזין…"); the
  attempt line says what to do ("תקנו את השדות המסומנים."). Length rules rely
  on the native `maxlength` and add no message.

## Spacing

Containers own `gap`; shared primitives have no outer margin. `main` owns the
page padding, so page roots add none. Lists span the header width;
editable/frozen activity documents use `max-w-3xl`.

| Token             | Use                                                     |
| ----------------- | ------------------------------------------------------- |
| `gap-1`           | Heading/description, status/content, stacked text links |
| `gap-2`           | Control and revealed content                            |
| `gap-3`           | Buttons in an action row                                |
| `gap-4`           | Fields and blocks, including field/action rows          |
| `gap-6`           | Page sections and headed groups                         |
| `gap-x-6 gap-y-2` | A wrapping row of actions and links                     |
| `gap-x-4`         | An inline row of links or status text                   |
| `mb-2`            | Label above its control, set once in `base.css`         |
| `mt-2`            | Hint/help/error below its control                       |

Keep each label/control in one field block, not separate gapped children.
Disclosures own summary/content spacing; empty live regions occupy no slot.
Use `role="group"` with an inset heading for grouped card fields, not a `.well`
fieldset legend; later headed groups use `card-section`. Related fields share
rows: two-column settings, sibling numbers and list-item remove controls.
Content beside a tile/actions uses `min-w-1/2 flex-1`, wrapping below half-width.
Render optional content only when present.

## Styles and theming

`frontend/src/styles.css` imports each layer from `frontend/src/styles/`:

- `theme.css` owns the colors, elevations and corner radii. Tailwind's default
  palettes and scales are cleared, so templates can only use theme values.
- `utilities.css` owns project variants: `pinned-header`, `pinned-actions`, and
  `dark` for an explicit dark choice or a dark device without an explicit light
  choice.
- `base.css` styles elements, including native form controls and focus.
- `components.css` holds every shared visual treatment, grouped as actions
  (buttons, links, `icon-button` with its `tooltip`, `chip`, `segmented`
  radios), surfaces
  (`panel`, `well`, `card-section`, `action-bar`, `list-row` with its
  `row-link`, `empty-state`), conversation (`bubble`, `composer`), marks
  (`badge`, `icon-tile`, `status-icon`, `ai-mark`), lists (`steps`,
  `progress-steps`) and feedback (`note`, `error`, `field-error`, `callout`).
  Use `note`, with one icon, for a short fact about how the app behaves, such as
  kept text, costs or limits; help on what to enter stays plain text under its
  field. Use `error` for a failure that replaces a page's content and
  `field-error` beside the field, card or action that failed.
- `icons.css` holds the icon set: masks painted with the current text color,
  drawn for right-to-left reading where they point.

Primitives read only tokens; templates add layout utilities and token colors
such as `text-muted`. Color tints have one role each: `/15` for brand
hairlines and focus halos, `/25` for the error edge and `/30` for hover edges.
Repeated treatments become primitives, with state from `aria-current`,
`data-done` or `data-problem` rather than alternative class lists. Component
stylesheets use theme variables only. Give `steps` and `progress-steps`
`role="list"` to preserve WebKit list semantics.

A theme redefines only `--color-*` tokens and, optionally, the corner roles
(`--radius-small`, `control`, `button`, `inset`, `card`), so templates need no
`dark:` utilities. Keep the contrast contract in `theme.css` for every theme
and check each token on every surface, tint and translucent layer it meets.
Forced colors drop backgrounds and rings, so a selected or current state that
shows only through them adds a `forced-colors:` border. The header's theme
picker follows the device by default. `Theme` stores an explicit choice in
localStorage, since no server render needs a cookie. The inline script in
`index.html` applies it before first paint, and CSS follows device changes
live. The brand `theme-color` suits both themes.

## Direction and copy

Set `lang="he"`, `dir="rtl"` and Angular's `he-IL` locale; keep the manifest
aligned. Use logical spacing (`ms`, `me`, `ps`, `pe`, `inset-s`, `inset-e`) and
normal DOM order. Use `dir="auto"` for learning-text blocks such as titles,
instructions and materials. Wrap inline user values and question prompts,
options and answers in `<bdi>`, so their own direction never moves them out of
the page's alignment or reorders a calculation. `<option>` text cannot hold
markup, so wrap its user values in first-strong isolates (`&#x2068;…&#x2069;`).
Isolate email and numeric inputs as LTR. Bind text fields' `dir` with
`[formField]`: `FieldDirection` keeps an empty `auto` field in the page
direction, so the caret and placeholder start on the page's side. Back arrows
point right, and decorative gradients start at the reading edge. Dates display
in Hebrew with a month name and local time through the app-wide `DatePipe`
default, while stored timestamps remain UTC.

Read every size limit, count cap and the numbers in their messages from the
server's `Limits`; child answer controls use `ChildSessionIdentity.answerLength`
instead, without calling parent APIs. Never hard-code content limits in forms.

Write concise Hebrew for labels, validation, loading and errors, and never expose
raw framework or provider errors. Address parents and children in the plural
imperative ("בדקו", "נסו שוב"), call the parent home "המרחב שלנו" and avoid
internal terms such as model, item or local copy. A count of one reads in words
("שאלה אחת"). Render generated text through interpolation,
never HTML, and never rewrite saved content for presentation. Generated text is
bare by contract, so presentation supplies its structure: keep line breaks with
`whitespace-pre-wrap` (blank lines between paragraphs, single breaks for poem
lines and dialogue turns), number questions in an ordered list and show choices
as separate items or native options. `bdi`/`dir="auto"` display a whole-item
calculation or comparison in order, but bidi mirroring reverses `<` and `>`
beside Hebrew words, so the content contract keeps signs out of Hebrew text.
[`AiPrompts`](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs) promises
this to the model, so change both together. Control labels and options come from
the reviewed template; the shared settings use application-owned labels, and
only difficulty has fixed options.

## Parent learning management

Family navigation sits below the main header. Profile/assignment lists use shared
rows, native controls and `Pager`, which stays hidden while a list fits on one
page. Empty states name the next step, or say that a filter or later page has
nothing more. Separate profile edits from device access; confirmations explain
disable/revoke effects. Activation codes stay selectable with expiry, never in
URLs or persistent browser storage.

Grade (free text) and age (LTR integer, server bounds) are optional; each can
be cleared, with no age-confirmation checkbox. Keep secondary dates under native
“פרטי זמנים” disclosures at the end of their card. Follow the [cleanup contract](product-specification.md#profile-and-device-cleanup):
unused profiles offer deletion, profiles with history disabling, active devices
revocation and inactive devices removal.

Frozen previews own child selection/assignment and link to an existing pair on
replay; a withdrawn pair explains that assigning again needs a new ready copy.
Review shows frozen content, exact submitted text and parent-only answer
disclosures. Pending grades start empty; automatic/completed awards stay
read-only. Distinguish pending subtotal from final score; percentages require
positive possible points. Recovery stays beside finalization and never silently
replaces local grades. Parent and child learning pages share primitives and
logical utilities without separate stylesheets.

Group creation, opening, last-save, submission and review dates in “פרטי זמנים”.
Label elapsed time “זמן מהפתיחה עד ההגשה (כולל הפסקות)”, using minutes/hours or
“פחות מדקה”. Unsubmitted work shows “עוד לא הוגשה”; missing/reversed timestamps
show unavailable duration. See [timing semantics](product-specification.md#elapsed-activity-time).

## Child learning

Use `PageShell`, shared theme/controls and child-only home/disconnect navigation;
no parent links or same-browser mode switch. Parent activation instructions give
a copyable address for a separate browser, without the code. Explain persistent
access and fresh activation after disconnect/revocation/expiry. An uncertain
activation offers a session check; availability failures offer retry.

Separate available/submitted inbox views with a `segmented` switch and bounded
paging; rows carry a status `badge`, which keeps the state on phones where the
tile drops. Keep materials above numbered questions in the reading column.
Numeric answers use LTR text inputs with a decimal keyboard, retaining invalid
edits; text preserves whitespace and native options isolate their labels. The
action bar owns save, submit, how many questions have answers, saved-work
recovery and dirty/saved status; the saved-work check shows only while
recovering or after submission, where it fetches the grade. Invalid submit
focuses the first problem; missing answers need confirmation. Checkpoint reads
never overwrite local text implicitly. A submission receipt locks editing and
leads with its outcome icon; pending review has no final score, and zero
possible points has no percentage.

## Workspace actions

Derive presentation from the workspace's single buffer; do not store or route
phases. Before a plan, emphasize the request with manual entry in a disclosure.
Shared settings (topic, audience, difficulty, count) are plan defaults in both
templates and activities, with source text beside them. Show only applicable
activity length, format, option count and requested choices. Templates define
plans and link to activity creation after publication.

Activities show four derived steps: describe, settings, review, ready. Only the
current step keeps its label on narrow screens. Put plan definitions/guidance
under **אפשרויות מתקדמות**, initially open only for template editing. End plan
authoring with **מה אפשר לשנות בכל פעילות**: applicable length, option-count and
one-format toggles, plus scoped choice cards and one add button. Template-based
activities only set permitted values. Hide ineffective options: combined length
needs several generated texts; format selection needs several formats; required
is moot with a default. Name each text in its fields and choices.

After a plan exists, its change conversation follows settings and can edit all
settings. Once content exists, collapse settings/chat to a derived one-line
summary visible only while collapsed; content becomes the main surface.

Parent turns sit at the end edge; AI replies show `ai-mark`, computed changes
and assumptions, with a typing bubble while pending. Enter sends; Shift+Enter
adds a line. Send becomes Stop while retaining focus; failure/cancellation
restores the request text. `SuggestionChips` only fill fields, keeping focus on
the chip; they never send. Starters cover what generation does best: a reading
text, an early reader, a drill, word problems on a shared story and questions
on the parent's own text, in formats the app grades. Change and improvement
suggestions suit any plan, so none contradicts its settings. Mark AI actions
with `icon-ai`.

Use parent terminology: "הגדרות" for plan, "טקסט" for material, source kinds
"כתבו עבורי תוכן חדש", "יש לי טקסט משלי" and (templates) "אבחר טקסט חדש בכל פעם".
Describe lengths as approximate words or strict ranges. Distinguish template
publication, draft saving and marking a reviewed revision ready. The primary
action progresses from create text to create questions to mark ready (plans
without generated text start at create questions); template editing uses
publish. The action bar holds it, save, save state and Undo; the review card
holds readiness. Put uncommon actions under **פעולות נוספות** or a disclosure.

Question cards show prompt, options and parent-only answer, with move/remove
icons in the header. From `sm`, typed answers share the prompt row; choice
answers follow options. Type/points live in a disclosure. Lists consistently
use an item remove icon and one secondary add button below. Scoped AI actions
keep optional instructions, progress, results and errors in their card; full
generation reports above content.

Confirm AI-extracted source text only while pending, preserving exact content.
Keep invalid edits repairable. When later edits/Undo fence off generation results,
offer the saved result for explicit inspection/reload. External saves/deletions
appear in save status with reload when available. Failed writes offer reload;
they may have applied unless rejection is definite, so `writeError` says where
to check only then. Local blockers send nothing
and offer no reload. Keep stages, outcomes, cost and raw output under
**פרטים טכניים**; never imply automatic paid retries.

Show server length measurements with diagnostics, distinguishing advisory targets,
strict blockers and parent educational review. Once content is present and ready
for review, with no generation writing it, show saved field diagnostics at the
field until edited. The review names where to fix and lists only non-field
blockers; it never shows ready during generation. Offer adoption only beside
saved-stale content. A released draft shows its content read-only and points to
assignment. Frozen previews disclose answers and offer copy-to-draft.
Reusable read-only text gets `CopyButton`; editable fields copy natively.

## Loading

Keep the shared `LoadingIndicator` mounted outside `aria-busy` containers, with
`active` bound to the request's pending state, so its empty live region exists
before the status changes. Use `variant="panel"` for page loads and long AI
calls and the default inline variant for shorter actions. Every variant fades in
only after a short delay, so quick requests never flash; the shell's
`variant="bar"` reports navigation from above the page without shifting content.
Set `label` and an optional `detail`; never invent progress percentages or
stages. Tune it with `--loader-color` (brand), `--loader-size` (1.5rem inline,
3rem panel) and `--loader-duration` (1.6s):

```html
<app-loading-indicator
  [active]="busy()"
  variant="panel"
  label="יוצרים את התרגול שלכם…"
  detail="זה עשוי לקחת כמה דקות."
  style="--loader-size: 3.5rem; --loader-duration: 2s"
/>
```

The animation respects reduced motion; the readable status remains.

## Check

Follow the [verification commands](../README.md#verify). Check keyboard
navigation and 360px screenshots at 200% text, including long mixed-language
content, and review screen-reader behavior when changing an interaction. Enlarge
text through the browser's text size, as the browser tests do: media queries
ignore text that page styles enlarge. Use Playwright's managed output paths for
screenshots and failure traces; keep captures out of source control.
