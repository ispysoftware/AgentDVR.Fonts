// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic
{
    /// <summary>
    /// The GDEF mark glyph sets: coverage tables a lookup can name (UseMarkFilteringSet) to process only
    /// the marks they contain.
    /// <see href="https://learn.microsoft.com/en-us/typography/opentype/spec/gdef#mark-glyph-sets-table"/>
    /// Modified for Agent DVR: the coverages are loaded and queried. The offsets were read as 16-bit (the
    /// spec says Offset32) and never loaded, so mark filtering sets were ignored.
    /// </summary>
    internal sealed class MarkGlyphSetsTable
    {
        private readonly CoverageTable?[] coverageTables;

        private MarkGlyphSetsTable(CoverageTable?[] coverageTables)
            => this.coverageTables = coverageTables;

        public static MarkGlyphSetsTable Load(BigEndianBinaryReader reader, long offset)
        {
            // +----------+-----------------------------------+------------------------------------------------+
            // | Type     | Name                              | Description                                    |
            // +==========+===================================+================================================+
            // | uint16   | format                            | Format identifier == 1                         |
            // +----------+-----------------------------------+------------------------------------------------+
            // | uint16   | markGlyphSetCount                 | Number of mark glyph sets defined              |
            // +----------+-----------------------------------+------------------------------------------------+
            // | Offset32 | coverageOffsets[markGlyphSetCount]| Array of offsets to mark glyph set coverage    |
            // |          |                                   | tables, from the start of this table.          |
            // +----------+-----------------------------------+------------------------------------------------+
            reader.Seek(offset, SeekOrigin.Begin);
            ushort format = reader.ReadUInt16();
            if (format != 1)
            {
                return new MarkGlyphSetsTable(Array.Empty<CoverageTable?>());
            }

            ushort markGlyphSetCount = reader.ReadUInt16();
            uint[] coverageOffsets = new uint[markGlyphSetCount];
            for (int i = 0; i < coverageOffsets.Length; i++)
            {
                coverageOffsets[i] = reader.ReadOffset32();
            }

            var coverageTables = new CoverageTable?[markGlyphSetCount];
            for (int i = 0; i < coverageTables.Length; i++)
            {
                coverageTables[i] = coverageOffsets[i] == 0 ? null : CoverageTable.Load(reader, offset + coverageOffsets[i]);
            }

            return new MarkGlyphSetsTable(coverageTables);
        }

        /// <summary>
        /// Gets a value indicating whether the given mark glyph set contains the glyph. A set the font
        /// doesn't define contains nothing.
        /// </summary>
        /// <param name="markGlyphSet">The mark glyph set index.</param>
        /// <param name="glyphId">The glyph id.</param>
        /// <returns><see langword="true"/> if the set covers the glyph.</returns>
        public bool Covers(ushort markGlyphSet, ushort glyphId)
            => markGlyphSet < this.coverageTables.Length
            && this.coverageTables[markGlyphSet] is CoverageTable coverage
            && coverage.CoverageIndexOf(glyphId) >= 0;
    }
}
