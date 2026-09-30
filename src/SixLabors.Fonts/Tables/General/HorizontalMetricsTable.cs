// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.Fonts.Tables.General
{
    internal sealed class HorizontalMetricsTable : Table
    {
        internal const string TableName = "hmtx";
        private readonly short[] leftSideBearings;
        private readonly ushort[] advancedWidths;

        public HorizontalMetricsTable(ushort[] advancedWidths, short[] leftSideBearings)
        {
            this.advancedWidths = advancedWidths;
            this.leftSideBearings = leftSideBearings;
        }

        // Modified for Agent DVR: glyphs past numberOfHMetrics take the LAST record's advance (the spec's
        // monospaced-run optimisation, common in CJK and pan-Unicode fonts); it used to return glyph 0's
        // (.notdef) advance. Ids past the glyph count have no metrics and get 0 instead of throwing.
        public ushort GetAdvancedWidth(int glyphIndex)
        {
            if ((uint)glyphIndex >= (uint)this.leftSideBearings.Length || this.advancedWidths.Length == 0)
            {
                return 0;
            }

            return glyphIndex < this.advancedWidths.Length
                ? this.advancedWidths[glyphIndex]
                : this.advancedWidths[this.advancedWidths.Length - 1];
        }

        internal short GetLeftSideBearing(int glyphIndex)
            => (uint)glyphIndex < (uint)this.leftSideBearings.Length ? this.leftSideBearings[glyphIndex] : (short)0;

        public static HorizontalMetricsTable Load(FontReader reader)
        {
            // you should load all dependent tables prior to manipulating the reader
            HorizontalHeadTable headTable = reader.GetTable<HorizontalHeadTable>();
            MaximumProfileTable profileTable = reader.GetTable<MaximumProfileTable>();

            // Move to start of table
            using BigEndianBinaryReader binaryReader = reader.GetReaderAtTablePosition(TableName);
            return Load(binaryReader, headTable.NumberOfHMetrics, profileTable.GlyphCount);
        }

        public static HorizontalMetricsTable Load(BigEndianBinaryReader reader, int metricCount, int glyphCount)
        {
            // Type           | Name                                          | Description
            // longHorMetric  | hMetrics[numberOfHMetrics]                    | Paired advance width and left side bearing values for each glyph. Records are indexed by glyph ID.
            // int16          | leftSideBearing[numGlyphs - numberOfHMetrics] | Left side bearings for glyph IDs greater than or equal to numberOfHMetrics.
            // Modified for Agent DVR: tolerate numberOfHMetrics > numGlyphs (malformed) instead of overrunning.
            int bearingCount = Math.Max(0, glyphCount - metricCount);
            ushort[] advancedWidth = new ushort[metricCount];
            short[] leftSideBearings = new short[Math.Max(glyphCount, metricCount)];

            for (int i = 0; i < metricCount; i++)
            {
                // longHorMetric Record:
                // Type   | Name         | Description
                // uint16 | advanceWidth | Glyph advance width, in font design units.
                // int16  | lsb          | Glyph left side bearing, in font design units.
                advancedWidth[i] = reader.ReadUInt16();
                leftSideBearings[i] = reader.ReadInt16();
            }

            for (int i = 0; i < bearingCount; i++)
            {
                leftSideBearings[metricCount + i] = reader.ReadInt16();
            }

            return new HorizontalMetricsTable(advancedWidth, leftSideBearings);
        }
    }
}
