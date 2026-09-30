# Agent DVR fork of SixLabors.Fonts

Branch `agent`, based on upstream tag **v1.0.1** (2023-09-15), the last release under the
Apache License 2.0. Upstream relicensed to the Six Labors Split License on 2023-08-24
(commit 8c06da29); nothing from after that point is merged here, and fixes for bugs reported
upstream since then are written independently from the bug description and the OpenType spec.

Modified by iSpyConnect / The Playful Group for Agent DVR. Changes from v1.0.1:

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
