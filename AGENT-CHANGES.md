# AgentDVR.Fonts - Agent DVR fork of SixLabors.Fonts

Branch `agent`, based on upstream tag **v1.0.1** (2023-09-15), the last release under the
Apache License 2.0. Upstream relicensed to the Six Labors Split License on 2023-08-24
(commit 8c06da29); nothing from after that point is merged or copied here. Bugs found upstream
since then are fixed in our own code, written against the OpenType spec.

Modified by iSpyConnect / The Playful Group for Agent DVR. Changes from v1.0.1:

- Renamed: the assembly and project are `AgentDVR.Fonts` so a modified build doesn't ship under Six
  Labors' name, and the README is our own. C# namespaces stay `SixLabors.Fonts` to keep the code
  comparable with its origin. Not affiliated with or endorsed by Six Labors.

- Build: self-contained net10.0 project. The `shared-infrastructure` submodule and upstream's
  global build props (StyleCop, strong naming, artifacts output, extra restore feeds) are
  removed; the seven SharedInfrastructure sources are copied into
  `src/SixLabors.Fonts/SharedInfrastructure`.
- `TextLayout.cs`: line gap no longer counted in the content area when centring the baseline
  in the 1em line box (fonts with a large line gap, e.g. Tekton Pro, rendered too high).
- Glyph metrics are cached per glyph id but carried the first code point that used the glyph,
  so whitespace/newline/render-skip decisions could come from a different character (every
  unmapped character shares glyph 0). The per-layout clone now takes the real code point; the
  cache stays one entry per glyph. `ArrayBuilder(int)` no longer starts "full" of defaults.
- Malformed fonts can no longer overflow the stack (which kills the process): composite glyphs
  are limited to the TrueType maximum nesting of 16 and CFF subroutine calls to the Type 2 limit
  of 10, and glyph ids and subroutine indexes are bounds-checked. Over-limit parts render empty.
- Empty `glyf` entries (spaces etc.) get empty bounds instead of glyph 0's (.notdef) bounds;
  the shared re-entrancy flag that tried to protect that lookup was not thread-safe.
- GSUB/GPOS/GDEF no longer throw on real-world fonts (Noto Sans, Noto Sans Arabic/Malayalam/
  Gurmukhi, BNazanin, SofiaSans): unknown coverage/class-definition/anchor formats load as
  empty/class 0/origin (HarfBuzz behaviour); newer GDEF minor versions are read as the fields
  they contain; every coverage, class, mark-class and ligature-component index is bounds-checked
  against its array; NULL anchors and NULL rule sets (both legal) mean "doesn't apply", and
  format-1 context rule sets with a NULL offset are no longer parsed from the subtable header.
- GSUB lookup type 8 (reverse chaining single substitution) follows the spec: only the covered
  glyph is replaced, by `substituteGlyphIDs[coverage index]`; lookahead starts after it; context
  reads stay inside the run. It previously overwrote following glyphs with the whole array.
- System font enumeration walks the font directories itself: an unreadable folder or a symlink
  loop is skipped instead of failing `SystemFonts` for the whole process (the lazy AllDirectories
  enumeration threw outside the per-font try/catch). `.otc` collections are included.
- `hmtx`/`vmtx`: glyphs past numberOfH/VMetrics take the last record's advance, per the spec (they
  got glyph 0's - wrong widths throughout CJK and pan-Unicode fonts); ids past the glyph count get
  0; a metric count above the glyph count no longer overruns. The `vmtx` reader is disposed.
- Legacy `kern`: the pair value goes on the left glyph's advance (it went on the right glyph, so
  the gap moved one glyph late); unsupported subtable formats are skipped by their length instead
  of desynchronising every later subtable; Apple version-1 tables are explicitly ignored.
- cmap format 4: binary search over the (sorted) segments; idDelta applied in the idRangeOffset
  branch; the glyph array index is bounds-checked; glyph 0 is reported as not found.
- CFF: 16.16 fixed operands are read as one signed 32-bit value (the fraction word was read as
  signed on its own, putting outlines off by a unit that accumulated); a charstring that draws
  nothing has empty bounds (the unset float seeds cast to short - +/-32767 on .NET 11); glyph ids
  past the glyph count are empty glyphs; a font with no CFF table fails at load, not at draw time.
- Default-ignorable characters (LRM/RLM and other bidi controls, ZWJ/ZWNJ/ZWSP, variation
  selectors, BOM, soft hyphen) take no space after positioning, as in HarfBuzz; they were already
  not drawn but kept their glyph's advance, leaving gaps (e.g. in .NET right-to-left date strings).
  They still take part in substitution and positioning, and a glyph a lookup substituted keeps its
  advance.
