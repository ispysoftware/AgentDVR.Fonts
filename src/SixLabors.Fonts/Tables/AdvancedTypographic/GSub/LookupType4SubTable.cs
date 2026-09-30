// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GSub
{
    /// <summary>
    /// A Ligature Substitution (LigatureSubst) subtable identifies ligature substitutions where a single glyph replaces multiple glyphs.
    /// One LigatureSubst subtable can specify any number of ligature substitutions.
    /// The subtable has one format: LigatureSubstFormat1.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gsub#lookuptype-4-ligature-substitution-subtable"/>
    /// </summary>
    internal static class LookupType4SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort substFormat = reader.ReadUInt16();

            return substFormat switch
            {
                1 => LookupType4Format1SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }
    }

    internal sealed class LookupType4Format1SubTable : LookupSubTable
    {
        private readonly LigatureSetTable[] ligatureSetTables;
        private readonly CoverageTable coverageTable;

        private LookupType4Format1SubTable(LigatureSetTable[] ligatureSetTables, CoverageTable coverageTable, LookupFlags lookupFlags)
            : base(lookupFlags)
        {
            this.ligatureSetTables = ligatureSetTables;
            this.coverageTable = coverageTable;
        }

        public static LookupType4Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            // Ligature Substitution Format 1
            // +----------+--------------------------------------+--------------------------------------------------------------------+
            // | Type     | Name                                 | Description                                                        |
            // +==========+======================================+====================================================================+
            // | uint16   | substFormat                          | Format identifier: format = 1                                      |
            // +----------+--------------------------------------+--------------------------------------------------------------------+
            // | Offset16 | coverageOffset                       | Offset to Coverage table, from beginning of substitution           |
            // |          |                                      | subtable                                                           |
            // +----------+--------------------------------------+--------------------------------------------------------------------+
            // | uint16   | ligatureSetCount                     | Number of LigatureSet tables                                       |
            // +----------+--------------------------------------+--------------------------------------------------------------------+
            // | Offset16 | ligatureSetOffsets[ligatureSetCount] | Array of offsets to LigatureSet tables. Offsets are from beginning |
            // |          |                                      | of substitution subtable, ordered by Coverage index                |
            // +----------+--------------------------------------+--------------------------------------------------------------------+
            ushort coverageOffset = reader.ReadOffset16();
            ushort ligatureSetCount = reader.ReadUInt16();

            using Buffer<ushort> ligatureSetOffsetsBuffer = new(ligatureSetCount);
            Span<ushort> ligatureSetOffsets = ligatureSetOffsetsBuffer.GetSpan();
            reader.ReadUInt16Array(ligatureSetOffsets);

            var ligatureSetTables = new LigatureSetTable[ligatureSetCount];
            for (int i = 0; i < ligatureSetTables.Length; i++)
            {
                // LigatureSet Table
                // +----------+--------------------------------+--------------------------------------------------------------------+
                // | Type     | Name                           | Description                                                        |
                // +==========+================================+====================================================================+
                // | uint16   | ligatureCount                  | Number of Ligature tables                                          |
                // +----------+--------------------------------+--------------------------------------------------------------------+
                // | Offset16 | ligatureOffsets[LigatureCount] | Array of offsets to Ligature tables. Offsets are from beginning of |
                // |          |                                | LigatureSet table, ordered by preference.                          |
                // +----------+--------------------------------+--------------------------------------------------------------------+
                long ligatureSetOffset = offset + ligatureSetOffsets[i];
                reader.Seek(ligatureSetOffset, SeekOrigin.Begin);
                ushort ligatureCount = reader.ReadUInt16();

                using Buffer<ushort> ligatureOffsetsBuffer = new(ligatureCount);
                Span<ushort> ligatureOffsets = ligatureOffsetsBuffer.GetSpan();
                reader.ReadUInt16Array(ligatureOffsets);

                var ligatureTables = new LigatureTable[ligatureCount];

                // Ligature Table
                // +--------+---------------------------------------+------------------------------------------------------+
                // | Type   | Name                                  | Description                                          |
                // +========+=======================================+======================================================+
                // | uint16 | ligatureGlyph                         | glyph ID of ligature to substitute                   |
                // +--------+---------------------------------------+------------------------------------------------------+
                // | uint16 | componentCount                        | Number of components in the ligature                 |
                // +--------+---------------------------------------+------------------------------------------------------+
                // | uint16 | componentGlyphIDs[componentCount - 1] | Array of component glyph IDs — start with the second |
                // |        |                                       | component, ordered in writing direction              |
                // +--------+---------------------------------------+------------------------------------------------------+
                for (int j = 0; j < ligatureTables.Length; j++)
                {
                    reader.Seek(ligatureSetOffset + ligatureOffsets[j], SeekOrigin.Begin);
                    ushort ligatureGlyph = reader.ReadUInt16();
                    ushort componentCount = reader.ReadUInt16();
                    ushort[] componentGlyphIds = reader.ReadUInt16Array(componentCount - 1);
                    ligatureTables[j] = new LigatureTable(ligatureGlyph, componentGlyphIds);
                }

                ligatureSetTables[i] = new LigatureSetTable(ligatureTables);
            }

            var coverageTable = CoverageTable.Load(reader, offset + coverageOffset);

            return new LookupType4Format1SubTable(ligatureSetTables, coverageTable, lookupFlags);
        }

        public override bool TrySubstitution(
            FontMetrics fontMetrics,
            GSubTable table,
            GlyphSubstitutionCollection collection,
            Tag feature,
            int index,
            int count)
        {
            ushort glyphId = collection[index].GlyphId;
            if (glyphId == 0)
            {
                return false;
            }

            // Modified for Agent DVR: bounds-checked coverage index.
            int offset = this.coverageTable.CoverageIndexOf(glyphId);
            if ((uint)offset >= (uint)this.ligatureSetTables.Length)
            {
                return false;
            }

            // Modified for Agent DVR: components are matched with the context matcher (default-ignorables such
            // as LRM or variation selectors no longer break ligatures; ZWNJ still does), glyphs attached to
            // different components of an earlier ligature don't ligate, and the ligature id/component
            // bookkeeping follows HarfBuzz (it was inverted: see Ligate).
            LigatureSetTable ligatureSetTable = this.ligatureSetTables[offset];
            SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
            Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
            foreach (LigatureTable ligatureTable in ligatureSetTable.Ligatures)
            {
                int componentCount = ligatureTable.ComponentGlyphs.Length;
                iterator.Index = index;
                if (!AdvancedTypographicUtils.MatchInput(ref iterator, new LigatureComponentMatcher(ligatureTable.ComponentGlyphs, feature), componentCount, positions)
                    || !CanLigate(collection, ref iterator, positions.Slice(0, componentCount + 1)))
                {
                    continue;
                }

                Ligate(fontMetrics, collection, positions.Slice(0, componentCount + 1), ligatureTable.GlyphId);
                return true;
            }

            return false;
        }

        private static int GetLigatureComponent(GlyphShapingData data) => Math.Max(0, data.LigatureComponent);

        /// <summary>
        /// Ligatures cannot be formed across glyphs attached to different components of an earlier ligature
        /// (e.g. after LAM,LAM,HEH ligate, the SHADDA and FATHA left between them must not ligate with each
        /// other), unless that ligature is itself ignored by this lookup. A component not attached to a
        /// ligature can't join one that is, other than the first glyph's own.
        /// </summary>
        private static bool CanLigate(GlyphSubstitutionCollection collection, ref SkippingGlyphIterator iterator, ReadOnlySpan<int> positions)
        {
            GlyphShapingData first = collection[positions[0]];
            int firstLigatureId = first.LigatureId;
            int firstComponent = GetLigatureComponent(first);
            int ligatureBase = 0; // 0: not checked, 1: may skip, 2: may not skip.

            for (int k = 1; k < positions.Length; k++)
            {
                GlyphShapingData data = collection[positions[k]];
                int ligatureId = data.LigatureId;
                int component = GetLigatureComponent(data);
                if (firstLigatureId != 0 && firstComponent != 0)
                {
                    if (firstLigatureId == ligatureId && firstComponent == component)
                    {
                        continue;
                    }

                    if (ligatureBase == 0)
                    {
                        // Find the ligature these components belong to, before the first one.
                        bool found = false;
                        int j = positions[0];
                        while (j > 0 && collection[j - 1].LigatureId == firstLigatureId)
                        {
                            j--;
                            if (GetLigatureComponent(collection[j]) == 0)
                            {
                                found = true;
                                break;
                            }
                        }

                        ligatureBase = found && iterator.IsIgnored(j) ? 1 : 2;
                    }

                    if (ligatureBase == 2)
                    {
                        return false;
                    }
                }
                else if (ligatureId != 0 && component != 0 && ligatureId != firstLigatureId)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Replaces the matched glyphs with the ligature, keeping mark attachment information usable by GPOS:
        /// <list type="bullet">
        /// <item>A ligature of marks, or of a base and marks, keeps the first glyph's ligature id and component
        /// so it can still attach to an earlier ligature (LAM,LAM,SHADDA,FATHA,HEH: SHADDA+FATHA keep
        /// component 2 of LAM-LAM-HEH).</item>
        /// <item>Any other ligature gets a new id, and marks skipped between its components - and marks after it
        /// that belonged to its last component - are renumbered to the new ligature's components.</item>
        /// </list>
        /// </summary>
        private static void Ligate(FontMetrics fontMetrics, GlyphSubstitutionCollection collection, ReadOnlySpan<int> positions, ushort ligatureGlyphId)
        {
            int index = positions[0];
            GlyphShapingData first = collection[index];
            GlyphShapingClass firstClass = AdvancedTypographicUtils.GetGlyphShapingClass(fontMetrics, first.GlyphId, first);
            bool isBaseLigature = firstClass.IsBase;
            bool isMarkLigature = firstClass.IsMark;
            int totalComponents = first.LigatureComponentCount;
            for (int k = 1; k < positions.Length; k++)
            {
                GlyphShapingData data = collection[positions[k]];
                totalComponents += data.LigatureComponentCount;
                if (!AdvancedTypographicUtils.IsMarkGlyph(fontMetrics, data.GlyphId, data))
                {
                    isBaseLigature = false;
                    isMarkLigature = false;
                }
            }

            bool isLigature = !isBaseLigature && !isMarkLigature;
            int ligatureId = isLigature ? collection.LigatureId++ : 0;
            int lastLigatureId = first.LigatureId;
            int lastComponentCount = first.LigatureComponentCount;
            int componentsSoFar = lastComponentCount;

            for (int k = 1; k < positions.Length; k++)
            {
                if (isLigature)
                {
                    // Marks skipped between the previous component and this one.
                    for (int j = positions[k - 1] + 1; j < positions[k]; j++)
                    {
                        GlyphShapingData skipped = collection[j];
                        int component = GetLigatureComponent(skipped);
                        if (component == 0)
                        {
                            component = lastComponentCount;
                        }

                        skipped.LigatureId = ligatureId;
                        skipped.LigatureComponent = componentsSoFar - lastComponentCount + Math.Min(component, lastComponentCount);
                    }
                }

                GlyphShapingData data = collection[positions[k]];
                lastLigatureId = data.LigatureId;
                lastComponentCount = data.LigatureComponentCount;
                componentsSoFar += lastComponentCount;
            }

            // Marks following the last component that belonged to its ligature move to the new one.
            if (!isMarkLigature && lastLigatureId != 0)
            {
                for (int j = positions[positions.Length - 1] + 1; j < collection.Count; j++)
                {
                    GlyphShapingData data = collection[j];
                    int component = GetLigatureComponent(data);
                    if (data.LigatureId != lastLigatureId || component == 0)
                    {
                        break;
                    }

                    data.LigatureId = ligatureId;
                    data.LigatureComponent = componentsSoFar - lastComponentCount + Math.Min(component, lastComponentCount);
                }
            }

            int lastPosition = positions[positions.Length - 1];
            int removed = positions.Length - 1;
            collection.Replace(index, positions.Slice(1), ligatureGlyphId, ligatureId, totalComponents);

            // Continue after the last component: marks skipped inside the ligature now follow it and must not
            // be matched again by this lookup.
            AdvancedTypographicUtils.SetResumeIndex(lastPosition - removed + 1);
        }

        public readonly struct LigatureSetTable
        {
            public LigatureSetTable(LigatureTable[] ligatures)
                => this.Ligatures = ligatures;

            public LigatureTable[] Ligatures { get; }
        }

        public readonly struct LigatureTable
        {
            public LigatureTable(ushort glyphId, ushort[] componentGlyphs)
            {
                this.GlyphId = glyphId;
                this.ComponentGlyphs = componentGlyphs;
            }

            public ushort GlyphId { get; }

            public ushort[] ComponentGlyphs { get; }
        }
    }
}
