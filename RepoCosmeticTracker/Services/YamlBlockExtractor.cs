using System;
using System.Collections.Generic;
using System.IO;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Pulls the list of entries under one top-level YAML field (e.g.
    /// "cosmeticAssets:") out of a Unity scene/prefab file, streaming line
    /// by line rather than loading the whole file into memory — .unity
    /// scene files can easily be tens or hundreds of MB once all the level
    /// data is included, most of which is irrelevant to what we need here.
    ///
    /// Unity serializes object-reference lists like:
    ///   cosmeticAssets:
    ///   - {fileID: 11400000, guid: 3fa9c1..., type: 2}
    ///   - {fileID: 11400000, guid: 8b2e05..., type: 2}
    /// with list items at the SAME indentation as the field name itself
    /// (valid YAML — the "-" sequence indicator doesn't have to be indented
    /// further than its parent key).
    /// </summary>
    public static class YamlBlockExtractor
    {
        public static List<string> ExtractListBlock(string filePath, string fieldName)
        {
            var results = new List<string>();
            bool inBlock = false;
            string fieldHeader = "  " + fieldName + ":";

            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!inBlock)
                {
                    if (line.StartsWith(fieldHeader, StringComparison.Ordinal))
                    {
                        inBlock = true;
                        // Rare case: a short list written inline on the same line.
                        string sameLine = line.Substring(fieldHeader.Length).Trim();
                        if (!string.IsNullOrEmpty(sameLine))
                            results.Add(sameLine);
                    }
                    continue;
                }

                // Still inside the block while lines are list items at the
                // field's own indentation ("  - ...").
                if (line.StartsWith("  - ", StringComparison.Ordinal))
                {
                    results.Add(line.Substring(4).Trim());
                }
                else
                {
                    // Hit the next field or a document boundary — done.
                    break;
                }
            }

            return results;
        }
    }
}
