# Writing a color theme

How to add a theme to Marqora's color theme gallery. This is for developers; people using the
app pick themes, they do not write them.

A color theme decides how Marqora colors markdown: headings, links, callouts, tables, code and
its syntax, lists, quotes, rules, highlights and diagrams. One theme is in force for the whole
app, on screen and in everything printed, exported or copied. A document's file is never
touched.

## What a theme is

One JSON file in `src/PaulTechGuy.MQ.Themes/Themes/`. The file name is the theme's id:
`vintage-press.json` is the theme `vintage-press`. Adding a theme means adding a file and
rebuilding. Nothing in the app needs editing: the gallery, `View > Color Theme` and both
theme choices in Preferences list what the folder holds.

**The order is Default first, then alphabetical by the theme's `name`, ignoring case.** Not by
id and not by file name: `sweetheart.json`, named "Sweetheart", lands between Sunset and Vintage
Press because that is where its name falls. `ThemeCatalog` sorts once, and every list in the
app reads that one list, so nothing else sorts and nothing needs telling where a new theme goes.
A test holds the order.

The files are embedded in the Themes library at build time, so they ship inside the app rather
than beside it.

Each file holds the theme's name, a one-sentence description (shown as its tooltip), and two
palettes, `light` and `dark`, each naming every slot once.

## The rules

**Every value is an opaque `#rrggbb`.** No alpha, no `rgba()`, no names. Word's shading has no
alpha and the rich-text clipboard throws `rgba()` away, so a tint is written already flattened
over its page: a callout fill that is the bar color at 10% over white is written as that blend.

**The page is neutral, and not part of the theme.** Every theme draws on the same page and
body text:

| | Page | Body text |
|---|---|---|
| Light | `#ffffff` | `#1b1b1b` |
| Dark | `#1f1f1f` | `#e6e6e6` |

These come from `webshell/app.css`, which is where the tests read them from. A theme colors
what sits on the page, never the page itself.

**Both palettes, in full.** Nothing is derived from the light palette at run time. Every output
(print, PDF, HTML, Folio, Word, the clipboard) uses the **light** palette, whatever the window
is wearing. The dark palette is for the screen only.

**Contrast.** Each text slot has a background it is read against and a minimum ratio, listed in
the table below. These are WCAG AA's bars: 4.5:1 for body-size text, 3:1 for large headings and
for a control like the task checkbox. The tests check every one in both modes, and they also
check that `diagram-primary-text` reads on the page, because mermaid draws edge labels and
sequence messages in it.

**Callouts keep their meaning.** Restyle them freely, but keep Caution in the red family and
Warning in amber or orange. This is guidance rather than a test; nothing enforces it.

**Diagrams.** A theme names all eight diagram slots, and Marqora draws mermaid diagrams in them
through mermaid's `base` theme. Default alone says `"diagrams": "stock"` instead, which keeps
mermaid's own light and dark themes and leaves the diagram slots out. A diagram that names a
mermaid theme of its own is always left as written.

**Beware pure black and white, and gray where a color belongs.** A text slot that comes out
`#000000` or `#ffffff` passes every contrast check while throwing away the theme's hue, and
so does a gray where a color should be. Both are almost always a sign that something pushed
the color as far as it would go. The tests refuse a pure black or white text slot, and a
`mark-fill` with no hue in either palette, which are the two ways this has gone wrong. Look
before accepting a palette anyway: nothing checks that a theme looks good.

## The slots

Generated from `src/PaulTechGuy.MQ.Themes/ThemeSlots.cs`, which is the only place slot ids are
written. Do not edit this table by hand; run `pwsh ./build/Update-ThemeSlotTable.ps1` after
changing the slot list.

