# Hebrew UI

The app is **לומדים ביחד**, with Hebrew UI and RTL layout. Keep UI copy beside
its feature; identifiers and developer docs remain English. Learning content and
parent labels keep their original language and values.

## Layout and controls

- Use native HTML, document scrolling and accessible controls. The header stays
  visible on screens at least 64rem wide and 40rem tall; let it scroll away on
  smaller screens. Reserve scroll padding for focus targets and include the
  skip link.
- Route changes never move the layout: the root keeps a stable scrollbar
  gutter, the footer rests at the window's bottom on short pages, new pages
  open at the top and Back restores the previous position.
- Use the shared light/dark theme tokens, visible focus/error states and locally
  bundled Heebo.
- Use `panel` for raised surfaces and `button` for primary actions; add
  `button-secondary`, or the quiet `button-danger` for destructive actions
  guarded by a confirmation. Keep one prominent action per card, with quieter
  edit links and `text-link-danger` deletions. Native disclosures reveal
  answers.
- Keep hover/press feedback brief, exclude disabled controls and respect reduced
  motion. Use theme shadows and colors rather than page-specific copies.
- Actions show a 2px offset focus ring. Fields tint their own edge instead, in
  the danger color when invalid. Script focus targets (`tabindex="-1"`) show no
  ring. Selects, checkboxes and fields stay native; supporting browsers open a
  styled select listbox.
- Use rem sizing, generous line height and wrapping for long user content.
  Use the viewport-capped `gutter` spacing for narrow containers so padding does
  not crowd enlarged text. Preserve browser zoom, iOS text scaling and the
  production bundle budgets.
- Label controls; associate errors with fields. Provide keyboard access, visible
  focus, loading status, error alerts and distinguishable repeated
  links/disclosures. Focus moves only to keep it from being lost: a button its
  own action makes unavailable stays focusable through `disabledInteractive`,
  and an action that removes, moves or replaces the focused control hands focus
  through `focusHolder` to the successor its owner names (the same control, a
  neighbour's disclosure or the list's add button), else to the nearest
  surviving region heading. Results are announced, never focused or scrolled
  to; only full generation brings its progress into view once, without motion
  when reduced motion is preferred.

## Spacing

Containers own spacing with `gap`; shared primitives carry no outer margin.
`main` owns the page's side gutter, so page sections add only vertical padding.
List pages span its width, so their edges meet the header's; activity documents,
whether edited or frozen, sit in the `max-w-3xl` reading column. Use one scale
everywhere: `gap-1` between a heading and its description or a status line and
its list, `gap-3` between buttons in an action row, `gap-4` between fields and
blocks in any stack, and `gap-6` between page sections and headed groups. A
field is one block: its label sits `mb-2` above the control and help or error
lines carry `mt-2`; never place a label and its control as separate children of
a gapped container. Open disclosures space their summary from the content, and
empty live regions take no layout slot, so they never double a gap. Group card
fields with `role="group"` and a heading inside the padding rather than a
`.well` fieldset legend. Optional content renders only when present.

## Styles and theming

`frontend/src/styles.css` imports each layer from `frontend/src/styles/`:

- `theme.css` owns the colors, elevations and corner radii. Tailwind's default
  palettes and scales are cleared, so templates can only use theme values.
- `utilities.css` owns project variants: `pinned-header`, and `dark` for an
  explicit dark choice or a dark device without an explicit light choice.
- `base.css` styles elements, including native form controls and focus.
- `components.css` holds every shared visual treatment, grouped as actions
  (buttons, links, `icon-button`, `chip`), surfaces (`panel`, `well`,
  `list-row`, `empty-state`), conversation (`bubble`, `composer`), marks
  (`badge`, `icon-tile`, `line-icon`, `status-icon`, `ai-mark`, `ai-icon`),
  lists (`steps`, `progress-steps`) and feedback (`error`, `callout`).

Primitives read only tokens; templates add layout utilities and token colors
such as `text-muted`. A visual treatment used in more than one place becomes a
primitive, and primitives take state from attributes (`aria-current`,
`data-done`, `data-problem`) rather than alternative class lists. A
component's own stylesheet uses theme variables only. Give `steps` and
`progress-steps` lists `role="list"` so WebKit keeps list semantics.

