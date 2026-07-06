using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RepoCosmeticTracker.Models;
using RepoCosmeticTracker.Services;

namespace RepoCosmeticTracker
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<CosmeticItem> _cosmetics = new();
        private string? _currentJson;
        private string? _lastRawJson; // pure JSON from the last single-file open, used for sync
        private string? _assemblyPath;
        private string? _exportFolder;
        private readonly AppSettings _settings;

        public MainWindow()
        {
            InitializeComponent();
            CosmeticsGrid.ItemsSource = _cosmetics;
            _settings = AppSettings.Load();
            _exportFolder = _settings.LastExportFolder;
            if (_exportFolder != null)
                ExportFolderText.Text = _exportFolder;
            DetectPaths();
        }

        // Window.Background only paints the client area — the title bar and
        // window border are drawn by Windows' own compositor (DWM) and don't
        // look at WPF properties at all. This flips the *native* title bar
        // into dark mode too (the same mechanism apps like VS Code and
        // Windows Terminal use). Requires Windows 10 20H1+ / Windows 11;
        // silently no-ops on anything older.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            TryEnableDarkTitleBar();
        }

        private void TryEnableDarkTitleBar()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int useImmersiveDarkMode = 1;

                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 20H1+/Windows 11.
                int hr = DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
                if (hr != 0)
                {
                    // 19 = the same attribute's older value, for early Windows 10 builds.
                    DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, sizeof(int));
                }
            }
            catch
            {
                // Not on Windows, or DWM unavailable — window still works fine,
                // it'll just keep the default light title bar.
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        private void DetectButton_Click(object sender, RoutedEventArgs e) => DetectPaths();

        private void DetectPaths()
        {
            string? installDir = GameLocator.FindRepoInstallFolder();
            InstallPathText.Text = installDir ?? "(not found — is REPO installed via Steam?)";

            string? saveFolder = GameLocator.GetSaveFolder();
            SaveFolderText.Text = saveFolder ?? "(not found)";

            _assemblyPath = GameLocator.FindAssemblyCSharp();

            SaveSlotCombo.Items.Clear();
            foreach (string slot in GameLocator.GetSaveSlotFolders())
                SaveSlotCombo.Items.Add(slot);

            if (SaveSlotCombo.Items.Count > 0)
                SaveSlotCombo.SelectedIndex = 0;
        }

        private void OpenSpecificFileButton_Click(object sender, RoutedEventArgs e)
        {
            string? startDir = GameLocator.GetRepoDataRoot();

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ES3 save files (*.es3)|*.es3|All files (*.*)|*.*",
                InitialDirectory = startDir ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Title = "Open a .es3 file to decrypt"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var decrypted = SaveService.DecryptFile(dialog.FileName);
                _lastRawJson = decrypted.Json;
                _currentJson = $"// ==== {decrypted.FileName} ====\r\n{decrypted.Json}";
                JsonOutputBox.Text = _currentJson;
                JsonSearchBox.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to decrypt: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ListAllEs3Button_Click(object sender, RoutedEventArgs e)
        {
            string? root = GameLocator.GetRepoDataRoot();
            if (root == null)
            {
                MessageBox.Show("Couldn't find the Repo AppData folder. Click Detect first.");
                return;
            }

            var files = SaveService.FindAllEs3Files(root).ToList();
            _currentJson = null;

            JsonOutputBox.Text = files.Count == 0
                ? "No .es3 files found anywhere under the Repo AppData folder."
                : $"Found {files.Count} .es3 file(s) under {root}:\r\n\r\n" + string.Join("\r\n", files);
        }

        private void DecryptButton_Click(object sender, RoutedEventArgs e)
        {
            if (SaveSlotCombo.SelectedItem is not string slotFolder)
            {
                MessageBox.Show("Pick a save slot first.");
                return;
            }

            try
            {
                var files = SaveService.DecryptAllInFolder(slotFolder);
                if (files.Count == 0)
                {
                    JsonOutputBox.Text = "No .es3 files found in that save slot.";
                    return;
                }

                var sb = new StringBuilder();
                foreach (var f in files)
                {
                    sb.AppendLine($"// ==== {f.FileName} ====");
                    sb.AppendLine(f.Json);
                    sb.AppendLine();
                }

                _currentJson = sb.ToString();
                JsonOutputBox.Text = _currentJson;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to decrypt: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void JsonSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentJson == null)
                return;

            string keyword = JsonSearchBox.Text;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                JsonOutputBox.Text = _currentJson;
                return;
            }

            var matches = SaveService.FindMatchingLines(_currentJson, keyword).ToList();
            JsonOutputBox.Text = matches.Count > 0
                ? string.Join(Environment.NewLine, matches)
                : "(no matching lines)";
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_assemblyPath == null)
            {
                MessageBox.Show("Couldn't find Assembly-CSharp.dll. Click Detect first, and make sure REPO is installed via Steam.");
                return;
            }

            var keywords = KeywordsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            try
            {
                var results = AssemblyInspector.ScanForCosmeticTypes(_assemblyPath, keywords);

                if (results.Count == 0)
                {
                    ScanOutputBox.Text = "No matching types found. Try different keywords.";
                    return;
                }

                var sb = new StringBuilder();
                foreach (var t in results)
                {
                    sb.AppendLine($"[{t.Kind}] {t.FullName}");
                    foreach (string m in t.Members)
                        sb.AppendLine($"    {m}");
                    sb.AppendLine();
                }

                ScanOutputBox.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                ScanOutputBox.Text = $"Scan failed: {ex.Message}\r\n\r\n{ex.StackTrace}";
            }
        }

        private async void RefreshCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            string? repoDataPath = GameLocator.FindRepoDataFolder();
            if (repoDataPath == null)
            {
                MessageBox.Show("Couldn't find REPO_Data automatically. Click \"Detect\" first and make sure REPO is installed via Steam.");
                return;
            }

            string? repoRoot = GameLocator.GetRepoDataRoot();
            string? metaSavePath = repoRoot != null ? Path.Combine(repoRoot, "MetaSave.es3") : null;
            if (metaSavePath == null || !File.Exists(metaSavePath))
            {
                MessageBox.Show("Couldn't find MetaSave.es3 automatically. Click \"Detect\" first and make sure REPO has been run at least once.");
                return;
            }

            RefreshCatalogButton.IsEnabled = false;
            CatalogStatusText.Text = "Reading directly from game files (no AssetRipper needed)...";

            try
            {
                DirectAssetReader.ReadResult readResult = await DirectAssetReader.BuildCatalogAsync(repoDataPath);

                if (readResult.Items.Count == 0)
                {
                    CatalogStatusText.Text = "Failed: " + string.Join(" | ", readResult.Log);
                    return;
                }

                DecryptedSaveFile decrypted = SaveService.DecryptFile(metaSavePath);
                List<int>? unlockedIds = SaveService.ExtractIntListField(decrypted.Json, "cosmeticUnlocks");
                var unlockedSet = new HashSet<int>(unlockedIds ?? new List<int>());

                _cosmetics.Clear();
                foreach (CosmeticItem item in readResult.Items)
                {
                    item.Owned = item.NumericId.HasValue && unlockedSet.Contains(item.NumericId.Value);
                    _cosmetics.Add(item);
                }

                string catalogPath = Path.Combine(AppContext.BaseDirectory, "catalog.json");
                string json = JsonSerializer.Serialize(_cosmetics, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(catalogPath, json);

                CatalogStatusText.Text =
                    $"Refreshed: {readResult.Items.Count} cosmetics, {unlockedSet.Count} owned. Saved to catalog.json. " +
                    "(Read directly from game files — no AssetRipper.)";
            }
            catch (Exception ex)
            {
                CatalogStatusText.Text = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                RefreshCatalogButton.IsEnabled = true;
            }
        }

        private void BrowseExportFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select the AssetRipper export folder"
            };

            if (dialog.ShowDialog() != true)
                return;

            _exportFolder = dialog.FolderName;
            ExportFolderText.Text = _exportFolder;
            _settings.LastExportFolder = _exportFolder;
            _settings.Save();
        }

        private async void AssetSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_exportFolder == null)
            {
                MessageBox.Show("Browse to the export folder first.");
                return;
            }

            string keyword = AssetSearchKeywordBox.Text;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                MessageBox.Show("Enter a keyword to search for.");
                return;
            }

            string[] extensions = AssetSearchExtensionsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            AssetSearchButton.IsEnabled = false;
            AssetSearchResultsBox.Text = "Searching...";

            var progress = new Progress<int>(count =>
            {
                AssetSearchResultsBox.Text = $"Scanned {count:N0} matching-extension files so far...";
            });

            try
            {
                List<AssetSearchResult> results = await Task.Run(() =>
                    AssetSearchService.SearchFiles(_exportFolder, keyword, extensions, progress, CancellationToken.None));

                if (results.Count == 0)
                {
                    AssetSearchResultsBox.Text =
                        $"No matches for \"{keyword}\" in *.{string.Join(", *.", extensions)} files under that folder.";
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"{results.Count} match(es):");
                    sb.AppendLine();
                    foreach (AssetSearchResult r in results)
                    {
                        sb.AppendLine(r.FilePath);
                        sb.AppendLine($"    {r.MatchedLine}");
                        sb.AppendLine();
                    }
                    AssetSearchResultsBox.Text = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                AssetSearchResultsBox.Text = $"Search failed: {ex.Message}";
            }
            finally
            {
                AssetSearchButton.IsEnabled = true;
            }
        }

        private void ExtractBlockButton_Click(object sender, RoutedEventArgs e)
        {
            string filePath = ExtractFilePathBox.Text.Trim();
            string fieldName = ExtractFieldNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("Enter a valid file path (paste one from the search results above).");
                return;
            }

            if (string.IsNullOrWhiteSpace(fieldName))
            {
                MessageBox.Show("Enter the field name to extract, e.g. cosmeticAssets.");
                return;
            }

            try
            {
                List<string> entries = YamlBlockExtractor.ExtractListBlock(filePath, fieldName);

                if (entries.Count == 0)
                {
                    AssetSearchResultsBox.Text =
                        $"Found no list entries under \"{fieldName}\" in that file — double check the field name, " +
                        "or the file/field combination might not be the one we're after.";
                    return;
                }

                string outputPath = Path.Combine(AppContext.BaseDirectory, $"{fieldName}_extracted.txt");
                File.WriteAllLines(outputPath, entries);

                var sb = new StringBuilder();
                sb.AppendLine($"{entries.Count} entries under \"{fieldName}\" (also saved to {outputPath}):");
                sb.AppendLine();
                foreach (string entry in entries)
                    sb.AppendLine(entry);

                AssetSearchResultsBox.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                AssetSearchResultsBox.Text = $"Extraction failed: {ex.Message}";
            }
        }

        private void ImportWithNumericIdsButton_Click(object sender, RoutedEventArgs e)
        {
            string guidListPath = Path.Combine(AppContext.BaseDirectory, "cosmeticAssets_extracted.txt");
            if (!File.Exists(guidListPath))
            {
                MessageBox.Show(
                    "cosmeticAssets_extracted.txt wasn't found next to the app. " +
                    "Run \"Extract\" on the \"4. Find in Export\" tab first.");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select the AssetRipper export folder (same one as before)"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var result = CatalogImporter.ImportWithNumericIds(dialog.FolderName, guidListPath);

                _cosmetics.Clear();
                foreach (var item in result.Items)
                    _cosmetics.Add(item);

                int unresolved = result.Items.Count - result.MatchedFiles;
                CatalogStatusText.Text =
                    $"Imported {result.Items.Count} cosmetics with real NumericId values " +
                    $"({result.MatchedFiles} resolved via GUID, {unresolved} unresolved). " +
                    "Click \"Save changes\", then \"Sync ownership from opened file\" (with MetaSave.es3 open) to fill in Owned.";
            }
            catch (Exception ex)
            {
                CatalogStatusText.Text = $"Import failed: {ex.Message}";
            }
        }

        private void ImportFromAssetRipperButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select the AssetRipper export folder (the one containing exported .asset files)"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var result = CatalogImporter.ImportFromAssetRipperExport(dialog.FolderName);

                if (result.Items.Count == 0)
                {
                    CatalogStatusText.Text =
                        $"Scanned {result.FilesScanned} .asset files, found 0 CosmeticAsset instances. " +
                        "Make sure this is the AssetRipper export root, not a subfolder.";
                    return;
                }

                _cosmetics.Clear();
                foreach (var item in result.Items)
                    _cosmetics.Add(item);

                CatalogStatusText.Text =
                    $"Imported {result.Items.Count} cosmetics from {result.FilesScanned} scanned files. " +
                    "Click \"Save changes\" to write this to catalog.json.";
            }
            catch (Exception ex)
            {
                CatalogStatusText.Text = $"Import failed: {ex.Message}";
            }
        }

        private void SyncOwnershipButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastRawJson == null)
            {
                MessageBox.Show("Open a file first (Save Data tab → Open specific file... → MetaSave.es3).");
                return;
            }

            List<int>? unlockedIds;
            try
            {
                unlockedIds = SaveService.ExtractIntListField(_lastRawJson, "cosmeticUnlocks");
            }
            catch (Exception ex)
            {
                CatalogStatusText.Text = $"Couldn't parse the opened file: {ex.Message}";
                return;
            }

            if (unlockedIds == null)
            {
                CatalogStatusText.Text = "No \"cosmeticUnlocks\" field in the currently opened file — open MetaSave.es3 first.";
                return;
            }

            if (_cosmetics.Count == 0)
            {
                CatalogStatusText.Text = $"Found {unlockedIds.Count} unlocked IDs, but the catalog is empty — load catalog.json first.";
                return;
            }

            int matched = 0;
            foreach (CosmeticItem item in _cosmetics)
            {
                bool isUnlocked = item.NumericId.HasValue && unlockedIds.Contains(item.NumericId.Value);
                item.Owned = isUnlocked;
                if (isUnlocked)
                    matched++;
            }

            CatalogStatusText.Text = $"Save has {unlockedIds.Count} unlocked IDs. {matched} matched a catalog entry with a NumericId set.";
        }

        private void LoadCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "catalog.json");
            if (!File.Exists(path))
            {
                CatalogStatusText.Text = "No catalog.json next to the .exe yet — see README.";
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var items = JsonSerializer.Deserialize<CosmeticItem[]>(json) ?? Array.Empty<CosmeticItem>();

                _cosmetics.Clear();
                foreach (var item in items)
                    _cosmetics.Add(item);

                CatalogStatusText.Text = $"Loaded {_cosmetics.Count} items.";
            }
            catch (Exception ex)
            {
                CatalogStatusText.Text = $"Failed to load catalog.json: {ex.Message}";
            }
        }

        private void SaveCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "catalog.json");
            try
            {
                string json = JsonSerializer.Serialize(_cosmetics, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                CatalogStatusText.Text = $"Saved {_cosmetics.Count} items.";
            }
            catch (Exception ex)
            {
                CatalogStatusText.Text = $"Failed to save: {ex.Message}";
            }
        }
    }
}
