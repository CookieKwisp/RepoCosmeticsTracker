using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RepoCosmeticTracker.Services
{
    public class AssetSearchResult
    {
        public string FilePath { get; init; } = "";
        public string MatchedLine { get; init; } = "";
    }

    /// <summary>
    /// Recursively greps text-based Unity export files (.prefab, .unity,
    /// .asset, etc.) for a keyword. Built because AssetRipper exports of a
    /// whole game can easily be tens of thousands of files — far too many
    /// to search by hand — but the actual files we care about (data assets,
    /// prefabs, scenes) are plain, readable YAML, so a simple text search
    /// is enough; no need for a full YAML/Unity-asset parser.
    /// </summary>
    public static class AssetSearchService
    {
        public static List<AssetSearchResult> SearchFiles(
            string rootFolder,
            string keyword,
            string[] extensions,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            var searchExtensions = new HashSet<string>(
                extensions.Select(e => e.StartsWith(".", StringComparison.Ordinal) ? e : "." + e),
                StringComparer.OrdinalIgnoreCase);

            var results = new List<AssetSearchResult>();
            int scanned = 0;

            foreach (string file in Directory.EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (searchExtensions.Count > 0 && !searchExtensions.Contains(Path.GetExtension(file)))
                    continue;

                scanned++;
                if (scanned % 250 == 0)
                    progress?.Report(scanned);

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(file);
                }
                catch
                {
                    // Binary or unreadable file (shouldn't happen much given
                    // the extension filter, but don't let one bad file stop the scan).
                    continue;
                }

                foreach (string line in lines)
                {
                    if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new AssetSearchResult { FilePath = file, MatchedLine = line.Trim() });
                        break; // one hit is enough to flag the file
                    }
                }
            }

            progress?.Report(scanned);
            return results;
        }
    }
}
