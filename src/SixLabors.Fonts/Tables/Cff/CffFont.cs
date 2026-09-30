// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.Fonts.Tables.Cff
{
    internal class CffFont
    {
        // Modified for Agent DVR: glyphs are a CffGlyphSet (created on request) instead of an array.
        public CffFont(string name, CffTopDictionary metrics, CffGlyphSet glyphs)
        {
            this.FontName = name;
            this.Metrics = metrics;
            this.Glyphs = glyphs;
        }

        public string FontName { get; set; }

        public CffTopDictionary Metrics { get; set; }

        public CffGlyphSet Glyphs { get; }
    }
}
