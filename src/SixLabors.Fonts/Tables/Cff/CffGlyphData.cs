// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;

namespace SixLabors.Fonts.Tables.Cff
{
    internal struct CffGlyphData
    {
        private readonly byte[][] globalSubrBuffers;
        private readonly byte[][] localSubrBuffers;
        private readonly byte[] charStrings;
        private readonly int nominalWidthX;

        public CffGlyphData(
            ushort glyphIndex,
            byte[][] globalSubrBuffers,
            byte[][] localSubrBuffers,
            int nominalWidthX,
            byte[] charStrings)
        {
            this.GlyphIndex = glyphIndex;
            this.globalSubrBuffers = globalSubrBuffers;
            this.localSubrBuffers = localSubrBuffers;
            this.nominalWidthX = nominalWidthX;
            this.charStrings = charStrings;

            this.GlyphName = null;
        }

        /// <summary>
        /// Modified for Agent DVR: a glyph with no charstring, for ids past the font's glyph count.
        /// </summary>
        /// <param name="glyphIndex">The requested glyph id.</param>
        /// <returns>An empty glyph.</returns>
        public static CffGlyphData Empty(ushort glyphIndex)
            => new(glyphIndex, System.Array.Empty<byte[]>(), System.Array.Empty<byte[]>(), 0, System.Array.Empty<byte>());

        public readonly ushort GlyphIndex { get; }

        public string? GlyphName { get; set; }

        public Bounds GetBounds()
        {
            using var engine = new CffEvaluationEngine(
                this.charStrings,
                this.globalSubrBuffers,
                this.localSubrBuffers,
                this.nominalWidthX);

            return engine.GetBounds();
        }

        public void RenderTo(IGlyphRenderer renderer, Vector2 origin, Vector2 scale, Vector2 offset, Matrix3x2 transform)
        {
            using var engine = new CffEvaluationEngine(
                 this.charStrings,
                 this.globalSubrBuffers,
                 this.localSubrBuffers,
                 this.nominalWidthX);

            engine.RenderTo(renderer, origin, scale, offset, transform);
        }
    }
}
