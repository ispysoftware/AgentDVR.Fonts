// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;

namespace SixLabors.Fonts.Tables.AdvancedTypographic.GPos
{
    /// <summary>
    /// Cursive Attachment Positioning Subtable.
    /// Some cursive fonts are designed so that adjacent glyphs join when rendered with their default positioning.
    /// However, if positioning adjustments are needed to join the glyphs, a cursive attachment positioning (CursivePos) subtable can describe
    /// how to connect the glyphs by aligning two anchor points: the designated exit point of a glyph, and the designated entry point of the following glyph.
    /// <see href="https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#cursive-attachment-positioning-format1-cursive-attachment"/>
    /// </summary>
    internal static class LookupType3SubTable
    {
        public static LookupSubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
        {
            reader.Seek(offset, SeekOrigin.Begin);
            ushort posFormat = reader.ReadUInt16();

            return posFormat switch
            {
                1 => LookupType3Format1SubTable.Load(reader, offset, lookupFlags),
                _ => new NotImplementedSubTable(),
            };
        }

        internal sealed class LookupType3Format1SubTable : LookupSubTable
        {
            private readonly CoverageTable coverageTable;
            private readonly EntryExitAnchors[] entryExitAnchors;

            public LookupType3Format1SubTable(CoverageTable coverageTable, EntryExitAnchors[] entryExitAnchors, LookupFlags lookupFlags)
                : base(lookupFlags)
            {
                this.coverageTable = coverageTable;
                this.entryExitAnchors = entryExitAnchors;
            }

            public static LookupType3Format1SubTable Load(BigEndianBinaryReader reader, long offset, LookupFlags lookupFlags)
            {
                // Cursive Attachment Positioning Format1.
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Type               |  Name                           | Description                                          |
                // +====================+=================================+======================================================+
                // | uint16             | posFormat                       | Format identifier: format = 1                        |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | Offset16           | coverageOffset                  | Offset to Coverage table,                            |
                // |                    |                                 | from beginning of CursivePos subtable.               |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | uint16             | entryExitCount                  | Number of EntryExit records.                         |
                // +--------------------+---------------------------------+------------------------------------------------------+
                // | EntryExitRecord    | entryExitRecord[entryExitCount] | Array of EntryExit records, in Coverage index order. |
                // +--------------------+---------------------------------+------------------------------------------------------+
                ushort coverageOffset = reader.ReadOffset16();
                ushort entryExitCount = reader.ReadUInt16();
                var entryExitRecords = new EntryExitRecord[entryExitCount];
                for (int i = 0; i < entryExitCount; i++)
                {
                    entryExitRecords[i] = new EntryExitRecord(reader, offset);
                }

                var entryExitAnchors = new EntryExitAnchors[entryExitCount];
                for (int i = 0; i < entryExitCount; i++)
                {
                    entryExitAnchors[i] = new EntryExitAnchors(reader, offset, entryExitRecords[i]);
                }

                var coverageTable = CoverageTable.Load(reader, offset + coverageOffset);

                return new LookupType3Format1SubTable(coverageTable, entryExitAnchors, lookupFlags);
            }

