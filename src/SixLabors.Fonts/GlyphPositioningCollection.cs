// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SixLabors.Fonts.Tables.AdvancedTypographic;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts
{
    /// <summary>
    /// Represents a collection of glyph metrics that are mapped to input codepoints.
    /// </summary>
    internal sealed class GlyphPositioningCollection : IGlyphShapingCollection
    {
        /// <summary>
        /// Contains a map the index of a map within the collection, non-sequential codepoint offsets, and their glyph ids, point size, and mtrics.
        /// </summary>
        private readonly List<GlyphPositioningData> glyphs = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphPositioningCollection"/> class.
        /// </summary>
        /// <param name="textOptions">The text options.</param>
        public GlyphPositioningCollection(TextOptions textOptions) => this.TextOptions = textOptions;

        /// <inheritdoc />
        public int Count => this.glyphs.Count;

        /// <inheritdoc />
        public TextOptions TextOptions { get; }

        /// <inheritdoc />
        public GlyphShapingData this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.glyphs[index].Data;
        }

        /// <inheritdoc />
        public void AddShapingFeature(int index, TagEntry feature)
            => this.glyphs[index].Data.Features.Add(feature);

        /// <inheritdoc />
        public void EnableShapingFeature(int index, Tag feature)
        {
            List<TagEntry> features = this.glyphs[index].Data.Features;
            for (int i = 0; i < features.Count; i++)
            {
                TagEntry tagEntry = features[i];
                if (tagEntry.Tag == feature)
                {
                    tagEntry.Enabled = true;
                    features[i] = tagEntry;
                    break;
                }
            }
        }

        /// <inheritdoc />
        public void DisableShapingFeature(int index, Tag feature)
        {
            List<TagEntry> features = this.glyphs[index].Data.Features;
            for (int i = 0; i < features.Count; i++)
            {
                TagEntry tagEntry = features[i];
                if (tagEntry.Tag == feature)
                {
                    tagEntry.Enabled = false;
                    features[i] = tagEntry;
                    break;
                }
            }
        }

        /// <summary>
        /// Gets the glyph metrics at the given codepoint offset.
        /// </summary>
        /// <param name="offset">The zero-based index within the input codepoint collection.</param>
        /// <param name="pointSize">The font size in PT units of the font containing this glyph.</param>
        /// <param name="isDecomposed">Whether the glyph is the result of a decomposition substitution.</param>
        /// <param name="metrics">
        /// When this method returns, contains the glyph metrics associated with the specified offset,
        /// if the value is found; otherwise, the default value for the type of the metrics parameter.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <returns>The metrics.</returns>
        public bool TryGetGlyphMetricsAtOffset(int offset, out float pointSize, out bool isDecomposed, [NotNullWhen(true)] out IReadOnlyList<GlyphMetrics>? metrics)
        {
            // Modified for Agent DVR: offsets are sorted (reordering moves glyph data, not offsets), so the
            // first glyph at the offset is found by binary search, and a single glyph's metrics array is
            // returned as is. It scanned from the start and built a new list for every code point - quadratic,
            // and the biggest single cost of laying out a line.
            int i = this.LowerBound(offset);
            if (i >= this.glyphs.Count || this.glyphs[i].Offset != offset)
            {
                pointSize = 0;
                isDecomposed = false;
                metrics = null;
                return false;
            }

            GlyphPositioningData first = this.glyphs[i];
            isDecomposed = first.Data.IsDecomposed;
            pointSize = first.PointSize;
            if (i + 1 >= this.glyphs.Count || this.glyphs[i + 1].Offset != offset)
            {
                metrics = first.Metrics;
                return true;
            }

            List<GlyphMetrics> match = new(first.Metrics);
            for (i++; i < this.glyphs.Count && this.glyphs[i].Offset == offset; i++)
            {
                GlyphPositioningData glyph = this.glyphs[i];
                isDecomposed = glyph.Data.IsDecomposed;
                pointSize = glyph.PointSize;
                match.AddRange(glyph.Metrics);
            }

            metrics = match;
            return true;
        }

        private int LowerBound(int offset)
        {
            int low = 0;
            int high = this.glyphs.Count;
            while (low < high)
            {
                int mid = (int)((uint)(low + high) >> 1);
                if (this.glyphs[mid].Offset < offset)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        /// <summary>
        /// Modified for Agent DVR: gets a value indicating whether the font maps at least one of the characters
        /// still drawn with a placeholder, i.e. whether trying it as a fallback can change anything.
        /// </summary>
        /// <param name="fontMetrics">The fallback font.</param>
        /// <returns><see langword="true"/> if the font has a glyph for a missing character.</returns>
        public bool CanFillFallbacks(FontMetrics fontMetrics)
        {
            for (int i = 0; i < this.glyphs.Count; i++)
            {
                GlyphMetrics m = this.glyphs[i].Metrics[0];
                if (m.GlyphType == GlyphType.Fallback
                    && fontMetrics.TryGetGlyphId(m.CodePoint, out ushort glyphId)
                    && glyphId != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Updates the collection of glyph ids to the metrics collection to overwrite any glyphs that have been previously
        /// identified as fallbacks.
        /// </summary>
        /// <param name="font">The font face with metrics.</param>
        /// <param name="collection">The glyph substitution collection.</param>
        /// <returns><see langword="true"/> if the metrics collection does not contain any fallbacks; otherwise <see langword="false"/>.</returns>
        public bool TryUpdate(Font font, GlyphSubstitutionCollection collection)
        {
            FontMetrics fontMetrics = font.FontMetrics;
            LayoutMode layoutMode = this.TextOptions.LayoutMode;
            ColorFontSupport colorFontSupport = this.TextOptions.ColorFontSupport;
            bool hasFallBacks = false;
            List<int> orphans = new();
            for (int i = 0; i < this.glyphs.Count; i++)
            {
                GlyphPositioningData current = this.glyphs[i];
                if (current.Metrics[0].GlyphType != GlyphType.Fallback)
                {
                    // We've already got the correct glyph.
                    continue;
                }

                int offset = current.Offset;
                float pointSize = current.PointSize;
                if (collection.TryGetGlyphShapingDataAtOffset(offset, out IReadOnlyList<GlyphShapingData>? data))
                {
                    int replacementCount = 0;
                    for (int j = 0; j < data.Count; j++)
                    {
                        GlyphShapingData shape = data[j];
                        ushort id = shape.GlyphId;
                        CodePoint codePoint = shape.CodePoint;

                        // Perform a semi-deep clone (FontMetrics is not cloned) so we can continue to
                        // cache the original in the font metrics and only update our collection.
                        var metrics = new List<GlyphMetrics>(data.Count);
                        TextAttributes textAttributes = shape.TextRun.TextAttributes;
                        TextDecorations textDecorations = shape.TextRun.TextDecorations;
                        foreach (GlyphMetrics gm in fontMetrics.GetGlyphMetrics(codePoint, id, textAttributes, textDecorations, layoutMode, colorFontSupport))
                        {
                            if (gm.GlyphType == GlyphType.Fallback && !CodePoint.IsControl(codePoint))
                            {
                                // If the glyphs are fallbacks we don't want them as
                                // we've already captured them on the first run.
                                hasFallBacks = true;
                                break;
                            }

                            metrics.Add(gm.CloneForRendering(shape.TextRun, codePoint));
                        }

                        // Modified for Agent DVR: the fallback glyph is removed when the FIRST replacement is
                        // inserted (it checked j == 0, so when the first shaped glyph at this offset was itself
                        // missing the placeholder stayed alongside the replacements), and replacements go at
                        // i, i+1, i+2... (it inserted at i += replacementCount - i, i+1, i+3 - mispositioning a
                        // third glyph and skipping one on the way out).
                        if (metrics.Count > 0)
                        {
                            if (replacementCount == 0)
                            {
                                // There should only be a single fallback glyph at this position from the previous collection.
                                this.glyphs.RemoveAt(i);
                            }

                            // Track the number of inserted glyphs at the offset so we can correctly increment our position.
                            ushort maxAdvancedWidth = 0;
                            ushort maxAdvancedHeight = 0;
                            for (int k = 0; k < metrics.Count; k++)
                            {
                                maxAdvancedWidth = Math.Max(maxAdvancedWidth, metrics[k].AdvanceWidth);
                                maxAdvancedHeight = Math.Max(maxAdvancedHeight, metrics[k].AdvanceHeight);
                            }

                            // Modified for Agent DVR: the advance goes in the layout direction only, as TryAdd does.
                            // Setting both put the vertical advance (a whole line height) into horizontal text,
                            // and mark attachment subtracts the advances between base and mark - so every mark from
                            // a fallback font (Devanagari vowel signs, virama, reph...) was pushed a line down,
                            // out of the text box.
                            GlyphShapingBounds bounds = AdvancedTypographicUtils.IsVerticalGlyph(codePoint, layoutMode)
                                ? new(0, 0, 0, maxAdvancedHeight)
                                : new(0, 0, maxAdvancedWidth, 0);
                            this.glyphs.Insert(i + replacementCount, new(offset, new(shape, true) { Bounds = bounds }, pointSize, metrics.ToArray()));
                            replacementCount++;
                        }
                    }

                    // Continue after the inserted glyphs.
                    if (replacementCount > 0)
                    {
                        i += replacementCount - 1;
                    }
                }
                else
                {
                    // If a font had glyphs but a follow up font also has them and can substitute. e.g ligatures
                    // then we end up with orphaned fallbacks. We need to remove them.
                    orphans.Add(i);
                }
            }

            // Remove any orphans.
            for (int i = orphans.Count - 1; i >= 0; i--)
            {
                this.glyphs.RemoveAt(orphans[i]);
            }

            return !hasFallBacks;
        }

        /// <summary>
        /// Adds the collection of glyph ids to the metrics collection.
        /// identified as fallbacks.
        /// </summary>
        /// <param name="font">The font face with metrics.</param>
        /// <param name="collection">The glyph substitution collection.</param>
        /// <returns><see langword="true"/> if the metrics collection does not contain any fallbacks; otherwise <see langword="false"/>.</returns>
        public bool TryAdd(Font font, GlyphSubstitutionCollection collection)
        {
            bool hasFallBacks = false;
            FontMetrics fontMetrics = font.FontMetrics;
            LayoutMode layoutMode = this.TextOptions.LayoutMode;
            ColorFontSupport colorFontSupport = this.TextOptions.ColorFontSupport;

            for (int i = 0; i < collection.Count; i++)
            {
                GlyphShapingData data = collection.GetGlyphShapingData(i, out int offset);
                CodePoint codePoint = data.CodePoint;
                ushort id = data.GlyphId;

                // Perform a semi-deep clone (FontMetrics is not cloned) so we can continue to
                // cache the original in the font metrics and only update our collection.
                TextAttributes textAttributes = data.TextRun.TextAttributes;
                TextDecorations textDecorations = data.TextRun.TextDecorations;
                bool isVerticalLayout = AdvancedTypographicUtils.IsVerticalGlyph(codePoint, layoutMode);

                // Modified for Agent DVR: fill the array directly (indexed, so no boxed enumerator) instead
                // of a temporary list copied out with ToArray.
                IReadOnlyList<GlyphMetrics> source = fontMetrics.GetGlyphMetrics(codePoint, id, textAttributes, textDecorations, layoutMode, colorFontSupport);
                int count = source.Count;
                if (count > 0)
                {
                    GlyphMetrics[] gm = new GlyphMetrics[count];
                    for (int m = 0; m < count; m++)
                    {
                        GlyphMetrics metric = source[m];
                        if (metric.GlyphType == GlyphType.Fallback && !CodePoint.IsControl(codePoint))
                        {
                            hasFallBacks = true;
                        }

                        gm[m] = metric.CloneForRendering(data.TextRun, codePoint);
                    }

                    if (isVerticalLayout)
                    {
                        this.glyphs.Add(new(offset, new(data, true) { Bounds = new(0, 0, 0, gm[0].AdvanceHeight) }, font.Size, gm));
                    }
                    else
                    {
                        this.glyphs.Add(new(offset, new(data, true) { Bounds = new(0, 0, gm[0].AdvanceWidth, 0) }, font.Size, gm));
                    }
                }
            }

            return !hasFallBacks;
        }

        /// <summary>
        /// Updates the position of the glyph at the specified index.
        /// </summary>
        /// <param name="fontMetrics">The font metrics.</param>
        /// <param name="index">The zero-based index of the element.</param>
        public void UpdatePosition(FontMetrics fontMetrics, int index)
        {
            GlyphShapingData data = this[index];
            bool isDirtyXY = data.Bounds.IsDirtyXY;
            bool isDirtyWH = data.Bounds.IsDirtyWH;
            if (!isDirtyXY && !isDirtyWH)
            {
                return;
            }

            ushort glyphId = data.GlyphId;
            GlyphMetrics[] metrics = this.glyphs[index].Metrics;
            for (int i = 0; i < metrics.Length; i++)
            {
                GlyphMetrics m = metrics[i];
                if (m.GlyphId == glyphId && fontMetrics == m.FontMetrics)
                {
                    if (isDirtyXY)
                    {
                        m.ApplyOffset((short)data.Bounds.X, (short)data.Bounds.Y);
                    }

                    if (isDirtyWH)
                    {
                        // Modified for Agent DVR: advances are unsigned; a negative one (cursive attachment
                        // can pull an advance below zero) is clamped rather than wrapping to ~65535.
                        m.SetAdvanceWidth((ushort)Math.Clamp(data.Bounds.Width, 0, ushort.MaxValue));
                        m.SetAdvanceHeight((ushort)Math.Clamp(data.Bounds.Height, 0, ushort.MaxValue));
                    }
                }
            }
        }

        /// <summary>
        /// Modified for Agent DVR: default-ignorable code points (bidi marks such as LRM/RLM, ZWJ/ZWNJ,
        /// ZWSP, variation selectors, BOM, soft hyphen...) take no space once positioning is done, as
        /// HarfBuzz does. They were already not drawn, but kept the font's advance for their glyph, leaving
        /// visible gaps - e.g. in .NET date strings for right-to-left cultures, which contain LRM/RLM.
        /// They stay in the run through substitution and positioning so joining/ligature context still
        /// sees them, and a glyph a lookup substituted keeps its advance.
        /// </summary>
        /// <param name="fontMetrics">The font whose glyphs were just positioned.</param>
        internal void HideDefaultIgnorables(FontMetrics fontMetrics)
        {
            for (int i = 0; i < this.glyphs.Count; i++)
            {
                GlyphPositioningData glyph = this.glyphs[i];
                GlyphShapingData data = glyph.Data;
                if (data.IsSubstituted || !GlyphMetrics.ShouldSkipGlyphRendering(data.CodePoint))
                {
                    continue;
                }

                foreach (GlyphMetrics m in glyph.Metrics)
                {
                    if (m.FontMetrics == fontMetrics)
                    {
                        m.SetAdvanceWidth(0);
                        m.SetAdvanceHeight(0);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the advanced metrics of the glyphs at the given index and id,
        /// adding dx and dy to the current advance.
        /// </summary>
        /// <param name="fontMetrics">The font face with metrics.</param>
        /// <param name="index">The zero-based index of the element.</param>
        /// <param name="glyphId">The id of the glyph to offset.</param>
        /// <param name="dx">The delta x-advance.</param>
        /// <param name="dy">The delta y-advance.</param>
        public void Advance(FontMetrics fontMetrics, int index, ushort glyphId, short dx, short dy)
        {
            LayoutMode layoutMode = this.TextOptions.LayoutMode;
            foreach (GlyphMetrics m in this.glyphs[index].Metrics)
            {
                if (m.GlyphId == glyphId && fontMetrics == m.FontMetrics)
                {
                    m.ApplyAdvance(dx, AdvancedTypographicUtils.IsVerticalGlyph(m.CodePoint, layoutMode) ? dy : (short)0);
                }
            }
        }

        /// <summary>
        /// Returns a value indicating whether the element at the given index should be processed.
        /// </summary>
        /// <param name="fontMetrics">The font face with metrics.</param>
        /// <param name="index">The zero-based index of the elements to position.</param>
        /// <returns><see langword="true"/> if the element should be processed; otherwise, <see langword="false"/>.</returns>
        public bool ShouldProcess(FontMetrics fontMetrics, int index)
            => this.glyphs[index].Metrics[0].FontMetrics == fontMetrics;

        [DebuggerDisplay("{DebuggerDisplay,nq}")]
        private class GlyphPositioningData
        {
            public GlyphPositioningData(int offset, GlyphShapingData data, float pointSize, GlyphMetrics[] metrics)
            {
                this.Offset = offset;
                this.Data = data;
                this.PointSize = pointSize;
                this.Metrics = metrics;
            }

            public int Offset { get; set; }

            public GlyphShapingData Data { get; set; }

            public float PointSize { get; set; }

            public GlyphMetrics[] Metrics { get; set; }

            private string DebuggerDisplay => FormattableString.Invariant($"Offset: {this.Offset}, Data: {this.Data.ToDebuggerDisplay()}");
        }
    }
}
