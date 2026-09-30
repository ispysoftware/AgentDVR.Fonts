// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.Fonts.Tables.TrueType.Glyphs;
using SixLabors.Fonts.Unicode;

namespace SixLabors.Fonts.Tables.TrueType
{
    /// <summary>
    /// Represents a glyph metric from a particular TrueType font face.
    /// </summary>
    public class TrueTypeGlyphMetrics : GlyphMetrics
    {
        private static readonly Vector2 YInverter = new(1, -1);
        private readonly GlyphVector vector;
        private ConcurrentDictionary<float, GlyphVector>? scaledVectorCache;

        internal TrueTypeGlyphMetrics(
            StreamFontMetrics font,
            ushort glyphId,
            CodePoint codePoint,
            GlyphVector vector,
            ushort advanceWidth,
            ushort advanceHeight,
            short leftSideBearing,
            short topSideBearing,
            ushort unitsPerEM,
            TextAttributes textAttributes,
            TextDecorations textDecorations,
            GlyphType glyphType = GlyphType.Standard,
            GlyphColor? glyphColor = null)
            : base(
                  font,
                  glyphId,
                  codePoint,
                  vector.Bounds,
                  advanceWidth,
                  advanceHeight,
                  leftSideBearing,
                  topSideBearing,
                  unitsPerEM,
                  textAttributes,
                  textDecorations,
                  glyphType,
                  glyphColor)
            => this.vector = vector;

        internal TrueTypeGlyphMetrics(
            StreamFontMetrics font,
            ushort glyphId,
            CodePoint codePoint,
            GlyphVector vector,
            ushort advanceWidth,
            ushort advanceHeight,
            short leftSideBearing,
            short topSideBearing,
            ushort unitsPerEM,
            Vector2 offset,
            Vector2 scaleFactor,
            TextRun textRun,
            GlyphType glyphType = GlyphType.Standard,
            GlyphColor? glyphColor = null)
            : base(
                  font,
                  glyphId,
                  codePoint,
                  vector.Bounds,
                  advanceWidth,
                  advanceHeight,
                  leftSideBearing,
                  topSideBearing,
                  unitsPerEM,
                  offset,
                  scaleFactor,
                  textRun,
                  glyphType,
                  glyphColor)
            => this.vector = vector;

        /// <inheritdoc/>
        internal override GlyphMetrics CloneForRendering(TextRun textRun, CodePoint codePoint)
            => new TrueTypeGlyphMetrics(
                this.FontMetrics,
                this.GlyphId,
                codePoint,
                this.vector, // Modified for Agent DVR: shared - the outline is never modified (it was deep-copied per glyph per layout).
                this.AdvanceWidth,
                this.AdvanceHeight,
                this.LeftSideBearing,
                this.TopSideBearing,
                this.UnitsPerEm,
                this.Offset,
                this.ScaleFactor,
                textRun,
                this.GlyphType,
                this.GlyphColor);

        /// <summary>
        /// Gets the outline for the current glyph.
        /// </summary>
        /// <returns>The <see cref="GlyphVector"/>.</returns>
        internal GlyphVector GetOutline() => this.vector;

