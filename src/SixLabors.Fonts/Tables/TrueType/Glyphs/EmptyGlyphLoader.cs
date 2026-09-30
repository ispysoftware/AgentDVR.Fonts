// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.Fonts.Tables.TrueType.Glyphs
{
    internal class EmptyGlyphLoader : GlyphLoader
    {
        private readonly GlyphVector glyph;

        public EmptyGlyphLoader(Bounds fallbackEmptyBounds)
            => this.glyph = GlyphVector.Empty(fallbackEmptyBounds);

        // Modified for Agent DVR: a zero-length glyf entry has no ink, so it gets empty bounds (measuring
        // falls back to the advance). It used to borrow glyph 0's bounds, which gave spaces and other
        // empty glyphs the .notdef box, behind a shared re-entrancy flag that wasn't thread-safe.
        public override GlyphVector CreateGlyph(GlyphTable table) => this.glyph;
    }
}
