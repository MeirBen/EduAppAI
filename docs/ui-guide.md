# Hebrew UI and RTL

The interface has one language: Hebrew. The product name is **לומדים ביחד**.
Application copy lives beside its feature, without a translation service or a
language switcher. Code identifiers, JSON keys and developer documentation stay in
English. Educational content and parent-authored names may contain other languages.

## Theme and layout

Tailwind CSS 4 runs through the official PostCSS plugin. Use plain CSS, not Sass.
`frontend/src/styles.css` is the single home for semantic theme tokens, base rules,
and the few shared controls: buttons, links, panels, badges, fields and errors.
Page layout belongs in template utilities. Do not add a UI framework, duplicate
component layer or configuration file to change a color or a gap.

Use `bg-canvas`, `bg-surface`, `text-ink`, `text-muted`, `text-brand` and `border-line`
instead of scattering literal colors through templates. Keep the light, restrained
green theme, visible focus rings and clear error states. Ionic supplies the shell;
its CSS is in the base layer so Tailwind utilities can override it predictably.

Heebo is bundled locally through a pinned Fontsource package with Unicode subsets
loaded as needed and `font-display: swap`. There are no external font requests. Use rem-based
text and spacing, generous Hebrew line height, and no letter spacing or uppercase
transforms. Keep Angular's existing production bundle budgets.

## Direction and mixed content

- Set `lang="he"` and `dir="rtl"` on the document. The PWA manifest has the same
  language and direction. Register Angular's Hebrew locale and use `he-IL` for pipes.
- Prefer `ms-*`, `me-*`, `ps-*`, `pe-*`, `start-*`, `end-*`, `border-s-*` and
  `text-start`. Flex and grid already follow the document direction; do not reverse
  their order just to implement RTL. Keep DOM, reading and keyboard order aligned.
- Use `dir="auto"` for authored text fields and `<bdi>` for inline user content.
  Email addresses, numeric fields and complete math expressions are explicit LTR
  islands. Keep an equation's operators and unknown in the same isolated element.
- Back arrows point right; forward arrows point left. Do not mirror books, checks,
  numerals or other nondirectional symbols.
- Use Hebrew date formatting. Keep the existing UTC API contract; formatting must
  not change stored timestamps. Avoid concatenating English status or enum values
  into visible copy.

## Copy and stored data

Write concise, natural Hebrew with neutral action labels such as “שמירת התבנית”.
Translate loading, empty, validation, error and accessible text as well as headings.
Use count labels such as “מספר השאלות” when a sentence would require plural rules.

API validation messages and newly generated instructions are Hebrew. The client
uses Hebrew status fallbacks and never displays arbitrary framework problem titles
or server-error details. Developer CLI output and internal exceptions remain English.

Wire values such as `easy`, `medium`, `hard`, `Draft` and `math-v1` stay unchanged.
`core/locale/hebrew.ts` presents built-in difficulty options, exact legacy field
labels and the original generator instruction in Hebrew. It never writes back to
the saved definition or content. Preserve arbitrary authored labels and text;
do not translate or migrate immutable snapshots in place.

## Accessibility and verification

Use native labeled controls, associated error descriptions, `aria-invalid`, visible
keyboard focus, loading status and error alerts. Navigation has a skip link. Give
repeated links and answer disclosures distinguishable accessible names. Controls
must be comfortable to touch; never disable browser zoom or clamp iOS text scaling.
The header scrolls with the page so it cannot consume the viewport on short screens
with enlarged text.

Run `./scripts/verify.sh`, then publish and run the isolated Playwright workflow as
described in the README. It checks Hebrew labels, document direction, keyboard
navigation, unchanged option values, LTR math, saved content after reload and every
screen at 360px with 200% text, plus a full action target in short landscape mode.
Inspect the screenshots in `artifacts/`, including
mixed Hebrew/Latin titles. Automated checks do not replace manual keyboard and
screen-reader review when adding a new interaction.

References: [Tailwind with Angular](https://tailwindcss.com/docs/installation/framework-guides/angular),
[Tailwind compatibility](https://tailwindcss.com/docs/compatibility),
[Angular locale formatting](https://angular.dev/guide/i18n/format-data-locale).
