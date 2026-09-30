// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.Fonts.Unicode;
using SixLabors.Fonts.WellKnownIds;

namespace SixLabors.Fonts.Tables.General.CMap
{
    internal sealed class Format4SubTable : CMapSubTable
    {
        public Format4SubTable(ushort language, PlatformIDs platform, ushort encoding, Segment[] segments, ushort[] glyphIds)
            : base(platform, encoding, 4)
        {
            this.Language = language;
            this.Segments = segments;
            this.GlyphIds = glyphIds;
        }

        public Segment[] Segments { get; }

        public ushort[] GlyphIds { get; }

        public ushort Language { get; }

        // Modified for Agent DVR, per the spec:
        // - segments are sorted by end code, so binary search instead of scanning them all per character;
        // - in the idRangeOffset branch idDelta is added to a non-zero glyphIdArray value (it was dropped),
        //   and the array index is bounds-checked (it could throw);
        // - glyph 0 is "missing" in both branches, so it is reported as not found.
        public override bool TryGetGlyphId(CodePoint codePoint, out ushort glyphId)
        {
            glyphId = 0;
            int c = codePoint.Value;
            Segment[] segments = this.Segments;

            int lo = 0;
            int hi = segments.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (segments[mid].End < c)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            if (segments.Length == 0 || segments[lo].End < c || segments[lo].Start > c)
            {
                return false;
            }

            ref Segment seg = ref segments[lo];
            int gid;
            if (seg.Offset == 0)
            {
                gid = c + seg.Delta;
            }
            else
            {
                long idx = (seg.Offset / 2) + (c - seg.Start) - segments.Length + seg.Index;
                if ((ulong)idx >= (ulong)this.GlyphIds.Length)
                {
                    return false;
                }

                gid = this.GlyphIds[idx];
                if (gid == 0)
                {
                    return false;
                }

                gid += seg.Delta;
            }

            glyphId = (ushort)(gid & ushort.MaxValue);
            return glyphId != 0;
        }

        public override IEnumerable<int> GetAvailableCodePoints()
            => this.Segments.SelectMany(segment => Enumerable.Range(segment.Start, segment.End - segment.Start + 1));

        public static IEnumerable<Format4SubTable> Load(IEnumerable<EncodingRecord> encodings, BigEndianBinaryReader reader)
        {
            // 'cmap' Subtable Format 4:
            // Type   | Name                       | Description
            // -------|----------------------------|------------------------------------------------------------------------
            // uint16 | format                     | Format number is set to 4.
            // uint16 | length                     | This is the length in bytes of the subtable.
            // uint16 | language                   | Please see “Note on the language field in 'cmap' subtables“ in this document.
            // uint16 | segCountX2                 | 2 x segCount.
            // uint16 | searchRange                | 2 x (2**floor(log2(segCount)))
            // uint16 | entrySelector              | log2(searchRange/2)
            // uint16 | rangeShift                 | 2 x segCount - searchRange
            // uint16 | endCount[segCount]         | End characterCode for each segment, last=0xFFFF.
            // uint16 | reservedPad                | Set to 0.
            // uint16 | startCount[segCount]       | Start character code for each segment.
            // int16  | idDelta[segCount]           | Delta for all character codes in segment.
            // uint16 | idRangeOffset[segCount]    | Offsets into glyphIdArray or 0
            // uint16 | glyphIdArray[ ]            | Glyph index array (arbitrary length)
            // format has already been read by this point skip it
            ushort length = reader.ReadUInt16();
            ushort language = reader.ReadUInt16();
            ushort segCountX2 = reader.ReadUInt16();
            ushort searchRange = reader.ReadUInt16();
            ushort entrySelector = reader.ReadUInt16();
            ushort rangeShift = reader.ReadUInt16();
            int segCount = segCountX2 / 2;

            using Buffer<ushort> endCountBuffer = new(segCount);
            Span<ushort> endCounts = endCountBuffer.GetSpan();
            reader.ReadUInt16Array(endCounts);

            ushort reserved = reader.ReadUInt16();

            using Buffer<ushort> startCountsBuffer = new(segCount);
            Span<ushort> startCounts = startCountsBuffer.GetSpan();
            reader.ReadUInt16Array(startCounts);

            using Buffer<short> idDeltaBuffer = new(segCount);
            Span<short> idDelta = idDeltaBuffer.GetSpan();
            reader.ReadInt16Array(idDelta);

            using Buffer<ushort> idRangeOffsetBuffer = new(segCount);
            Span<ushort> idRangeOffset = idRangeOffsetBuffer.GetSpan();
            reader.ReadUInt16Array(idRangeOffset);

            // table length thus far
            int headerLength = 16 + (segCount * 8);
            // Modified for Agent DVR: a malformed length shorter than the header no longer gives a negative count.
            int glyphIdCount = Math.Max(0, (length - headerLength) / 2);

            ushort[] glyphIds = reader.ReadUInt16Array(glyphIdCount);

            Segment[] segments = Segment.Create(endCounts, startCounts, idDelta, idRangeOffset);

            List<Format4SubTable> table = new();
            foreach (EncodingRecord encoding in encodings)
            {
                table.Add(new Format4SubTable(language, encoding.PlatformID, encoding.EncodingID, segments, glyphIds));
            }

            return table;
        }

        internal readonly struct Segment
        {
            public Segment(ushort index, ushort end, ushort start, short delta, ushort offset)
            {
                this.Index = index;
                this.End = end;
                this.Start = start;
                this.Delta = delta;
                this.Offset = offset;
            }

            public ushort Index { get; }

            public short Delta { get; }

            public ushort End { get; }

            public ushort Offset { get; }

            public ushort Start { get; }

            public static Segment[] Create(ReadOnlySpan<ushort> endCounts, ReadOnlySpan<ushort> startCode, ReadOnlySpan<short> idDelta, ReadOnlySpan<ushort> idRangeOffset)
            {
                int count = endCounts.Length;
                var segments = new Segment[count];
                for (ushort i = 0; i < count; i++)
                {
                    ushort start = startCode[i];
                    ushort end = endCounts[i];
                    short delta = idDelta[i];
                    ushort offset = idRangeOffset[i];
                    segments[i] = new Segment(i, end, start, delta, offset);
                }

                return segments;
            }
        }
    }
}
