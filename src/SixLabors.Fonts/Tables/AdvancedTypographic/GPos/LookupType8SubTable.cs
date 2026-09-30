// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GPos
{
    /// <summary>
    /// LookupType 8: Chained Contexts Positioning Subtable.
    /// A Chained Contexts Positioning subtable describes glyph positioning in context with an ability to look back and/or look ahead in the sequence of glyphs.
    /// The design of the Chained Contexts Positioning subtable is parallel to that of the Contextual Positioning subtable, including the availability of three formats.
    /// Each format can describe one or more chained backtrack, input, and lookahead sequence combinations, and one or more positioning adjustments for glyphs in each input sequence.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#lookuptype-8-chained-contexts-positioning-subtable"/>
    /// </summary>
    /// <remarks>
    /// Modified for Agent DVR: all three formats match through the shared context matcher (backtrack
    /// nearest-first, lookup flags, mark filtering sets and default-ignorables honoured) and apply their
    /// nested lookups through <see cref="AdvancedTypographicUtils"/>, instead of three inline copies that
    /// applied each record at index + sequenceIndex regardless of skipped glyphs.
    /// </remarks>
    internal static class LookupType8SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort substFormat = reader.ReadUInt16();

            return substFormat switch
            {
                1 => LookupType8Format1SubTable.Load(reader, offset, lookupFlags),
                2 => LookupType8Format2SubTable.Load(reader, offset, lookupFlags),
                3 => LookupType8Format3SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }

        internal sealed class LookupType8Format1SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly ChainedSequenceRuleSetTable[] seqRuleSetTables;

            private LookupType8Format1SubTable(
                CoverageTable coverageTable,
                ChainedSequenceRuleSetTable[] seqRuleSetTables,
                LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.coverageTable = coverageTable;
                this.seqRuleSetTables = seqRuleSetTables;
            }

            public static LookupType8Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                ChainedSequenceRuleSetTable[] seqRuleSets = TableLoadingUtils.LoadChainedSequenceContextFormat1(reader, offset, out CoverageTable coverageTable);
                return new LookupType8Format1SubTable(coverageTable, seqRuleSets, lookupFlags);
            }

            public override bool TryUpdatePosition(
                FontMetrics fontMetrics,
                GPosTable table,
                GlyphPositioningCollection collection,
                Tag feature,
                int index,
                int count)
            {
                // Implements Chained Contexts Positioning, Format 1:
                // https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#chained-sequence-context-format-1-simple-glyph-contexts
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                // Modified for Agent DVR: bounds-checked coverage index; a NULL rule set means no rules.
                int offset = this.coverageTable.CoverageIndexOf(glyphId);
                if (this.seqRuleSetTables is null
                    || (uint)offset >= (uint)this.seqRuleSetTables.Length
                    || this.seqRuleSetTables[offset] is not ChainedSequenceRuleSetTable seqRuleSet)
                {
                    return false;
                }

                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
                foreach (ChainedSequenceRuleTable rule in seqRuleSet.SequenceRuleTables)
                {
                    if (!AdvancedTypographicUtils.MatchChainedRule(
                        ref iterator,
                        index,
                        new GlyphIdMatcher(rule.BacktrackSequence),
                        rule.BacktrackSequence.Length,
                        new GlyphIdMatcher(rule.InputSequence),
                        rule.InputSequence.Length,
                        new GlyphIdMatcher(rule.LookaheadSequence),
                        rule.LookaheadSequence.Length,
                        positions))
                    {
                        continue;
                    }

                    return AdvancedTypographicUtils.ApplyLookupList(fontMetrics, table, feature, rule.SequenceLookupRecords, collection, positions, rule.InputSequence.Length + 1, index + count);
                }

                return false;
            }
        }

        internal sealed class LookupType8Format2SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly ClassDefinitionTable inputClassDefinitionTable;
            private readonly ClassDefinitionTable backtrackClassDefinitionTable;
            private readonly ClassDefinitionTable lookaheadClassDefinitionTable;
            private readonly ChainedClassSequenceRuleSetTable[] sequenceRuleSetTables;

            private LookupType8Format2SubTable(
                ChainedClassSequenceRuleSetTable[] sequenceRuleSetTables,
                ClassDefinitionTable backtrackClassDefinitionTable,
                ClassDefinitionTable inputClassDefinitionTable,
                ClassDefinitionTable lookaheadClassDefinitionTable,
                CoverageTable coverageTable,
                LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.sequenceRuleSetTables = sequenceRuleSetTables;
                this.backtrackClassDefinitionTable = backtrackClassDefinitionTable;
                this.inputClassDefinitionTable = inputClassDefinitionTable;
                this.lookaheadClassDefinitionTable = lookaheadClassDefinitionTable;
                this.coverageTable = coverageTable;
            }

            public static LookupType8Format2SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                ChainedClassSequenceRuleSetTable[] seqRuleSets = TableLoadingUtils.LoadChainedSequenceContextFormat2(
                    reader,
                    offset,
                    out CoverageTable coverageTable,
                    out ClassDefinitionTable backtrackClassDefTable,
                    out ClassDefinitionTable inputClassDefTable,
                    out ClassDefinitionTable lookaheadClassDefTable);

                return new LookupType8Format2SubTable(seqRuleSets, backtrackClassDefTable, inputClassDefTable, lookaheadClassDefTable, coverageTable, lookupFlags);
            }

            public override bool TryUpdatePosition(
                FontMetrics fontMetrics,
                GPosTable table,
                GlyphPositioningCollection collection,
                Tag feature,
                int index,
                int count)
            {
                // Implements Chained Contexts Positioning, Format 2:
                // https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#chained-sequence-context-format-2-class-based-glyph-contexts
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                if (this.coverageTable.CoverageIndexOf(glyphId) < 0)
                {
                    return false;
                }

                // Modified for Agent DVR: a NULL rule set (legal) means no rules.
                int classId = this.inputClassDefinitionTable.ClassIndexOf(glyphId);
                ChainedClassSequenceRuleTable[]? rules = classId >= 0 && classId < this.sequenceRuleSetTables.Length ? this.sequenceRuleSetTables[classId]?.SubRules : null;
                if (rules is null)
                {
                    return false;
                }

                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
                foreach (ChainedClassSequenceRuleTable rule in rules)
                {
                    if (!AdvancedTypographicUtils.MatchChainedRule(
                        ref iterator,
                        index,
                        new GlyphClassMatcher(rule.BacktrackSequence, this.backtrackClassDefinitionTable),
                        rule.BacktrackSequence.Length,
                        new GlyphClassMatcher(rule.InputSequence, this.inputClassDefinitionTable),
                        rule.InputSequence.Length,
                        new GlyphClassMatcher(rule.LookaheadSequence, this.lookaheadClassDefinitionTable),
                        rule.LookaheadSequence.Length,
                        positions))
                    {
                        continue;
                    }

                    return AdvancedTypographicUtils.ApplyLookupList(fontMetrics, table, feature, rule.SequenceLookupRecords, collection, positions, rule.InputSequence.Length + 1, index + count);
                }

                return false;
            }
        }

        internal sealed class LookupType8Format3SubTable : LookupSubTable
        {
            private readonly SequenceLookupRecord[] seqLookupRecords;
            private readonly CoverageTable[] backtrackCoverageTables;
            private readonly CoverageTable[] inputCoverageTables;
            private readonly CoverageTable[] lookaheadCoverageTables;

            private LookupType8Format3SubTable(
                SequenceLookupRecord[] seqLookupRecords,
                CoverageTable[] backtrackCoverageTables,
                CoverageTable[] inputCoverageTables,
                CoverageTable[] lookaheadCoverageTables,
                LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.seqLookupRecords = seqLookupRecords;
                this.backtrackCoverageTables = backtrackCoverageTables;
                this.inputCoverageTables = inputCoverageTables;
                this.lookaheadCoverageTables = lookaheadCoverageTables;
            }

            public static LookupType8Format3SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                SequenceLookupRecord[] seqLookupRecords = TableLoadingUtils.LoadChainedSequenceContextFormat3(
                    reader,
                    offset,
                    out CoverageTable[] backtrackCoverageTables,
                    out CoverageTable[] inputCoverageTables,
                    out CoverageTable[] lookaheadCoverageTables);

                return new LookupType8Format3SubTable(seqLookupRecords, backtrackCoverageTables, inputCoverageTables, lookaheadCoverageTables, lookupFlags);
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

                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                Span<int> positions = stackalloc int[AdvancedTypographicUtils.MaxContextLength];
                if (!AdvancedTypographicUtils.MatchCoverageContext(
                    ref iterator,
                    index,
                    this.inputCoverageTables,
                    this.backtrackCoverageTables,
                    this.lookaheadCoverageTables,
                    positions))
                {
                    return false;
                }

                return AdvancedTypographicUtils.ApplyLookupList(fontMetrics, table, feature, this.seqLookupRecords, collection, positions, this.inputCoverageTables.Length, index + count);
            }
        }
    }
}
