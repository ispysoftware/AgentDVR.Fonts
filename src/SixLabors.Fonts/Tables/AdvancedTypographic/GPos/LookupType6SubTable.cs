// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GPos
{
    /// <summary>
    /// Lookup Type 6: Mark-to-Mark Attachment Positioning Subtable.
    /// The MarkToMark attachment (MarkMarkPos) subtable is identical in form to the MarkToBase attachment subtable, although its function is different.
    /// MarkToMark attachment defines the position of one mark relative to another mark as when, for example,
    /// positioning tone marks with respect to vowel diacritical marks in Vietnamese.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#lookup-type-6-mark-to-mark-attachment-positioning-subtable"/>
    /// </summary>
    internal static class LookupType6SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort subTableFormat = reader.ReadUInt16();

            return subTableFormat switch
            {
                1 => LookupType6Format1SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }

        internal sealed class LookupType6Format1SubTable : LookupSubTable
        {
            private readonly CoverageTable mark1Coverage;
            private readonly CoverageTable mark2Coverage;
            private readonly MarkArrayTable mark1ArrayTable;
            private readonly Mark2ArrayTable mark2ArrayTable;

            public LookupType6Format1SubTable(
                CoverageTable mark1Coverage,
                CoverageTable mark2Coverage,
                MarkArrayTable mark1ArrayTable,
                Mark2ArrayTable mark2ArrayTable,
                LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.mark1Coverage = mark1Coverage;
                this.mark2Coverage = mark2Coverage;
                this.mark1ArrayTable = mark1ArrayTable;
                this.mark2ArrayTable = mark2ArrayTable;
            }

            public static LookupType6Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                // MarkMarkPosFormat1 Subtable.
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Type               |  Name                           | Description                                          |
                // +====================+=================================+======================================================+
                // | uint16             | posFormat                       | Format identifier: format = 1                        |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Offset16           | mark1CoverageOffset             | Offset to Combining Mark Coverage table,             |
                // |                    |                                 | from beginning of MarkMarkPos subtable.              |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Offset16           | mark2CoverageOffset             | Offset to Base Mark Coverage table,                  |
                // |                    |                                 | from beginning of MarkMarkPos subtable.              |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | uint16             | markClassCount                  | Number of Combining Mark classes defined             |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Offset16           | mark1ArrayOffset                | Offset to MarkArray table for mark1,                 |
                // |                    |                                 | from beginning of MarkMarkPos subtable.              |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Offset16           | mark2ArrayOffset                | Offset to Mark2Array table for mark2,                |
                // |                    |                                 | from beginning of MarkMarkPos subtable.              |
                // +--------------------+---------------------------------+------------------------------------------------------+
                ushort mark1CoverageOffset = reader.ReadOffset16();
                ushort mark2CoverageOffset = reader.ReadOffset16();
                ushort markClassCount = reader.ReadUInt16();
                ushort mark1ArrayOffset = reader.ReadOffset16();
                ushort mark2ArrayOffset = reader.ReadOffset16();

                var mark1Coverage = CoverageTable.Load(reader, offset + mark1CoverageOffset);
                var mark2Coverage = CoverageTable.Load(reader, offset + mark2CoverageOffset);
                var mark1ArrayTable = new MarkArrayTable(reader, offset + mark1ArrayOffset);
                var mark2ArrayTable = new Mark2ArrayTable(reader, markClassCount, offset + mark2ArrayOffset);

                return new LookupType6Format1SubTable(mark1Coverage, mark2Coverage, mark1ArrayTable, mark2ArrayTable, lookupFlags);
            }

            public override bool TryUpdatePosition(
                FontMetrics fontMetrics,
                GPosTable table,
                GlyphPositioningCollection collection,
                Tag feature,
                int index,
                int count)
            {
                // Mark to mark positioning.
                // Implements: https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#lookup-type-6-mark-to-mark-attachment-positioning-subtable
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                // Modified for Agent DVR: every index taken from the font is bounds-checked, and a NULL
                // mark2 anchor means the lookup doesn't apply.
                int mark1Index = this.mark1Coverage.CoverageIndexOf(glyphId);
                if ((uint)mark1Index >= (uint)this.mark1ArrayTable.MarkRecords.Length)
                {
                    return false;
                }

                // Get the previous mark to attach to.
                // Modified for Agent DVR (HarfBuzz): found with the lookup's own mark filtering (mark attachment
                // type or filtering set) but not its ignore flags, skipping default-ignorables - it took the raw
                // previous glyph. The ligature test below was also inverted: marks with no ligature id belong to
                // the same base; marks of the same ligature must share a component.
                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(
                    fontMetrics,
                    collection,
                    index,
                    this.LookupFlags & ~(LookupFlags.IgnoreBaseGlyphs | LookupFlags.IgnoreLigatures | LookupFlags.IgnoreMarks),
                    this.MarkFilteringSet,
                    feature,
                    index + count);
                int prevIdx = iterator.Previous();
                if (prevIdx < 0)
                {
                    return false;
                }

                ushort prevGlyphId = collection[prevIdx].GlyphId;
                GlyphShapingData prevGlyph = collection[prevIdx];
                if (!AdvancedTypographicUtils.IsMarkGlyph(fontMetrics, prevGlyphId, prevGlyph))
                {
                    return false;
                }

                bool good = false;
                GlyphShapingData curGlyph = collection[index];
                int curComponent = Math.Max(0, curGlyph.LigatureComponent);
                int prevComponent = Math.Max(0, prevGlyph.LigatureComponent);
                if (curGlyph.LigatureId == prevGlyph.LigatureId)
                {
                    // Marks belonging to the same base, or to the same ligature component.
                    good = curGlyph.LigatureId == 0 || curComponent == prevComponent;
                }
                else
                {
                    // If ligature ids don't match, it may be the case that one of the marks
                    // itself is a ligature, in which case match.
                    good = (curGlyph.LigatureId > 0 && curComponent == 0)
                        || (prevGlyph.LigatureId > 0 && prevComponent == 0);
                }

                if (!good)
                {
                    return false;
                }

                int mark2Index = this.mark2Coverage.CoverageIndexOf(prevGlyphId);
                if ((uint)mark2Index >= (uint)this.mark2ArrayTable.Mark2Records.Length)
                {
                    return false;
                }

                MarkRecord markRecord = this.mark1ArrayTable.MarkRecords[mark1Index];
                AnchorTable?[] anchors = this.mark2ArrayTable.Mark2Records[mark2Index].MarkAnchorTable;
                if ((uint)markRecord.MarkClass >= (uint)anchors.Length || anchors[markRecord.MarkClass] is not AnchorTable baseAnchor)
                {
                    return false;
                }

                AdvancedTypographicUtils.ApplyAnchor(fontMetrics, collection, index, baseAnchor, markRecord, prevIdx);

                return true;
            }
        }
    }
}