            public override bool TryUpdatePosition(
                FontMetrics fontMetrics,
                GPosTable table,
                GlyphPositioningCollection collection,
                Tag feature,
                int index,
                int count)
            {
                // Implements Cursive Attachment Positioning Subtable:
                // https://docs.microsoft.com/en-us/typography/opentype/spec/gpos#lookup-type-3-cursive-attachment-positioning-subtable
                // Modified for Agent DVR, following HarfBuzz: the current glyph's entry anchor joins the exit
                // anchor of the previous glyph the lookup sees (it paired this glyph's exit with the raw next
                // glyph's entry, so a mark between two letters broke the join); the RightToLeft flag decides
                // which glyph of the pair stays on the baseline the right way round (it was inverted);
                // "no attachment" is 0 rather than -1, which is also a real link to the previous glyph.
                ushort glyphId = collection[index].GlyphId;
                if (glyphId == 0)
                {
                    return false;
                }

                int coverage = this.coverageTable.CoverageIndexOf(glyphId);
                if ((uint)coverage >= (uint)this.entryExitAnchors.Length
                    || this.entryExitAnchors[coverage].EntryAnchor is not AnchorTable entry)
                {
                    return false;
                }

                SkippingGlyphIterator iterator = SkippingGlyphIterator.ForContext(fontMetrics, collection, index, this.LookupFlags, this.MarkFilteringSet, feature, index + count);
                int previousIndex = iterator.Previous();
                if (previousIndex < 0)
                {
                    return false;
                }

                int previousCoverage = this.coverageTable.CoverageIndexOf(collection[previousIndex].GlyphId);
                if ((uint)previousCoverage >= (uint)this.entryExitAnchors.Length
                    || this.entryExitAnchors[previousCoverage].ExitAnchor is not AnchorTable exit)
                {
                    return false;
                }

                GlyphShapingData previous = collection[previousIndex];
                GlyphShapingData current = collection[index];
                AnchorXY exitXY = exit.GetAnchor(fontMetrics, previous, collection);
                AnchorXY entryXY = entry.GetAnchor(fontMetrics, current, collection);

                // Main-direction adjustment.
                bool horizontal = !AdvancedTypographicUtils.IsVerticalGlyph(current.CodePoint, collection.TextOptions.LayoutMode);
                if (horizontal)
                {
                    if (current.Direction == TextDirection.LeftToRight)
                    {
                        previous.Bounds.Width = exitXY.XCoordinate + previous.Bounds.X;

                        int delta = entryXY.XCoordinate + current.Bounds.X;
                        current.Bounds.Width -= delta;
                        current.Bounds.X -= delta;
                    }
                    else
                    {
                        int delta = exitXY.XCoordinate + previous.Bounds.X;
                        previous.Bounds.Width -= delta;
                        previous.Bounds.X -= delta;

                        current.Bounds.Width = entryXY.XCoordinate + current.Bounds.X;
                    }
                }
                else
                {
                    if (current.Direction == TextDirection.LeftToRight)
                    {
                        previous.Bounds.Height = exitXY.YCoordinate + previous.Bounds.Y;

                        int delta = entryXY.YCoordinate + current.Bounds.Y;
                        current.Bounds.Height -= delta;
                        current.Bounds.Y -= delta;
                    }
                    else
                    {
                        int delta = exitXY.YCoordinate + previous.Bounds.Y;
                        previous.Bounds.Height -= delta;
                        previous.Bounds.Y -= delta;

                        current.Bounds.Height = entryXY.YCoordinate + current.Bounds.Y;
                    }
                }

                // Cross-direction adjustment: the child aligns itself against its parent; the root of the chain
                // stays on the baseline. With RightToLeft the last glyph is the root.
                int child = previousIndex;
                int parent = index;
                int xOffset = entryXY.XCoordinate - exitXY.XCoordinate;
                int yOffset = entryXY.YCoordinate - exitXY.YCoordinate;
                if ((this.LookupFlags & LookupFlags.RightToLeft) == 0)
                {
                    (child, parent) = (parent, child);
                    xOffset = -xOffset;
                    yOffset = -yOffset;
                }

                // If child was already connected to someone else, walk through its old chain and reverse the
                // link direction, so the whole tree of its previous connection now attaches to the new parent.
                ReverseCursiveMinorOffset(collection, child, horizontal, parent, AdvancedTypographicUtils.MaxContextLength);

                GlyphShapingData c = collection[child];
                c.CursiveAttachment = parent - child;
                c.MarkAttachment = -1;
                if (horizontal)
                {
                    c.Bounds.Y = yOffset;
                }
                else
                {
                    c.Bounds.X = xOffset;
                }

                // If parent was attached to child, separate them.
                GlyphShapingData p = collection[parent];
                if (p.CursiveAttachment == -c.CursiveAttachment)
                {
                    p.CursiveAttachment = 0;
                    if (horizontal)
                    {
                        p.Bounds.Y = 0;
                    }
                    else
                    {
                        p.Bounds.X = 0;
                    }
                }

                return true;
            }

            private static void ReverseCursiveMinorOffset(
                GlyphPositioningCollection collection,
                int i,
                bool horizontal,
                int newParent,
                int depth)
            {
                GlyphShapingData c = collection[i];
                int chain = c.CursiveAttachment;
                if (chain == 0 || depth == 0)
                {
                    return;
                }

                c.CursiveAttachment = 0;

                int j = i + chain;

                // Stop if we see the new parent in the chain.
                if (j == newParent || (uint)j >= (uint)collection.Count)
                {
                    return;
                }

                ReverseCursiveMinorOffset(collection, j, horizontal, newParent, depth - 1);

                GlyphShapingData p = collection[j];
                if (horizontal)
                {
                    p.Bounds.Y = -c.Bounds.Y;
                }
                else
                {
                    p.Bounds.X = -c.Bounds.X;
                }

                p.CursiveAttachment = -chain;
            }
        }
    }
}
