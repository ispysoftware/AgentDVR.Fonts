// Copyright (c) The Playful Group.
// Licensed under the Apache License, Version 2.0.
// Added for Agent DVR (not part of SixLabors.Fonts v1.0.1).

using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.Fonts.Unicode;
using SixLabors.Fonts.WellKnownIds;

namespace SixLabors.Fonts.Tables.General.CMap
{
    /// <summary>
    /// cmap formats 6 (trimmed table mapping, 16-bit codes) and 10 (trimmed array, 32-bit codes): a dense
    /// run of glyph ids for the character codes starting at <see cref="FirstCode"/>. Neither was read, so
    /// a font whose only usable subtable used them mapped nothing.
    /// </summary>
    internal sealed class TrimmedArraySubTable : CMapSubTable
    {
        private readonly ushort[] glyphIds;

        private TrimmedArraySubTable(PlatformIDs platform, ushort encoding, ushort format, uint firstCode, ushort[] glyphIds)
            : base(platform, encoding, format)
        {
            this.FirstCode = firstCode;
            this.glyphIds = glyphIds;
        }

        public uint FirstCode { get; }

        public override bool TryGetGlyphId(CodePoint codePoint, out ushort glyphId)
        {
            ulong index = (ulong)(uint)codePoint.Value - this.FirstCode;
            glyphId = index < (ulong)this.glyphIds.Length ? this.glyphIds[index] : (ushort)0;
            return glyphId != 0;
        }

        public override IEnumerable<int> GetAvailableCodePoints()
            => Enumerable.Range(0, this.glyphIds.Length)
                .Where(i => this.glyphIds[i] != 0)
                .Select(i => (int)(this.FirstCode + (uint)i));

        public static IEnumerable<TrimmedArraySubTable> LoadFormat6(IEnumerable<EncodingRecord> encodings, BigEndianBinaryReader reader)
        {
            // Format has already been read.
            // uint16 length | uint16 language | uint16 firstCode | uint16 entryCount | uint16 glyphIdArray[entryCount]
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            ushort firstCode = reader.ReadUInt16();
            ushort entryCount = reader.ReadUInt16();
            ushort[] glyphIds = reader.ReadUInt16Array(entryCount);
            return encodings.Select(e => new TrimmedArraySubTable(e.PlatformID, e.EncodingID, 6, firstCode, glyphIds)).ToArray();
        }

        public static IEnumerable<TrimmedArraySubTable> LoadFormat10(IEnumerable<EncodingRecord> encodings, BigEndianBinaryReader reader)
        {
            // Format has already been read.
            // uint16 reserved | uint32 length | uint32 language | uint32 startCharCode | uint32 numChars | uint16 glyphIdArray[numChars]
            _ = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            uint startCharCode = reader.ReadUInt32();
            uint numChars = reader.ReadUInt32();

            // Never trust a count beyond what the subtable's own length can hold.
            uint maxChars = length > 24 ? (length - 24) / 2 : 0;
            ushort[] glyphIds = reader.ReadUInt16Array((int)Math.Min(numChars, Math.Min(maxChars, (uint)ushort.MaxValue * 16)));
            return encodings.Select(e => new TrimmedArraySubTable(e.PlatformID, e.EncodingID, 10, startCharCode, glyphIds)).ToArray();
        }
    }
}
