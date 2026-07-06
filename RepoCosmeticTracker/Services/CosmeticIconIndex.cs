using System;
using System.Collections.Generic;
using System.IO;
using RepoCosmeticTracker.Models;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// The game renders its own icon for every cosmetic and caches it as a
    /// PNG under LocalLow\semiwork\Repo\Cache\Icons\Cosmetics, named
    /// "cosmetic - {category} - {name}.png" (all lowercase, category words
    /// spaced out). We just index that folder and match our catalog entries
    /// against it — real in-game imagery for free, no asset extraction.
    /// </summary>
    public static class CosmeticIconIndex
    {
        public static string? GetIconsFolder()
        {
            string? root = GameLocator.GetRepoDataRoot();
            if (root == null)
                return null;

            string path = Path.Combine(root, "Cache", "Icons", "Cosmetics");
            return Directory.Exists(path) ? path : null;
        }

        /// <summary>Filename (without extension) → full path, case-insensitive.</summary>
        public static Dictionary<string, string> BuildIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? folder = GetIconsFolder();
            if (folder == null)
                return index;

            foreach (string file in Directory.EnumerateFiles(folder, "*.png"))
                index[Path.GetFileNameWithoutExtension(file).Trim()] = file;

            return index;
        }

        public static string? Resolve(Dictionary<string, string> index, CosmeticItem item)
        {
            if (index.Count == 0)
                return null;

            string name = item.DisplayName.Trim();
            string category = CosmeticItem.SpaceOutPascalCase(item.Category);

            // The cache's own naming convention.
            if (index.TryGetValue($"cosmetic - {category} - {name}", out string? path))
                return path;

            // In case assetName already is the full cache-style name.
            if (index.TryGetValue(name, out path))
                return path;

            // Last resort: match by the name segment alone, but only if it's
            // unambiguous (many cosmetics share names across categories).
            string suffix = $" - {name}";
            string? match = null;
            foreach (KeyValuePair<string, string> entry in index)
            {
                if (entry.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (match != null)
                        return null;
                    match = entry.Value;
                }
            }

            return match;
        }
    }
}