- cmap: characters map through the one subtable the font intends (Windows symbol, then full
  Unicode, then BMP Unicode, Macintosh Roman last) instead of the first subtable that answers;
  symbol fonts also try U+F000+code; reported coverage follows the same subtable. Formats 6, 10
  and 13 are read (new `TrimmedArraySubTable`; format 13 shares format 12's class). Format 12/13
  groups are binary searched, their count is capped by the subtable length, and ids 0 or above
  65535 are "not found".
- Universal Shaping Engine: the reph check read the glyph at the run-relative match index without
  the run offset (wrong glyph, or a crash, for runs not starting at 0).
- Lazy `glyf` decoding (performance; not an upstream change): for plain sfnt fonts the raw table is
  kept and each glyph is decoded on first request, published lock-free. Previously every glyph was
  decoded when the font was first used and all outlines were held for the life of the process. A
  glyph that fails to decode is now empty instead of failing the whole font. WOFF/WOFF2 keep the
  eager path.
- CFF glyphs on request (performance; not an upstream change): the CharStrings INDEX is read as one
  block and glyphs are views into it (new `CffGlyphSet`), instead of a per-glyph charstring copy and
  a `CffGlyphData` built for every glyph at load. The unused glyph names (charset) are no longer read
  - that reader misread predefined charsets and threw on unknown formats. FDSelect is a flat
  per-glyph table (`FDRangeProvider` and the per-glyph Dictionary are gone), which also fixes CID
  fonts using FDSelect format 0: they weren't recognised as CID, so glyphs lost their local
  subroutines.
- Fallback fonts (`TextOptions.FallbackFontFamilies`):
  - Replacing a missing glyph removed the placeholder only if the first glyph shaped at that offset
    was found, and inserted replacements at i, i+1, i+3... (a cumulative index); now the placeholder
    goes with the first replacement and replacements are consecutive.
  - The Indic, Hangul and Universal shapers looked glyphs up (dotted circle, decompositions,
    zero-width checks) in the text run's primary font; they now use the font being shaped.
  - A script-specific shaper only runs where the font has glyphs for that script's characters, so
    the primary font no longer reorders or inserts dotted circles into runs it can't render.
    (Upstream keys this on the script list, which would also drop joining in Arabic fonts that file
    features under DFLT.)
  - GPOS runs end where the font changes and legacy `kern` only pairs a font's own glyphs, so one
    font's lookups are never matched against another font's glyph ids.
  - Positioning ran once per text run, so a font used by several runs (or listed both as the main
    font and as a fallback) had its GPOS applied twice - kerning and mark offsets doubled. It also
    ran for every fallback family, loading each fallback font even when no glyph needed it. Each
    font is now positioned once, and only the fonts actually tried.
- Colour (COLR) glyphs: every layer was flagged as the start of the line, so a line beginning with
  an emoji measured one line-height per layer. Only the first glyph of a line is flagged now.
- Nested lookups (GSUB 5/6, GPOS 7/8) are depth-limited to 64 (HarfBuzz's limit) and each nested
  call spends from the pass's operation budget: lookups that call each other in a cycle recursed
  until the stack overflowed, which kills the process. Feature and lookup indices from the font,
  and nested lookup positions, are bounds-checked (out-of-range ones are skipped instead of
  throwing).
- Text with nothing to lay out (e.g. only U+FE0F) returns an empty layout instead of throwing; so
  do empty lines, including a line left empty by trimming whitespace before a wrap. Line metrics
  are recalculated in one pass rather than four LINQ passes.
- `glyf`: a simple glyph whose contour end points don't strictly increase is empty, not an outline
  that indexes past its points.
- cmap format 14: repeated selector records or mappings keep the first instead of failing the
  font; the selector-record count is capped by the subtable length; default-UVS ranges include
  their last code point.
- Contextual matching (GSUB 5/6, GPOS 7/8, ligatures, pair/cursive/mark searches) rewritten on one
  context-aware iterator (`SkippingGlyphIterator.ForContext`) with struct matchers instead of
  allocated delegates:
  - Backtrack sequences are matched nearest-first, as the spec stores them. They were compared
    farthest-first, so any rule with two or more backtrack glyphs misfired (Latin calt/liga included).
  - Default-ignorables (LRM/RLM, variation selectors, ...) are stepped over unless a rule matches
    them, as in HarfBuzz; ZWNJ blocks GSUB matching; ZWJ is transparent except for features that
    handle joiners (Indic/USE basic features, rlig, rclt); GPOS steps over both. CGJ, Mongolian FVS
    and TAG characters stay visible to GSUB. They previously broke ligatures and kerning.
  - GDEF mark glyph sets are loaded (offsets are 32-bit) and UseMarkFilteringSet is honoured.
  - Forward matching stops at the end of the run; in GPOS another font's glyphs end the context.
  - Nested lookups apply at the glyphs actually matched for each sequence index, with positions
    corrected as nested lookups add or remove glyphs (HarfBuzz's scheme), instead of a re-count that
    disagreed with the matching.
  - A matched context finishes the lookup at that glyph even if nothing changed (later subtables
    still ran), and the lookup continues after the matched input rather than re-running over it.
    Ligatures, multiple substitutions and pairs whose second glyph was adjusted likewise continue
    after what they consumed or produced; a deleted glyph no longer makes the next one be skipped.
- Lookup application: consecutive shaping stages without pre/post actions are applied as one pass -
  their lookups merged, each applied once, in lookup-list order - as the spec and HarfBuzz do; all
  GPOS features are one pass. A lookup shared by two features (Indic/USE 'dist' and 'kern') was
  applied twice, and lookups interleaved across features ran out of order. Stages that must run on
  their own are marked `standalone` (rvrn, the Arabic joining forms, the Indic basic features).
  Each lookup's walk starts at the first glyph it doesn't ignore and uses its mark filtering set.
- Script/language system selection follows HarfBuzz: the text's script tags, then DFLT, dflt, latn,
  else no features (it fell back to the font's first script, e.g. arab lookups on Latin text); a
  'dflt' LangSys record, then the default LangSys, else none (with no default it mixed every
  language's features together).
- Ligatures (GSUB 4): an ordinary ligature gets a new ligature id and the marks skipped between
  its components, and those following its last component, are renumbered to the new components; a
  mark or base-plus-marks ligature keeps its first glyph's id and component. This was inverted
  (ids allocated for mark ligatures, 0 for real ones; skipped marks renumbered in the wrong case).
  Component counts are tracked per glyph (`LigatureComponentCount`) rather than taken from the code
  point count, and glyphs attached to different components of an earlier ligature don't ligate
  unless that ligature is ignored by the lookup. A single substitution keeps the glyph's ligature id
  and component; a multiple substitution numbers its output only outside an existing ligature.
- Mark positioning: mark-to-base and mark-to-ligature find their base with the skipping iterator
  (they walked raw indices); of a multiple-substitution sequence only the first glyph takes marks
  unless a mark sits inside it (it skipped every glyph with a ligature component). Mark-to-mark finds
  the previous mark with the lookup's own filtering (it took the raw previous glyph) and its
  ligature test is the right way round. Mark-to-ligature uses the font's component count.
  `IsMarkGlyph` agrees with the glyph class (it returned false for every glyph in fonts without GDEF
  classes, so mark searches and mark-advance zeroing ignored combining marks there). Without GDEF
  classes only non-spacing marks count as marks, as HarfBuzz synthesizes them - spacing marks kept
  getting their advance zeroed.
- Cursive attachment: the current glyph's entry anchor joins the exit anchor of the previous glyph
  the lookup sees (it used the raw next glyph, so a mark between letters broke the join); the
  RightToLeft flag's choice of which glyph stays on the baseline was inverted; "no attachment" is 0
  (it was -1, also a real link to the previous glyph); separating a reversed pair clears its offset.
  Attachment offsets (cursive and mark) are propagated recursively, parent first, with a depth cap -
  chains longer than two got partial offsets, and a bad index comparison abandoned the rest of the run.
  Negative advances are clamped to 0 instead of wrapping to ~65535.
- Pair positioning (GPOS 2): the second glyph is found with the lookup's skipping rules (it was the
  raw next glyph, in both formats); pair sets are binary searched (records are sorted by second
  glyph; unsorted fonts are still scanned).
- Indic/USE broken clusters: the dotted circle goes before the first non-Repha glyph with its own code
  point and category (the Repha search lagged a glyph, so it duplicated or overwrote one; the circle
  went after the broken character and inherited its code point and category).
- `TextLayout.BreakLines`: removed the "negative top side bearing" ascender adjustment, which lowered
  the baseline of lines containing a glyph taller than the ascender (stacked Vietnamese/Thai marks,
  some symbols) without growing the measured height, and read the vertical bearing when a font has
  vmtx. Upstream removed it too.
- Fallback fonts: a glyph taken from a fallback font got both its horizontal and vertical advance as
  its positioning bounds (the primary font path sets only the one for the layout direction). Mark
  attachment subtracts the advances between base and mark, so in horizontal text every mark from a
  fallback font - Devanagari vowel signs, virama, reph, anusvara - was pushed a whole line down and
  out of the text box.
- Performance pass (not upstream changes; output is pixel-identical to before):
  - Build: `LangVersion latest`; the netstandard2.0/netcoreapp polyfills (`HashCode`, `MathF`,
    nullable attributes, stream/encoding extensions) and the `SUPPORTS_*` conditional code are gone.
  - Layout: fallback `Font` objects are cached per family/size/style instead of built per call; a
    fallback font whose cmap covers none of the still-missing glyphs is skipped before shaping. Glyph
    lookups by code point offset are binary searches. Lines lay out straight into one pre-sized list;
    all-LTR lines skip building bidi runs. Per-glyph enumerator, closure and temporary-list
    allocations are removed from positioning.
  - Shaping: the lookups selected for a LangSys + feature set are cached (they were re-collected and
    sorted for every run); GDEF glyph class and mark-attachment class are cached per glyph id; the
    Universal shaper's category/decomposition tables are built once, not per access; shaping stages
    are a small list instead of a HashSet; a copied glyph no longer allocates its feature list twice.
  - Rendering: unhinted TrueType glyphs stream their points through one transform instead of
    building a scaled copy of the outline per size; the per-layout glyph clone shares the outline.
  - `TextMeasurer.MeasureAdvanceAndBounds`: both measurements from one layout.
