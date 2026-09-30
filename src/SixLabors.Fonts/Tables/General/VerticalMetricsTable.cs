// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.Fonts.Tables.General
{
    internal sealed class VerticalMetricsTable : Table
    {
        internal const string TableName = "vmtx";
        private readonly short[] topSideBearings;
        private readonly ushort[] advancedHeights;

        public VerticalMetricsTable(ushort[] advancedHeights, short[] topSideBearings)
        {
            this.advancedHeights = advancedHeights;
            this.topSideBearings = topSideBearings;
        }

        // Modified for Agent DVR: glyphs past numberOfVMetrics take the LAST record's advance, per the spec
        // (it returned glyph 0's); ids past the glyph count get 0 instead of throwing.
        public ushort GetAdvancedHeight(int glyphIndex)
        {
            if ((uint)glyphIndex >= (uint)this.topSideBearings.Length || this.advancedHeights.Length == 0)
            {
                return 0;
            }

            return glyphIndex < this.advancedHeights.Length
                ? this.advancedHeights[glyphIndex]
                : this.advancedHeights[this.advancedHeights.Length - 1];
        }

        internal short GetTopSideBearing(int glyphIndex)
            => (uint)glyphIndex < (uint)this.topSideBearings.Length ? this.topSideBearings[glyphIndex] : (short)0;

        public static VerticalMetricsTable? Load(FontReader reader)
        {
            // You should load all dependent tables prior to manipulating the reader
            VerticalHeadTable headTable = reader.GetTable<VerticalHeadTable>();
            MaximumProfileTable profileTable = reader.GetTable<MaximumProfileTable>();

            // Move to start of table
            if (!reader.TryGetReaderAtTablePosition(TableName, out BigEndianBinaryReader? binaryReader))
            {
                return null;
            }

            // Modified for Agent DVR: dispose the reader (it was leaked on every font load).
            using (binaryReader)
            {
                return Load(binaryReader, headTable.NumberOfVMetrics, profileTable.GlyphCount);
            }
        }

        public static VerticalMetricsTable Load(BigEndianBinaryReader reader, int metricCount, int glyphCount)
        {
            // Type           | Name                                          | Description
            // longVerMetric  | vMetrics[numberOfVMetrics]                    | Paired advance height and top side bearing values for each glyph. Records are indexed by glyph ID.
            // int16          | leftSideBearing[numGlyphs - numberOfVMetrics] | Top side bearings for glyph IDs greater than or equal to numberOfVMetrics.
            // Modified for Agent DVR: tolerate numberOfVMetrics > numGlyphs (malformed) instead of overrunning.
            int bearingCount = Math.Max(0, glyphCount - metricCount);
            ushort[] advancedHeights = new ushort[metricCount];
            short[] topSideBearings = new short[Math.Max(glyphCount, metricCount)];

            for (int i = 0; i < metricCount; i++)
            {
                // longVerMetric Record:
                // Type   | Name          | Description
                // -------| ------------- | -----------------------------------------------------------
                // uint16 | advanceHeight | The advance height of the glyph.Signed integer in FUnits.
                // int16  | topSideBearing| The top side bearing of the glyph. Signed integer in FUnits
                advancedHeights[i] = reader.ReadUInt16();
                topSideBearings[i] = reader.ReadInt16();
            }

            for (int i = 0; i < bearingCount; i++)
            {
                topSideBearings[metricCount + i] = reader.ReadInt16();
            }

            return new VerticalMetricsTable(advancedHeights, topSideBearings);
        }
    }
}
