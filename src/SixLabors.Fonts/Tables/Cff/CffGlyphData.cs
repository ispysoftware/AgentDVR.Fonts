// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.Fonts.Tables.Cff
{
    internal struct CffGlyphData
    {
        private readonly byte[][] globalSubrBuffers;
        private readonly byte[][] localSubrBuffers;
        private readonly ReadOnlyMemory<byte> charStrings;
        private readonly int nominalWidthX;

        // Modified for Agent DVR: the charstring is a view into the font's CharStrings data (see
        // CffGlyphSet) rather than a per-glyph copy, and the unused per-glyph GlyphName is gone.
        public CffGlyphData(
            ushort glyphIndex,
            byte[][] globalSubrBuffers,
            byte[][] localSubrBuffers,
            int nominalWidthX,
            ReadOnlyMemory<byte> charStrings)
        {
            this.GlyphIndex = glyphIndex;
            this.globalSubrBuffers = globalSubrBuffers;
            this.localSubrBuffers = localSubrBuffers;
            this.nominalWidthX = nominalWidthX;
            this.charStrings = charStrings;
        }

        /// <summary>
        /// Modified for Agent DVR: a glyph with no charstring, for ids past the font's glyph count.
        /// </summary>
        /// <param name="glyphIndex">The requested glyph id.</param>
        /// <returns>An empty glyph.</returns>
        public static CffGlyphData Empty(ushort glyphIndex)
            => new(glyphIndex, Array.Empty<byte[]>(), Array.Empty<byte[]>(), 0, ReadOnlyMemory<byte>.Empty);

        public readonly ushort GlyphIndex { get; }

        public Bounds GetBounds()
        {
            using var engine = new CffEvaluationEngine(
                this.charStrings.Span,
                this.globalSubrBuffers,
                this.localSubrBuffers,
                this.nominalWidthX);

            return engine.GetBounds();
        }

        public void RenderTo(IGlyphRenderer renderer, Vector2 origin, Vector2 scale, Vector2 offset, Matrix3x2 transform)
        {
            using var engine = new CffEvaluationEngine(
                 this.charStrings.Span,
                 this.globalSubrBuffers,
                 this.localSubrBuffers,
                 this.nominalWidthX);

            engine.RenderTo(renderer, origin, scale, offset, transform);
        }
    }
}
