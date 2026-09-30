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

## Building

`src/SixLabors.Fonts/AgentDVR.Fonts.csproj` is a self-contained net10.0 project with no submodules
or custom package feeds. The upstream test and sample projects are kept for reference but are not
maintained.
