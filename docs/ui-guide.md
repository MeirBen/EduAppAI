# Hebrew UI

The app is **לומדים ביחד**, with Hebrew UI and RTL layout. Keep UI copy beside
its feature; identifiers and developer docs remain English. Learning content and
parent labels keep their original language and values.

## Layout and controls

- Use native HTML, document scrolling and accessible controls. The header,
  page and footer share one `app-frame` (`max-w-page`), so their edges line up.
  The header holds the brand and one primary nav: every signed-in destination
  plus the quiet sign-out (`nav-link-quiet`). It stays visible on screens at
  least 64rem wide and 40rem tall and scrolls away on smaller ones. Below tablet
  width it keeps one row: nav links show only their icons, and on phones the
  signed-in brand only its mark. The theme picker sits in the footer. Where the
  header is pinned, it and the footer are rows of the shell height, and scroll
  padding clears the same `--spacing-pinned` band at each window edge for focus
  targets; include the skip link.
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
- A page that edits or acts on one item ends with an `action-bar`, a tinted
  dock over the page: one row of current actions and its status, with their
  errors and recovery above. Forms, such as assignment, stay in their own panel
  so the bar never hides content. It pins to the window's bottom while the
  window is at least 40rem tall at the current text size, and scroll padding
  keeps focus clear of it. Its main row is `action-bar-row`: the actions, then
  `action-bar-status`, which ends at the far edge. Every action leads with an
  icon and keeps its label as its accessible name; on phones secondary actions
  show only the icon, so the row stays one line at normal text sizes. On long
  working pages (`actionBar` with `app-action-bar-toggle`) the pinned bar can
  shrink to one row of icons; an alert always shows it whole. The `save-state`
  is announced on every change and shows only when it needs attention.
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
  assistive technology. Tooltips show on hover or keyboard focus; Escape hides
  them, and pressing a control hides its tooltip until the pointer or focus
  leaves it; anchor positioning keeps them within the window. Actions with
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
  not crowd enlarged text. Buttons, links, fields, chips and icon buttons take
  the theme's `--spacing-control` height: 44px for touch and 40px for a mouse.
  On phones the page heading steps down one size and list rows drop their
  decorative tile; body and field text stay 16px. Preserve browser zoom, iOS
  text scaling and the production bundle budgets.
- Label controls; associate errors with fields. Provide keyboard access, visible
  focus, loading status, error alerts and distinguishable repeated
  links/disclosures. Opening or adding an editor focuses its first field.
  Otherwise, focus moves only to keep it from being lost: a button its
  own action makes unavailable stays focusable through `disabledInteractive`,
  and an action that removes, moves or replaces the focused control hands focus
  through `focusHolder` to the successor its owner names (the same control, a
  neighbour's disclosure or the list's add button), else to the nearest
  surviving region heading; content that replaces the request, such as the
  first plan, takes it at its heading. Results are announced, never focused or
  scrolled to; only full generation brings its progress into view once, without
  motion when reduced motion is preferred.
- Each problem shows on the field or group that can fix it: its edge and message
  appear once the parent leaves it, and on every field once they try to save or
  send. A failed attempt sets one announced line beside its action, where
  problems with the whole form also show; a closed disclosure names invalid
  content in its summary. A problem the field did not just cause shows at once:
  an answer that another field's edit left unmatched, or one the saved check
  found. Field messages state their rule ("יש להזין…"); the attempt line says
  what to do ("תקנו את השדות המסומנים."). Length rules rely on the native
  `maxlength` and add no message.
- Final or replacing actions ask first with the native
  confirm, naming the effect: deleting, reset, withdrawal, disabling, revoking,
  marking ready, regenerating and submitting. Reversible edits rely on Undo
  instead.

## Spacing

Containers own `gap`; shared primitives have no outer margin. `main` owns the
page padding, so page roots add none. Lists span the frame and sit on the page
as raised `list-row`s; forms and documents sit in panels. A page with one
reading column, such as an activity document, uses `max-w-reading` with its
header inside it; a single centered card uses `max-w-card`. A failed read shows
its alert above an `icon-refresh` retry link in a `grid gap-2`.

