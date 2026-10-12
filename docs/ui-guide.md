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
  targets; include the skip link. Signed-out pages (entry choice, parent
  sign-in, child code and both unavailable pages) share `EntryLayout`: the
  app's promise beside one card that holds the page's `h1` and its single task,
  centered between the pinned bands; primary actions in that card span it.
- Route changes never move the layout: the root keeps a stable scrollbar
  gutter, the footer rests at the window's bottom on short pages, new pages
  open at the top and Back restores the previous position.
- Use the shared light/dark theme tokens, visible focus/error states and locally
  bundled Heebo in three weights: semibold headings with snug leading, medium
  field captions and actions, and regular body, option and help text with the
  theme's 1.7 leading. Underlines sit under the text
  (`text-underline-position: under`), clearing vowel marks and descenders;
  Hebrew text keeps its natural letter spacing.
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
  show only the icon, so the row stays one line at normal text sizes. The
  `save-state` is announced on every change and shows only when it needs
  attention.
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
  lasting effects keep their visible label. Where a `nav-link` or an `icon-only`
  row action keeps only its icon on narrow screens, an `aria-hidden` tooltip
  shows that label there.
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
page padding (`py-main`), so page roots add none. Density is a theme concern:
the structural rhythm uses the `section`, `stack` and `inset` spacing roles from
`theme.css`, and panels pad by the `gutter`, so a theme can tighten or loosen
every page by redefining those tokens. Lists span the frame and sit on the page
as raised `list-row`s; forms and documents sit in panels. A page with one
reading column, such as an activity document, uses `max-w-reading` with its
header inside it; a single centered card uses `max-w-card`. A failed read shows
`ReadFailure`: its `error` alert above an `icon-refresh` retry link. Reads that
offer something else, such as signing in again, keep that layout.

| Token             | Use                                                      |
| ----------------- | -------------------------------------------------------- |
| `gap-1`           | Heading/description, status/content, stacked text links  |
| `gap-2`           | Control and revealed content                             |
| `gap-3`           | Buttons in an action row; the chat's turns and blocks    |
| `gap-stack`       | Fields, blocks, list rows and field/action rows          |
| `gap-section`     | Page sections and headed groups                          |
| `py-inset`        | Block padding of rows, choices, notes, errors and toasts |
| `gap-x-6 gap-y-2` | A wrapping row of actions and links                      |
| `gap-x-4`         | An inline row of links or status text                    |
| `mb-2`            | Label above its control, set once in `base.css`          |
| `mt-2`            | Hint/help/error below its control                        |

Keep each label/control in one field block, not separate gapped children.
Disclosures own summary/content spacing; empty live regions occupy no slot,
except a bounded chat's thinking row, which keeps its place once a conversation
starts.
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
  `-card`), the control height (`--spacing-control`), the density roles
  (`--spacing-section`, `-stack`, `-inset`, `-gutter`) and the pinned layout:
  the shell rows (`--spacing-shell`), main's block padding (`--spacing-main`),
  the band they clear together (`--spacing-pinned`) and the window height left
  between them (`--spacing-pinned-fill`). A spacing token also names
  `w-*`/`max-w-*` utilities, so it never reuses a container name.
- `utilities.css` owns project variants: `pinned-header`, `pinned-actions`, and
  `dark` for an explicit dark choice or a dark device without any explicit theme
  choice.
- `base.css` styles elements, including native form controls and focus. A plain
  `ul` reads as bullets; layout lists opt out with `role="list"`.
- `components.css` holds every shared visual treatment, grouped as actions
  (buttons, links, `nav-link`, `icon-button` with its `tooltip`, `chip`,
  `segmented` radios with `segmented-compact` for icon segments, `choice`
  answer cards; a `data-attention` button carries an unsaved dot), the shell
  (`app-frame`,
  `app-header`, `brand`, `brand-mark`), surfaces
  (`panel`, `well`, `card-section`, `action-bar` with its
  `save-state`, `list-row` with its `row-link` and `aria-current` selection,
  `empty-state`), conversation (`chat-panel`, `chat-title`, `chat-message`,
  `chat-reply`, `chat-latest`, `chat-target`, `composer`), marks (`badge` and
  `icon-tile` with their shared `data-tone`, `status-icon`, `ai-mark`, `code`),
  lists (`steps`) and feedback (`loading`, `progress`, `note`, `error`,
  `field-error`, `callout`, `toast`).
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
keeping the composer visible; below desktop width it is a sheet over the page
that, where the action bar is pinned and anchor positioning works, rises from
the bar at its width (`anchor-name: --action-bar`), and otherwise from the
window's edge. An empty conversation hides its history until the first request.
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
the page's alignment or reorders a calculation. Formatted dates are `<time>`
elements: one unit that never breaks inside or reorders the sentence around it.
`<option>` text cannot hold markup, so wrap its user values in first-strong
isolates (`&#x2068;…&#x2069;`). Isolate email and numeric inputs as LTR. Bind
text fields' `dir` with `[formField]`: `FieldDirection` keeps an empty `auto`
field in the page direction, so the caret and placeholder start on the page's
side. Back arrows point right, and decorative gradients start at the reading
edge. Dates display in Hebrew with a month name and local time through the
app-wide `DatePipe` default, while stored timestamps remain UTC.

