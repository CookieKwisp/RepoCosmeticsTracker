using System.IO;
using RepoCosmeticTracker.Models;

namespace RepoCosmeticTracker.Services
{
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

            if (index.TryGetValue($"cosmetic - {category} - {name}", out string? path)) return path;

            if (index.TryGetValue(name, out path)) return path;

            string suffix = $" - {name}";
            string? match = null;
            foreach (KeyValuePair<string, string> entry in index)
            {
                if (entry.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (match != null) return null;
                    match = entry.Value;
                }
            }

            return match;
        }
    }
}
