// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GSub
{
    /// <summary>
    /// An Alternate Substitution (AlternateSubst) subtable identifies any number of aesthetic alternatives
    /// from which a user can choose a glyph variant to replace the input glyph.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gsub#lookuptype-3-alternate-substitution-subtable"/>
    /// </summary>
    internal static class LookupType8SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort substFormat = reader.ReadUInt16();

            return substFormat switch
            {
                1 => LookupType8Format1SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }
    }

    internal sealed class LookupType8Format1SubTable : LookupSubTable
    {
        private readonly ushort[] substituteGlyphIds;
        private readonly CoverageTable coverageTable;
        private readonly CoverageTable[] backtrackCoverageTables;
        private readonly CoverageTable[] lookaheadCoverageTables;

        private LookupType8Format1SubTable(
            ushort[] substituteGlyphIds,
            CoverageTable coverageTable,
            CoverageTable[] backtrackCoverageTables,
            CoverageTable[] lookaheadCoverageTables,
            LookupFlags lookupFlags)
            : base(lookupFlags)
        {
            this.substituteGlyphIds = substituteGlyphIds;
            this.coverageTable = coverageTable;
            this.backtrackCoverageTables = backtrackCoverageTables;
            this.lookaheadCoverageTables = lookaheadCoverageTables;
        }

        public static LookupType8Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            // ReverseChainSingleSubstFormat1
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | Type     | Name                                          | Description                                  |
            // +==========+===============================================+==============================================+
            // | uint16   | substFormat                                   | Format identifier: format = 1                |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | Offset16 | coverageOffset                                | Offset to Coverage table, from beginning     |
            // |          |                                               | of substitution subtable.                    |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | uint16   | backtrackGlyphCount                           | Number of glyphs in the backtrack sequence.  |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | Offset16 | backtrackCoverageOffsets[backtrackGlyphCount] | Array of offsets to coverage tables in       |
            // |          |                                               | backtrack sequence, in glyph sequence        |
            // |          |                                               | order.                                       |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | uint16   | lookaheadGlyphCount                           | Number of glyphs in lookahead sequence.      |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | Offset16 | lookaheadCoverageOffsets[lookaheadGlyphCount] | Array of offsets to coverage tables in       |
            // |          |                                               | lookahead sequence, in glyph sequence order. |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | uint16   | glyphCount                                    | Number of glyph IDs in the                   |
            // |          |                                               | substituteGlyphIDs array.                    |
            // +----------+-----------------------------------------------+----------------------------------------------+
            // | uint16   | substituteGlyphIDs[glyphCount]                | Array of substitute glyph IDs — ordered      |
            // |          |                                               | by Coverage index.                           |
            // +----------+-----------------------------------------------+----------------------------------------------+
            ushort coverageOffset = reader.ReadOffset16();
            ushort backtrackGlyphCount = reader.ReadUInt16();

            using Buffer<ushort> backtrackCoverageOffsetsBuffer = new(backtrackGlyphCount);
            Span<ushort> backtrackCoverageOffsets = backtrackCoverageOffsetsBuffer.GetSpan();
            reader.ReadUInt16Array(backtrackCoverageOffsets);

            ushort lookaheadGlyphCount = reader.ReadUInt16();

            using Buffer<ushort> lookaheadCoverageOffsetsBuffer = new(lookaheadGlyphCount);
            Span<ushort> lookaheadCoverageOffsets = lookaheadCoverageOffsetsBuffer.GetSpan();
            reader.ReadUInt16Array(lookaheadCoverageOffsets);

            ushort glyphCount = reader.ReadUInt16();
            ushort[] substituteGlyphIds = reader.ReadUInt16Array(glyphCount);

            var coverageTable = CoverageTable.Load(reader, offset + coverageOffset);
            CoverageTable[] backtrackCoverageTables = CoverageTable.LoadArray(reader, offset, backtrackCoverageOffsets);
            CoverageTable[] lookaheadCoverageTables = CoverageTable.LoadArray(reader, offset, lookaheadCoverageOffsets);

            return new LookupType8Format1SubTable(substituteGlyphIds, coverageTable, backtrackCoverageTables, lookaheadCoverageTables, lookupFlags);
        }

        public override bool TrySubstitution(
            FontMetrics fontMetrics,
            GSubTable table,
            GlyphSubstitutionCollection collection,
            Tag feature,
            int index,
            int count)
        {
            // https://docs.microsoft.com/en-us/typography/opentype/spec/gsub#81-reverse-chaining-contextual-single-substitution-format-1-coverage-based-glyph-contexts
            ushort glyphId = collection[index].GlyphId;
            if (glyphId == 0)
            {
                return false;
            }

            // Modified for Agent DVR, per the spec: the covered glyph alone is replaced by
            // substituteGlyphIDs[coverage index]. This used to overwrite the glyphs from here onwards with
            // the whole substitute array, start the lookahead on the covered glyph itself rather than the
            // one after it, and read backtrack/lookahead glyphs past either end of the run.
            int offset = this.coverageTable.CoverageIndexOf(glyphId);
            if ((uint)offset >= (uint)this.substituteGlyphIds.Length)
            {
                return false;
            }

            if (index - this.backtrackCoverageTables.Length < 0
                || index + this.lookaheadCoverageTables.Length >= collection.Count)
            {
                return false;
            }

            for (int i = 0; i < this.backtrackCoverageTables.Length; ++i)
            {
                ushort id = collection[index - 1 - i].GlyphId;
                if (id == 0 || this.backtrackCoverageTables[i].CoverageIndexOf(id) < 0)
                {
                    return false;
                }
            }

            for (int i = 0; i < this.lookaheadCoverageTables.Length; ++i)
            {
                ushort id = collection[index + 1 + i].GlyphId;
                if (id == 0 || this.lookaheadCoverageTables[i].CoverageIndexOf(id) < 0)
                {
                    return false;
                }
            }

            collection.Replace(index, this.substituteGlyphIds[offset]);
            return true;
        }
    }
}
