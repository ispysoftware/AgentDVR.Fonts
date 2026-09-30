// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

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
                // Implements Chained Contexts Substitution, Format 1:
                // https://docs.microsoft.com/en-us/typography/opentype/spec/gsub#61-chained-contexts-substitution-format-1-simple-glyph-contexts
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                // Search for the current glyph in the Coverage table.
                int offset = this.coverageTable.CoverageIndexOf(glyphId);
                if (offset <= -1)
                {
                    return false;
                }

                // Modified for Agent DVR: bounds-checked coverage index.
                if (this.seqRuleSetTables is null || (uint)offset >= (uint)this.seqRuleSetTables.Length)
                {
                    return false;
                }

                ChainedSequenceRuleSetTable seqRuleSet = this.seqRuleSetTables[offset];
                if (seqRuleSet is null)
                {
                    return false;
                }

                // Apply ruleset for the given glyph id.
                ChainedSequenceRuleTable[] rules = seqRuleSet.SequenceRuleTables;
                SkippingGlyphIterator iterator = new(fontMetrics, collection, index, this.LookupFlags);
                for (int lookupIndex = 0; lookupIndex < rules.Length; lookupIndex++)
                {
                    ChainedSequenceRuleTable rule = rules[lookupIndex];
                    if (!AdvancedTypographicUtils.ApplyChainedSequenceRule(iterator, rule))
                    {
                        continue;
                    }

                    return ApplyNestedLookups(fontMetrics, table, collection, feature, index, rule.SequenceLookupRecords);
                }

                return false;
            }
        }

        /// <summary>
        /// Modified for Agent DVR: applies a matched rule's lookups (formats 1 and 2) with the lookup index
        /// and position bounds-checked and nesting depth/budget-limited - a malformed or cyclic font threw
        /// or overflowed the stack here.
        /// </summary>
        private static bool ApplyNestedLookups(
            FontMetrics fontMetrics,
            GPosTable table,
            GlyphPositioningCollection collection,
            Tag feature,
            int index,
            SequenceLookupRecord[] records)
        {
            bool hasChanged = false;
            for (int j = 0; j < records.Length; j++)
            {
                SequenceLookupRecord sequenceLookupRecord = records[j];
                int position = index + sequenceLookupRecord.SequenceIndex;
                if (position >= collection.Count
                    || !AdvancedTypographicUtils.TryGetAt(table.LookupList.LookupTables, sequenceLookupRecord.LookupListIndex, out LookupTable? lookup)
                    || !AdvancedTypographicUtils.TryEnterNested())
                {
                    continue;
                }

                try
                {
                    hasChanged |= lookup.TryUpdatePosition(fontMetrics, table, collection, feature, position, 1);
                }
                finally
                {
                    AdvancedTypographicUtils.ExitNested();
                }
            }

            return hasChanged;
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
                // Implements Chained Contexts Substitution for Format 2:
                // https://docs.microsoft.com/en-us/typography/opentype/spec/gsub#62-chained-contexts-substitution-format-2-class-based-glyph-contexts
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                // Search for the current glyph in the Coverage table.
                int offset = this.coverageTable.CoverageIndexOf(glyphId);
                if (offset <= -1)
                {
                    return false;
                }

                // Search in the class definition table to find the class value assigned to the currently glyph.
                int classId = this.inputClassDefinitionTable.ClassIndexOf(glyphId);
                // Modified for Agent DVR: a NULL rule set (legal) means no rules.
                ChainedClassSequenceRuleTable[]? rules = classId >= 0 && classId < this.sequenceRuleSetTables.Length ? this.sequenceRuleSetTables[classId]?.SubRules : null;
                if (rules is null)
                {
                    return false;
                }

                // Apply ruleset for the given glyph class id.
                SkippingGlyphIterator iterator = new(fontMetrics, collection, index, this.LookupFlags);
                for (int lookupIndex = 0; lookupIndex < rules.Length; lookupIndex++)
                {
                    ChainedClassSequenceRuleTable rule = rules[lookupIndex];
                    if (!AdvancedTypographicUtils.ApplyChainedClassSequenceRule(iterator, rule, this.inputClassDefinitionTable, this.backtrackClassDefinitionTable, this.lookaheadClassDefinitionTable))
                    {
                        continue;
                    }

                    // It's a match. Perform position update and return true if anything changed.
                    return ApplyNestedLookups(fontMetrics, table, collection, feature, index, rule.SequenceLookupRecords);
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

                if (!AdvancedTypographicUtils.CheckAllCoverages(fontMetrics, this.LookupFlags, collection, index, count, this.inputCoverageTables, this.backtrackCoverageTables, this.lookaheadCoverageTables))
                {
                    return false;
                }

                // It's a match. Perform position update and return true if anything changed.
                bool hasChanged = false;
                foreach (SequenceLookupRecord lookupRecord in this.seqLookupRecords)
                {
                    ushort sequenceIndex = lookupRecord.SequenceIndex;
                    ushort lookupIndex = lookupRecord.LookupListIndex;

                    // Modified for Agent DVR: bounds-checked lookup and position, depth/budget-limited nesting.
                    int position = index + sequenceIndex;
                    if (position >= collection.Count
                        || !AdvancedTypographicUtils.TryGetAt(table.LookupList.LookupTables, lookupIndex, out LookupTable? lookup)
                        || !AdvancedTypographicUtils.TryEnterNested())
                    {
                        continue;
                    }

                    try
                    {
                        hasChanged |= lookup.TryUpdatePosition(fontMetrics, table, collection, feature, position, count - sequenceIndex);
                    }
                    finally
                    {
                        AdvancedTypographicUtils.ExitNested();
                    }
                }

                return hasChanged;
            }
        }
    }
}
