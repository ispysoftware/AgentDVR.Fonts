// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts.Tables.AdvancedTypographic
{
    /// <summary>
    /// Walks a glyph collection skipping the glyphs a lookup ignores.
    /// </summary>
    /// <remarks>
    /// Modified for Agent DVR. The iterator has two modes:
    /// <list type="bullet">
    /// <item>Position walking (the constructor): used by the main lookup loops to visit the glyphs a lookup
    /// may start at. Only the lookup flags and mark filtering set are applied.</item>
    /// <item>Context matching (<see cref="ForContext"/>): used inside subtables to find context, input,
    /// ligature-component, pair and attachment glyphs. As in HarfBuzz, default-ignorable characters
    /// (bidi marks, variation selectors, ...) are also stepped over unless the rule matches them
    /// explicitly; ZWNJ blocks GSUB matching, ZWJ is transparent except for features that handle joiners
    /// themselves (the Indic/USE basic features, rlig, rclt), and GPOS steps over both. Glyphs from another
    /// font (fallback runs) end the context, and forward matching stops at the run end.</item>
    /// </list>
    /// Mark filtering sets (UseMarkFilteringSet) were ignored before, and default-ignorables broke ligatures,
    /// contextual forms and kerning.
    /// </remarks>
    internal struct SkippingGlyphIterator
    {
        private static readonly Tag[] ManualJoinerFeatures =
        {
            Tag.Parse("nukt"), Tag.Parse("akhn"), Tag.Parse("rphf"), Tag.Parse("rkrf"), Tag.Parse("pref"),
            Tag.Parse("blwf"), Tag.Parse("abvf"), Tag.Parse("half"), Tag.Parse("pstf"), Tag.Parse("vatu"),
            Tag.Parse("cjct"), Tag.Parse("init"), Tag.Parse("pres"), Tag.Parse("abvs"), Tag.Parse("blws"),
            Tag.Parse("psts"), Tag.Parse("haln"), Tag.Parse("rlig"), Tag.Parse("rclt")
        };

        private readonly FontMetrics fontMetrics;
        private readonly bool contextMatching;
        private readonly bool ignoreZwnj;
        private readonly bool ignoreZwj;
        private readonly bool ignoreHidden;
        private readonly int end;
        private bool ignoreMarks;
        private bool ignoreBaseGlyphs;
        private bool ignoreLigatures;
        private bool useMarkFilteringSet;
        private ushort markFilteringSet;
        private ushort markAttachmentType;

        public SkippingGlyphIterator(
            FontMetrics fontMetrics,
            IGlyphShapingCollection collection,
            int index,
            LookupFlags lookupFlags,
            ushort markFilteringSet = 0)
            : this(fontMetrics, collection, index, lookupFlags, markFilteringSet, false, false, false, false, int.MaxValue)
        {
        }

        private SkippingGlyphIterator(
            FontMetrics fontMetrics,
            IGlyphShapingCollection collection,
            int index,
            LookupFlags lookupFlags,
            ushort markFilteringSet,
            bool contextMatching,
            bool ignoreZwnj,
            bool ignoreZwj,
            bool ignoreHidden,
            int end)
        {
            this.fontMetrics = fontMetrics;
            this.Collection = collection;
            this.Index = index;
            this.contextMatching = contextMatching;
            this.ignoreZwnj = ignoreZwnj;
            this.ignoreZwj = ignoreZwj;
            this.ignoreHidden = ignoreHidden;
            this.end = end;
            this.ignoreMarks = false;
            this.ignoreBaseGlyphs = false;
            this.ignoreLigatures = false;
            this.useMarkFilteringSet = false;
            this.markFilteringSet = 0;
            this.markAttachmentType = 0;
            this.SetFlags(lookupFlags, markFilteringSet);
        }

        private enum Skip : byte
        {
            No,
            Yes,
            Maybe
        }

        public IGlyphShapingCollection Collection { get; }

        public int Index { get; set; }

        /// <summary>
        /// Gets the exclusive upper bound of the glyphs this iterator visits.
        /// </summary>
        public int Limit => Math.Min(this.end, this.Collection.Count);

        /// <summary>
        /// Creates a context-matching iterator (see the type remarks).
        /// </summary>
        /// <param name="fontMetrics">The font being shaped.</param>
        /// <param name="collection">The glyphs.</param>
        /// <param name="index">The starting glyph.</param>
        /// <param name="lookupFlags">The lookup flags.</param>
        /// <param name="markFilteringSet">The lookup's mark filtering set.</param>
        /// <param name="feature">The feature being applied (decides whether ZWJ is transparent).</param>
        /// <param name="end">The exclusive end of the run; forward matching stops there.</param>
        /// <returns>The iterator.</returns>
        public static SkippingGlyphIterator ForContext(
            FontMetrics fontMetrics,
            IGlyphShapingCollection collection,
            int index,
            LookupFlags lookupFlags,
            ushort markFilteringSet,
            Tag feature,
            int end)
        {
            bool positioning = collection is GlyphPositioningCollection;
            return new SkippingGlyphIterator(
                fontMetrics,
                collection,
                index,
                lookupFlags,
                markFilteringSet,
                true,
                positioning,
                positioning || !IsManualJoinerFeature(feature),
                positioning,
                end);
        }

        public int Next()
        {
            this.Move(1);
            return this.Index;
        }

        public int Previous()
        {
            this.Move(-1);
            return this.Index;
        }

        public int Increment(int count = 1)
        {
            int direction = count < 0 ? -1 : 1;
            count = Math.Abs(count);
            while (count-- > 0)
            {
                this.Move(direction);
            }

            return this.Index;
        }

        public void Reset(int index, LookupFlags lookupFlags, ushort markFilteringSet = 0)
        {
            this.Index = index;
            this.SetFlags(lookupFlags, markFilteringSet);
        }

        /// <summary>
        /// Gets a value indicating whether the lookup flags ignore the glyph at <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The glyph index.</param>
        /// <returns><see langword="true"/> if the glyph is skipped.</returns>
        public readonly bool IsIgnored(int index) => this.MaySkip(index) == Skip.Yes;

        /// <summary>
        /// Steps from the current glyph in <paramref name="direction"/> to the next glyph that isn't skipped
        /// and checks it against element <paramref name="sequenceIndex"/> of <paramref name="matcher"/>.
        /// A default-ignorable glyph is stepped over unless it matches. On success <see cref="Index"/> is the
        /// matched glyph; on failure it is unchanged.
        /// </summary>
        /// <typeparam name="TMatcher">The matcher type.</typeparam>
        /// <param name="direction">1 to look forward, -1 to look back.</param>
        /// <param name="matcher">The matcher.</param>
        /// <param name="sequenceIndex">The element of the matcher's sequence to compare with.</param>
        /// <returns><see langword="true"/> if the next glyph matched.</returns>
        public bool TryMatchNext<TMatcher>(int direction, ref TMatcher matcher, int sequenceIndex)
            where TMatcher : struct, IGlyphMatcher
        {
            int limit = this.Limit;
            for (int i = this.Index + direction; i >= 0 && i < limit; i += direction)
            {
                if (this.IsBoundary(i))
                {
                    return false;
                }

                Skip skip = this.MaySkip(i);
                if (skip == Skip.Yes)
                {
                    continue;
                }

                if (matcher.Matches(sequenceIndex, this.Collection[i]))
                {
                    this.Index = i;
                    return true;
                }

                if (skip == Skip.No)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsManualJoinerFeature(Tag feature)
        {
            foreach (Tag tag in ManualJoinerFeatures)
            {
                if (tag == feature)
                {
                    return true;
                }
            }

            return false;
        }

        // Characters that are default-ignorable but must not be stepped over in GSUB (HarfBuzz's "hidden"
        // set): CGJ, Mongolian free variation selectors and TAG characters (flag emoji sequences).
        private static bool IsHidden(uint codePoint)
            => codePoint == 0x034F
            || (codePoint >= 0x180B && codePoint <= 0x180D)
            || codePoint == 0x180F
            || (codePoint >= 0xE0020 && codePoint <= 0xE007F);

        private void SetFlags(LookupFlags lookupFlags, ushort markFilteringSet)
        {
            this.ignoreMarks = (lookupFlags & LookupFlags.IgnoreMarks) != 0;
            this.ignoreBaseGlyphs = (lookupFlags & LookupFlags.IgnoreBaseGlyphs) != 0;
            this.ignoreLigatures = (lookupFlags & LookupFlags.IgnoreLigatures) != 0;
            this.useMarkFilteringSet = (lookupFlags & LookupFlags.UseMarkFilteringSet) != 0;
            this.markFilteringSet = markFilteringSet;
            this.markAttachmentType = (ushort)((int)(lookupFlags & LookupFlags.MarkAttachmentTypeMask) >> 8);
        }

        private void Move(int direction)
        {
            int limit = this.Limit;
            this.Index += direction;
            while (this.Index >= 0 && this.Index < limit)
            {
                if (this.IsBoundary(this.Index))
                {
                    this.Index = direction > 0 ? limit : -1;
                    return;
                }

                if (this.MaySkip(this.Index) == Skip.No)
                {
                    break;
                }

                this.Index += direction;
            }
        }

        // In positioning, a glyph from another font (a fallback run) can't take part in this font's context.
        private readonly bool IsBoundary(int index)
            => this.contextMatching
            && this.Collection is GlyphPositioningCollection positioning
            && !positioning.ShouldProcess(this.fontMetrics, index);

        private readonly Skip MaySkip(int index)
        {
            GlyphShapingData data = this.Collection[index];
            GlyphShapingClass shapingClass = AdvancedTypographicUtils.GetGlyphShapingClass(this.fontMetrics, data.GlyphId, data);
            if ((this.ignoreMarks && shapingClass.IsMark) ||
                (this.ignoreBaseGlyphs && shapingClass.IsBase) ||
                (this.ignoreLigatures && shapingClass.IsLigature))
            {
                return Skip.Yes;
            }

            if (shapingClass.IsMark)
            {
                if (this.useMarkFilteringSet)
                {
                    if (!this.fontMetrics.IsInMarkGlyphSet(this.markFilteringSet, data.GlyphId))
                    {
                        return Skip.Yes;
                    }
                }
                else if (this.markAttachmentType > 0 && shapingClass.MarkAttachmentType != this.markAttachmentType)
                {
                    return Skip.Yes;
                }
            }

            if (!this.contextMatching || data.IsSubstituted)
            {
                return Skip.No;
            }

            uint codePoint = (uint)data.CodePoint.Value;
            if (!UnicodeUtility.IsDefaultIgnorableCodePoint(codePoint))
            {
                return Skip.No;
            }

            if (codePoint == 0x200C)
            {
                return this.ignoreZwnj ? Skip.Maybe : Skip.No;
            }

            if (codePoint == 0x200D)
            {
                return this.ignoreZwj ? Skip.Maybe : Skip.No;
            }

            if (IsHidden(codePoint))
            {
                return this.ignoreHidden ? Skip.Maybe : Skip.No;
            }

            return Skip.Maybe;
        }
    }

    /// <summary>
    /// Modified for Agent DVR: compares a glyph with one element of a lookup's glyph sequence. Implemented by
    /// structs so matching is generic over them rather than going through allocated delegates.
    /// </summary>
    internal interface IGlyphMatcher
    {
        bool Matches(int sequenceIndex, GlyphShapingData data);
    }

    /// <summary>Matches glyph ids.</summary>
    internal readonly struct GlyphIdMatcher : IGlyphMatcher
    {
        private readonly ushort[] sequence;

        public GlyphIdMatcher(ushort[] sequence) => this.sequence = sequence;

        public bool Matches(int sequenceIndex, GlyphShapingData data) => this.sequence[sequenceIndex] == data.GlyphId;
    }

    /// <summary>Matches glyph classes.</summary>
    internal readonly struct GlyphClassMatcher : IGlyphMatcher
    {
        private readonly ushort[] sequence;
        private readonly ClassDefinitionTable classDefinitionTable;

        public GlyphClassMatcher(ushort[] sequence, ClassDefinitionTable classDefinitionTable)
        {
            this.sequence = sequence;
            this.classDefinitionTable = classDefinitionTable;
        }

        public bool Matches(int sequenceIndex, GlyphShapingData data)
            => this.sequence[sequenceIndex] == this.classDefinitionTable.ClassIndexOf(data.GlyphId);
    }

    /// <summary>Matches coverage tables, starting <c>offset</c> elements into the array.</summary>
    internal readonly struct CoverageMatcher : IGlyphMatcher
    {
        private readonly CoverageTable[] sequence;
        private readonly int offset;

        public CoverageMatcher(CoverageTable[] sequence, int offset = 0)
        {
            this.sequence = sequence;
            this.offset = offset;
        }

        public bool Matches(int sequenceIndex, GlyphShapingData data)
            => this.sequence[sequenceIndex + this.offset].CoverageIndexOf(data.GlyphId) >= 0;
    }

    /// <summary>Matches ligature components: the glyph id, on a glyph the feature applies to.</summary>
    internal readonly struct LigatureComponentMatcher : IGlyphMatcher
    {
        private readonly ushort[] sequence;
        private readonly Tag feature;

        public LigatureComponentMatcher(ushort[] sequence, Tag feature)
        {
            this.sequence = sequence;
            this.feature = feature;
        }

        public bool Matches(int sequenceIndex, GlyphShapingData data)
        {
            if (this.sequence[sequenceIndex] != data.GlyphId)
            {
                return false;
            }

            foreach (TagEntry entry in data.Features)
            {
                if (entry.Tag == this.feature && entry.Enabled)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