A theme redefines only `--color-*` tokens and, optionally, the corner roles
(`--radius-small`, `control`, `button`, `inset`, `card`), so templates need no
`dark:` utilities. Keep the contrast contract in `theme.css` for every theme
and check each token on every surface, tint and translucent layer it meets.
The header's theme picker follows the device by default. `Theme` stores an
explicit choice in localStorage, since no server render needs a cookie. The
inline script in `index.html` applies it before first paint, and CSS follows
device changes live. The brand `theme-color` suits both themes.

## Direction and copy

Set `lang="he"`, `dir="rtl"` and Angular's `he-IL` locale; keep the manifest
aligned. Use logical spacing (`ms`, `me`, `ps`, `pe`, `inset-s`, `inset-e`) and
normal DOM order. Use `dir="auto"` for learning text and `<bdi>` for inline user
values; isolate email and numeric inputs as LTR. Bind text fields' `dir` with
`[formField]`: `FieldDirection` keeps an empty `auto` field in the page
direction, so the caret and placeholder start on the page's side. Back arrows
point right, and decorative gradients start at the reading edge. Dates display
in Hebrew with a month name and local time through the app-wide `DatePipe`
default, while stored timestamps remain UTC.

Read every size limit, count cap and the numbers in their messages from the
server's `Limits`; never hard-code them in templates or forms.

Write concise Hebrew for labels, validation, loading and errors, and never expose
raw framework or provider errors. Render generated text through interpolation,
never HTML, and never rewrite saved content for presentation. Control labels and
options come from the reviewed template; the shared settings use
application-owned labels, and only difficulty has fixed options.

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
Once a plan exists, the change conversation follows every setting, including
the advanced options. Once content exists, settings collapse to a derived
one-line summary, shown only while they are collapsed, and the content becomes
the main surface.

Authoring is a conversation: the parent's turns sit at the end edge, the
assistant's replies carry the `ai-mark` with the computed changes and
assumptions, and a typing bubble shows while a request runs. The composer sends
on Enter (Shift+Enter adds a line) and turns its send button into a stop button,
keeping keyboard focus on whichever is present; a failed or cancelled request
returns its text to the composer. `IdeaChips` offer ready wording for the first
request, common changes and scoped improvements; an idea only fills its field,
leaving focus on the idea, and never sends. Mark AI actions with `ai-icon`.

Use parent language, never internal terms: say "הגדרות" for the plan and "טקסט"
for materials; source kinds read as "כתבו עבורי תוכן חדש", "יש לי טקסט משלי"
and, for templates, "אבחר טקסט חדש בכל פעם"; length reads as approximate words
or a strict range, never modes. Keep template defaults, per-activity choices and
editable content visibly distinct. Saving a template publishes only the plan;
saving a draft retains editable work; marking ready freezes the reviewed
revision. Give each state one primary action: create the activity, then mark it
ready; template editing makes publication primary. Uncommon actions live under
**פעולות נוספות** or a quiet disclosure. Question cards keep prompt, options
with their add and remove actions, and the parent-only answer visible; type,
points, ordering and removal sit in a per-question disclosure. Every form list
edits the same way: a remove link (**הסרת**) beside each item and one secondary
add button after the list. Scoped AI improvement is a contextual action with an
optional instruction; its progress, result and any error show in that card,
while full generation reports above the content.

Show source confirmation for AI-extracted text only while it is pending and
preserve its exact content. Keep invalid keystrokes visible for correction. A
generation result cannot replace later local edits or Undo; offer the saved
server result for inspection and explicit reload. A save or deletion on another
device reads in the save state, beside the reload control when there is a
version to load. Describe operations in plain
language, keep stages, outcomes, cost and raw output behind **פרטים טכניים**,
and never imply that another paid attempt is automatic.

Display server length measurements beside saved diagnostics. Distinguish
advisory targets from strict range blockers, and technical readiness from
the parent's educational review. Offer adoption only in a callout beside content
that saved diagnostics mark as stale. Frozen previews expose answers in native
disclosures and offer an explicit copy to a new draft.

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
content, and review screen-reader behavior when changing an interaction.
