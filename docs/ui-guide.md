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
  state and current actions, with their errors and recovery. It pins to the
  window's bottom while the window is at least 40rem tall at the current text
  size, and scroll padding keeps focus clear of it.
- Pages opened from the library lead back with a link above their heading. A
  `list-row` with a `row-link` opens from anywhere on the row, which shows the
  link's hover and focus; the row's other actions stay separate controls.
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
  to save or send. That attempt adds one line beside its action, where problems
  with the whole form also show; a closed disclosure names invalid content in
  its summary. A problem the field did not just cause shows at once: an answer
  that another field's edit left unmatched, or one the saved check found.

## Spacing

Containers own spacing with `gap`; shared primitives carry no outer margin.
`main` owns the page's side gutter, so page sections add only vertical padding.
List pages span its width, so their edges meet the header's; activity documents,
whether edited or frozen, sit in the `max-w-3xl` reading column. Use one scale
everywhere: `gap-1` between a heading and its description, a status line and its
list or action, and stacked text links, `gap-2` between a control and what it
reveals, `gap-3` between buttons in an action row, `gap-4` between fields and
blocks in any stack, including a row of fields and its action, and `gap-6`
between page sections and headed groups. A field is one block: its label sits
`mb-2` above the control and hints, help or error lines carry `mt-2` below it;
never place a label and its control as separate children of a gapped container.
Open disclosures space their summary from the content, and empty live regions
take no layout slot, so they never double a gap. Group card fields with
`role="group"` and a heading inside the padding rather than a `.well` fieldset
legend; each later headed group in a card is a `card-section`, whose rule marks
where the previous one ends. Short related fields share a row: settings fill one
two-column grid, numbers sit beside their siblings and a list's remove action
sits beside its item. Content beside a tile or trailing actions takes
`min-w-1/2 flex-1`, so they stay beside it and wrap only once it would fall
below half the row. Optional content renders only when present.

## Styles and theming

`frontend/src/styles.css` imports each layer from `frontend/src/styles/`:

- `theme.css` owns the colors, elevations and corner radii. Tailwind's default
  palettes and scales are cleared, so templates can only use theme values.
- `utilities.css` owns project variants: `pinned-header`, `pinned-actions`, and
  `dark` for an explicit dark choice or a dark device without an explicit light
  choice.
- `base.css` styles elements, including native form controls and focus.
- `components.css` holds every shared visual treatment, grouped as actions
  (buttons, links, `icon-button` with its `tooltip`, `chip`), surfaces
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
A visual treatment used in
more than one place becomes a primitive, and primitives take state from
attributes (`aria-current`, `data-done`, `data-problem`) rather than
alternative class lists. A component's own stylesheet uses theme variables
only. Give `steps` and
`progress-steps` lists `role="list"` so WebKit keeps list semantics.

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
raw framework or provider errors. Render generated text through interpolation,
never HTML, and never rewrite saved content for presentation. Generated text is
bare by contract, so presentation supplies its structure: keep line breaks with
`whitespace-pre-wrap` (blank lines between paragraphs, single breaks for poem
lines and dialogue turns), number questions in an ordered list and show choices
as separate items or native options.
[`AiPrompts`](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs) promises
this to the model, so change both together. Control labels and options come from
the reviewed template; the shared settings use application-owned labels, and
only difficulty has fixed options.

## Parent learning management

Family navigation sits below the main header so its existing compact controls
remain usable on phones. Profile and assignment lists use the shared list rows,
native controls and bounded previous/next paging. Profile edits and device
access are separate labelled sections; disabling and revoking name their effects
before confirmation. Activation codes remain selectable text with their expiry,
never a URL or persistent browser value.

The frozen preview owns child selection and assignment, and links to the
existing assignment when the pair was already assigned. Parent review displays
the frozen content, exact submitted text and parent-only answer disclosures.
Pending grades start empty; automatic awards and completed grades are read-only.
Show the automatic subtotal as pending, and show a final percentage only with a
positive possible total. Keep saved-result recovery beside the finalization
action without replacing local grades. Reuse shared theme primitives and
logical layout utilities; these pages need no separate stylesheets.

## Child learning

The child area uses the same `PageShell`, theme tokens and controls as the parent
area, with its own home and device-disconnect navigation. It has no parent links
or same-browser mode switch. Parent activation instructions offer a copyable
address for the separate child browser; codes never enter that address.
Activation explains persistent access and offers a session check after an
uncertain result instead of repeating a code.

