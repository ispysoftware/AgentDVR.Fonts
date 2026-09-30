// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GPos
{
    /// <summary>
    /// A pair adjustment positioning subtable (PairPos) is used to adjust the placement or advances of two glyphs in relation to one another —
    /// for instance, to specify kerning data for pairs of glyphs. Compared to a typical kerning table, however,
    /// a PairPos subtable offers more flexibility and precise control over glyph positioning.
    /// The PairPos subtable can adjust each glyph in a pair independently in both the X and Y directions,
    /// and it can explicitly describe the particular type of adjustment applied to each glyph.
    /// PairPos subtables can be either of two formats: one that identifies glyphs individually by index(Format 1), and one that identifies glyphs by class (Format 2).
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#lookup-type-2-pair-adjustment-positioning-subtable"/>
    /// </summary>
    internal static class LookupType2SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort posFormat = reader.ReadUInt16();

            return posFormat switch
            {
                1 => LookupType2Format1SubTable.Load(reader, offset, lookupFlags),
                2 => LookupType2Format2SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }

        /// <summary>
        /// Modified for Agent DVR: finds the second glyph of a pair - the next glyph the lookup doesn't skip.
        /// </summary>
        private static bool TryGetSecondGlyph(
            FontMetrics fontMetrics,
            GlyphPositioningCollection collection,
            LookupSubTable subTable,
            Tag feature,
            int index,
            int count,
            out int index2)
        {
            SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, subTable.LookupFlags, subTable.MarkFilteringSet, feature, index + count);
            index2 = iterator.Next();
            return index2 < iterator.Limit;
        }

        /// <summary>
        /// Modified for Agent DVR: when the second glyph was adjusted too, the next pair starts after it
        /// (HarfBuzz; otherwise it was adjusted again as the first glyph of the next pair).
        /// </summary>
        private static void ResumeAfterPair(int index2, bool hasValue2)
            => AdvancedTypographicUtils.SetResumeIndex(hasValue2 ? index2 + 1 : index2);

        internal sealed class LookupType2Format1SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly PairSetTable[] pairSets;
            private readonly bool hasValue2;

            public LookupType2Format1SubTable(CoverageTable coverageTable, PairSetTable[] pairSets, LookupFlags lookupFlags, ValueFormat valueFormat2)
                : base(lookupFlags)
            {
                this.coverageTable = coverageTable;
                this.pairSets = pairSets;
                this.hasValue2 = valueFormat2 != 0;
            }

            public static LookupType2Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                // Pair Adjustment Positioning Subtable format 1.
                // +-------------+------------------------------+------------------------------------------------+
                // | Type        |  Name                        | Description                                    |
                // +=============+==============================+================================================+
                // | uint16      | posFormat                    | Format identifier: format = 1                  |
                // +-------------+------------------------------+------------------------------------------------+
                // | Offset16    | coverageOffset               | Offset to Coverage table, from beginning of    |
                // |             |                              | PairPos subtable.                              |
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | valueFormat1                 | Defines the types of data in valueRecord1 —    |
                // |             |                              | for the first glyph in the pair (may be zero). |
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | valueFormat2                 | Defines the types of data in valueRecord2 —    |
                // |             |                              | for the second glyph in the pair (may be zero).|
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | pairSetCount                 | Number of PairSet tables                       |
                // +-------------+------------------------------+------------------------------------------------+
                // | Offset16    | pairSetOffsets[pairSetCount] | Array of offsets to PairSet tables.            |
                // |             |                              | Offsets are from beginning of PairPos subtable,|
                // |             |                              | ordered by Coverage Index.                     |
                // +-------------+------------------------------+------------------------------------------------+
                ushort coverageOffset = reader.ReadOffset16();
                ValueFormat valueFormat1 = reader.ReadUInt16<ValueFormat>();
                ValueFormat valueFormat2 = reader.ReadUInt16<ValueFormat>();
                ushort pairSetCount = reader.ReadUInt16();

                using Buffer<ushort> pairSetOffsetsBuffer = new(pairSetCount);
                Span<ushort> pairSetOffsets = pairSetOffsetsBuffer.GetSpan();
                reader.ReadUInt16Array(pairSetOffsets);

                var pairSets = new PairSetTable[pairSetCount];
                for (int i = 0; i < pairSetCount; i++)
                {
                    reader.Seek(offset + pairSetOffsets[i], SeekOrigin.Begin);
                    pairSets[i] = PairSetTable.Load(reader, offset + pairSetOffsets[i], valueFormat1, valueFormat2);
                }

                var coverageTable = CoverageTable.Load(reader, offset + coverageOffset);

                return new LookupType2Format1SubTable(coverageTable, pairSets, lookupFlags, valueFormat2);
            }

            public override bool TryUpdatePosition(
                FontMetrics fontMetrics,
                GPosTable table,
                GlyphPositioningCollection collection,
                Tag feature,
                int index,
                int count)
            {
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                // Modified for Agent DVR: bounds-checked coverage index; the second glyph is found with the
                // lookup's skipping rules (it was the raw next glyph, so a mark or LRM between the pair stopped
                // kerning, and an ignored mark could be kerned as the pair).
                int coverage = this.coverageTable.CoverageIndexOf(glyphId);
                if ((uint)coverage >= (uint)this.pairSets.Length
                    || !TryGetSecondGlyph(fontMetrics, collection, this, feature, index, count, out int index2))
                {
                    return false;
                }

                ushort glyphId2 = collection[index2].GlyphId;
                if (glyphId2 == 0 || !this.pairSets[coverage].TryGetPairValueRecord(glyphId2, out PairValueRecord pairValueRecord))
                {
                    return false;
                }

                AdvancedTypographicUtils.ApplyPosition(collection, index, pairValueRecord.ValueRecord1);
                AdvancedTypographicUtils.ApplyPosition(collection, index2, pairValueRecord.ValueRecord2);
                ResumeAfterPair(index2, this.hasValue2);
                return true;
            }

            internal sealed class PairSetTable
            {
                private readonly PairValueRecord[] pairValueRecords;
                private readonly bool sorted;

                private PairSetTable(PairValueRecord[] pairValueRecords)
                {
                    this.pairValueRecords = pairValueRecords;
                    this.sorted = true;
                    for (int i = 1; i < pairValueRecords.Length; i++)
                    {
                        if (pairValueRecords[i].SecondGlyph < pairValueRecords[i - 1].SecondGlyph)
                        {
                            this.sorted = false;
                            break;
                        }
                    }
                }

                public static PairSetTable Load(BigEndianBinaryReader reader, long offset, ValueFormat valueFormat1, ValueFormat valueFormat2)
                {
                    // +-----------------+----------------------------------+---------------------------------------+
                    // | Type            | Name                             | Description                           |
                    // +=================+==================================+=======================================+
                    // | uint16          | pairValueCount                   | Number of PairValueRecords            |
                    // +-----------------+----------------------------------+---------------------------------------+
                    // | PairValueRecord | pairValueRecords[pairValueCount] | Array of PairValueRecords, ordered by |
                    // |                 |                                  | glyph ID of the second glyph.         |
                    // +-----------------+----------------------------------+---------------------------------------+
                    reader.Seek(offset, SeekOrigin.Begin);
                    ushort pairValueCount = reader.ReadUInt16();
                    var pairValueRecords = new PairValueRecord[pairValueCount];
                    for (int i = 0; i < pairValueRecords.Length; i++)
                    {
                        pairValueRecords[i] = new PairValueRecord(reader, valueFormat1, valueFormat2);
                    }

                    return new PairSetTable(pairValueRecords);
                }

                public bool TryGetPairValueRecord(ushort glyphId, [NotNullWhen(true)] out PairValueRecord pairValueRecord)
                {
                    // Modified for Agent DVR: the records are sorted by second glyph (spec), so binary search
                    // (it scanned linearly for every pair of every kerned run). A font whose records aren't
                    // sorted is still scanned.
                    PairValueRecord[] records = this.pairValueRecords;
                    if (!this.sorted)
                    {
                        foreach (PairValueRecord pair in records)
                        {
                            if (pair.SecondGlyph == glyphId)
                            {
                                pairValueRecord = pair;
                                return true;
                            }
                        }

                        pairValueRecord = default;
                        return false;
                    }

                    int low = 0;
                    int high = records.Length - 1;
                    while (low <= high)
                    {
                        int mid = (int)((uint)(low + high) >> 1);
                        ushort second = records[mid].SecondGlyph;
                        if (second == glyphId)
                        {
                            pairValueRecord = records[mid];
                            return true;
                        }

                        if (second < glyphId)
                        {
                            low = mid + 1;
                        }
                        else
                        {
                            high = mid - 1;
                        }
                    }

                    pairValueRecord = default;
                    return false;
                }
            }
        }

        internal sealed class LookupType2Format2SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly Class1Record[] class1Records;
            private readonly ClassDefinitionTable classDefinitionTable1;
            private readonly ClassDefinitionTable classDefinitionTable2;
            private readonly bool hasValue2;

            public LookupType2Format2SubTable(
                CoverageTable coverageTable,
                Class1Record[] class1Records,
                ClassDefinitionTable classDefinitionTable1,
                ClassDefinitionTable classDefinitionTable2,
                LookupFlags lookupFlags,
                ValueFormat valueFormat2)
                : base(lookupFlags)
            {
                this.coverageTable = coverageTable;
                this.class1Records = class1Records;
                this.classDefinitionTable1 = classDefinitionTable1;
                this.classDefinitionTable2 = classDefinitionTable2;
                this.hasValue2 = valueFormat2 != 0;
            }

            public static LookupType2Format2SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                // Pair Adjustment Positioning Subtable format 2.
                // +-------------+------------------------------+------------------------------------------------+
                // | Type        |  Name                        | Description                                    |
                // +=============+==============================+================================================+
                // | uint16      | posFormat                    | Format identifier: format = 2                  |
                // +-------------+------------------------------+------------------------------------------------+
                // | Offset16    | coverageOffset               | Offset to Coverage table, from beginning of    |
                // |             |                              | PairPos subtable.                              |
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | valueFormat1                 | Defines the types of data in valueRecord1 —    |
                // |             |                              | for the first glyph in the pair (may be zero). |
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | valueFormat2                 | Defines the types of data in valueRecord2 —    |
                // |             |                              | for the second glyph in the pair (may be zero).|
                // +-------------+------------------------------+------------------------------------------------+
                // | Offset16    | classDef1Offset              | Offset to ClassDef table, from beginning of    |
                // |             |                              | PairPos subtable —                             |
                // |             |                              | for the first glyph of the pair.               |
                // +-------------+------------------------------+------------------------------------------------+
                // | Offset16    | classDef2Offset              | Offset to ClassDef table, from beginning of    |
                // |             |                              | PairPos subtable —                             |
                // |             |                              | for the second glyph of the pair. —            |
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | class1Count                  | Number of classes in classDef1 table —         |
                // |             |                              | includes Class 0.                              |
                // +-------------+------------------------------+------------------------------------------------+
                // | uint16      | class2Count                  | Number of classes in classDef2 table —         |
                // |             |                              | includes Class 0.                              |
                // +-------------+------------------------------+------------------------------------------------+
                // | Class1Record| class1Records[class1Count]   | Array of Class1 records,                       |
                // |             |                              | ordered by classes in classDef1.               |
                // +-------------+------------------------------+------------------------------------------------+
                ushort coverageOffset = reader.ReadOffset16();
                ValueFormat valueFormat1 = reader.ReadUInt16<ValueFormat>();
                ValueFormat valueFormat2 = reader.ReadUInt16<ValueFormat>();
                ushort classDef1Offset = reader.ReadOffset16();
                ushort classDef2Offset = reader.ReadOffset16();
                ushort class1Count = reader.ReadUInt16();
                ushort class2Count = reader.ReadUInt16();

                var class1Records = new Class1Record[class1Count];
                for (int i = 0; i < class1Records.Length; i++)
                {
                    class1Records[i] = Class1Record.Load(reader, class2Count, valueFormat1, valueFormat2);
                }

                var coverageTable = CoverageTable.Load(reader, offset + coverageOffset);
                var classDefTable1 = ClassDefinitionTable.Load(reader, offset + classDef1Offset);
                var classDefTable2 = ClassDefinitionTable.Load(reader, offset + classDef2Offset);

                return new LookupType2Format2SubTable(coverageTable, class1Records, classDefTable1, classDefTable2, lookupFlags, valueFormat2);
            }

            public override bool TryUpdatePosition(
                FontMetrics fontMetrics,
                GPosTable table,
                GlyphPositioningCollection collection,
                Tag feature,
                int index,
                int count)
            {
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                int coverage = this.coverageTable.CoverageIndexOf(glyphId);

                // Modified for Agent DVR: the second glyph follows the lookup's skipping rules (see format 1).
                if (coverage > -1 && TryGetSecondGlyph(fontMetrics, collection, this, feature, index, count, out int index2))
                {
                    int classDef1 = this.classDefinitionTable1.ClassIndexOf(glyphId);
                    ushort glyphId2 = collection[index2].GlyphId;
                    if (glyphId2 == 0)
                    {
                        return false;
                    }

                    int classDef2 = this.classDefinitionTable2.ClassIndexOf(glyphId2);

                    // Modified for Agent DVR: class values beyond class1Count/class2Count are malformed;
                    // ignore the pair rather than throw.
                    if ((uint)classDef1 >= (uint)this.class1Records.Length)
                    {
                        return false;
                    }

                    Class1Record class1Record = this.class1Records[classDef1];
                    if ((uint)classDef2 >= (uint)class1Record.Class2Records.Length)
                    {
                        return false;
                    }

                    Class2Record class2Record = class1Record.Class2Records[classDef2];

                    ValueRecord record1 = class2Record.ValueRecord1;
                    AdvancedTypographicUtils.ApplyPosition(collection, index, record1);

                    ValueRecord record2 = class2Record.ValueRecord2;
                    AdvancedTypographicUtils.ApplyPosition(collection, index2, record2);
                    ResumeAfterPair(index2, this.hasValue2);

                    return true;
                }

                return false;
            }
        }
    }
}
