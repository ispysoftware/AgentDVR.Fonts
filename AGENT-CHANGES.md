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
