// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using SixLabors.Fonts.Tables.AdvancedTypographic.GSub;
using SixLabors.Fonts.Tables.AdvancedTypographic.Shapers;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts.Tables.AdvancedTypographic
{
    /// <summary>
    /// The Glyph Substitution (GSUB) table provides data for substitution of glyphs for appropriate rendering of scripts,
    /// such as cursively-connecting forms in Arabic script, or for advanced typographic effects, such as ligatures.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gsub"/>
    /// </summary>
    internal class GSubTable : Table
    {
        internal const string TableName = "GSUB";

        public GSubTable(ScriptList? scriptList, FeatureListTable featureList, LookupListTable lookupList)
        {
            this.ScriptList = scriptList;
            this.FeatureList = featureList;
            this.LookupList = lookupList;
        }

        public ScriptList? ScriptList { get; }

        public FeatureListTable FeatureList { get; }

        public LookupListTable LookupList { get; }

        public static GSubTable? Load(FontReader fontReader)
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

        internal static GSubTable Load(BigEndianBinaryReader reader)
        {
            // GSUB Header, Version 1.0
            // +----------+-------------------+-----------------------------------------------------------+
            // | Type     | Name              | Description                                               |
            // +==========+===================+===========================================================+
            // | uint16   | majorVersion      | Major version of the GSUB table, = 1                      |
            // +----------+-------------------+-----------------------------------------------------------+
            // | uint16   | minorVersion      | Minor version of the GSUB table, = 0                      |
            // +----------+-------------------+-----------------------------------------------------------+
            // | Offset16 | scriptListOffset  | Offset to ScriptList table, from beginning of GSUB table  |
            // +----------+-------------------+-----------------------------------------------------------+
            // | Offset16 | featureListOffset | Offset to FeatureList table, from beginning of GSUB table |
            // +----------+-------------------+-----------------------------------------------------------+
            // | Offset16 | lookupListOffset  | Offset to LookupList table, from beginning of GSUB table  |
            // +----------+-------------------+-----------------------------------------------------------+

            // GSUB Header, Version 1.1
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Type     | Name                    | Description                                                                   |
            // +==========+=========================+===============================================================================+
            // | uint16   | majorVersion            | Major version of the GSUB table, = 1                                          |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | uint16   | minorVersion            | Minor version of the GSUB table, = 1                                          |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset16 | scriptListOffset        | Offset to ScriptList table, from beginning of GSUB table                      |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset16 | featureListOffset       | Offset to FeatureList table, from beginning of GSUB table                     |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset16 | lookupListOffset        | Offset to LookupList table, from beginning of GSUB table                      |
            // +----------+-------------------------+-------------------------------------------------------------------------------+
            // | Offset32 | featureVariationsOffset | Offset to FeatureVariations table, from beginning of GSUB table (may be NULL) |
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
            return new GSubTable(scriptList, featureList, lookupList);
        }

        public void ApplySubstitution(FontMetrics fontMetrics, GlyphSubstitutionCollection collection)
        {
            // Set max constraints to prevent OutOfMemoryException or infinite loops from attacks.
            int maxCount = AdvancedTypographicUtils.GetMaxAllowableShapingCollectionCount(collection.Count);
            int maxOperationsCount = AdvancedTypographicUtils.GetMaxAllowableShapingOperationsCount(collection.Count);
            int currentOperations = 0;
            AdvancedTypographicUtils.BeginShapingPass(maxOperationsCount);

            for (int i = 0; i < collection.Count; i++)
            {
                // Choose a shaper based on the script.
                // This determines which features to apply to which glyphs.
                ScriptClass current = CodePoint.GetScriptClass(collection[i].CodePoint);

                int index = i;
                int count = 1;
                while (i < collection.Count - 1)
                {
                    // We want to assign the same feature lookups to individual sections of the text rather
                    // than the text as a whole to ensure that different language shapers do not interfere
                    // with each other when the text contains multiple languages.
                    ScriptClass next = CodePoint.GetScriptClass(collection[i + 1].CodePoint);
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

                // Plan substitution features for each glyph.
                // Shapers can adjust the count during initialization and feature processing so we must capture
                // the current count to allow resetting indexes and processing counts.
                int collectionCount = collection.Count;
                shaper.Plan(collection, index, count);
                int delta = collection.Count - collectionCount;
                i += delta;
                count += delta;

                // Modified for Agent DVR: consecutive stages with no pre/post action that aren't marked standalone
                // are applied together - their lookups merged, each applied once, in lookup-list order (see
                // AdvancedTypographicUtils.CollectLookups) - with features taken from the language system
                // HarfBuzz would choose.
                LangSysTable? langSys = AdvancedTypographicUtils.SelectLangSys(this.ScriptList, current);
                List<ShapingStage> stages = new(shaper.GetShapingStages());
                List<Tag> group = new();
                SkippingGlyphIterator iterator = new(fontMetrics, collection, index, default);
                int s = 0;
                while (s < stages.Count)
                {
                    ShapingStage stage = stages[s];
                    group.Clear();
                    if (stage.CanMerge)
                    {
                        while (s < stages.Count && stages[s].CanMerge)
                        {
                            group.Add(stages[s].FeatureTag);
                            s++;
                        }

                        List<FeatureLookup> merged = AdvancedTypographicUtils.CollectLookups(this.FeatureList, this.LookupList.LookupTables.Length, langSys, group);
                        this.ApplyLookups(fontMetrics, collection, ref iterator, merged, index, ref count, ref i, maxCount, maxOperationsCount, ref currentOperations);
                        continue;
                    }

                    collectionCount = collection.Count;
                    stage.PreProcessFeature(collection, index, count);

                    // Account for substitutions changing the length of the collection.
                    delta = collection.Count - collectionCount;
                    count += delta;
                    i += delta;

                    group.Add(stage.FeatureTag);
                    List<FeatureLookup> lookups = AdvancedTypographicUtils.CollectLookups(this.FeatureList, this.LookupList.LookupTables.Length, langSys, group);
                    this.ApplyLookups(fontMetrics, collection, ref iterator, lookups, index, ref count, ref i, maxCount, maxOperationsCount, ref currentOperations);

                    collectionCount = collection.Count;
                    stage.PostProcessFeature(collection, index, count);

                    // Account for substitutions changing the length of the collection.
                    delta = collection.Count - collectionCount;
                    count += delta;
                    i += delta;
                    s++;
                }
            }
        }

        internal void ApplyFeature(
            FontMetrics fontMetrics,
            GlyphSubstitutionCollection collection,
            ref SkippingGlyphIterator iterator,
            in Tag featureTag,
            ScriptClass current,
            int index,
            ref int count,
            ref int i,
            ref int collectionCount,
            int maxCount,
            int maxOperationsCount,
            ref int currentOperations)
        {
            if (this.TryGetFeatureLookups(in featureTag, current, out List<FeatureLookup>? lookups))
            {
                this.ApplyLookups(fontMetrics, collection, ref iterator, lookups, index, ref count, ref i, maxCount, maxOperationsCount, ref currentOperations);
            }

            collectionCount = collection.Count;
        }

        internal bool TryGetFeatureLookups(
            in Tag feature,
            ScriptClass script,
            [NotNullWhen(true)] out List<FeatureLookup>? value)
        {
            LangSysTable? langSys = AdvancedTypographicUtils.SelectLangSys(this.ScriptList, script);
            value = AdvancedTypographicUtils.CollectLookups(this.FeatureList, this.LookupList.LookupTables.Length, langSys, new[] { feature });
            return value.Count > 0;
        }

        /// <summary>
        /// Applies each lookup across the run, at every glyph the lookup doesn't ignore that has one of the
        /// lookup's features enabled.
        /// Modified for Agent DVR: the walk starts at the first glyph the lookup doesn't ignore (it applied at
        /// the run start regardless), uses the lookup's mark filtering set, and continues where the lookup
        /// says - after a matched context, a ligature, a multiple substitution's output - instead of at the
        /// next glyph, so glyphs a lookup consumed or produced aren't processed by it again (and a deleted
        /// glyph no longer made the following one be skipped).
        /// </summary>
        private void ApplyLookups(
            FontMetrics fontMetrics,
            GlyphSubstitutionCollection collection,
            ref SkippingGlyphIterator iterator,
            List<FeatureLookup> lookups,
            int index,
            ref int count,
            ref int i,
            int maxCount,
            int maxOperationsCount,
            ref int currentOperations)
        {
            foreach (FeatureLookup featureLookup in lookups)
            {
                if (!AdvancedTypographicUtils.TryGetAt(this.LookupList.LookupTables, featureLookup.LookupIndex, out LookupTable? lookupTable))
                {
                    continue;
                }

                iterator.Reset(index - 1, lookupTable.LookupFlags, lookupTable.MarkFilteringSet);
                iterator.Next();
                while (iterator.Index < index + count)
                {
                    if (collection.Count >= maxCount || currentOperations++ >= maxOperationsCount)
                    {
                        return;
                    }

                    if (!featureLookup.TryGetEnabledFeature(collection[iterator.Index].Features, out Tag feature))
                    {
                        iterator.Next();
                        continue;
                    }

                    int collectionCount = collection.Count;
                    AdvancedTypographicUtils.TakeResumeIndex();
                    lookupTable.TrySubstitution(fontMetrics, this, collection, feature, iterator.Index, count - (iterator.Index - index));

                    // Account for substitutions changing the length of the collection.
                    int delta = collection.Count - collectionCount;
                    count += delta;
                    i += delta;

                    int resume = AdvancedTypographicUtils.TakeResumeIndex();
                    if (resume >= 0)
                    {
                        iterator.Index = resume - 1;
                    }

                    iterator.Next();
                }
            }
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

    }
}
