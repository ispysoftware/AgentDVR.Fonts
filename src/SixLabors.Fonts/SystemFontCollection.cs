// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SixLabors.Fonts
{
    /// <summary>
    /// Provides a collection of fonts.
    /// </summary>
    internal sealed class SystemFontCollection : IReadOnlySystemFontCollection, IReadOnlyFontMetricsCollection
    {
        private readonly FontCollection collection;
        private readonly IReadOnlyCollection<string> searchDirectories;

        /// <summary>
        /// Gets the default set of locations we probe for System Fonts.
        /// </summary>
        private static readonly IReadOnlyCollection<string> StandardFontLocations;

        static SystemFontCollection()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                StandardFontLocations = new[]
                {
                    @"%SYSTEMROOT%\Fonts",
                    @"%APPDATA%\Microsoft\Windows\Fonts",
                    @"%LOCALAPPDATA%\Microsoft\Windows\Fonts",
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                StandardFontLocations = new[]
                {
                    "%HOME%/.fonts/",
                    "%HOME%/.local/share/fonts/",
                    "/usr/local/share/fonts/",
                    "/usr/share/fonts/",
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                StandardFontLocations = new[]
                {
                    // As documented on "Mac OS X: Font locations and their purposes"
                    // https://web.archive.org/web/20191015122508/https://support.apple.com/en-us/HT201722
                    "%HOME%/Library/Fonts/",
                    "/Library/Fonts/",
                    "/System/Library/Fonts/",
                    "/Network/Library/Fonts/",
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("Android")))
            {
                StandardFontLocations = new[]
                {
                    "/system/fonts/"
                };
            }
            else
            {
                StandardFontLocations = Array.Empty<string>();
            }
        }

        public SystemFontCollection()
        {
            IEnumerable<string> paths;
            Native.MacSystemFontsEnumerator? nativeEnumerator = null;

            bool forceDirectoryEnumeration = AppContext.TryGetSwitch("Switch.SixLabors.Fonts.DoNotUseNativeSystemFontsEnumeration", out bool isEnabled) && isEnabled;
            if (!forceDirectoryEnumeration && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                nativeEnumerator = new Native.MacSystemFontsEnumerator();

                // The CTFontManagerCopyAvailableFontURLs method might return duplicate paths, hence the call to Distinct()
                paths = nativeEnumerator.Distinct();

                this.searchDirectories = Array.Empty<string>();
            }
            else
            {
                string[] expanded = StandardFontLocations.Select(x => Environment.ExpandEnvironmentVariables(x)).ToArray();
                string[] existingDirectories = expanded.Where(x => Directory.Exists(x)).ToArray();

                paths = EnumerateFontFiles(existingDirectories).ToList();

                this.searchDirectories = existingDirectories;
            }

            this.collection = CreateSystemFontCollection(paths, this.searchDirectories);

            nativeEnumerator?.Dispose();
        }

        /// <inheritdoc/>
        public IEnumerable<FontFamily> Families => this.collection.Families;

        /// <inheritdoc/>
        public IEnumerable<string> SearchDirectories => this.searchDirectories;

        /// <inheritdoc/>
        public FontFamily Get(string name) => this.collection.Get(name);

        /// <inheritdoc/>
        public bool TryGet(string name, out FontFamily family)
            => this.collection.TryGet(name, out family);

        /// <inheritdoc/>
        public IEnumerable<FontFamily> GetByCulture(CultureInfo culture)
            => this.collection.GetByCulture(culture);

        /// <inheritdoc/>
        public FontFamily Get(string name, CultureInfo culture)
            => this.collection.Get(name, culture);

        /// <inheritdoc/>
        public bool TryGet(string name, CultureInfo culture, out FontFamily family)
            => this.collection.TryGet(name, culture, out family);

        /// <inheritdoc/>
        bool IReadOnlyFontMetricsCollection.TryGetMetrics(string name, CultureInfo culture, FontStyle style, [NotNullWhen(true)] out FontMetrics? metrics)
            => ((IReadOnlyFontMetricsCollection)this.collection).TryGetMetrics(name, culture, style, out metrics);

        /// <inheritdoc/>
        IEnumerable<FontMetrics> IReadOnlyFontMetricsCollection.GetAllMetrics(string name, CultureInfo culture)
            => ((IReadOnlyFontMetricsCollection)this.collection).GetAllMetrics(name, culture);

        /// <inheritdoc/>
        IEnumerable<FontStyle> IReadOnlyFontMetricsCollection.GetAllStyles(string name, CultureInfo culture)
            => ((IReadOnlyFontMetricsCollection)this.collection).GetAllStyles(name, culture);

        /// <inheritdoc/>
        IEnumerator<FontMetrics> IReadOnlyFontMetricsCollection.GetEnumerator()
            => ((IReadOnlyFontMetricsCollection)this.collection).GetEnumerator();

        /// <summary>
        /// Modified for Agent DVR: walks the font directories so that one unreadable folder, or a symlink
        /// loop, can't break system font enumeration for the whole process. It used a lazy AllDirectories
        /// enumeration whose IO errors surfaced outside the per-font try/catch and failed the collection.
        /// Each directory is visited once (by resolved path) to a bounded depth. Extensions are matched
        /// case-insensitively for a consistent experience on case-sensitive file systems.
        /// </summary>
        /// <param name="roots">The directories to search.</param>
        /// <returns>The font file paths.</returns>
        private static IEnumerable<string> EnumerateFontFiles(IEnumerable<string> roots)
        {
            const int maxDepth = 16;
            HashSet<string> visited = new(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Stack<(string Path, int Depth)> pending = new();
            foreach (string root in roots.Reverse())
            {
                pending.Push((root, 0));
            }

            while (pending.Count > 0)
            {
                (string dir, int depth) = pending.Pop();
                string key;
                try
                {
                    key = Directory.ResolveLinkTarget(dir, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(dir);
                }
                catch
                {
                    continue;
                }

                if (!visited.Add(key))
                {
                    continue;
                }

                string[] files;
                string[] subdirectories;
                try
                {
                    files = Directory.GetFiles(dir);
                    subdirectories = depth < maxDepth ? Directory.GetDirectories(dir) : Array.Empty<string>();
                }
                catch
                {
                    // Unreadable (permissions, broken link, removed while enumerating) - skip it.
                    continue;
                }

                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file);
                    if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
                        || IsCollection(file))
                    {
                        yield return file;
                    }
                }

                for (int i = subdirectories.Length - 1; i >= 0; i--)
                {
                    pending.Push((subdirectories[i], depth + 1));
                }
            }
        }

        private static bool IsCollection(string path)
            => path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".otc", StringComparison.OrdinalIgnoreCase);

        private static FontCollection CreateSystemFontCollection(IEnumerable<string> paths, IReadOnlyCollection<string> searchDirectories)
        {
            var collection = new FontCollection(searchDirectories);

            foreach (string path in paths)
            {
                try
                {
                    if (IsCollection(path))
                    {
                        collection.AddCollection(path);
                    }
                    else
                    {
                        collection.Add(path);
                    }
                }
                catch
                {
                    // We swallow exceptions installing system fonts as we hold no guarantees about permissions etc.
                }
            }

            return collection;
        }
    }
}