<!-- slot-table:start -->
| Slot | Group | What it colors | Read against | At least |
|---|---|---|---|---|
| `heading-1` | Headings and rules | Level 1 heading text. | the page | 3.0:1 |
| `heading-2` | Headings and rules | Level 2 heading text. | the page | 3.0:1 |
| `heading-3` | Headings and rules | Level 3 heading text. | the page | 3.0:1 |
| `heading-4` | Headings and rules | Level 4 heading text. | the page | 4.5:1 |
| `heading-5` | Headings and rules | Level 5 heading text. | the page | 4.5:1 |
| `heading-6` | Headings and rules | Level 6 heading text, small and uppercase. | the page | 4.5:1 |
| `heading-rule` | Headings and rules | The line under a level 2 heading. | — | — |
| `rule` | Headings and rules | A horizontal rule (---). | — | — |
| `link` | Links | Link text. | the page | 4.5:1 |
| `link-underline` | Links | The quiet line under a link at rest; hover draws it in the link color. | — | — |
| `strong` | Emphasis | Bold text. | the page | 4.5:1 |
| `emphasis` | Emphasis | Italic text. | the page | 4.5:1 |
| `list-marker` | Lists | Bullets and the numbers of an ordered list. | the page | 4.5:1 |
| `task-check` | Lists | A task list checkbox. | the page | 3.0:1 |
| `footnote-ref` | Lists | The superscript footnote number in the text. | the page | 4.5:1 |
| `quote-bar` | Quotes | The bar down the left of a blockquote. | — | — |
| `quote-fill` | Quotes | Behind a blockquote's text; the page color for none. | — | — |
| `quote-text` | Quotes | A blockquote's text. | `quote-fill` | 4.5:1 |
| `callout-note-bar` | Callouts | A Note callout's bar down the left edge. | — | — |
| `callout-note-fill` | Callouts | Behind a Note callout's text. | — | — |
| `callout-note-title` | Callouts | A Note callout's title and icon. | `callout-note-fill` | 4.5:1 |
| `callout-tip-bar` | Callouts | A Tip callout's bar down the left edge. | — | — |
| `callout-tip-fill` | Callouts | Behind a Tip callout's text. | — | — |
| `callout-tip-title` | Callouts | A Tip callout's title and icon. | `callout-tip-fill` | 4.5:1 |
| `callout-important-bar` | Callouts | An Important callout's bar down the left edge. | — | — |
| `callout-important-fill` | Callouts | Behind an Important callout's text. | — | — |
| `callout-important-title` | Callouts | An Important callout's title and icon. | `callout-important-fill` | 4.5:1 |
| `callout-warning-bar` | Callouts | A Warning callout's bar down the left edge. | — | — |
| `callout-warning-fill` | Callouts | Behind a Warning callout's text. | — | — |
| `callout-warning-title` | Callouts | A Warning callout's title and icon. | `callout-warning-fill` | 4.5:1 |
| `callout-caution-bar` | Callouts | A Caution callout's bar down the left edge. | — | — |
| `callout-caution-fill` | Callouts | Behind a Caution callout's text. | — | — |
| `callout-caution-title` | Callouts | A Caution callout's title and icon. | `callout-caution-fill` | 4.5:1 |
| `table-header-fill` | Tables | Behind the header row. | — | — |
| `table-header-text` | Tables | Header row text. | `table-header-fill` | 4.5:1 |
| `table-header-rule` | Tables | The heavier line under the header row. | — | — |
| `table-border` | Tables | Cell borders. | — | — |
| `table-stripe` | Tables | Behind every other body row. | — | — |
| `code-inline-text` | Code | Inline code text. | `code-inline-fill` | 4.5:1 |
| `code-inline-fill` | Code | Behind inline code. | — | — |
| `code-inline-border` | Code | The outline around inline code. | — | — |
| `code-block-text` | Code | Code block text that no syntax color claims. | `code-block-fill` | 4.5:1 |
| `code-block-fill` | Code | Behind a code block. | — | — |
| `code-block-border` | Code | The outline around a code block. | — | — |
| `syntax-keyword` | Syntax | Keywords: if, return, class, and the language's own variables such as this. | `code-block-fill` | 4.5:1 |
| `syntax-type` | Syntax | Type names. | `code-block-fill` | 4.5:1 |
| `syntax-function` | Syntax | Function, method and class names where they are declared. | `code-block-fill` | 4.5:1 |
| `syntax-variable` | Syntax | Variables. | `code-block-fill` | 4.5:1 |
| `syntax-number` | Syntax | Numbers and literals, attributes, operators, selectors, and a markdown section. | `code-block-fill` | 4.5:1 |
| `syntax-string` | Syntax | Strings and regular expressions. | `code-block-fill` | 4.5:1 |
| `syntax-builtin` | Syntax | Built-ins and symbols, and a list bullet inside a markdown block. | `code-block-fill` | 4.5:1 |
| `syntax-comment` | Syntax | Comments. | `code-block-fill` | 4.5:1 |
| `syntax-tag` | Syntax | Tag and element names, and quotes. | `code-block-fill` | 4.5:1 |
| `syntax-punctuation` | Syntax | Punctuation. | `code-block-fill` | 4.5:1 |
| `syntax-addition` | Syntax | An added line in a diff. | `syntax-addition-fill` | 4.5:1 |
| `syntax-addition-fill` | Syntax | Behind an added line in a diff. | — | — |
| `syntax-deletion` | Syntax | A removed line in a diff. | `syntax-deletion-fill` | 4.5:1 |
| `syntax-deletion-fill` | Syntax | Behind a removed line in a diff. | — | — |
| `mark-fill` | Highlight | Behind ==highlighted== text. | body text, on this fill | 4.5:1 |
| `diagram-primary` | Diagrams | Diagram node fill. | — | — |
| `diagram-primary-border` | Diagrams | Diagram node outline. | — | — |
| `diagram-primary-text` | Diagrams | Text in a diagram node. | `diagram-primary` | 4.5:1 |
| `diagram-secondary` | Diagrams | Second node fill, which mermaid uses for alternates and some diagram kinds. | — | — |
| `diagram-tertiary` | Diagrams | Third node fill, used for clusters and backgrounds. | — | — |
| `diagram-line` | Diagrams | Diagram lines and arrows. | — | — |
| `diagram-note` | Diagrams | Diagram note fill. | — | — |
| `diagram-note-text` | Diagrams | Text in a diagram note. | `diagram-note` | 4.5:1 |
<!-- slot-table:end -->

