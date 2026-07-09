using System.IO;
using System.IO.Compression;
using System.Net.Http;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using RepoCosmeticTracker.Models;

namespace RepoCosmeticTracker.Services
{
    public static class DirectAssetReader
    {
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
        private static readonly string[] RarityNames = { "Common", "Uncommon", "Rare", "UltraRare" };

        public class ReadResult
        {
            public List<CosmeticItem> Items { get; init; } = new();
            public List<string> Log { get; init; } = new();
        }

        public static async Task<ReadResult> BuildCatalogAsync(string repoDataPath)
        {
            var log = new List<string>();
            var items = new List<CosmeticItem>();

            string managedPath = Path.Combine(repoDataPath, "Managed");
            string tpkPath = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");

            if (!Directory.Exists(managedPath))
            {
                log.Add($"ERROR: Managed folder not found at {managedPath}");
                return new ReadResult { Items = items, Log = log };
            }

            if (!File.Exists(tpkPath))
            {
                log.Add("classdata.tpk not found downloading...");
                bool downloaded = await TryDownloadClassDataTpk(tpkPath, log);
                if (!downloaded)
                    return new ReadResult { Items = items, Log = log };
            }

            var manager = new AssetsManager();
            try
            {
                manager.LoadClassPackage(tpkPath);
            }
            catch (Exception ex)
            {
                log.Add($"classdata.tpk unreadable ({ex.Message}) re-downloading a compatible one");
                try { File.Delete(tpkPath); } catch { /* best effort */ }

                if (!await TryDownloadClassDataTpk(tpkPath, log))
                    return new ReadResult { Items = items, Log = log };

                try
                {
                    manager.LoadClassPackage(tpkPath);
                }
                catch (Exception ex2)
                {
                    log.Add($"ERROR setting up AssetsManager: {ex2.Message}");
                    return new ReadResult { Items = items, Log = log };
                }
            }

            try
            {
                manager.MonoTempGenerator = new MonoCecilTempGenerator(managedPath);
            }
            catch (Exception ex)
            {
                log.Add($"ERROR setting up AssetsManager: {ex.Message}");
                return new ReadResult { Items = items, Log = log };
            }

            AssetsFileInstance? sceneInst = null;
            AssetTypeValueField? metaManagerBase = null;

            foreach (string levelName in new[] { "level0", "level1", "level2" })
            {
                string levelPath = Path.Combine(repoDataPath, levelName);
                if (!File.Exists(levelPath))
                    continue;

                AssetsFileInstance afileInst;
                try
                {
                    afileInst = manager.LoadAssetsFile(levelPath, true);
                    manager.LoadClassDatabaseFromPackage(afileInst.file.Metadata.UnityVersion);
                }
                catch
                {
                    continue;
                }

                List<AssetFileInfo> monoBehaviours;
                try
                {
                    monoBehaviours = afileInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour);
                }
                catch
                {
                    continue;
                }

                foreach (AssetFileInfo mbInfo in monoBehaviours)
                {
                    AssetTypeValueField mbBase;
                    try
                    {
                        mbBase = manager.GetBaseField(afileInst, mbInfo);
                    }
                    catch
                    {
                        continue;
                    }

                    AssetTypeValueField? cosmeticAssetsField;
                    try
                    {
                        cosmeticAssetsField = mbBase["cosmeticAssets"];
                    }
                    catch
                    {
                        continue;
                    }

                    if (cosmeticAssetsField != null && !cosmeticAssetsField.IsDummy)
                    {
                        AssetTypeValueField arrayField;
                        try
                        {
                            arrayField = cosmeticAssetsField["Array"];
                        }
                        catch
                        {
                            continue;
                        }

                        if (arrayField != null && arrayField.Children.Count > 10)
                        {
                            sceneInst = afileInst;
                            metaManagerBase = mbBase;
                            break;
                        }
                    }
                }

                if (metaManagerBase != null)
                    break;
            }

            if (metaManagerBase == null || sceneInst == null)
            {
                log.Add("Could not find MetaManager (a MonoBehaviour with a populated \"cosmeticAssets\" list) in level0/level1/level2.");
                return new ReadResult { Items = items, Log = log };
            }

