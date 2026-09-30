// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using SixLabors.Fonts.Unicode;
using SixLabors.Fonts.WellKnownIds;

namespace SixLabors.Fonts.Tables.General.CMap
{
    /// <summary>
    /// cmap formats 12 (segmented coverage) and 13 (many-to-one range mappings), which share a layout.
    /// Modified for Agent DVR: groups are binary searched (they are sorted by start code; this scanned them
    /// all per character, slow in CJK fonts), format 13 is supported, glyph 0 or an id above 65535 is
    /// "not found", and Format reports 12/13 (it said 4).
    /// </summary>
    internal sealed class Format12SubTable : CMapSubTable
    {
        private readonly bool manyToOne;

        public Format12SubTable(uint language, PlatformIDs platform, ushort encoding, SequentialMapGroup[] groups, bool manyToOne = false)
            : base(platform, encoding, manyToOne ? (ushort)13 : (ushort)12)
        {
            this.Language = language;
            this.SequentialMapGroups = groups;
            this.manyToOne = manyToOne;
        }

        public SequentialMapGroup[] SequentialMapGroups { get; }

        public uint Language { get; }

        public override bool TryGetGlyphId(CodePoint codePoint, out ushort glyphId)
        {
            glyphId = 0;
            uint c = (uint)codePoint.Value;
            SequentialMapGroup[] groups = this.SequentialMapGroups;

            int lo = 0;
            int hi = groups.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref SequentialMapGroup group = ref groups[mid];
                if (c < group.StartCodePoint)
                {
                    hi = mid - 1;
                }
                else if (c > group.EndCodePoint)
                {
                    lo = mid + 1;
                }
                else
                {
                    uint gid = this.manyToOne ? group.StartGlyphId : group.StartGlyphId + (c - group.StartCodePoint);
                    if (gid == 0 || gid > ushort.MaxValue)
                    {
                        return false;
                    }

                    glyphId = (ushort)gid;
                    return true;
                }
            }

            return false;
        }

        public override IEnumerable<int> GetAvailableCodePoints()
            => this.SequentialMapGroups.SelectMany(segment =>
            {
                int start = (int)segment.StartCodePoint;
                int end = (int)segment.EndCodePoint;
                return Enumerable.Range(start, end - start + 1);
            });

        public static IEnumerable<Format12SubTable> Load(IEnumerable<EncodingRecord> encodings, BigEndianBinaryReader reader)
            => Load(encodings, reader, false);

        public static IEnumerable<Format12SubTable> Load(IEnumerable<EncodingRecord> encodings, BigEndianBinaryReader reader, bool manyToOne)
        {
            // 'cmap' Subtable Format 4:
            // Type               | Name              | Description
            // -------------------|-------------------|------------------------------------------------------------------------
            // uint16             | format            | Subtable format; set to 12.
            // uint16             | reserved          | Reserved; set to 0
            // uint32             | length            | Byte length of this subtable(including the header)
            // uint32             | language          | For requirements on use of the language field, see “Use of the language field in 'cmap' subtables” in this document.
            // uint32             | numGroups         | Number of groupings which follow
            // SequentialMapGroup | groups[numGroups] | Array of SequentialMapGroup records.

            // format has already been read by this point skip it
            ushort reserved = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            uint language = reader.ReadUInt32();
            uint numGroups = reader.ReadUInt32();

            // Modified for Agent DVR: never trust a count beyond what the subtable's own length can hold.
            numGroups = System.Math.Min(numGroups, length >= 16 ? (length - 16) / 12 : 0);

            var groups = new SequentialMapGroup[numGroups];
            for (var i = 0; i < numGroups; i++)
            {
                groups[i] = SequentialMapGroup.Load(reader);
            }

            foreach (EncodingRecord encoding in encodings)
            {
                yield return new Format12SubTable(language, encoding.PlatformID, encoding.EncodingID, groups, manyToOne);
            }
        }

        internal readonly struct SequentialMapGroup
        {
            public readonly uint StartCodePoint;
            public readonly uint EndCodePoint;
            public readonly uint StartGlyphId;

            public SequentialMapGroup(uint startCodePoint, uint endCodePoint, uint startGlyph)
            {
                this.StartCodePoint = startCodePoint;
                this.EndCodePoint = endCodePoint;
                this.StartGlyphId = startGlyph;
            }

            public static SequentialMapGroup Load(BigEndianBinaryReader reader)
            {
                var startCodePoint = reader.ReadUInt32();
                var endCodePoint = reader.ReadUInt32();
                var startGlyph = reader.ReadUInt32();
                return new SequentialMapGroup(startCodePoint, endCodePoint, startGlyph);
            }
        }
    }
}