Read every size limit, count cap and the numbers in their messages from the
server's `Limits`; child answer controls use `ChildSessionIdentity.answerLength`
instead, without calling parent APIs. Never hard-code content limits in forms.

Write concise Hebrew for labels, validation, loading and errors, and never
expose raw framework or provider errors. Address parents and children in the
plural imperative ("בדקו", "נסו שוב"), call the parent home "המרחב שלנו" and
avoid internal terms such as model, item or local copy. A count of one reads in
words ("שאלה אחת"). Write everyday, semi-formal Hebrew; prefer "אחרי" to
"לאחר" and an imperative to "יש ל…" outside field rules. Name the child where
a page knows them: parent headings and confirmations use the child's name, and
the child's own pages greet and praise them by name in forms that read the same
for any gender ("שלך", "ענית"). Children read short, warm sentences that say
what to do next, without technical words such as access, device, browser,
request or saved work; actions name their result ("הגשה להורה", "בדיקת
עדכונים"). Render
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
and assignment lists use shared rows, native controls and `Pager`: chevron
icon buttons around the page number, hidden while a list fits on one page. On
wide screens the profile list sits beside its editor, and the row being edited
carries `aria-current="true"`.
Empty states name the next step, or say that a filter or later page has nothing
more. Separate profile edits from device access; confirmations explain
disable/revoke effects. Activation codes show large as `XXXX-XXXX`, selectable
with expiry, never in URLs or persistent browser storage. Withdrawn rows offer
restore, which needs no confirmation. Withdraw and restore keep their labels and
lead with an icon; on phones they show only the icon (`max-sm:icon-only`), named
for their item, so the row keeps its room for the title.

Lists, the result page and the child inbox follow changes live through
`liveLists`, with no refresh controls: a change elsewhere rereads them in place,
keeping shown rows, and rows that arrive fade in. The inline `LoadingIndicator`
shows only while a list has nothing to show (its first read, or a new view or
child). Rows sit in one `.list`, which is `aria-busy` while it loads another
page or saves a change: its rows fade after a beat and return at once, so
nothing moves and quick answers never flicker. A failed read offers its retry.
When the stream is refused, the shared `LiveStatus` says so once and offers
**בדיקת עדכונים**, which reconnects and rereads. A status `badge` and its row's
`icon-tile` share one `data-tone`: `new` asks for attention (work awaiting the
parent's grading, a new activity for the child), `done` is finished and `quiet`
is history (withdrawn, archived); work in progress has none.

Filters form one toolbar row: a `segmented` view switch, which is a control's
height, beside fields whose label is screen-reader-only there. Its labels never
wrap; on phones it spans the row and its segments share it, like the full-width
fields below it. The assignments page opens on **פתוחות** (assigned or awaiting
review), with **הושלמו** and **בוטלו** as the other views and a child filter
beside them. Every view label, here and in the library, carries its exact size
in `Count`.

The library's drafts, ready activities and archive are `section-fold`
disclosures whose summary is the section heading with its exact `Count` (a quiet
0 when empty), so a folded section still says what it holds. A `Count` keeps its
place unseen until known, so counts never shift the layout. Ready rows show what
put them in their view, newest first: **הוקצתה לאחרונה**, **הושלמה** (when the
last child finished) or **אושרה** with its date, and each child as a
`ChildBadge`: the name, with the grade beside it in its own badge once
completed. An activity's page names the same children, and an assignment row
shows its child as that badge under the activity's title. Ready activities open
with the page unless that first count is 0; drafts and the archive start folded.
Later view switches and changes never fold or unfold them. Ready activities open
on **מוקצות** (work with the children or awaiting review), with **לא הוקצו** and
**הושלמו** beside it; their count is every ready activity. Deleting from the
archive names the children's answers and grades it erases.

A final score shows as one `ScoreBadge`, wherever it appears (ready and archived
rows by child, completed assignment rows, the result page and the child's
receipt and list): a grade out of 100 with no percent sign, never joined to a
name; `data-band` `high` from 80, `mid` from 55 and `low` below, in existing
contrast-checked pairs, with the number as its text and "ציון" for screen
readers.

Grade (free text) and age (LTR integer, server bounds) are optional; each can
be cleared, with no age-confirmation checkbox. Keep secondary dates under native
“פרטי זמנים” disclosures at the end of their card. Follow the [cleanup
contract](product-specification.md#profile-and-device-cleanup):
unused profiles offer deletion, profiles with history disabling, active devices
revocation and inactive devices removal.

Frozen previews own child selection/assignment and link to an existing pair on
replay; a withdrawn pair offers its explicit restore. Review shows frozen
content, exact submitted text and parent-only answer disclosures. Pending grades
start empty; automatic/completed awards stay read-only. Distinguish pending
subtotal from final score; grades require positive possible points. Recovery
stays beside finalization and never silently replaces local grades. Parent and
child learning pages share primitives and logical utilities without separate
stylesheets.

Group creation, opening, last-save, submission and review dates in “פרטי זמנים”.
Label elapsed time “זמן מהפתיחה עד ההגשה (כולל הפסקות)”, using minutes/hours or
“פחות מדקה”. A result page's heading names the child, with the activity link
and status badge on one line; unsubmitted work says so in one note above its
frozen content. Missing/reversed timestamps show unavailable duration. See [timing
semantics](product-specification.md#elapsed-activity-time).

## Child learning

Use `PageShell`, shared theme/controls and child-only home/disconnect
navigation; no parent links or same-browser mode switch. Parent activation
instructions give a copyable address for a separate browser, without the code.
Explain persistent access and fresh activation after
disconnect/revocation/expiry. An uncertain activation offers a session check;
availability failures offer retry.

The inbox greets the child by name. Separate available/submitted inbox views
with a `segmented` switch and bounded paging; each row's details line, like the
parent lists', holds its status `badge` (the state on phones, where the tile
drops), its grade and its date. Keep materials above numbered questions in the
reading column. Numeric answers use LTR text inputs with a decimal keyboard,
retaining invalid edits and sending no surrounding spaces; text preserves
whitespace. Choice questions are a fieldset whose legend is the prompt, with
every option a `choice` card around its native radio, so long answers wrap
rather than hide in a closed select. An answered question fills its number.
While work is open, the action bar leads with one meta line: a `progress` track
of answered questions (the count is its accessible text) and the time since the
first start, which ticks only while work is open and the page is visible. Below
it, one row holds save, submit, saved-work recovery and the save state, so the
row stays one line at any timer length down to 320px. On phones the unsaved
state shows as the save button's `data-attention` dot; it is still announced.
The saved-work check shows only while recovering or after submission, where it
fetches the grade. Invalid submit focuses the first problem; missing answers
need confirmation. Checkpoint reads never overwrite local text implicitly. A
submission receipt leads the page, locks editing and praises the child by name
beside its outcome icon, which fills (`data-done`) once reviewed; pending review
has no final score, and zero possible points has no grade.

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

Before the first plan, the chat is the page: one reading column around the first
request. From the first plan on, the chat keeps one layout, `data-beside`:
beside the activity on wide screens, and below desktop width a sheet over the
page. The sheet stays closed after the first plan, whose settings take the
focus; the action bar's AI button and every “ask about this” open it. Its close
button or Escape returns focus to the control that opened it, or to the AI
button where tapping gave the opener no focus, with the page unmoved. Focus
moves never scroll the page. A request never moves focus: the composer turns
read-only and one control sends, then stops. An empty chat fits its title, ideas
and composer, beside the activity or as the sheet resting just above the bar.
Once a conversation starts, the composer keeps its place: the sheet grows up
from the bar, and beside the activity the chat takes its column height. The
conversation, or before the first request its ideas, scrolls when short, and the
thinking row keeps its place, so sending never moves the composer or resizes the
conversation. **עריכה** opens titles, instructions, texts, questions/options,
answers and points in the same buffer. Edits save themselves while editing;
**סיום עריכה** returns to reading, and invalid fields stay open for correction.
Structural changes use chat. Each text/question offers an accessible “ask about
this” action that fills and focuses the composer without sending. Keep the
selected target visible and update only the app-inserted prefix when switching
targets, preserving parent text. At widths of 64rem and heights of 40rem or
more, keep chat beside the activity with only its history scrolling. Its column
starts at the top of the page, beside the heading, and fills exactly the space
between the pinned bands, so it never moves while the page scrolls and typing
never scrolls the page. The action bar ends the document column, so it never
covers the chat. Respect enlarged browser text.

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
`SuggestionChips` only fill fields and never send; they sit in equal cells, two
per row from tablet width. Suggestions appear before the
first exchange and apply to the current activity; mark AI execution actions with
`icon-ai`.

Every valid change saves itself about a second after the parent pauses; there
is no save action. Only the first save pauses setup, keeping supplied text
visible and disabled. If its outcome is uncertain, offer **בדיקת שמירת הטיוטה**
and keep setup paused until it is resolved. Later saves never lock the fields.
Use **יצירת הפעילות** for initial
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
