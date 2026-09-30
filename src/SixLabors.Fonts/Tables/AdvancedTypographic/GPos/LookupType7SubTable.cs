// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GPos
{
    /// <summary>
    /// Lookup Type 7: Contextual Positioning Subtables.
    /// A Contextual Positioning subtable describes glyph positioning in context so a text-processing client can adjust the position
    /// of one or more glyphs within a certain pattern of glyphs.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#lookup-type-7-contextual-positioning-subtables"/>
    /// </summary>
    internal static class LookupType7SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort subTableFormat = reader.ReadUInt16();

            return subTableFormat switch
            {
                1 => LookupType7Format1SubTable.Load(reader, offset, lookupFlags),
                2 => LookupType7Format2SubTable.Load(reader, offset, lookupFlags),
                3 => LookupType7Format3SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }

        internal sealed class LookupType7Format1SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly SequenceRuleSetTable[] seqRuleSetTables;

            public LookupType7Format1SubTable(CoverageTable coverageTable, SequenceRuleSetTable[] seqRuleSetTables, LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.seqRuleSetTables = seqRuleSetTables;
                this.coverageTable = coverageTable;
            }

            public static LookupType7Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                SequenceRuleSetTable[] seqRuleSets = TableLoadingUtils.LoadSequenceContextFormat1(reader, offset, out CoverageTable coverageTable);

                return new LookupType7Format1SubTable(coverageTable, seqRuleSets, lookupFlags);
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

                // Modified for Agent DVR: bounds-checked, and a NULL rule set (legal) means no rules.
                int offset = this.coverageTable.CoverageIndexOf(glyphId);
                if ((uint)offset >= (uint)this.seqRuleSetTables.Length || this.seqRuleSetTables[offset] is not SequenceRuleSetTable ruleSetTable)
                {
                    return false;
                }

                // Modified for Agent DVR: context-aware matching with recorded positions (see ApplyLookupList).
                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
                foreach (SequenceRuleTable ruleTable in ruleSetTable.SequenceRuleTables)
                {
                    iterator.Index = index;
                    if (!AdvancedTypographicUtils.MatchInput(ref iterator, new GlyphIdMatcher(ruleTable.InputSequence), ruleTable.InputSequence.Length, positions))
                    {
                        continue;
                    }

                    return AdvancedTypographicUtils.ApplyLookupList(fontMetrics, table, feature, ruleTable.SequenceLookupRecords, collection, positions, ruleTable.InputSequence.Length + 1, index + count);
                }

                return false;
            }
        }

        internal sealed class LookupType7Format2SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly ClassDefinitionTable classDefinitionTable;
            private readonly ClassSequenceRuleSetTable[] sequenceRuleSetTables;

            public LookupType7Format2SubTable(
                CoverageTable coverageTable,
                ClassDefinitionTable classDefinitionTable,
                ClassSequenceRuleSetTable[] sequenceRuleSetTables,
                LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.coverageTable = coverageTable;
                this.classDefinitionTable = classDefinitionTable;
                this.sequenceRuleSetTables = sequenceRuleSetTables;
            }

            public static LookupType7Format2SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                CoverageTable coverageTable = TableLoadingUtils.LoadSequenceContextFormat2(reader, offset, out ClassDefinitionTable classDefTable, out ClassSequenceRuleSetTable[] classSeqRuleSets);

                return new LookupType7Format2SubTable(coverageTable, classDefTable, classSeqRuleSets, lookupFlags);
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

                if (this.coverageTable.CoverageIndexOf(glyphId) < 0)
                {
                    return false;
                }

                // Modified for Agent DVR: classes without a rule set (beyond the array, or NULL) have no rules.
                int offset = this.classDefinitionTable.ClassIndexOf(glyphId);
                if ((uint)offset >= (uint)this.sequenceRuleSetTables.Length || this.sequenceRuleSetTables[offset] is not ClassSequenceRuleSetTable ruleSetTable)
                {
                    return false;
                }

                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
                foreach (ClassSequenceRuleTable ruleTable in ruleSetTable.SequenceRuleTables)
                {
                    iterator.Index = index;
                    if (!AdvancedTypographicUtils.MatchInput(ref iterator, new GlyphClassMatcher(ruleTable.InputSequence, this.classDefinitionTable), ruleTable.InputSequence.Length, positions))
                    {
                        continue;
                    }

                    return AdvancedTypographicUtils.ApplyLookupList(fontMetrics, table, feature, ruleTable.SequenceLookupRecords, collection, positions, ruleTable.InputSequence.Length + 1, index + count);
                }

                return false;
            }
        }

        internal sealed class LookupType7Format3SubTable : LookupSubTable
        {
            private readonly CoverageTable[] coverageTables;
            private readonly SequenceLookupRecord[] sequenceLookupRecords;

            public LookupType7Format3SubTable(CoverageTable[] coverageTables, SequenceLookupRecord[] sequenceLookupRecords, LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.coverageTables = coverageTables;
                this.sequenceLookupRecords = sequenceLookupRecords;
            }

            public static LookupType7Format3SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                SequenceLookupRecord[] seqLookupRecords = TableLoadingUtils.LoadSequenceContextFormat3(reader, offset, out CoverageTable[] coverageTables);

                return new LookupType7Format3SubTable(coverageTables, seqLookupRecords, lookupFlags);
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

                if (this.coverageTables.Length == 0 || this.coverageTables[0].CoverageIndexOf(glyphId) < 0)
                {
                    return false;
                }

                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
                if (!AdvancedTypographicUtils.MatchInput(ref iterator, new CoverageMatcher(this.coverageTables, 1), this.coverageTables.Length - 1, positions))
                {
                    return false;
                }

                return AdvancedTypographicUtils.ApplyLookupList(fontMetrics, table, feature, this.sequenceLookupRecords, collection, positions, this.coverageTables.Length, index + count);
            }
        }
    }
}
