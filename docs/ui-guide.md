# Hebrew UI

The app is **לומדים ביחד**, with Hebrew UI and RTL layout. Keep UI copy beside
its feature; identifiers and developer docs remain English. Learning content and
parent labels keep their original language and values.

## Layout and controls

- Use native HTML, document scrolling and accessible controls. Keep the header
  scrollable and include the skip link.
- Use Tailwind theme tokens from `frontend/src/styles.css`; shared controls live
  there and page layout uses template utilities. Keep the light green theme,
  visible focus/error states and locally bundled Heebo. Avoid another UI layer.
- Use rem sizing, generous line height and wrapping for long user content.
  Preserve browser zoom, iOS text scaling and the production bundle budgets.
- Label controls; associate errors with fields. Provide keyboard access, visible
  focus, loading status, error alerts and distinguishable repeated
  links/disclosures.

## Direction and copy

Set `lang="he"`, `dir="rtl"` and Angular's `he-IL` locale; keep the manifest
aligned. Use logical spacing (`ms`, `me`, `ps`, `pe`, `start`, `end`) and normal
DOM order. Use `dir="auto"` for learning text and `<bdi>` for inline user
values; isolate email and numeric inputs as LTR. Back arrows point right. Dates
display in Hebrew while stored timestamps remain UTC.

Write concise Hebrew for labels, validation, loading and errors. Do not expose
raw framework/provider errors. Render generated text through Angular
interpolation, never HTML. Parameter labels/options come from the reviewed
template; no reserved subject names or translation tables. Never rewrite saved
content for presentation.

## Check

Follow the [verification commands](../README.md#verify). The isolated browser
suite covers prompt/review/save, parameter choices, frozen previews, failures
and revision conflicts. Check keyboard navigation and screenshots at 360px with
200% text, including long mixed-language content. Review screen-reader behavior
when changing an interaction.
