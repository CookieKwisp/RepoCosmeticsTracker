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
        public static List<DecryptedSaveFile> DecryptAllInFolder(string saveSlotFolder)
        {
            var results = new List<DecryptedSaveFile>();

            foreach (string file in Directory.EnumerateFiles(saveSlotFolder, "*.es3", SearchOption.TopDirectoryOnly))
                results.Add(DecryptFile(file));

            return results;
        }
        public static DecryptedSaveFile DecryptFile(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            byte[] decrypted = Es3Crypto.Decrypt(raw, Es3Crypto.RepoPassword);
            string json = Encoding.UTF8.GetString(decrypted);
            json = TryPrettyPrint(json);
            return new DecryptedSaveFile { FilePath = path, Json = json };
        }
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
                return json;
            }
        }
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