            AssetTypeValueField cosmeticAssetsArray = metaManagerBase["cosmeticAssets"]["Array"];
            int total = cosmeticAssetsArray.Children.Count;
            log.Add($"Found MetaManager with {total} cosmeticAssets entries.");

            int resolved = 0;
            for (int i = 0; i < total; i++)
            {
                AssetTypeValueField pptrField = cosmeticAssetsArray.Children[i];
                try
                {
                    var ext = manager.GetExtAsset(sceneInst, pptrField);
                    if (ext.baseField == null)
                    {
                        items.Add(new CosmeticItem
                        {
                            NumericId = i,
                            Id = $"unresolved_{i}",
                            DisplayName = "(null reference)",
                            Category = "Unknown",
                            Rarity = "Unknown",
                            Owned = false
                        });
                        continue;
                    }

                    AssetTypeValueField assetBase = ext.baseField;
                    string assetName = SafeGetString(assetBase, "assetName");
                    string assetId = SafeGetString(assetBase, "assetId");
                    int typeInt = SafeGetInt(assetBase, "type");
                    int rarityInt = SafeGetInt(assetBase, "rarity");

                    items.Add(new CosmeticItem
                    {
                        NumericId = i,
                        Id = string.IsNullOrWhiteSpace(assetId) ? $"vanilla:{i}" : assetId,
                        DisplayName = string.IsNullOrWhiteSpace(assetName) ? $"(unnamed {i})" : assetName,
                        Category = MapEnumName(typeInt, CosmeticTypeNames),
                        Rarity = MapEnumName(rarityInt, RarityNames),
                        Owned = false
                    });
                    resolved++;
                }
                catch (Exception ex)
                {
                    items.Add(new CosmeticItem
                    {
                        NumericId = i,
                        Id = $"error_{i}",
                        DisplayName = $"(error: {ex.Message})",
                        Category = "Unknown",
                        Rarity = "Unknown",
                        Owned = false
                    });
                }
            }

            log.Add($"Resolved {resolved} / {total}.");
            return new ReadResult { Items = items, Log = log };
        }

        private static string SafeGetString(AssetTypeValueField parent, string fieldName)
        {
            try
            {
                AssetTypeValueField field = parent[fieldName];
                if (field == null || field.IsDummy)
                    return "";
                return field.AsString ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static int SafeGetInt(AssetTypeValueField parent, string fieldName)
        {
            try
            {
                AssetTypeValueField field = parent[fieldName];
                if (field == null || field.IsDummy)
                    return -1;
                return field.AsInt;
            }
            catch
            {
                return -1;
            }
        }

        private static string MapEnumName(int index, string[] names)
        {
            if (index >= 0 && index < names.Length)
                return names[index];
            return index.ToString();
        }

        private static async Task<bool> TryDownloadClassDataTpk(string destinationPath, List<string> log)
        {
            const string url = "https://github.com/nesrak1/UABEA/releases/download/v8/uabea-windows.zip";

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(120);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoCosmeticTracker/1.0");

            string tempZipPath = Path.Combine(Path.GetTempPath(), $"tpk_download_{Guid.NewGuid():N}.zip");

            try
            {
                using HttpResponseMessage response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    log.Add($"classdata.tpk download failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    return false;
                }

                byte[] zipBytes = await response.Content.ReadAsByteArrayAsync();
                if (zipBytes.Length < 1000)
                {
                    log.Add($"Downloaded file is only {zipBytes.Length} bytes too small to be real.");
                    return false;
                }

                await File.WriteAllBytesAsync(tempZipPath, zipBytes);

                using ZipArchive archive = ZipFile.OpenRead(tempZipPath);
                ZipArchiveEntry? tpkEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(".tpk", StringComparison.OrdinalIgnoreCase));

                if (tpkEntry == null)
                    tpkEntry = archive.Entries.OrderByDescending(e => e.Length).FirstOrDefault();

                if (tpkEntry == null)
                {
                    log.Add("Downloaded zip was empty.");
                    return false;
                }

                string? dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                tpkEntry.ExtractToFile(destinationPath, overwrite: true);
                log.Add($"Downloaded and extracted classdata.tpk ({tpkEntry.Length:N0} bytes).");
                return true;
            }
            catch (Exception ex)
            {
                log.Add($"classdata.tpk download failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { /* best effort */ }
            }
        }
    }
}
