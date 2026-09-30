// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.Shapers
{
    /// <summary>
    /// An individual shaping stage.
    /// Each stage must have a feature tag but can also contain pre and post feature processing operations.
    /// </summary>
    /// <remarks>
    /// For comparison purposes we only care about the feature tag as we want to avoid duplication.
    /// </remarks>
    internal readonly struct ShapingStage : IEquatable<ShapingStage>
    {
        private readonly Action<IGlyphShapingCollection, int, int>? preAction;
        private readonly Action<IGlyphShapingCollection, int, int>? postAction;

        public ShapingStage(Tag featureTag, Action<IGlyphShapingCollection, int, int>? preAction, Action<IGlyphShapingCollection, int, int>? postAction, bool standalone = false)
        {
            this.FeatureTag = featureTag;
            this.preAction = preAction;
            this.postAction = postAction;
            this.Standalone = standalone;
        }

        public Tag FeatureTag { get; }

        /// <summary>
        /// Gets a value indicating whether the feature must be applied on its own, completely, before the next
        /// one starts (HarfBuzz's pauses; e.g. each Indic basic feature and each Arabic joining form).
        /// </summary>
        public bool Standalone { get; }

        /// <summary>
        /// Gets a value indicating whether the stage's lookups can be merged with its neighbours' (applied in
        /// one pass in lookup order). Modified for Agent DVR.
        /// </summary>
        public bool CanMerge => this.preAction is null && this.postAction is null && !this.Standalone;

        public void PreProcessFeature(IGlyphShapingCollection collection, int index, int count)
            => this.preAction?.Invoke(collection, index, count);

        public void PostProcessFeature(IGlyphShapingCollection collection, int index, int count)
            => this.postAction?.Invoke(collection, index, count);

        public override bool Equals(object? obj)
            => obj is ShapingStage stage && this.Equals(stage);

        public bool Equals(ShapingStage other) => this.FeatureTag.Equals(other.FeatureTag);

        public override int GetHashCode() => HashCode.Combine(this.FeatureTag);
    }
}
