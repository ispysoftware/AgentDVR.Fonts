// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;
using SixLabors.Fonts.Tables.Woff;

namespace SixLabors.Fonts.Tables.TrueType.Glyphs
{
    internal class GlyphTable : Table
    {
        internal const string TableName = "glyf";
        private readonly GlyphLoader[] loaders;

        public GlyphTable(GlyphLoader[] glyphLoaders)
            => this.loaders = glyphLoaders;

        public int GlyphCount => this.loaders.Length;

        // TODO: Make this non-virtual
        internal virtual GlyphVector GetGlyph(int index)
            => this.GetGlyph(index, 0);

        /// <summary>
        /// Modified for Agent DVR: bounds-checked, and composite nesting is tracked so a self-referencing
        /// or cyclic composite degrades to an empty outline instead of overflowing the stack.
        /// </summary>
        /// <param name="index">The glyph id.</param>
        /// <param name="compositeDepth">The number of composite glyphs above this one.</param>
        /// <returns>The outline, or an empty one for an out-of-range id.</returns>
        internal GlyphVector GetGlyph(int index, int compositeDepth)
        {
            if ((uint)index >= (uint)this.loaders.Length)
            {
                return GlyphVector.Empty();
            }

            GlyphLoader loader = this.loaders[index];
            return loader is CompositeGlyphLoader composite
                ? composite.CreateGlyph(this, compositeDepth)
                : loader.CreateGlyph(this);
        }

        public static GlyphTable Load(FontReader reader)
        {
            uint[] locations = reader.GetTable<IndexLocationTable>().GlyphOffsets;

            // Use an empty bounds instance as the fallback.
            // We will substitute this with the advance width/height to determine bounds instead when rendering/measuring.
            Bounds fallbackEmptyBounds = Bounds.Empty;

            using BigEndianBinaryReader binaryReader = reader.GetReaderAtTablePosition(TableName);
            return Load(binaryReader, reader.TableFormat, locations, in fallbackEmptyBounds);
        }

        public static GlyphTable Load(BigEndianBinaryReader reader, TableFormat format, uint[] locations, in Bounds fallbackEmptyBounds)
        {
            EmptyGlyphLoader empty = new(fallbackEmptyBounds);
            int entryCount = locations.Length;
            int glyphCount = entryCount - 1; // last entry is a placeholder to the end of the table
            var glyphs = new GlyphLoader[glyphCount];

            // Special case for WOFF2 format where all glyphs need to be read in one go.
            if (format is TableFormat.Woff2)
            {
                return new GlyphTable(Woff2Utils.LoadAllGlyphs(reader, empty));
            }

            for (int i = 0; i < glyphCount; i++)
            {
                if (locations[i] == locations[i + 1])
                {
                    // This is an empty glyph;
                    glyphs[i] = empty;
                }
                else
                {
                    // Move to start of glyph.
                    reader.Seek(locations[i], SeekOrigin.Begin);
                    glyphs[i] = GlyphLoader.Load(reader);
                }
            }

            return new GlyphTable(glyphs);
        }
    }
}
