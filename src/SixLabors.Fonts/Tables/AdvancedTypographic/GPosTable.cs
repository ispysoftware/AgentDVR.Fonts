// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using SixLabors.Fonts.Tables.AdvancedTypographic.GPos;
using SixLabors.Fonts.Tables.AdvancedTypographic.Shapers;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts.Tables.AdvancedTypographic
{
    /// <summary>
    /// The Glyph Positioning table (GPOS) provides precise control over glyph placement for
    /// sophisticated text layout and rendering in each script and language system that a font supports.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gpos"/>
    /// </summary>
    internal class GPosTable : Table
    {
        private static readonly Tag KernTag = Tag.Parse("kern");

        private static readonly Tag VKernTag = Tag.Parse("vkrn");

        internal const string TableName = "GPOS";

        public GPosTable(ScriptList? scriptList, FeatureListTable featureList, LookupListTable lookupList)
        {
            this.ScriptList = scriptList;
            this.FeatureList = featureList;
            this.LookupList = lookupList;
        }

        public ScriptList? ScriptList { get; }

        public FeatureListTable FeatureList { get; }

        public LookupListTable LookupList { get; }

        public static GPosTable? Load(FontReader fontReader)
        {
            if (!fontReader.TryGetReaderAtTablePosition(TableName, out BigEndianBinaryReader? binaryReader))
            {
                return null;
            }

            using (binaryReader)
            {
                return Load(binaryReader);
            }
        }

        internal static GPosTable Load(BigEndianBinaryReader reader)
        {
            // GPOS Header, Version 1.0
            // +----------+-------------------+-----------------------------------------------------------+
            // | Type     | Name              | Description                                               |
            // +==========+===================+===========================================================+
            // | uint16   | majorVersion      | Major version of the GPOS table, = 1                      |
            // +----------+-------------------+-----------------------------------------------------------+
            // | uint16   | minorVersion      | Minor version of the GPOS table, = 0                      |
            // +----------+-------------------+-----------------------------------------------------------+
            // | Offset16 | scriptListOffset  | Offset to ScriptList table, from beginning of GPOS table  |
            // +----------+-------------------+-----------------------------------------------------------+
            // | Offset16 | featureListOffset | Offset to FeatureList table, from beginning of GPOS table |
            // +----------+-------------------+-----------------------------------------------------------+
            // | Offset16 | lookupListOffset  | Offset to LookupList table, from beginning of GPOS table  |
            // +----------+-------------------+-----------------------------------------------------------+

            // GPOS Header, Version 1.1
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Type     | Name                    | Description                                                                   |
            // +==========+=========================+===============================================================================+
            // | uint16   | majorVersion            | Major version of the GPOS table, = 1                                          |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | uint16   | minorVersion            | Minor version of the GPOS table, = 1                                          |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset16 | scriptListOffset        | Offset to ScriptList table, from beginning of GPOS table                      |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset16 | featureListOffset       | Offset to FeatureList table, from beginning of GPOS table                     |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset16 | lookupListOffset        | Offset to LookupList table, from beginning of GPOS table                      |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset32 | featureVariationsOffset | Offset to FeatureVariations table, from beginning of GPOS table (may be NULL) |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            ushort majorVersion = reader.ReadUInt16();
            ushort minorVersion = reader.ReadUInt16();

            ushort scriptListOffset = reader.ReadOffset16();
            ushort featureListOffset = reader.ReadOffset16();
            ushort lookupListOffset = reader.ReadOffset16();
            uint featureVariationsOffset = (minorVersion == 1) ? reader.ReadOffset32() : 0;

            // TODO: Optimization. Allow only reading the scriptList.
            var scriptList = ScriptList.Load(reader, scriptListOffset);

            var featureList = FeatureListTable.Load(reader, featureListOffset);

            var lookupList = LookupListTable.Load(reader, lookupListOffset);

            // TODO: Feature Variations.
            return new GPosTable(scriptList, featureList, lookupList);
        }

        public bool TryUpdatePositions(FontMetrics fontMetrics, GlyphPositioningCollection collection, out bool kerned)
        {
            // Set max constraints to prevent OutOfMemoryException or infinite loops from attacks.
            int maxCount = AdvancedTypographicUtils.GetMaxAllowableShapingCollectionCount(collection.Count);
            int maxOperationsCount = AdvancedTypographicUtils.GetMaxAllowableShapingOperationsCount(collection.Count);
            int currentOperations = 0;
            bool maxOperationsReached = false;
            AdvancedTypographicUtils.BeginShapingPass(maxOperationsCount);

            kerned = false;
            bool updated = false;
            for (int i = 0; i < collection.Count; i++)
            {
                if (!collection.ShouldProcess(fontMetrics, i))
                {
                    continue;
                }

                ScriptClass current = CodePoint.GetScriptClass(collection[i].CodePoint);

                int index = i;
                int count = 1;
                while (i < collection.Count - 1)
                {
                    // We want to assign the same feature lookups to individual sections of the text rather
                    // than the text as a whole to ensure that different language shapers do not interfere
                    // with each other when the text contains multiple languages.
                    // Modified for Agent DVR: a run also ends where the font changes (fallback fonts), so
                    // this font's lookups are never matched against another font's glyph ids.
                    if (!collection.ShouldProcess(fontMetrics, i + 1))
                    {
                        break;
                    }

                    GlyphShapingData nextData = collection[i + 1];
                    ScriptClass next = CodePoint.GetScriptClass(nextData.CodePoint);
                    if (next != current &&
                        current is not ScriptClass.Common and not ScriptClass.Unknown and not ScriptClass.Inherited &&
                        next is not ScriptClass.Common and not ScriptClass.Unknown and not ScriptClass.Inherited)
                    {
                        break;
                    }

                    if (current is ScriptClass.Common or ScriptClass.Unknown or ScriptClass.Inherited)
                    {
                        current = next;
                    }

                    i++;
                    count++;

                    if (i >= maxCount)
                    {
                        break;
                    }
                }

                // Modified for Agent DVR: the shaper is chosen for what this font can render, and is given
                // this font (it used the text run's primary font, wrong during a fallback pass).
                Tag unicodeScriptTag = this.GetUnicodeScriptTag(current);
                ScriptClass shaperScript = AdvancedTypographicUtils.GetShaperScript(collection, index, count, current);
                BaseShaper shaper = ShaperFactory.Create(shaperScript, unicodeScriptTag, fontMetrics, collection.TextOptions);

                if (shaper.MarkZeroingMode == MarkZeroingMode.PreGPos)
                {
                    ZeroMarkAdvances(fontMetrics, collection, index, count);
                }

                // Plan positioning features for each glyph.
                shaper.Plan(collection, index, count);

                // Modified for Agent DVR: every positioning feature is applied in one pass - lookups merged, each
                // applied once, in lookup-list order - as the spec and HarfBuzz do (a PairPos lookup shared by
                // 'dist' and 'kern' was applied twice). Stage actions only act on substitution, so there are
                // no pauses here. Features come from the language system HarfBuzz would choose.
                List<Tag> features = new();
                foreach (ShapingStage stage in shaper.GetShapingStages())
                {
                    features.Add(stage.FeatureTag);
                }

                LangSysTable? langSys = AdvancedTypographicUtils.SelectLangSys(this.ScriptList, current);
                List<FeatureLookup> lookups = AdvancedTypographicUtils.CollectLookups(this.FeatureList, this.LookupList.LookupTables.Length, langSys, features);
                SkippingGlyphIterator iterator = new(fontMetrics, collection, index, default);
                foreach (FeatureLookup featureLookup in lookups)
                {
                    if (!AdvancedTypographicUtils.TryGetAt(this.LookupList.LookupTables, featureLookup.LookupIndex, out LookupTable? lookupTable))
                    {
                        continue;
                    }

                    // Start at the first glyph the lookup doesn't ignore, and continue where the lookup says
                    // (after a matched context, or past an adjusted second glyph of a pair).
                    iterator.Reset(index - 1, lookupTable.LookupFlags, lookupTable.MarkFilteringSet);
                    iterator.Next();
                    while (iterator.Index < index + count)
                    {
                        if (currentOperations++ >= maxOperationsCount)
                        {
                            maxOperationsReached = true;
                            goto EndLookups;
                        }

                        if (!featureLookup.TryGetEnabledFeature(collection[iterator.Index].Features, out Tag feature))
                        {
                            iterator.Next();
                            continue;
                        }

                        AdvancedTypographicUtils.TakeResumeIndex();
                        bool success = lookupTable.TryUpdatePosition(fontMetrics, this, collection, feature, iterator.Index, count - (iterator.Index - index));
                        kerned |= success && (feature == KernTag || feature == VKernTag);
                        updated |= success;

                        int resume = AdvancedTypographicUtils.TakeResumeIndex();
                        if (resume >= 0)
                        {
                            iterator.Index = resume - 1;
                        }

                        iterator.Next();
                    }
                }

                EndLookups:
                if (shaper.MarkZeroingMode == MarkZeroingMode.PostGpos)
                {
                    ZeroMarkAdvances(fontMetrics, collection, index, count);
                }

                PropagateAttachmentOffsets(collection, index, count);
                UpdatePositions(fontMetrics, collection, index, count);

                if (i >= maxCount || maxOperationsReached)
                {
                    return updated;
                }
            }

            return updated;
        }

        private Tag GetUnicodeScriptTag(ScriptClass script)
        {
            if (this.ScriptList is null)
            {
                return default;
            }

            Tag[] tags = UnicodeScriptTagMap.Instance[script];
            for (int i = 0; i < tags.Length; i++)
            {
                if (this.ScriptList.TryGetValue(tags[i].Value, out ScriptListTable? _))
                {
                    return tags[i];
                }
            }

            return default;
        }

        /// <summary>
        /// Adds to each attached glyph the offset of the glyph it is attached to (cursive and mark attachment).
        /// Modified for Agent DVR: resolved recursively, parent first, with a depth cap, as HarfBuzz does. The
        /// cursive pass went in index order, so a chain longer than two glyphs (or one pointing forward, as
        /// with the RightToLeft flag) got partial offsets, and it compared an absolute index with the run
        /// length and returned, abandoning the rest of the run.
        /// </summary>
        private static void PropagateAttachmentOffsets(GlyphPositioningCollection collection, int index, int count)
        {
            LayoutMode layoutMode = collection.TextOptions.LayoutMode;
            int end = Math.Min(index + count, collection.Count);
            for (int i = index; i < end; i++)
            {
                PropagateAttachmentOffset(collection, i, layoutMode, AdvancedTypographicUtils.MaxContextLength);
            }
        }

        private static void PropagateAttachmentOffset(GlyphPositioningCollection collection, int i, LayoutMode layoutMode, int depth)
        {
            GlyphShapingData data = collection[i];
            if (data.CursiveAttachment != 0)
            {
                int j = i + data.CursiveAttachment;
                data.CursiveAttachment = 0;
                if ((uint)j >= (uint)collection.Count || depth == 0)
                {
                    return;
                }

                PropagateAttachmentOffset(collection, j, layoutMode, depth - 1);
                GlyphShapingData parent = collection[j];
                if (!AdvancedTypographicUtils.IsVerticalGlyph(data.CodePoint, layoutMode))
                {
                    data.Bounds.Y += parent.Bounds.Y;
                }
                else
                {
                    data.Bounds.X += parent.Bounds.X;
                }
            }
            else if (data.MarkAttachment >= 0)
            {
                int j = data.MarkAttachment;
                data.MarkAttachment = -1;
                if (j >= i || depth == 0)
                {
                    return;
                }

                PropagateAttachmentOffset(collection, j, layoutMode, depth - 1);
                GlyphShapingData parent = collection[j];
                data.Bounds.X += parent.Bounds.X;
                data.Bounds.Y += parent.Bounds.Y;

                if (data.Direction == TextDirection.LeftToRight)
                {
                    for (int k = j; k < i; k++)
                    {
                        GlyphShapingData between = collection[k];
                        data.Bounds.X -= between.Bounds.Width;
                        data.Bounds.Y -= between.Bounds.Height;
                    }
                }
                else
                {
                    for (int k = j + 1; k < i + 1; k++)
                    {
                        GlyphShapingData between = collection[k];
                        data.Bounds.X += between.Bounds.Width;
                        data.Bounds.Y += between.Bounds.Height;
                    }
                }
            }
        }

        private static void ZeroMarkAdvances(FontMetrics fontMetrics, GlyphPositioningCollection collection, int index, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int currentIndex = i + index;
                GlyphShapingData data = collection[currentIndex];
                if (AdvancedTypographicUtils.IsMarkGlyph(fontMetrics, data.GlyphId, data))
                {
                    data.Bounds.Width = 0;
                    data.Bounds.Height = 0;
                }
            }
        }

        private static void UpdatePositions(FontMetrics fontMetrics, GlyphPositioningCollection collection, int index, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int currentIndex = i + index;
                collection.UpdatePosition(fontMetrics, currentIndex);
            }
        }
    }
}