## Drafting a theme with an AI

Paste this prompt, replacing the theme idea in the first line, and attach or paste the slot
table above.

> Generate a color theme for a markdown editor called «a theme idea, such as "an old library:
> leather, brass and green baize"». Use the slot table I have attached: each slot says what it
> colors and what it is read against. Return only JSON, exactly in the shape below, with every
> value an opaque lowercase `#rrggbb`.
>
> - The light palette is drawn on a `#ffffff` page with `#1b1b1b` body text; the dark palette on
>   `#1f1f1f` with `#e6e6e6` body text. The page is not yours to color.
> - Every text slot must reach the ratio its row gives against what it is read against, in
>   both palettes. Check them; do not guess.
> - A fill that should look tinted is written as its blend over the page, not with alpha.
> - Keep Caution in the red family and Warning in amber or orange.
> - The highlight (`mark-fill`) must be a visible color in both palettes, never a gray.
> - Choose an id in lowercase-with-hyphens, a name, and a one-sentence description.

The skeleton, generated from the slot list like the table:

<!-- skeleton:start -->
```json
{
  "schema": 1,
  "id": "your-theme-id",
  "name": "Your Theme",
  "description": "One sentence, shown as the theme's tooltip.",
  "light": {
    "heading-1": "#rrggbb",
    "heading-2": "#rrggbb",
    "heading-3": "#rrggbb",
    "heading-4": "#rrggbb",
    "heading-5": "#rrggbb",
    "heading-6": "#rrggbb",
    "heading-rule": "#rrggbb",
    "rule": "#rrggbb",
    "link": "#rrggbb",
    "link-underline": "#rrggbb",
    "strong": "#rrggbb",
    "emphasis": "#rrggbb",
    "list-marker": "#rrggbb",
    "task-check": "#rrggbb",
    "footnote-ref": "#rrggbb",
    "quote-bar": "#rrggbb",
    "quote-fill": "#rrggbb",
    "quote-text": "#rrggbb",
    "callout-note-bar": "#rrggbb",
    "callout-note-fill": "#rrggbb",
    "callout-note-title": "#rrggbb",
    "callout-tip-bar": "#rrggbb",
    "callout-tip-fill": "#rrggbb",
    "callout-tip-title": "#rrggbb",
    "callout-important-bar": "#rrggbb",
    "callout-important-fill": "#rrggbb",
    "callout-important-title": "#rrggbb",
    "callout-warning-bar": "#rrggbb",
    "callout-warning-fill": "#rrggbb",
    "callout-warning-title": "#rrggbb",
    "callout-caution-bar": "#rrggbb",
    "callout-caution-fill": "#rrggbb",
    "callout-caution-title": "#rrggbb",
    "table-header-fill": "#rrggbb",
    "table-header-text": "#rrggbb",
    "table-header-rule": "#rrggbb",
    "table-border": "#rrggbb",
    "table-stripe": "#rrggbb",
    "code-inline-text": "#rrggbb",
    "code-inline-fill": "#rrggbb",
    "code-inline-border": "#rrggbb",
    "code-block-text": "#rrggbb",
    "code-block-fill": "#rrggbb",
    "code-block-border": "#rrggbb",
    "syntax-keyword": "#rrggbb",
    "syntax-type": "#rrggbb",
    "syntax-function": "#rrggbb",
    "syntax-variable": "#rrggbb",
    "syntax-number": "#rrggbb",
    "syntax-string": "#rrggbb",
    "syntax-builtin": "#rrggbb",
    "syntax-comment": "#rrggbb",
    "syntax-tag": "#rrggbb",
    "syntax-punctuation": "#rrggbb",
    "syntax-addition": "#rrggbb",
    "syntax-addition-fill": "#rrggbb",
    "syntax-deletion": "#rrggbb",
    "syntax-deletion-fill": "#rrggbb",
    "mark-fill": "#rrggbb",
    "diagram-primary": "#rrggbb",
    "diagram-primary-border": "#rrggbb",
    "diagram-primary-text": "#rrggbb",
    "diagram-secondary": "#rrggbb",
    "diagram-tertiary": "#rrggbb",
    "diagram-line": "#rrggbb",
    "diagram-note": "#rrggbb",
    "diagram-note-text": "#rrggbb"
  },
  "dark": {
    "heading-1": "#rrggbb",
    "heading-2": "#rrggbb",
    "heading-3": "#rrggbb",
    "heading-4": "#rrggbb",
    "heading-5": "#rrggbb",
    "heading-6": "#rrggbb",
    "heading-rule": "#rrggbb",
    "rule": "#rrggbb",
    "link": "#rrggbb",
    "link-underline": "#rrggbb",
    "strong": "#rrggbb",
    "emphasis": "#rrggbb",
    "list-marker": "#rrggbb",
    "task-check": "#rrggbb",
    "footnote-ref": "#rrggbb",
    "quote-bar": "#rrggbb",
    "quote-fill": "#rrggbb",
    "quote-text": "#rrggbb",
    "callout-note-bar": "#rrggbb",
    "callout-note-fill": "#rrggbb",
    "callout-note-title": "#rrggbb",
    "callout-tip-bar": "#rrggbb",
    "callout-tip-fill": "#rrggbb",
    "callout-tip-title": "#rrggbb",
    "callout-important-bar": "#rrggbb",
    "callout-important-fill": "#rrggbb",
    "callout-important-title": "#rrggbb",
    "callout-warning-bar": "#rrggbb",
    "callout-warning-fill": "#rrggbb",
    "callout-warning-title": "#rrggbb",
    "callout-caution-bar": "#rrggbb",
    "callout-caution-fill": "#rrggbb",
    "callout-caution-title": "#rrggbb",
    "table-header-fill": "#rrggbb",
    "table-header-text": "#rrggbb",
    "table-header-rule": "#rrggbb",
    "table-border": "#rrggbb",
    "table-stripe": "#rrggbb",
    "code-inline-text": "#rrggbb",
    "code-inline-fill": "#rrggbb",
    "code-inline-border": "#rrggbb",
    "code-block-text": "#rrggbb",
    "code-block-fill": "#rrggbb",
    "code-block-border": "#rrggbb",
    "syntax-keyword": "#rrggbb",
    "syntax-type": "#rrggbb",
    "syntax-function": "#rrggbb",
    "syntax-variable": "#rrggbb",
    "syntax-number": "#rrggbb",
    "syntax-string": "#rrggbb",
    "syntax-builtin": "#rrggbb",
    "syntax-comment": "#rrggbb",
    "syntax-tag": "#rrggbb",
    "syntax-punctuation": "#rrggbb",
    "syntax-addition": "#rrggbb",
    "syntax-addition-fill": "#rrggbb",
    "syntax-deletion": "#rrggbb",
    "syntax-deletion-fill": "#rrggbb",
    "mark-fill": "#rrggbb",
    "diagram-primary": "#rrggbb",
    "diagram-primary-border": "#rrggbb",
    "diagram-primary-text": "#rrggbb",
    "diagram-secondary": "#rrggbb",
    "diagram-tertiary": "#rrggbb",
    "diagram-line": "#rrggbb",
    "diagram-note": "#rrggbb",
    "diagram-note-text": "#rrggbb"
  }
}
```
<!-- skeleton:end -->

