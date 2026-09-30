// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.Fonts.Tables.Woff;

namespace SixLabors.Fonts.Tables.TrueType.Glyphs
{
    /// <summary>
    /// The 'glyf' table.
    /// Modified for Agent DVR: for plain sfnt fonts the raw table is kept and each glyph is decoded the
    /// first time it is requested. It used to decode every glyph when the font was first used and hold
    /// all the decoded outlines for the life of the process - thousands of glyphs (tens of thousands in
    /// CJK fonts) when text rendering touches a few dozen. A glyph that fails to decode is empty, where
    /// previously one bad glyph failed the whole font.
    /// </summary>
    internal class GlyphTable : Table
    {
        internal const string TableName = "glyf";
        private readonly GlyphLoader?[] loaders;

        // Lazy mode: the raw table and 'loca' offsets that loaders are decoded from on demand.
        private readonly byte[]? data;
        private readonly uint[]? locations;
        private readonly EmptyGlyphLoader? empty;

        public GlyphTable(GlyphLoader[] glyphLoaders)
            => this.loaders = glyphLoaders;

        private GlyphTable(byte[] data, uint[] locations, EmptyGlyphLoader empty)
        {
            this.data = data;
            this.locations = locations;
            this.empty = empty;
            this.loaders = new GlyphLoader?[Math.Max(0, locations.Length - 1)];
        }

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

            GlyphLoader loader = this.GetLoader(index);
            return loader is CompositeGlyphLoader composite
                ? composite.CreateGlyph(this, compositeDepth)
                : loader.CreateGlyph(this);
        }

        private GlyphLoader GetLoader(int index)
        {
            GlyphLoader? loader = Volatile.Read(ref this.loaders[index]);
            if (loader is not null)
            {
                return loader;
            }

            // Decode outside any lock; if two threads race, both results are equivalent and the first wins.
            loader = this.Decode(index);
            return Interlocked.CompareExchange(ref this.loaders[index], loader, null) ?? loader;
        }

        private GlyphLoader Decode(int index)
        {
            byte[] data = this.data!;
            uint start = this.locations![index];
            uint end = Math.Min(this.locations[index + 1], (uint)data.Length);
            if (end <= start)
            {
                return this.empty!;
            }

            try
            {
                using MemoryStream stream = new(data, (int)start, (int)(end - start), writable: false);
                using BigEndianBinaryReader reader = new(stream, leaveOpen: true);
                return GlyphLoader.Load(reader);
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or InvalidFontFileException or ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                // A malformed glyph renders empty rather than taking down the text it appears in.
                return this.empty!;
            }
        }

        public static GlyphTable Load(FontReader reader)
        {
            uint[] locations = reader.GetTable<IndexLocationTable>().GlyphOffsets;

            // Use an empty bounds instance as the fallback.
            // We will substitute this with the advance width/height to determine bounds instead when rendering/measuring.
            Bounds fallbackEmptyBounds = Bounds.Empty;

            if (!reader.TryGetReaderAtTablePosition(TableName, out BigEndianBinaryReader? binaryReader, out TableHeader? header))
            {
                throw new InvalidFontTableException($"Unable to find table {TableName}", TableName);
            }

            using (binaryReader)
            {
                // Plain sfnt (every installed .ttf/.otf): keep the raw table and decode glyphs on first use.
                // WOFF/WOFF2 tables arrive compressed or transformed, so they keep the eager path.
                if (reader.TableFormat is TableFormat.Otf && header.Length <= int.MaxValue)
                {
                    byte[] data = binaryReader.ReadBytes((int)header.Length);
                    return new GlyphTable(data, locations, new EmptyGlyphLoader(fallbackEmptyBounds));
                }

                return Load(binaryReader, reader.TableFormat, locations, in fallbackEmptyBounds);
            }
        }

        public static GlyphTable Load(BigEndianBinaryReader reader, TableFormat format, uint[] locations, in Bounds fallbackEmptyBounds)
        {
            EmptyGlyphLoader empty = new(fallbackEmptyBounds);
            int entryCount = locations.Length;
            int glyphCount = Math.Max(0, entryCount - 1); // last entry is a placeholder to the end of the table
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
