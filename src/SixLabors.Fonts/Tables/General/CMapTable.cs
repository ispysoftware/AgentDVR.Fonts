// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.Fonts.Tables.General.CMap;
using SixLabors.Fonts.Unicode;
using SixLabors.Fonts.WellKnownIds;

namespace SixLabors.Fonts.Tables.General
{
    internal sealed class CMapTable : Table
    {
        internal const string TableName = "cmap";

        private readonly Format14SubTable[] format14SubTables = Array.Empty<Format14SubTable>();
        private readonly CMapSubTable? mapping;
        private readonly bool isSymbol;
        private CodePoint[]? codepoints;

        public CMapTable(IEnumerable<CMapSubTable> tables)
        {
            this.Tables = tables.ToArray();
            this.format14SubTables = this.Tables.OfType<Format14SubTable>().ToArray();

            // Modified for Agent DVR: characters map through the ONE subtable the font intends, chosen in the
            // order implementations agree on. Consulting every subtable in turn resolved characters through
            // encodings the font keeps only for older readers - e.g. a symbol font's characters came from
            // its Macintosh byte table - and a glyph-0 answer fell through to the next table.
            this.mapping = this.Tables
                .Where(t => t is not Format14SubTable && GetEncodingRank(t) < int.MaxValue)
                .OrderBy(GetEncodingRank)
                .FirstOrDefault();
            this.isSymbol = this.mapping is { Platform: PlatformIDs.Windows, Encoding: 0 };
        }

        internal CMapSubTable[] Tables { get; }

        private static int GetEncodingRank(CMapSubTable t)
            => (t.Platform, t.Encoding) switch
            {
                (PlatformIDs.Windows, 0) => 0,   // Symbol: the font is a symbol font.
                (PlatformIDs.Windows, 10) => 1,  // Unicode, full repertoire.
                (PlatformIDs.Unicode, 6) => 2,
                (PlatformIDs.Unicode, 4) => 3,
                (PlatformIDs.Windows, 1) => 4,   // Unicode, BMP only.
                (PlatformIDs.Unicode, 3) => 5,
                (PlatformIDs.Unicode, 2) => 6,
                (PlatformIDs.Unicode, 1) => 7,
                (PlatformIDs.Unicode, 0) => 8,
                (PlatformIDs.Macintosh, 0) => 9, // Mac Roman: only when nothing else is offered.
                _ => int.MaxValue
            };

        public bool TryGetGlyphId(CodePoint codePoint, CodePoint? nextCodePoint, out ushort glyphId, out bool skipNextCodePoint)
        {
            skipNextCodePoint = false;
            if (this.TryGetGlyphId(codePoint, out glyphId))
            {
                // If there is a second codepoint, we are asked whether this is an UVS sequence
                // - If true, return a glyph Id.
                // - Otherwise, return 0.
                if (nextCodePoint != null && this.format14SubTables.Length > 0)
                {
                    foreach (Format14SubTable? cmap14 in this.format14SubTables)
                    {
                        ushort pairGlyphId = cmap14.CharacterPairToGlyphId(codePoint, glyphId, nextCodePoint.Value);
                        if (pairGlyphId > 0)
                        {
                            glyphId = pairGlyphId;
                            skipNextCodePoint = true;
                            return true;
                        }
                    }
                }

                return true;
            }

            return false;
        }

        private bool TryGetGlyphId(CodePoint codePoint, out ushort glyphId)
        {
            // Character codes with no glyph map to glyph 0 (.notdef), which counts as not found.
            if (this.mapping is not null)
            {
                if (this.mapping.TryGetGlyphId(codePoint, out glyphId) && glyphId > 0)
                {
                    return true;
                }

                // Symbol fonts address their glyphs one private-use page up (U+F000 + byte), which is how
                // Windows reaches them from 8-bit text.
                if (this.isSymbol && codePoint.Value <= 0xFF
                    && this.mapping.TryGetGlyphId(new CodePoint(0xF000 + codePoint.Value), out glyphId) && glyphId > 0)
                {
                    return true;
                }
            }

            glyphId = 0;
            return false;
        }

        /// <summary>
        /// Gets the unicode codepoints for which a glyph exists in the font.
        /// </summary>
        /// <returns>The <see cref="IReadOnlyList{CodePoint}"/>.</returns>
        public IReadOnlyList<CodePoint> GetAvailableCodePoints()
        {
            if (this.codepoints is not null)
            {
                return this.codepoints;
            }

            // Coverage comes from the subtable lookups use, so the font doesn't advertise characters it
            // won't resolve.
            HashSet<int> values = new();

            if (this.mapping is not null)
            {
                foreach (int v in this.mapping.GetAvailableCodePoints())
                {
                    values.Add(v);
                }
            }

            return this.codepoints = values.OrderBy(v => v).Select(v => new CodePoint(v)).ToArray();
        }

        public static CMapTable Load(FontReader reader)
        {
            using BigEndianBinaryReader binaryReader = reader.GetReaderAtTablePosition(TableName);
            return Load(binaryReader);
        }

        public static CMapTable Load(BigEndianBinaryReader reader)
        {
            ushort version = reader.ReadUInt16();
            ushort numTables = reader.ReadUInt16();

            var encodings = new EncodingRecord[numTables];
            for (int i = 0; i < numTables; i++)
            {
                encodings[i] = EncodingRecord.Read(reader);
            }

            // foreach encoding we move forward looking for the subtables
            var tables = new List<CMapSubTable>(numTables);
            foreach (IGrouping<uint, EncodingRecord> encoding in encodings.GroupBy(x => x.Offset))
            {
                long offset = encoding.Key;
                reader.Seek(offset, SeekOrigin.Begin);

                // Subtable format.
                switch (reader.ReadUInt16())
                {
                    case 0:
                        tables.AddRange(Format0SubTable.Load(encoding, reader));
                        break;
                    case 4:
                        tables.AddRange(Format4SubTable.Load(encoding, reader));
                        break;
                    case 6:
                        tables.AddRange(TrimmedArraySubTable.LoadFormat6(encoding, reader));
                        break;
                    case 10:
                        tables.AddRange(TrimmedArraySubTable.LoadFormat10(encoding, reader));
                        break;
                    case 12:
                        tables.AddRange(Format12SubTable.Load(encoding, reader, false));
                        break;
                    case 13:
                        tables.AddRange(Format12SubTable.Load(encoding, reader, true));
                        break;
                    case 14:
                        tables.AddRange(Format14SubTable.Load(encoding, reader, offset));
                        break;
                }
            }

            return new CMapTable(tables);
        }
    }
}