## Adding the file

1. Save the JSON as `src/PaulTechGuy.MQ.Themes/Themes/<id>.json`. The `id` inside must match
   the file name.
2. Run the theme tests:

   ```powershell
   dotnet test tests/PaulTechGuy.MQ.Themes.Tests
   ```

   A theme that fails is reported in full: every missing slot, every value that is not
   `#rrggbb`, and every text slot that falls short, with the ratio it reached and the one it
   needs. Darken a light-palette text color, or lighten a dark-palette one, keeping its hue,
   until it passes.
3. Build, open `docs/UltimateMarkdownContent.md` (never run a formatter over it, because its
   untidiness is what it tests), and open the gallery from the palette button on the menu bar.
   Point at the new card in light mode and in dark, and scroll through the document while it is
   previewed. Check that the card sits where its name should: after Default, in alphabetical
   order.
4. Export a PDF and a Word file in the new theme and look at both. Output uses the light
   palette, so this is the light palette's real test.
5. Bring the places that show the themes up to date. The app needs nothing, but three pieces
   of documentation do:

   ```powershell
   pwsh ./build/Update-HomepageThemes.ps1        # the swatches on docs/index.html
   pwsh ./build/Update-HomepageThemes.ps1 -Check # report only; non-zero if they are stale
   ```

   The homepage cards are drawn from the theme files, in the app's order, so that is the only
   step there. Two lists of names are written by hand: the color themes section of
   `README.md`, and the color themes section of `docs/releases/vNext.md` (which also says how
   many, so that count changes with it) while a release is in progress. Find them by searching for the name of any other theme, for example
   `Select-String -Path README.md,docs/releases/vNext.md -Pattern Sunset`. Nothing else counts
   the themes: the welcome document, the homepage text and the README's feature row are written
   without a number so that a new theme does not make them wrong.

