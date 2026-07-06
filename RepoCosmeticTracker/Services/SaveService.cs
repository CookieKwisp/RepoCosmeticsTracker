using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RepoCosmeticTracker.Services
{
    public class DecryptedSaveFile
    {
        public string FilePath { get; init; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string Json { get; init; } = "";
    }

    public static class SaveService
    {
        /// <summary>
        /// Decrypts every .es3 file directly inside a save-slot folder.
        /// A slot may contain more than one .es3 file, so we don't assume
        /// a single filename.
        /// </summary>
        public static List<DecryptedSaveFile> DecryptAllInFolder(string saveSlotFolder)
        {
            var results = new List<DecryptedSaveFile>();

            foreach (string file in Directory.EnumerateFiles(saveSlotFolder, "*.es3", SearchOption.TopDirectoryOnly))
                results.Add(DecryptFile(file));

            return results;
        }

        /// <summary>
        /// Decrypts a single arbitrary .es3 file — used for the "Open specific
        /// file..." picker, e.g. a persistent profile/cosmetics file that
        /// lives outside the per-run saves\&lt;slot&gt;\ folders.
        /// </summary>
        public static DecryptedSaveFile DecryptFile(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            byte[] decrypted = Es3Crypto.Decrypt(raw, Es3Crypto.RepoPassword);
            string json = Encoding.UTF8.GetString(decrypted);
            json = TryPrettyPrint(json);
            return new DecryptedSaveFile { FilePath = path, Json = json };
        }

        /// <summary>
        /// Lists every .es3 file anywhere under the Repo AppData folder,
        /// not just inside saves\&lt;slot&gt;\ — useful for spotting a
        /// persistent profile file that sits alongside "saves".
        /// </summary>
        public static IEnumerable<string> FindAllEs3Files(string repoDataRoot)
        {
            if (!Directory.Exists(repoDataRoot))
                yield break;

            foreach (string file in Directory.EnumerateFiles(repoDataRoot, "*.es3", SearchOption.AllDirectories))
                yield return file;
        }

        private static string TryPrettyPrint(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                // Not valid JSON on its own (or decryption/padding was wrong) — return raw so the
                // user can still see what came out instead of losing the data entirely.
                return json;
            }
        }

        /// <summary>
        /// Pulls a top-level field's "value" array out of decrypted ES3 JSON
        /// as a list of ints. ES3's JSON shape wraps every field like:
        /// "fieldName": { "__type": "...", "value": [ ... ] }
        /// Used for fields like "cosmeticUnlocks" — confirmed present in
        /// MetaSave.es3 as a List&lt;int&gt; of unlocked cosmetic IDs.
        /// </summary>
        public static List<int>? ExtractIntListField(string json, string fieldName)
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(fieldName, out JsonElement fieldElement))
                return null;

            if (!fieldElement.TryGetProperty("value", out JsonElement valueElement))
                return null;

            if (valueElement.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<int>();
            foreach (JsonElement item in valueElement.EnumerateArray())
            {
                if (item.TryGetInt32(out int i))
                    result.Add(i);
            }

            return result;
        }

        /// <summary>
        /// Searches decrypted JSON line-by-line for a keyword, to help
        /// locate where cosmetic ownership data lives in the save structure
        /// (try things like "hat", "skin", "unlock", "cosmetic", "item").
        /// </summary>
        public static IEnumerable<string> FindMatchingLines(string json, string keyword)
        {
            foreach (string line in json.Split('\n'))
            {
                if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    yield return line.Trim();
            }
        }
    }
}