        /// <inheritdoc/>
        internal override void RenderTo(IGlyphRenderer renderer, Vector2 location, Vector2 offset, GlyphLayoutMode mode, TextOptions options)
        {
            // https://www.unicode.org/faq/unsup_char.html
            if (ShouldSkipGlyphRendering(this.CodePoint))
            {
                return;
            }

            float pointSize = this.TextRun.Font?.Size ?? options.Font.Size;
            float dpi = options.Dpi;

            // The glyph vector is rendered offset to the location.
            // For horizontal text, the offset is always zero but vertical or rotated text
            // will be offset against the location.
            location *= dpi;
            offset *= dpi;
            Vector2 renderLocation = location + offset;
            float scaledPPEM = this.GetScaledSize(pointSize, dpi);

            Matrix3x2 rotation = GetRotationMatrix(mode);
            FontRectangle box = this.GetBoundingBox(mode, renderLocation, scaledPPEM);
            GlyphRendererParameters parameters = new(this, this.TextRun, pointSize, dpi, mode);

            if (renderer.BeginGlyph(in box, in parameters))
            {
                if (!ShouldRenderWhiteSpaceOnly(this.CodePoint))
                {
                    if (this.GlyphColor.HasValue && renderer is IColorGlyphRenderer colorSurface)
                    {
                        colorSurface.SetColor(this.GlyphColor.Value);
                    }

                    // Modified for Agent DVR: unhinted glyphs (the default) are transformed point by point as they
                    // are emitted. Every render deep-copied and transformed the outline, cached in a dictionary
                    // allocated per glyph instance - and glyph instances are per layout, so the cache never hit.
                    // Hinted glyphs still cache the hinted outline per size.
                    Vector2 scale = new Vector2(scaledPPEM) / this.ScaleFactor;
                    GlyphVector outline;
                    Matrix3x2 transform;
                    if (options.HintingMode == HintingMode.None)
                    {
                        outline = this.vector;
                        transform = Matrix3x2.CreateScale(scale);
                        transform.Translation = this.Offset * scale;
                        transform *= rotation;
                    }
                    else
                    {
                        this.scaledVectorCache ??= new ConcurrentDictionary<float, GlyphVector>();
                        outline = this.scaledVectorCache.GetOrAdd(scaledPPEM, _ =>
                        {
                            // Create a scaled deep copy of the vector so that we do not alter
                            // the globally cached instance.
                            var clone = GlyphVector.DeepClone(this.vector);
                            var matrix = Matrix3x2.CreateScale(scale);
                            matrix.Translation = this.Offset * scale;
                            GlyphVector.TransformInPlace(ref clone, matrix);

                            float pixelSize = scaledPPEM / 72F;
                            this.FontMetrics.ApplyTrueTypeHinting(options.HintingMode, this, ref clone, scale, pixelSize);

                            // Rotation must happen after hinting.
                            GlyphVector.TransformInPlace(ref clone, rotation);
                            return clone;
                        });
                        transform = Matrix3x2.Identity;
                    }

                    IList<ControlPoint> controlPoints = outline.ControlPoints;
                    IReadOnlyList<ushort> endPoints = outline.EndPoints;

                    int endOfContour = -1;
                    for (int i = 0; i < endPoints.Count; i++)
                    {
                        renderer.BeginFigure();
                        int startOfContour = endOfContour + 1;
                        endOfContour = endPoints[i];

                        Vector2 curr =(YInverter * Vector2.Transform(controlPoints[endOfContour].Point, transform)) + renderLocation;
                        Vector2 next = (YInverter * Vector2.Transform(controlPoints[startOfContour].Point, transform)) + renderLocation;

                        if (controlPoints[endOfContour].OnCurve)
                        {
                            renderer.MoveTo(curr);
                        }
                        else
                        {
                            if (controlPoints[startOfContour].OnCurve)
                            {
                                renderer.MoveTo(next);
                            }
                            else
                            {
                                // If both first and last points are off-curve, start at their middle.
                                Vector2 startPoint = (curr + next) * .5F;
                                renderer.MoveTo(startPoint);
                            }
                        }

                        int length = endOfContour - startOfContour + 1;
                        for (int p = 0; p < length; p++)
                        {
                            curr = next;
                            int currentIndex = startOfContour + p;
                            int nextIndex = p + 1 < length ? currentIndex + 1 : startOfContour;
                            next = (YInverter * Vector2.Transform(controlPoints[nextIndex].Point, transform)) + renderLocation;

                            if (controlPoints[currentIndex].OnCurve)
                            {
                                // This is a straight line.
                                renderer.LineTo(curr);
                            }
                            else
                            {
                                // Modified for Agent DVR: the pen is already at the curve's start (the previous
                                // on-curve point, or the implied midpoint the previous curve ended on), so the two
                                // zero-length LineTo calls that preceded every curve are gone.
                                Vector2 next2 = next;
                                if (!controlPoints[nextIndex].OnCurve)
                                {
                                    next2 = (curr + next) * .5F;
                                }

                                renderer.QuadraticBezierTo(curr, next2);
                            }
                        }

                        renderer.EndFigure();
                    }
                }

                this.RenderDecorationsTo(renderer, location, mode, rotation, scaledPPEM);
            }

            renderer.EndGlyph();
        }
    }
}