## A worked example: Pastel

Pastel is the theme this document was tested against: periwinkle, mint, blush and butter.

The difficulty is that pastel colors are pale, and pale text on a white page fails contrast
immediately. Pastel at full strength cannot be the color of the words. So the theme splits the
work:

- **The pastel lives in the panels and swatches.** Callout fills are blended at 18% rather than
  the usual 10%, the table header and code surfaces are tinted lavender, and the diagram fills
  and the gallery card's swatches carry the soft colors directly.
- **The words are the same hues, deepened.** Headings, links, callout titles and syntax colors
  start pastel and are darkened, hue kept, just far enough to reach their ratios. The heading is
  a dusky periwinkle rather than baby blue, and the result still reads as pastel because
  everything around it is.
- **Dark mode is where pastel is easy.** Light pastels on `#1f1f1f` pass contrast as they stand,
  so the dark palette is closest to the idea.

The general lesson: decide which slots carry a theme's character. The fills and bars can be as
soft or as loud as the theme wants, and the text slots then follow the same hues as far as
contrast allows.

## A second example: Sweetheart

Sweetheart, a Valentine's Day theme, is the opposite problem. Nothing about it is pale: it is
crimson, rose and blush pink, so nearly every slot wants to be some kind of red. That is easy to
make look right and easy to make unusable, and three decisions kept it usable:

- **The callouts stay distinguishable.** With red everywhere, Note, Tip, Important, Warning and
  Caution could blur into one. Note is a rose, Tip a green, Important a plum, Warning an amber
  and Caution a vivid crimson, so the hue still says what the callout means even in a palette
  that is mostly red. This is the "keep Caution red and Warning amber" rule doing its job, and
  Tip and Important are where the theme steps outside its own family.
- **The headings read as a hierarchy, not as six pinks.** Level 1 is the darkest wine, levels 2
  and 3 step toward raspberry, and levels 4 to 6 settle into muted rose-grays, so size and depth
  of color say the same thing. The seeds were chosen to clear their contrast bars as they
  stand: none of the light heading, link or emphasis colors had to be moved to pass.
- **The highlight is pink in both modes**, and the dark one is a deep rose, not a gray. A
  highlight in a pink theme is the easiest place to lose the hue: light text on a deep rose
  passes contrast, and so does light text on a neutral gray, so only the highlight-hue test
  stands between the two.

The general lesson: an occasion theme is a single hue family, and the work is keeping the
meaning-bearing slots (callouts, the diff colors, the highlight) readable apart from it.
