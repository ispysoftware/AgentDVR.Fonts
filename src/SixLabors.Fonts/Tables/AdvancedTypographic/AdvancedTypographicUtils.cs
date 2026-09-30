// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.Fonts.Tables.AdvancedTypographic.GPos;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts.Tables.AdvancedTypographic
{
    internal static class AdvancedTypographicUtils
    {
        // The following properties are used to prevent overflows caused
        // by maliciously crafted fonts.
        // Based on HarfBuzz hb-buffer.hh
        public const int MaxContextLength = 64;
        private const int MaxLengthFactor = 64;
        private const int MaxLengthMinimum = 16384;
        private const int MaxOperationsFactor = 1024;
        private const int MaxOperationsMinimum = 16384;
        private const int MaxShapingCharsLength = 0x3FFFFFFF; // Half int max.

        // Modified for Agent DVR: contextual lookups call other lookups, which may be contextual
        // themselves. A font whose lookups reference each other in a cycle recursed until the stack
        // overflowed (uncatchable - it takes the process down), and nested calls weren't counted
        // against the operation budget. Depth is capped as HarfBuzz does (HB_MAX_NESTING_LEVEL) and
        // every nested call spends from a per-pass budget. Shaping runs synchronously on the calling
        // thread, so the state is thread-static.
        private const int MaxNestingLevel = 64;

        [ThreadStatic]
        private static int nestingLevel;

        [ThreadStatic]
        private static int nestedOperationsLeft;

        [ThreadStatic]
        private static int resumeIndexPlusOne;

        private static readonly Tag[] FallbackScriptTags = { Tag.Parse("DFLT"), Tag.Parse("dflt"), Tag.Parse("latn") };

        private static readonly Tag DefaultLanguageTag = Tag.Parse("dflt");

        /// <summary>
        /// Gets a value indicating whether the glyph represented by the codepoint should be interpreted vertically.
        /// </summary>
        /// <param name="codePoint">The codepoint represented by the glyph.</param>
        /// <param name="layoutMode">The layout mode.</param>
        /// <returns>The <see cref="bool"/>.</returns>
        public static bool IsVerticalGlyph(CodePoint codePoint, LayoutMode layoutMode)
        {
            if (layoutMode.IsVertical())
            {
                return true;
            }

            bool isVerticalLayout = layoutMode.IsVerticalMixed();
            return isVerticalLayout && CodePoint.GetVerticalOrientationType(codePoint) is VerticalOrientationType.Upright or VerticalOrientationType.TransformUpright;
        }

        public static int GetMaxAllowableShapingCollectionCount(int length)
            => (int)Math.Min(Math.Max((long)length * MaxLengthFactor, MaxLengthMinimum), MaxShapingCharsLength);

        /// <summary>
        /// Modified for Agent DVR: the script whose shaper should run over a script run in this font. A
        /// script-specific shaper only runs if the font has a glyph for at least one of the run's own
        /// characters; otherwise the default shaper is used. With fallback fonts the primary font sees runs
        /// it can't render, and the Indic/Hangul/USE shapers would reorder them and insert dotted circles
        /// that the fallback font's pass then has to reconcile. (Upstream decides from the font's script
        /// list, which would also drop joining in Arabic fonts that file their features under DFLT.)
        /// </summary>
        /// <param name="collection">The glyphs.</param>
        /// <param name="index">The run start.</param>
        /// <param name="count">The run length.</param>
        /// <param name="script">The run's script.</param>
        /// <returns><paramref name="script"/>, or <see cref="ScriptClass.Unknown"/> for the default shaper.</returns>
        public static ScriptClass GetShaperScript(IGlyphShapingCollection collection, int index, int count, ScriptClass script)
        {
            if (script is ScriptClass.Common or ScriptClass.Unknown or ScriptClass.Inherited)
            {
                return script;
            }

            int end = Math.Min(index + count, collection.Count);
            for (int i = index; i < end; i++)
            {
                GlyphShapingData data = collection[i];
                if (data.GlyphId != 0 && CodePoint.GetScriptClass(data.CodePoint) == script)
                {
                    return script;
                }
            }

            return ScriptClass.Unknown;
        }

        public static int GetMaxAllowableShapingOperationsCount(int length)
            => (int)Math.Min(Math.Max((long)length * MaxOperationsFactor, MaxOperationsMinimum), MaxShapingCharsLength);

        /// <summary>
        /// Modified for Agent DVR: starts a GSUB/GPOS pass, giving nested lookups
        /// <paramref name="maxOperations"/> applications between them.
        /// </summary>
        /// <param name="maxOperations">The pass's operation budget.</param>
        public static void BeginShapingPass(int maxOperations)
        {
            nestingLevel = 0;
            nestedOperationsLeft = maxOperations;
            resumeIndexPlusOne = 0;
        }

        /// <summary>
        /// Modified for Agent DVR: enters a nested lookup application, unless that would exceed the
        /// nesting depth or the pass's operation budget. On success the caller must call
        /// <see cref="ExitNested"/> in a finally block.
        /// </summary>
        /// <returns><see langword="true"/> if the nested lookup may be applied.</returns>
        public static bool TryEnterNested()
        {
            if (nestingLevel >= MaxNestingLevel || nestedOperationsLeft <= 0)
            {
                return false;
            }

            nestedOperationsLeft--;
            nestingLevel++;
            return true;
        }

        /// <summary>Modified for Agent DVR: leaves a nested lookup entered with <see cref="TryEnterNested"/>.</summary>
        public static void ExitNested() => nestingLevel--;

        /// <summary>
        /// Modified for Agent DVR: bounds-checked access to a feature or lookup list - the index comes
        /// straight from the font file and indexing past the list threw.
        /// </summary>
        /// <typeparam name="T">The table type.</typeparam>
        /// <param name="items">The list.</param>
        /// <param name="index">The index.</param>
        /// <param name="item">The table, when the index is valid.</param>
        /// <returns><see langword="true"/> if the index is valid.</returns>
        public static bool TryGetAt<T>(T[] items, int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? item)
            where T : class
        {
            if ((uint)index < (uint)items.Length && items[index] is T value)
            {
                item = value;
                return true;
            }

            item = null;
            return false;
        }

        /// <summary>
        /// Modified for Agent DVR: selects the language system to take features from, as HarfBuzz does: the
        /// script's own tags, then DFLT, dflt and latn; within the script a 'dflt' LangSys record, then the
        /// default LangSys; otherwise none. It used the first script in the font when the text's script was
        /// missing (arab lookups on Latin text, say), and with no default LangSys it mixed the features of
        /// every language together, applying shared lookups more than once.
        /// </summary>
        /// <param name="scriptList">The script list.</param>
        /// <param name="script">The text's script.</param>
        /// <returns>The language system, or null for none.</returns>
        public static LangSysTable? SelectLangSys(ScriptList? scriptList, ScriptClass script)
        {
            if (scriptList is null)
            {
                return null;
            }

            ScriptListTable? scriptTable = null;
            foreach (Tag tag in UnicodeScriptTagMap.Instance[script])
            {
                if (scriptList.TryGetValue(tag, out ScriptListTable? table))
                {
                    scriptTable = table;
                    break;
                }
            }

            if (scriptTable is null)
            {
                foreach (Tag tag in FallbackScriptTags)
                {
                    if (scriptList.TryGetValue(tag, out ScriptListTable? table))
                    {
                        scriptTable = table;
                        break;
                    }
                }
            }

            if (scriptTable is null)
            {
                return null;
            }

            foreach (LangSysTable langSys in scriptTable.LangSysTables)
            {
                if (langSys.LangSysTag == DefaultLanguageTag.Value)
                {
                    return langSys;
                }
            }

            return scriptTable.DefaultLangSysTable;
        }

        /// <summary>
        /// Modified for Agent DVR: collects the lookups of a group of features, each lookup once and in lookup
        /// list order (the order the spec and HarfBuzz apply them in), with the features that reference it.
        /// Features were applied one at a time in registration order, so a lookup two features shared (Indic
        /// and USE register both 'dist' and 'kern') was applied twice, and lookups ordered across features
        /// ran out of order.
        /// </summary>
        /// <param name="featureList">The feature list.</param>
        /// <param name="lookupCount">The number of lookups in the lookup list.</param>
        /// <param name="langSys">The selected language system.</param>
        /// <param name="features">The features.</param>
        /// <returns>The lookups, sorted by index.</returns>
        public static List<FeatureLookup> CollectLookups(FeatureListTable featureList, int lookupCount, LangSysTable? langSys, IReadOnlyList<Tag> features)
        {
            List<FeatureLookup> lookups = new();
            if (langSys is null || features.Count == 0)
            {
                return lookups;
            }

            Dictionary<ushort, FeatureLookup>? byIndex = null;
            foreach (ushort featureIndex in langSys.FeatureIndices)
            {
                if (!TryGetAt(featureList.FeatureTables, featureIndex, out FeatureTable? featureTable))
                {
                    continue;
                }

                Tag tag = featureTable.FeatureTag;
                bool requested = false;
                for (int i = 0; i < features.Count; i++)
                {
                    if (features[i] == tag)
                    {
                        requested = true;
                        break;
                    }
                }

                if (!requested)
                {
                    continue;
                }

                foreach (ushort lookupIndex in featureTable.LookupListIndices)
                {
                    if (lookupIndex >= lookupCount)
                    {
                        continue;
                    }

                    byIndex ??= new Dictionary<ushort, FeatureLookup>();
                    if (!byIndex.TryGetValue(lookupIndex, out FeatureLookup? lookup))
                    {
                        lookup = new FeatureLookup(lookupIndex);
                        byIndex.Add(lookupIndex, lookup);
                        lookups.Add(lookup);
                    }

                    lookup.AddFeature(tag);
                }
            }

            lookups.Sort((x, y) => x.LookupIndex.CompareTo(y.LookupIndex));
            return lookups;
        }

        /// <summary>
        /// Modified for Agent DVR: records where the main lookup loop should continue after the lookup just
        /// applied (after a matched context, a pair whose second glyph was adjusted, or a multiple
        /// substitution's output). Ignored inside nested lookups.
        /// </summary>
        /// <param name="index">The index to continue at.</param>
        public static void SetResumeIndex(int index)
        {
            if (nestingLevel == 0)
            {
                resumeIndexPlusOne = index + 1;
            }
        }

        /// <summary>
        /// Modified for Agent DVR: takes the index recorded by <see cref="SetResumeIndex"/>, or -1.
        /// </summary>
        /// <returns>The index, or -1 if none was recorded.</returns>
        public static int TakeResumeIndex()
        {
            int index = resumeIndexPlusOne - 1;
            resumeIndexPlusOne = 0;
            return index;
        }

        /// <summary>
        /// Matches the input sequence of a contextual rule. The glyph at the iterator's index is the first
        /// input glyph (the caller has checked it); <paramref name="length"/> more follow.
        /// Modified for Agent DVR: the matched positions are recorded, so nested lookups apply to the glyphs
        /// that actually matched rather than to a re-count that disagreed with the matching.
        /// </summary>
        /// <typeparam name="TMatcher">The matcher type.</typeparam>
        /// <param name="iterator">A context iterator positioned on the first input glyph.</param>
        /// <param name="matcher">The matcher for the remaining input glyphs.</param>
        /// <param name="length">The number of input glyphs after the first.</param>
        /// <param name="positions">Receives the indices of all input glyphs, the first included.</param>
        /// <returns><see langword="true"/> if the input matched.</returns>
        public static bool MatchInput<TMatcher>(ref SkippingGlyphIterator iterator, TMatcher matcher, int length, Span<int> positions)
            where TMatcher : struct, IGlyphMatcher
        {
            if (length + 1 > positions.Length)
            {
                return false;
            }

            positions[0] = iterator.Index;
            for (int i = 0; i < length; i++)
            {
                if (!iterator.TryMatchNext(1, ref matcher, i))
                {
                    return false;
                }

                positions[i + 1] = iterator.Index;
            }

            return true;
        }

        /// <summary>
        /// Modified for Agent DVR: matches a backtrack sequence, which the font stores nearest-first: element 0
        /// is compared with the glyph just before <paramref name="index"/>. It was compared farthest-first,
        /// so any rule with two or more backtrack glyphs matched the wrong context.
        /// </summary>
        /// <typeparam name="TMatcher">The matcher type.</typeparam>
        /// <param name="iterator">A context iterator.</param>
        /// <param name="index">The first input glyph.</param>
        /// <param name="matcher">The backtrack matcher.</param>
        /// <param name="length">The backtrack length.</param>
        /// <returns><see langword="true"/> if the backtrack matched.</returns>
        public static bool MatchBacktrack<TMatcher>(ref SkippingGlyphIterator iterator, int index, TMatcher matcher, int length)
            where TMatcher : struct, IGlyphMatcher
        {
            iterator.Index = index;
            for (int i = 0; i < length; i++)
            {
                if (!iterator.TryMatchNext(-1, ref matcher, i))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Matches a lookahead sequence, starting after the last input glyph.
        /// </summary>
        /// <typeparam name="TMatcher">The matcher type.</typeparam>
        /// <param name="iterator">A context iterator.</param>
        /// <param name="lastInput">The last input glyph.</param>
        /// <param name="matcher">The lookahead matcher.</param>
        /// <param name="length">The lookahead length.</param>
        /// <returns><see langword="true"/> if the lookahead matched.</returns>
        public static bool MatchLookahead<TMatcher>(ref SkippingGlyphIterator iterator, int lastInput, TMatcher matcher, int length)
            where TMatcher : struct, IGlyphMatcher
        {
            iterator.Index = lastInput;
            for (int i = 0; i < length; i++)
            {
                if (!iterator.TryMatchNext(1, ref matcher, i))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Matches a chained context rule: input (after the first glyph), backtrack and lookahead.
        /// </summary>
        /// <typeparam name="TBacktrack">The backtrack matcher type.</typeparam>
        /// <typeparam name="TInput">The input matcher type.</typeparam>
        /// <typeparam name="TLookahead">The lookahead matcher type.</typeparam>
        /// <param name="iterator">A context iterator.</param>
        /// <param name="index">The first input glyph.</param>
        /// <param name="backtrack">The backtrack matcher.</param>
        /// <param name="backtrackLength">The backtrack length.</param>
        /// <param name="input">The matcher for the input glyphs after the first.</param>
        /// <param name="inputLength">The number of input glyphs after the first.</param>
        /// <param name="lookahead">The lookahead matcher.</param>
        /// <param name="lookaheadLength">The lookahead length.</param>
        /// <param name="positions">Receives the input glyph indices.</param>
        /// <returns><see langword="true"/> if the rule matched.</returns>
        public static bool MatchChainedRule<TBacktrack, TInput, TLookahead>(
            ref SkippingGlyphIterator iterator,
            int index,
            TBacktrack backtrack,
            int backtrackLength,
            TInput input,
            int inputLength,
            TLookahead lookahead,
            int lookaheadLength,
            Span<int> positions)
            where TBacktrack : struct, IGlyphMatcher
            where TInput : struct, IGlyphMatcher
            where TLookahead : struct, IGlyphMatcher
        {
            iterator.Index = index;
            if (!MatchInput(ref iterator, input, inputLength, positions))
            {
                return false;
            }

            int lastInput = iterator.Index;
            return MatchBacktrack(ref iterator, index, backtrack, backtrackLength)
                && MatchLookahead(ref iterator, lastInput, lookahead, lookaheadLength);
        }

        /// <summary>
        /// Applies the nested lookups of a matched GSUB contextual rule.
        /// Modified for Agent DVR (following the spec and HarfBuzz):
        /// <list type="bullet">
        /// <item>each record applies at the glyph matched for its sequence index, and the positions are
        /// corrected as nested lookups add or remove glyphs;</item>
        /// <item>a matched rule finishes the lookup at this glyph even if nothing changed (it returned
        /// "changed", so later subtables still ran after a match);</item>
        /// <item>the main loop continues after the matched input instead of re-running over it;</item>
        /// <item>nesting is depth/budget-limited and lookup indices are bounds-checked.</item>
        /// </list>
        /// </summary>
        /// <param name="fontMetrics">The font.</param>
        /// <param name="table">The GSUB table.</param>
        /// <param name="feature">The feature being applied.</param>
        /// <param name="records">The rule's lookup records.</param>
        /// <param name="collection">The glyphs.</param>
        /// <param name="positions">The matched input positions (updated in place).</param>
        /// <param name="matchLength">The number of input glyphs.</param>
        /// <param name="runEnd">The exclusive end of the run.</param>
        /// <returns>Always <see langword="true"/>: the rule matched.</returns>
        public static bool ApplyLookupList(
            FontMetrics fontMetrics,
            GSubTable table,
            Tag feature,
            SequenceLookupRecord[] records,
            GlyphSubstitutionCollection collection,
            Span<int> positions,
            int matchLength,
            int runEnd)
        {
            int count = matchLength;
            int end = positions[count - 1] + 1;
            foreach (SequenceLookupRecord lookupRecord in records)
            {
                int idx = lookupRecord.SequenceIndex;
                if (idx >= count)
                {
                    continue;
                }

                int position = positions[idx];
                int originalLength = collection.Count;
                if (position >= originalLength
                    || !TryGetAt(table.LookupList.LookupTables, lookupRecord.LookupListIndex, out GSub.LookupTable? lookup)
                    || !TryEnterNested())
                {
                    continue;
                }

                try
                {
                    lookup.TrySubstitution(fontMetrics, table, collection, feature, position, Math.Max(1, runEnd - position));
                }
                finally
                {
                    ExitNested();
                }

                int delta = collection.Count - originalLength;
                if (delta == 0)
                {
                    continue;
                }

                runEnd += delta;

                // The nested lookup changed the glyph count. As in HarfBuzz, added glyphs are taken to follow
                // the current position, and removed glyphs to be the match positions after it.
                end += delta;
                if (end < position)
                {
                    delta += position - end;
                    end = position;
                }

                int next = idx + 1;
                if (delta > 0)
                {
                    if (count + delta > positions.Length)
                    {
                        break;
                    }
                }
                else
                {
                    delta = Math.Max(delta, next - count);
                    next -= delta;
                }

                positions.Slice(next, count - next).CopyTo(positions.Slice(next + delta));
                next += delta;
                count += delta;

                for (int j = idx + 1; j < next; j++)
                {
                    positions[j] = positions[j - 1] + 1;
                }

                for (; next < count; next++)
                {
                    positions[next] += delta;
                }
            }

            SetResumeIndex(end);
            return true;
        }

        /// <summary>
        /// Applies the nested lookups of a matched GPOS contextual rule (see the GSUB overload).
        /// </summary>
        /// <param name="fontMetrics">The font.</param>
        /// <param name="table">The GPOS table.</param>
        /// <param name="feature">The feature being applied.</param>
        /// <param name="records">The rule's lookup records.</param>
        /// <param name="collection">The glyphs.</param>
        /// <param name="positions">The matched input positions.</param>
        /// <param name="matchLength">The number of input glyphs.</param>
        /// <param name="runEnd">The exclusive end of the run.</param>
        /// <returns>Always <see langword="true"/>: the rule matched.</returns>
        public static bool ApplyLookupList(
            FontMetrics fontMetrics,
            GPosTable table,
            Tag feature,
            SequenceLookupRecord[] records,
            GlyphPositioningCollection collection,
            ReadOnlySpan<int> positions,
            int matchLength,
            int runEnd)
        {
            foreach (SequenceLookupRecord lookupRecord in records)
            {
                int idx = lookupRecord.SequenceIndex;
                if (idx >= matchLength)
                {
                    continue;
                }

                int position = positions[idx];
                if (position >= collection.Count
                    || !TryGetAt(table.LookupList.LookupTables, lookupRecord.LookupListIndex, out LookupTable? lookup)
                    || !TryEnterNested())
                {
                    continue;
                }

                try
                {
                    lookup.TryUpdatePosition(fontMetrics, table, collection, feature, position, Math.Max(1, runEnd - position));
                }
                finally
                {
                    ExitNested();
                }
            }

            SetResumeIndex(positions[matchLength - 1] + 1);
            return true;
        }

        /// <summary>
        /// Matches a coverage-based chained context (format 3): the input coverages include the first glyph.
        /// </summary>
        /// <param name="iterator">A context iterator.</param>
        /// <param name="index">The first input glyph.</param>
        /// <param name="input">The input coverages.</param>
        /// <param name="backtrack">The backtrack coverages, nearest first.</param>
        /// <param name="lookahead">The lookahead coverages.</param>
        /// <param name="positions">Receives the input glyph indices.</param>
        /// <returns><see langword="true"/> if the context matched.</returns>
        public static bool MatchCoverageContext(
            ref SkippingGlyphIterator iterator,
            int index,
            CoverageTable[] input,
            CoverageTable[] backtrack,
            CoverageTable[] lookahead,
            Span<int> positions)
        {
            if (input.Length == 0 || input[0].CoverageIndexOf(iterator.Collection[index].GlyphId) < 0)
            {
                return false;
            }

            return MatchChainedRule(
                ref iterator,
                index,
                new CoverageMatcher(backtrack),
                backtrack.Length,
                new CoverageMatcher(input, 1),
                input.Length - 1,
                new CoverageMatcher(lookahead),
                lookahead.Length,
                positions);
        }

        public static void ApplyAnchor(
            FontMetrics fontMetrics,
            GlyphPositioningCollection collection,
            int index,
            AnchorTable baseAnchor,
            MarkRecord markRecord,
            int baseGlyphIndex)
        {
            GlyphShapingData baseData = collection[baseGlyphIndex];
            AnchorXY baseXY = baseAnchor.GetAnchor(fontMetrics, baseData, collection);

            GlyphShapingData markData = collection[index];
            AnchorXY markXY = markRecord.MarkAnchorTable.GetAnchor(fontMetrics, markData, collection);

            markData.Bounds.X = baseXY.XCoordinate - markXY.XCoordinate;
            markData.Bounds.Y = baseXY.YCoordinate - markXY.YCoordinate;
            markData.MarkAttachment = baseGlyphIndex;

            // Modified for Agent DVR: a glyph has one attachment; the mark attachment replaces any cursive one.
            markData.CursiveAttachment = 0;
        }

        public static void ApplyPosition(
            GlyphPositioningCollection collection,
            int index,
            ValueRecord record)
        {
            GlyphShapingData current = collection[index];
            current.Bounds.Width += record.XAdvance;
            current.Bounds.Height += record.YAdvance;
            current.Bounds.X += record.XPlacement;
            current.Bounds.Y += record.YPlacement;
        }

        /// <summary>
        /// Gets a value indicating whether the glyph is a mark.
        /// Modified for Agent DVR: same classification as <see cref="GetGlyphShapingClass"/>. Without GDEF glyph
        /// classes it returned false for every glyph (a null class never equals MarkGlyph), so mark searches
        /// and mark-advance zeroing never saw combining marks in such fonts.
        /// </summary>
        /// <param name="fontMetrics">The font.</param>
        /// <param name="glyphId">The glyph id.</param>
        /// <param name="shapingData">The glyph.</param>
        /// <returns><see langword="true"/> if the glyph is a mark.</returns>
        public static bool IsMarkGlyph(FontMetrics fontMetrics, ushort glyphId, GlyphShapingData shapingData)
            => GetGlyphShapingClass(fontMetrics, glyphId, shapingData).IsMark;

        public static GlyphShapingClass GetGlyphShapingClass(FontMetrics fontMetrics, ushort glyphId, GlyphShapingData shapingData)
        {
            bool isMark;
            bool isBase;
            bool isLigature;
            ushort markAttachmentType = 0;
            if (fontMetrics.TryGetGlyphClass(glyphId, out GlyphClassDef? glyphClass))
            {
                isMark = glyphClass == GlyphClassDef.MarkGlyph;
                isBase = glyphClass == GlyphClassDef.BaseGlyph;
                isLigature = glyphClass == GlyphClassDef.LigatureGlyph;
                if (fontMetrics.TryGetMarkAttachmentClass(glyphId, out GlyphClassDef? markAttachmentClass))
                {
                    markAttachmentType = (ushort)markAttachmentClass;
                }
            }
            else
            {
                // Modified for Agent DVR: without GDEF glyph classes, only non-spacing marks count as marks
                // (as HarfBuzz synthesizes them). Spacing marks (Mc, e.g. Indic vowel signs) and enclosing
                // marks have advances; treating them as marks let mark-advance zeroing collapse them.
                CodePoint codePoint = shapingData.CodePoint;
                isMark = CodePoint.GetGeneralCategory(codePoint) == System.Globalization.UnicodeCategory.NonSpacingMark
                    && !UnicodeUtility.IsDefaultIgnorableCodePoint((uint)codePoint.Value);
                isBase = !isMark;
                isLigature = shapingData.CodePointCount > 1;
            }

            return new GlyphShapingClass(isMark, isBase, isLigature, markAttachmentType);
        }
    }

    /// <summary>
    /// Modified for Agent DVR: a lookup to apply, with the features that reference it.
    /// </summary>
    internal sealed class FeatureLookup
    {
        private Tag[] features = Array.Empty<Tag>();

        public FeatureLookup(ushort lookupIndex) => this.LookupIndex = lookupIndex;

        public ushort LookupIndex { get; }

        public void AddFeature(Tag feature)
        {
            if (this.HasFeature(feature))
            {
                return;
            }

            Array.Resize(ref this.features, this.features.Length + 1);
            this.features[this.features.Length - 1] = feature;
        }

        public bool HasFeature(Tag feature)
        {
            foreach (Tag tag in this.features)
            {
                if (tag == feature)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the first of this lookup's features that is enabled on a glyph.
        /// </summary>
        /// <param name="glyphFeatures">The glyph's features.</param>
        /// <param name="feature">The feature.</param>
        /// <returns><see langword="true"/> if the lookup applies to the glyph.</returns>
        public bool TryGetEnabledFeature(List<TagEntry> glyphFeatures, out Tag feature)
        {
            for (int i = 0; i < glyphFeatures.Count; i++)
            {
                TagEntry entry = glyphFeatures[i];
                if (entry.Enabled && this.HasFeature(entry.Tag))
                {
                    feature = entry.Tag;
                    return true;
                }
            }

            feature = default;
            return false;
        }
    }
}