The inbox separates available and submitted work with native selection and bounded
paging. The player keeps materials above a numbered question list in the reading
column. Numeric answers use LTR text inputs with a decimal keyboard so invalid
keystrokes remain repairable; text answers keep whitespace and native choices
use isolated option labels. The action bar reports unsaved/saved progress and
owns explicit save, submit and saved-work recovery. Invalid submission focuses
the first problem; missing answers require confirmation. Reading a checkpoint
never silently replaces local text. Confirmed submission locks editing and shows
a receipt; pending review has no final score, and a zero possible total has no
percentage. No child page has its own stylesheet.

## Workspace actions

The workspace owns one editable buffer and derives its presentation from it:
describe, then adjust settings, then review content. Phases are never stored or
routed. Before a plan exists, the request dominates; a quiet disclosure offers
manual entry. The visible settings (topic, audience, difficulty and question
count) are the plan's defaults in a template and an activity alike, and the
source text stays beside them. An activity also shows only the applicable
length, format, option count and requested choices; a template has no activity
of its own, so it creates none and links to creating one once a version exists.
Activities show a derived four-step indicator (describe, settings, review,
ready); only the current step keeps its label on narrow screens. The plan
definition and guidance sit under **אפשרויות מתקדמות**, open by default only for
template editing. A plan authored here, a template or a new activity's own
plan, ends with **מה אפשר לשנות בכל פעילות**: the applicable length,
option-count and one-format toggles, and every choice definition as a compact
card that names the part it changes and is added with one button. An activity
created from a template only sets the values its template allows.
Options that have no effect in the current state stay hidden: the combined
length needs several generated texts, one format per activity needs several
formats, and a required flag is moot once a choice has a default. With several
texts, each text's fields and choices name that text.
Once a plan exists, the change conversation is its own card after the settings
and can change every setting, including the advanced options. Once content
exists, settings and the conversation collapse to a derived one-line summary,
shown only while they are collapsed, and the content becomes the main surface.

Authoring is a conversation: the parent's turns sit at the end edge, the
assistant's replies carry the `ai-mark` with the computed changes and
assumptions, and a typing bubble shows while a request runs. The composer sends
on Enter (Shift+Enter adds a line) and turns its send button into a stop button,
keeping keyboard focus on whichever is present; a failed or cancelled request
returns its text to the composer. `SuggestionChips` offer ready wording for the
first request, common changes and scoped improvements; a suggestion only fills
its field, leaving focus on the suggestion, and never sends. Mark AI actions
with `icon-ai`.

Use parent language, never internal terms: say "הגדרות" for the plan and "טקסט"
for materials; source kinds read as "כתבו עבורי תוכן חדש", "יש לי טקסט משלי"
and, for templates, "אבחר טקסט חדש בכל פעם"; length reads as approximate words
or a strict range, never modes. Keep template defaults, per-activity choices and
editable content visibly distinct. Saving a template publishes only the plan;
saving a draft retains editable work; marking ready freezes the reviewed
revision. Give each state one primary action: create the activity, then mark it
ready; template editing makes publication primary. It sits in the action bar
with saving the draft, the save state and Undo; the review card keeps the
readiness summary. Uncommon actions live under **פעולות נוספות** or a quiet
disclosure. Question cards keep prompt, options and the parent-only answer
visible, with icons to move or remove the question in their header; from `sm`, a
typed answer shares the prompt's row, while a choice answer follows its options.
Answer type and points sit in a per-question disclosure. Every form list edits
the same way: a remove icon beside each item and one secondary add button after
the list.
Scoped AI improvement is a contextual action with an optional instruction; its
progress, result and any error show in that card, while full generation reports
above the content.

Show source confirmation for AI-extracted text only while it is pending and
preserve its exact content. Keep invalid keystrokes visible for correction. A
generation result cannot replace later local edits or Undo; offer the saved
server result for inspection and explicit reload. A save or deletion on another
device reads in the save state, beside the reload control when there is a
version to load. A failed request offers that reload beside its message and,
unless the server definitely rejected it, says it may have applied; a local
blocker, such as content that still blocks Mark Ready, sends nothing and offers
no reload. Describe operations in plain language, keep stages, outcomes, cost
and raw output behind **פרטים טכניים**, and never imply that another paid
attempt is automatic.

Display server length measurements beside saved diagnostics. Distinguish
advisory targets from strict range blockers, and technical readiness from
the parent's educational review. Once content is ready to review, present and no
generation writing it, a saved diagnostic of one field, such as the title, a text
or a question's answer, shows at that field like a validation error until the
field changes; the review names where to fix and lists only blockers without a
field, and never calls content ready while a generation runs. Offer adoption
only in a callout beside content that saved diagnostics mark as stale. Frozen
previews expose answers in native disclosures and offer an explicit copy to a
new draft. A read-only box whose text a parent may reuse, such as an AI response
or a material text, carries a `CopyButton`; editable fields copy natively.

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
ignore text that page styles enlarge.