| Token             | Use                                                     |
| ----------------- | ------------------------------------------------------- |
| `gap-1`           | Heading/description, status/content, stacked text links |
| `gap-2`           | Control and revealed content                            |
| `gap-3`           | Buttons in an action row; the chat's turns and blocks   |
| `gap-4`           | Fields and blocks, including field/action rows          |
| `gap-6`           | Page sections and headed groups                         |
| `gap-x-6 gap-y-2` | A wrapping row of actions and links                     |
| `gap-x-4`         | An inline row of links or status text                   |
| `mb-2`            | Label above its control, set once in `base.css`         |
| `mt-2`            | Hint/help/error below its control                       |

Keep each label/control in one field block, not separate gapped children.
Disclosures own summary/content spacing; empty live regions occupy no slot.
Use `role="group"` with an inset heading for grouped card fields, not a
`.well` fieldset legend; later headed groups use `card-section`. Related
fields share rows: two-column settings, sibling numbers and list-item remove
controls. Content beside a tile/actions uses `min-w-1/2 flex-1`, wrapping
below half-width. Render optional content only when present.

## Styles and theming

`frontend/src/styles.css` imports each layer from `frontend/src/styles/`:

- `theme.css` owns colors, elevations, corner radii, typography and motion.
  Tailwind's default palettes and scales are cleared, so templates can only use
  theme values.
- `theme.css` also owns the layout widths (`--container-page`, `-reading`,
  `-card`), the control height (`--spacing-control`) and the pinned layout:
  the shell rows (`--spacing-shell`), main's block padding (`--spacing-main`)
  and the band they clear together (`--spacing-pinned`). A spacing token also
  names `w-*`/`max-w-*` utilities, so it never reuses a container name.
- `utilities.css` owns project variants: `pinned-header`, `pinned-actions`, and
  `dark` for an explicit dark choice or a dark device without any explicit theme
  choice.
- `base.css` styles elements, including native form controls and focus. A plain
  `ul` reads as bullets; layout lists opt out with `role="list"`.
- `components.css` holds every shared visual treatment, grouped as actions
  (buttons, links, `nav-link`, `icon-button` with its `tooltip`, `chip`,
  `segmented` radios, `choice` answer cards), the shell (`app-frame`,
  `app-header`, `brand`, `brand-mark`), surfaces
  (`panel`, `well`, `card-section`, `action-bar` with its
  `save-state`, `list-row` with its `row-link` and `aria-current` selection,
  `empty-state`), conversation (`chat-panel`, `chat-title`, `chat-message`,
  `chat-reply`, `chat-latest`, `chat-target`, `composer`), marks (`badge`,
  `icon-tile`, `status-icon`, `ai-mark`, `code`), lists (`steps`) and feedback
  (`loading`, `note`, `error`, `field-error`, `callout`, `toast`).
  Use `note`, with one icon, for a short fact about how the app behaves, such as
  kept text, costs or limits; help on what to enter stays plain text under its
  field. Use `error` for a failure that replaces a page's content and
  `field-error` beside the field, card or action that failed. `toast` is only
  for app-wide news that arrives while a page is open, such as a new build: it
  floats without shifting the page or taking focus, and it can be closed.
- `icons.css` holds the icon set: masks painted with the current text color,
  drawn for right-to-left reading where they point.

Primitives read only tokens; templates add layout utilities and token colors
such as `text-muted`. Color tints have one role each: `/15` for brand
hairlines and focus halos, `/25` for the error edge and `/30` for hover edges.
Repeated treatments become primitives, with state from `aria-current`,
`data-done` or `data-problem` rather than alternative class lists. Keep visual
CSS in these shared layers; components own markup, behavior and layout
utilities, without local stylesheets, inline visual overrides or separate theme
branches. The shared `chat-panel` bounds desktop conversation history while
keeping the composer visible; below desktop width it is a sheet over the page.
Give `steps` `role="list"` to preserve WebKit list semantics.

