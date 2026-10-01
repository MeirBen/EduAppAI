# Hebrew UI

The app is **לומדים ביחד**, with Hebrew UI and RTL layout. Keep UI copy beside
its feature; identifiers and developer docs remain English. Learning content and
parent labels keep their original language and values.

## Layout and controls

- Use native HTML, document scrolling and accessible controls. The header stays
  visible on screens at least 64rem wide and 40rem tall; let it scroll away on
  smaller screens. Reserve scroll padding for focus targets and include the
  skip link.
- Use the shared light/dark theme tokens, visible focus/error states and locally
  bundled Heebo.
- Use `panel` for raised surfaces and `button` for primary actions; add
  `button-secondary` or `button-danger` for other actions. Keep one prominent
  action per card, with quieter edit/delete links. Native disclosures reveal
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
  links/disclosures.

## Styles and theming

`frontend/src/styles.css` imports each layer from `frontend/src/styles/`:

- `theme.css` owns shared theme tokens. Default Tailwind colors and shadows are
  cleared, so templates can only use theme colors and elevations.
- `utilities.css` owns project variants: `pinned-header`, and `dark` for an
  explicit dark choice or a dark device without an explicit light choice.
- `base.css` styles elements, including native form controls and focus.
- `components.css` holds small shared primitives: panels, `well`, buttons,
  links, `eyebrow`, `badge`, `icon-tile`, `steps` and field feedback.

Single-use styling stays as utilities in the owning template; a component's own
stylesheet uses theme variables only. Give `steps` lists `role="list"` so WebKit
keeps list semantics.

Themes redefine only the `--color-*` tokens, so templates need no `dark:`
utilities. Keep the contrast contract in `theme.css` for every theme and check
each token on every surface, tint and translucent layer it meets. The header's
theme picker follows the device by default. `Theme` stores an explicit choice in
localStorage, since no server render needs a cookie. The inline script in
`index.html` applies it before first paint, and CSS follows device changes live.
The brand `theme-color` suits both themes.

## Direction and copy

Set `lang="he"`, `dir="rtl"` and Angular's `he-IL` locale; keep the manifest
aligned. Use logical spacing (`ms`, `me`, `ps`, `pe`, `inset-s`, `inset-e`) and
normal DOM order. Use `dir="auto"` for learning text and `<bdi>` for inline user
values; isolate email and numeric inputs as LTR. Empty `dir="auto"` fields keep
the page direction for the caret and placeholder. Back arrows point right, and
decorative gradients start at the reading edge. Dates display in Hebrew while
stored timestamps remain UTC.

Write concise Hebrew for labels, validation, loading and errors. Do not expose
raw framework/provider errors. Render generated text through Angular
interpolation, never HTML. Control labels/options come from the reviewed
template. The shared topic, audience, difficulty and question-count controls
use application-owned labels and validation; only difficulty has fixed
options. Never rewrite saved content for presentation.

## Workspace actions

The workspace owns one editable buffer. Keep plan defaults, per-activity choices
and editable content visibly distinct. Saving a template publishes only the
plan;
saving a draft retains editable work; marking ready freezes the reviewed
revision.
Use explicit labels for these separate actions.

Show source confirmation for AI-extracted text and preserve its exact content.
Keep invalid keystrokes visible for correction. A generation result cannot
replace later local edits or Undo; offer the saved server result for inspection
and explicit reload. Show actual operation stages and unknown outcomes without
implying that another paid attempt is automatic.

Display server length measurements beside saved diagnostics. Distinguish
advisory targets from strict exact/range blockers, and technical readiness from
the parent's educational review. Frozen previews expose answers in native
disclosures and offer an explicit copy to a new draft; no child-delivery
placeholder is shown.

## Loading

Keep the shared `LoadingIndicator` mounted outside `aria-busy` containers, with
`active` bound to the request's pending state. Its empty live region exists
before
the status changes; the animation and text disappear on completion or failure.
Use `variant="panel"` for page loads and long AI calls, or the default inline
variant for shorter actions. Set `label` and optional `detail` for the
operation;
avoid invented progress percentages or generation stages.

Customize the animation through CSS properties on the component or an ancestor:
`--loader-color` (brand by default), `--loader-size` (1.5rem inline, 3rem panel)
and `--loader-duration` (1.6s). For example:

```html
<app-loading-indicator
  [active]="busy()"
  variant="panel"
  label="יוצרים את התרגול שלכם…"
  detail="זה עשוי לקחת כמה דקות."
  style="--loader-size: 3.5rem; --loader-duration: 2s"
/>
```

The decorative animation respects reduced motion; readable status remains.

## Check

Follow the [verification commands](../README.md#verify). The isolated browser
suite covers prompt-to-activity generation, independent template publication,
scoped choices, frozen previews, failure recovery and revision conflicts. Check
keyboard navigation and screenshots at 360px with
200% text, including long mixed-language content. Review screen-reader behavior
when changing an interaction.
