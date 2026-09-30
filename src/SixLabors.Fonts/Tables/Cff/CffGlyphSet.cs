// Copyright (c) The Playful Group.
// Licensed under the Apache License, Version 2.0.
// Added for Agent DVR (not part of SixLabors.Fonts v1.0.1).

using System;

namespace SixLabors.Fonts.Tables.Cff
{
    /// <summary>
    /// The glyphs of a CFF font, created on request as views into the CharStrings INDEX. Replaces a
    /// <see cref="CffGlyphData"/> array built at load, which copied every charstring into its own array.
    /// Also resolves CID fonts' per-glyph font DICT (for local subroutines) from a flat FDSelect table,
    /// which fixes FDSelect format 0 fonts: they were not recognised as CID fonts, so every glyph ran
    /// without its local subroutines.
    /// </summary>
    internal sealed class CffGlyphSet
    {
        private readonly byte[] charStrings;
        private readonly int[] starts;
        private readonly byte[][] globalSubrs;
        private readonly byte[][] localSubrs;
        private readonly FontDict[] fontDicts;
        private readonly byte[]? fdSelect;
        private readonly int nominalWidthX;

        /// <param name="charStrings">The CharStrings INDEX object data.</param>
        /// <param name="starts">Glyph count + 1 offsets into <paramref name="charStrings"/>.</param>
        /// <param name="globalSubrs">The global subroutines.</param>
        /// <param name="localSubrs">The Private DICT's local subroutines (non-CID fonts).</param>
        /// <param name="fontDicts">The FDArray (CID fonts), else empty.</param>
        /// <param name="fdSelect">The font DICT index of each glyph (CID fonts), else null.</param>
        /// <param name="nominalWidthX">The nominal width.</param>
        public CffGlyphSet(byte[] charStrings, int[] starts, byte[][] globalSubrs, byte[][] localSubrs, FontDict[] fontDicts, byte[]? fdSelect, int nominalWidthX)
        {
            this.charStrings = charStrings;
            this.starts = starts;
            this.globalSubrs = globalSubrs;
            this.localSubrs = localSubrs;
            this.fontDicts = fontDicts;
            this.fdSelect = fdSelect;
            this.nominalWidthX = nominalWidthX;
        }

        public int Count => this.starts.Length - 1;

        /// <summary>
        /// Gets the glyph. An id past the glyph count, or one whose charstring lies outside the data, is empty.
        /// </summary>
        /// <param name="index">The glyph id.</param>
        /// <returns>The glyph.</returns>
        public CffGlyphData Get(int index)
        {
            if ((uint)index >= (uint)this.Count)
            {
                return CffGlyphData.Empty((ushort)index);
            }

            int start = this.starts[index];
            int end = Math.Min(this.starts[index + 1], this.charStrings.Length);
            if (start < 0 || end <= start)
            {
                return CffGlyphData.Empty((ushort)index);
            }

            byte[][] local = this.localSubrs;
            if (this.fdSelect is not null)
            {
                int fd = index < this.fdSelect.Length ? this.fdSelect[index] : 0;
                local = fd < this.fontDicts.Length ? this.fontDicts[fd].LocalSubr ?? Array.Empty<byte[]>() : Array.Empty<byte[]>();
            }

            return new CffGlyphData((ushort)index, this.globalSubrs, local, this.nominalWidthX, new ReadOnlyMemory<byte>(this.charStrings, start, end - start));
        }
    }
}