A theme redefines semantic `--color-*` tokens and, optionally, the corner and
elevation roles (`--radius-*`, `--shadow-*`) on the root. These stay runtime
variables, including composed shadows, so templates need no `dark:` utilities.
Shared styles use `shadow-(--shadow-panel)` and the corresponding roles to
retain runtime shadow overrides; named shadow utilities inline their geometry at
build time. Keep the contrast contract in `theme.css` for every theme and check
each token on every surface, tint and translucent layer it meets.
Forced colors drop backgrounds and rings, so a selected or current state that
shows only through them adds a `forced-colors:` border. The footer's theme
picker follows the device by default. `Theme` stores an explicit choice in
localStorage, since no server render needs a cookie. The inline script in
`index.html` applies it before first paint, and CSS follows device changes
live. The brand `theme-color` suits both themes.

To add a theme, add its root token overrides and `color-scheme` in `theme.css`,
then register the preference in `Theme`, the pre-paint whitelist in `index.html`
and `ThemePicker`. No feature template or component style needs to change.
Device preference applies only while `data-theme` is absent. Extend the browser
theme checks for the new palette: contrast, persisted/system selection and token
overrides. This uses [Tailwind's theme
variables](https://tailwindcss.com/docs/theme)
and the native CSS cascade, without a theme registry or runtime stylesheet
loader.

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

Write concise Hebrew for labels, validation, loading and errors, and never
expose raw framework or provider errors. Address parents and children in the
plural imperative ("בדקו", "נסו שוב"), call the parent home "המרחב שלנו" and
avoid internal terms such as model, item or local copy. A count of one reads in
words ("שאלה אחת"). Children read short, warm sentences that say what to do
next, without technical words such as access, device, browser, request or saved
work; actions name their result ("הגשה להורה", "בדיקת עדכונים"). Render
generated text through interpolation, never HTML, and never rewrite saved
content for presentation. Generated text is bare by contract, so presentation
supplies its structure: keep line breaks with `whitespace-pre-wrap` (blank lines
between paragraphs, single breaks for poem lines and dialogue turns), number
questions in an ordered list and show choices as separate items or native
options. `bdi`/`dir="auto"` display a whole-item calculation or comparison in
order, but bidi mirroring reverses `<` and `>` beside Hebrew words, so the
content contract keeps signs out of Hebrew text.
[`AiPrompts`](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs) promises
this to the model, so change both together. Question labels/options keep their
saved text; the settings summary uses application-owned labels.

## Parent learning management

Family pages are destinations in the header's primary nav. Library, profile
and assignment lists use shared rows, native controls and `Pager`, which stays
hidden while a list fits on one page; a count badge appears only while one page
holds the whole list. On wide screens the profile list sits beside its editor,
and the row being edited carries `aria-current="true"`. Empty states name the
next step, or say that a filter or later page has
nothing more. Separate profile edits from device access; confirmations explain
disable/revoke effects. Activation codes show large as `XXXX-XXXX`, selectable
with expiry, never in URLs or persistent browser storage. Withdrawn rows offer
restore, which needs no confirmation. Lists of children's work and a profile's
devices refresh when the page becomes visible again (`refreshOnReturn`) and keep
a refresh link.

Grade (free text) and age (LTR integer, server bounds) are optional; each can
be cleared, with no age-confirmation checkbox. Keep secondary dates under native
“פרטי זמנים” disclosures at the end of their card. Follow the [cleanup
contract](product-specification.md#profile-and-device-cleanup):
unused profiles offer deletion, profiles with history disabling, active devices
revocation and inactive devices removal.

Frozen previews own child selection/assignment and link to an existing pair on
replay; a withdrawn pair offers its explicit restore.
Review shows frozen content, exact submitted text and parent-only answer
disclosures. Pending grades start empty; automatic/completed awards stay
read-only. Distinguish pending subtotal from final score; percentages require
positive possible points. Recovery stays beside finalization and never silently
replaces local grades. Parent and child learning pages share primitives and
logical utilities without separate stylesheets.

Group creation, opening, last-save, submission and review dates in “פרטי זמנים”.
Label elapsed time “זמן מהפתיחה עד ההגשה (כולל הפסקות)”, using minutes/hours or
“פחות מדקה”. Unsubmitted work shows “עוד לא הוגשה”; missing/reversed timestamps
show unavailable duration. See [timing
semantics](product-specification.md#elapsed-activity-time).

## Child learning

Use `PageShell`, shared theme/controls and child-only home/disconnect
navigation; no parent links or same-browser mode switch. Parent activation
instructions give a copyable address for a separate browser, without the code.
Explain persistent access and fresh activation after
disconnect/revocation/expiry. An uncertain activation offers a session check;
availability failures offer retry.

Separate available/submitted inbox views with a `segmented` switch and bounded
paging; rows carry a status `badge`, which keeps the state on phones where the
tile drops. Keep materials above numbered questions in the reading column.
Numeric answers use LTR text inputs with a decimal keyboard, retaining invalid
edits and sending no surrounding spaces; text preserves whitespace. Choice
questions are a fieldset whose legend is the prompt, with every option a
`choice` card around its native radio, so long answers wrap rather than hide in
a closed select. An answered question fills its number. The action bar owns
save, submit, saved-work recovery and one status line: the save state when it
is unsaved, how many questions have answers (from tablet width; the filled
numbers show it on phones) and the time since the first start, which ticks only
while work is open and the page is visible. The
saved-work check shows only while recovering or after submission, where it
fetches the grade. Invalid submit focuses the first problem; missing answers
need confirmation. Checkpoint reads never overwrite local text implicitly. A
submission receipt locks editing and leads with its outcome icon; pending review
has no final score, and zero possible points has no percentage.

## Workspace actions

The [product specification](product-specification.md#activity-lifecycle) owns
the lifecycle and the [chat design](activity-chat-design.md) its execution.

The library shows **טיוטות** and **פעילויות מוכנות**, with **פעילות חדשה** as
its creation entry. Presentation derives from one activity buffer, with no
stored page phases.

Before content exists, emphasize the request, the concrete requirements summary
and any required source input/confirmation. Show settings read-only and change
requirements through chat. The first valid plan becomes a draft by itself, and
incomplete content saves and resumes later.
Keep each text's guidance and length beside its name, and show the number of
answer choices. A combined text length is explicitly labelled as a total.

Before the first plan, the chat is the page: one reading column around the
first request. From the first plan on, the chat keeps one layout, `data-beside`:
beside the activity on wide screens, and below desktop width a sheet over the
page. The sheet opens with the first plan, so the conversation stays where the
parent is talking; afterwards the action bar's AI button and every “ask about
this” open it. Its close button or Escape returns focus to the control that
opened it, or to the AI button when the sheet opened itself, with the page
unmoved. After every reply, focus that fell to the page moves to the history
holding it.
**עריכה** opens titles, instructions, texts, questions/options, answers and
points in the same buffer. Edits save themselves while editing; **סיום עריכה**
returns to reading, and invalid fields stay open for correction. Structural
changes use chat. Each text/question offers an accessible “ask about this”
action that fills and focuses the composer without sending. Keep the selected
target visible and update only the app-inserted prefix when switching targets,
preserving parent text. At widths of 64rem and heights of 40rem or more, keep
chat beside the activity with only its history scrolling. Its column starts at
the top of the page, beside the heading, and fills exactly the space between
the pinned bands, so it never moves while the page scrolls and typing never
scrolls the page. The action bar ends the document column, so it never covers
the chat. Respect enlarged browser text.

Every turn is a bubble on the action bar's tinted dock. As in messaging apps,
the parent's own `chat-message` takes the brand tint at the end edge, and the
assistant's `chat-reply` stays on the surface at the start edge beside
`ai-mark`, which sits by the bubble's tail. What a reply changed lists under “מה
השתנה” as `note` rows with a check, and its assumptions as `note` rows inside
their disclosure. While a request runs, the assistant's thinking row
(`LoadingIndicator variant="assistant"`) follows the turns. The history fades at
its ends and, once the parent scrolls away from the latest turn, offers
`chat-latest` to return. Enter sends; Shift+Enter adds a line. Send becomes Stop
while retaining focus; when a reply arrives, focus that would be lost moves to
the history, never into a field. A sent request leaves the composer for the
history; failure/cancellation returns it for an explicit new attempt.
`SuggestionChips` only fill fields and never send. Suggestions appear before the
first exchange and apply to the current activity; mark AI execution actions with
`icon-ai`.

Every valid change saves itself about a second after the parent pauses, without
locking the fields; there is no save action. Use **יצירת הפעילות** for initial
generation and **אישור הפעילות** for explicit approval of the saved revision.
The action bar shows the relevant action and available Undo. Saving and approval
make no AI call. Keep source text confirmation separate from activity approval.
Use “טקסט” for material and “הגדרות” for its requirements summary; source
choices are “כתבו עבורי תוכן חדש” and “יש לי טקסט משלי”. Describe lengths as
approximate word counts or strict ranges, without reusable-template terminology.

AI starts after saving any pending edit; while active, keep the canvas readable,
pause editing and show truthful operation status with Stop. Apply a complete
validated change at once. Failure/cancellation leaves saved content intact;
reconcile uncertain responses with saved state. External saves/deletions offer
explicit reload without discarding local edits. Keep stage details, outcomes,
usage and raw output under **פרטים טכניים**; successful operations leave this
quiet disclosure below the content. Never imply automatic paid retries.

Question cards show prompts/options and disclose parent-only answers. Manual
fields keep native labels; format is plain read-only text and points can use a
disclosure. Structure changes go through chat, never per-item add, remove or
reorder controls. Show server diagnostics and length measurements beside their
fields. After a
saved source/text edit, offer question regeneration or validated confirmation
that the questions still fit; derive the offer from saved diagnostics.

Approval remains unavailable until the complete saved activity passes release
checks and no operation is active. A ready preview is read-only, discloses
parent answers and offers assignment. Editing ready content creates a new draft
and keeps assigned content intact. Read-only text can use `CopyButton`; editable
fields copy natively. Keep keyboard focus, screen-reader announcements, RTL and
360px/200% text usable across reading, editing, chat and failure states.

## Loading

Keep the shared `LoadingIndicator` mounted outside `aria-busy` containers, with
`active` bound to the request's pending state, so its empty live region exists
before the status changes. Use `variant="panel"` for page loads and long AI
calls, `variant="assistant"` for the chat's thinking turn and the default inline
variant for shorter actions. Every variant fades in
only after a short delay, so quick requests never flash; the shell's
`variant="bar"` reports navigation from above the page without shifting content.
Set `label` and an optional `detail`; never invent progress percentages or
stages. `components.css` owns the indicator's variants and `theme.css` owns its
motion; use the shared sizes and brand color without per-page overrides:

```html
<app-loading-indicator
  [active]="busy()"
  variant="panel"
  label="יוצרים את התרגול שלכם…"
  detail="זה עשוי לקחת כמה דקות."
/>
```

The animation respects reduced motion; the readable status remains. Distinguish
pending availability checks from failed reads and confirmed unavailability.
Failed reads offer an explicit retry in place; retries never start AI work.

## Check

Follow the [verification commands](../README.md#verify). Check keyboard
navigation and 360px screenshots at 200% text, including long mixed-language
content, and review screen-reader behavior when changing an interaction. Enlarge
text through the browser's text size, as the browser tests do: media queries
ignore text that page styles enlarge. Use Playwright's managed output paths for
screenshots and failure traces; keep captures out of source control.
