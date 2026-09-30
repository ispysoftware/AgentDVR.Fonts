# AgentDVR.Fonts

Font loading, text shaping and layout for [Agent DVR](https://www.ispyconnect.com): TrueType,
OpenType (CFF) and font collections, GSUB/GPOS shaping including Arabic, Hangul, Indic and the
Universal Shaping Engine, and bidirectional text. Glyph outlines are handed to the caller through
`IGlyphRenderer`; Agent DVR rasterises them itself.

## Origin and licence

This is a modified fork of **SixLabors.Fonts v1.0.1** (2023-09-15), the last release published
under the [Apache License 2.0](LICENSE). It is maintained independently and is not affiliated with
or endorsed by Six Labors. Upstream relicensed later versions under the Six Labors Split License;
no code from those versions is included here.

Original copyright notices are retained in each source file. Every change from v1.0.1 is listed
in [AGENT-CHANGES.md](AGENT-CHANGES.md); modified code is also marked "Modified for Agent DVR".

The C# namespaces remain `SixLabors.Fonts` so the code stays comparable with its origin; the
assembly is `AgentDVR.Fonts`.

## Performance

Measured 2026-09-30 with BenchmarkDotNet (short run, memory diagnoser) on an AMD Ryzen 7 9800X3D,
Windows 11, .NET 10.0.10 x64. Text is 40px, with Agent DVR's fallback font list; caches are warm.
Times are per call; allocations are per call.

| String | Font |
|---|---|
| Timestamp: `30/09/2026 12:34:56 FPS: 25` | Arial |
| Long Latin: a 90-character camera caption | Segoe UI |
| Devanagari: `क्षत्रिय र्कि कु कं हिन्दी` | Nirmala UI |
| Arabic: `مُحَمَّد لا سلام عليكم 12:34` | Segoe UI |
| Mixed with fallback: `Camera 1 क्षत्रिय 👩‍💻 مرحبا` | Arial + fallbacks |

**Measure** (shaping and layout only, advance and ink bounds):

| String | AgentDVR.Fonts | SixLabors.Fonts 2.1.3 | SixLabors.Fonts 3.0.0 |
|---|---|---|---|
| Timestamp | **16 µs** / 42 KB | 550 µs / 247 KB | 267 µs / 160 KB |
| Long Latin | **63 µs** / 134 KB | 899 µs / 761 KB | 359 µs / 499 KB |
| Devanagari | **55 µs** / 145 KB | 742 µs / 843 KB | 387 µs / 665 KB |
| Arabic | **49 µs** / 47 KB | 1,301 µs / 322 KB | 313 µs / 190 KB |
| Mixed with fallback | **57 µs** / 137 KB | 1,510 µs / 4,795 KB | 477 µs / 1,017 KB |

**Measure and render** to an 8-bit coverage mask:

| String | AgentDVR.Fonts + Agent DVR rasteriser | Fonts 2.1.3 + ImageSharp.Drawing 2.1.7 | Fonts 3.0.0 + ImageSharp.Drawing 3.0.0 |
|---|---|---|---|
| Timestamp | **65 µs** / 112 KB | 1,532 µs / 1,748 KB | 739 µs / 861 KB |
| Long Latin | **247 µs** / 366 KB | 3,532 µs / 4,943 KB | 1,318 µs / 2,409 KB |
| Devanagari | **149 µs** / 314 KB | 1,815 µs / 2,650 KB | 1,058 µs / 1,894 KB |
| Arabic | **133 µs** / 122 KB | 2,556 µs / 1,971 KB | 846 µs / 1,067 KB |
| Mixed with fallback | **147 µs** / 301 KB | 2,977 µs / 8,773 KB | 1,645 µs / 3,696 KB |

Notes:
- Each library is measured with its fastest API for getting both measurements: this fork's
  `TextMeasurer.MeasureAdvanceAndBounds` (one layout), Fonts 3.0.0's `TextBlock` (one layout), and
  Fonts 2.1.3's `MeasureAdvance` + `MeasureBounds` (it has no single-layout call).
- The render column is not library against library alone. Ours includes Agent DVR's scanline
  rasteriser, which is not part of this repository; the others draw into an `Image<L8>` with
  ImageSharp.Drawing.
- Output was compared with Fonts 3.0.0: every string above gets the same box size, and the
  Devanagari and mixed strings, compared by eye, render the same (conjuncts, reph and marks, Arabic
  joining, fallback). The only visible difference is the emoji: Agent DVR draws a colour font's
  plain outline, where ImageSharp flattens its colour layers.
- Newer Six Labors releases have features this fork doesn't, such as the `TextBlock` API. These are
  figures from one machine; yours will differ.

## Building

`src/SixLabors.Fonts/AgentDVR.Fonts.csproj` is a self-contained net10.0 project with no submodules
or custom package feeds. The upstream test and sample projects are kept for reference but are not
maintained.
