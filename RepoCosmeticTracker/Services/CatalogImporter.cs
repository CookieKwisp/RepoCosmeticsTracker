using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RepoCosmeticTracker.Models;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Builds the cosmetics catalog from an AssetRipper export of REPO's
    /// game files. AssetRipper reconstructs Unity's serialized data assets
    /// as readable YAML (.asset files) — this is the actual instance data
    /// (names, rarities, slots) that reflection on Assembly-CSharp.dll can
    /// never see, since it only exposes the C# schema, not the data.
    /// </summary>
    public static class CatalogImporter
    {
        // GUID of REPO's CosmeticAsset.cs script, read directly from a real
        // exported instance's "m_Script" reference. Filtering by this GUID
        // is exact — far more reliable than guessing from folder/file names,
        // since it's how Unity itself identifies which script a MonoBehaviour
        // data asset is an instance of.
        private const string CosmeticAssetScriptGuid = "598ab77c4e4df5c1d373284b16c5b104";

        // Ordinal position in each list matches the raw integers Unity
        // serializes for "type" and "rarity" — confirmed against a real
        // sample (type: 3 on a "Leg Right" item lines up with index 3 here).
        private static readonly string[] CosmeticTypeNames =
        {
            "Hat", "ArmRight", "ArmLeft", "LegRight", "LegLeft",
            "HeadTopMesh", "HeadBottomMesh", "BodyTopMesh", "BodyBottomMesh",
            "ArmRightMesh", "ArmLeftMesh", "LegRightMesh", "LegLeftMesh",
            "GrabberMesh", "EyeLidRightMesh", "EyeLidLeftMesh", "BodyTopOverlay",
            "Ears", "Eyewear", "FootRight", "BodyTop", "BodyBottom", "FootLeft",
            "BodyBottomOverlay", "HeadTopOverlay", "HeadBottomOverlay",
            "ArmRightOverlay", "ArmLeftOverlay", "LegRightOverlay", "LegLeftOverlay",
            "HeadBottom", "FaceTop", "FaceBottom"
        };

        // Inferred from sound-effect field ordering seen on CosmeticShopMachineAnimator
        // (soundScreenCosmeticRewardCommon/Uncommon/Rare/UltraRare) rather than a
        // direct Rarity enum dump — sanity-check this against a couple of
        // known-rarity items in-game once you can compare.
        private static readonly string[] RarityNames =
        {
            "Common", "Uncommon", "Rare", "UltraRare"
        };

        public class ImportResult
        {
            public List<CosmeticItem> Items { get; init; } = new();
            public int FilesScanned { get; init; }
            public int MatchedFiles { get; init; }
        }

        public static ImportResult ImportFromAssetRipperExport(string exportRootFolder)
        {
            var items = new List<CosmeticItem>();
            int scanned = 0;

            foreach (string file in Directory.EnumerateFiles(exportRootFolder, "*.asset", SearchOption.AllDirectories))
            {
                scanned++;

                string text = File.ReadAllText(file);
                if (!text.Contains(CosmeticAssetScriptGuid, StringComparison.Ordinal))
                    continue;

                string? assetName = ExtractField(text, "assetName");
                string? assetId = ExtractField(text, "assetId");
                string? mName = ExtractField(text, "m_Name");
                string? typeRaw = ExtractField(text, "type");
                string? rarityRaw = ExtractField(text, "rarity");

                string fallbackName = mName ?? Path.GetFileNameWithoutExtension(file);

                items.Add(new CosmeticItem
                {
                    Id = !string.IsNullOrWhiteSpace(assetId) ? assetId! : fallbackName,
                    DisplayName = !string.IsNullOrWhiteSpace(assetName) ? assetName! : fallbackName,
                    Category = MapEnumName(typeRaw, CosmeticTypeNames),
                    Rarity = MapEnumName(rarityRaw, RarityNames),
                    NumericId = null, // filled in once the MetaManager ordering is confirmed
                    Owned = false
                });
            }

            return new ImportResult
            {
                Items = items,
                FilesScanned = scanned,
                MatchedFiles = items.Count
            };
        }

        /// <summary>
        /// The real finishing step: takes the ordered list of {fileID, guid, type}
        /// entries extracted from MetaManager's "cosmeticAssets" list (position
        /// in the file == the NumericId used in cosmeticUnlocks), matches each
        /// GUID against every .meta file in the export (every Unity asset has
        /// one, containing its GUID — that's how Unity itself resolves these
        /// references), and reads the matching CosmeticAsset's real data.
        /// </summary>
        public static ImportResult ImportWithNumericIds(string exportRootFolder, string guidOrderListPath)
        {
            List<string> guidLines = File.ReadAllLines(guidOrderListPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // Build guid -> asset file path map by scanning every .meta file once.
            var guidToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string metaFile in Directory.EnumerateFiles(exportRootFolder, "*.meta", SearchOption.AllDirectories))
            {
                string? guid = ExtractMetaGuid(metaFile);
                if (guid == null)
                    continue;

                string assetFile = metaFile.Substring(0, metaFile.Length - ".meta".Length);
                if (File.Exists(assetFile))
                    guidToFile[guid] = assetFile;
            }

            var items = new List<CosmeticItem>();
            int resolved = 0;

            for (int i = 0; i < guidLines.Count; i++)
            {
                string? guid = ExtractGuidFromReferenceLine(guidLines[i]);

                if (guid == null || !guidToFile.TryGetValue(guid, out string? assetFilePath))
                {
                    items.Add(new CosmeticItem
                    {
                        NumericId = i,
                        Id = guid ?? $"unresolved_{i}",
                        DisplayName = $"(unresolved — guid {guid ?? "none"} not found in export)",
                        Category = "Unknown",
                        Rarity = "Unknown",
                        Owned = false
                    });
                    continue;
                }

                string text = File.ReadAllText(assetFilePath);
                string? assetName = ExtractField(text, "assetName");
                string? assetId = ExtractField(text, "assetId");
                string? mName = ExtractField(text, "m_Name");
                string? typeRaw = ExtractField(text, "type");
                string? rarityRaw = ExtractField(text, "rarity");
                string fallbackName = mName ?? Path.GetFileNameWithoutExtension(assetFilePath);

                items.Add(new CosmeticItem
                {
                    NumericId = i,
                    Id = !string.IsNullOrWhiteSpace(assetId) ? assetId! : fallbackName,
                    DisplayName = !string.IsNullOrWhiteSpace(assetName) ? assetName! : fallbackName,
                    Category = MapEnumName(typeRaw, CosmeticTypeNames),
                    Rarity = MapEnumName(rarityRaw, RarityNames),
                    Owned = false
                });
                resolved++;
            }

            return new ImportResult
            {
                Items = items,
                FilesScanned = guidToFile.Count,
                MatchedFiles = resolved
            };
        }

        private static string? ExtractMetaGuid(string metaFilePath)
        {
            foreach (string line in File.ReadLines(metaFilePath))
            {
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                    return line.Substring("guid:".Length).Trim();
            }
            return null;
        }

        private static string? ExtractGuidFromReferenceLine(string line)
        {
            // Format: {fileID: 11400000, guid: XXXXXXXX, type: 2}
            Match m = Regex.Match(line, @"guid:\s*([0-9a-fA-F]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string MapEnumName(string? raw, string[] names)
        {
            if (raw != null && int.TryParse(raw, out int index) && index >= 0 && index < names.Length)
                return names[index];
            return raw ?? "Unknown";
        }

        private static string? ExtractField(string yamlText, string fieldName)
        {
            // Matches a top-level (2-space indented) "key: value" line,
            // e.g. "  assetName: Kneepad". Unity's exported YAML for these
            // simple data assets is regular enough that a full YAML parser
            // isn't needed for the handful of scalar fields we care about.
            Match m = Regex.Match(yamlText, $@"^  {Regex.Escape(fieldName)}:\s?(.*)$", RegexOptions.Multiline);
            if (!m.Success)
                return null;

            string value = m.Groups[1].Value.Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
