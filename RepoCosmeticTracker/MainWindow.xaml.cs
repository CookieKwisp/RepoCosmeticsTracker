using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RepoCosmeticTracker.Models;
using RepoCosmeticTracker.Services;

namespace RepoCosmeticTracker
{
    /// <summary>
    /// The whole app is one screen: a grid of rarity-colored cards, filters,
    /// and a status bar. Everything else is automatic —
    ///  - on launch: load cached catalog.json instantly, rebuild it from the
    ///    game's own data files only when they've changed since the cache,
    ///  - ownership syncs from MetaSave.es3 (decrypted in place) and re-syncs
    ///    whenever the game writes a save, via an event-driven file watcher,
    ///  - clicking a card toggles it and persists immediately.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const double ProgressBarWidth = 200;

        private readonly ObservableCollection<CosmeticItem> _cosmetics = new();
        private ListCollectionView _view = null!;

        private readonly HashSet<string> _rarityFilter = new();
        private string _search = "";
        private string? _categoryFilter;      // null = all categories
        private bool _hideOwned;
        private bool _updatingCategories;

        private SaveWatcher? _watcher;
        private bool _rebuilding;
        private Dictionary<string, string> _iconIndex = new();

        private static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "catalog.json");

        public MainWindow()
        {
            InitializeComponent();

            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_cosmetics);
            _view.Filter = FilterItem;
            _view.SortDescriptions.Add(new SortDescription(nameof(CosmeticItem.RarityRank), ListSortDirection.Descending));
            _view.SortDescriptions.Add(new SortDescription(nameof(CosmeticItem.DisplayName), ListSortDirection.Ascending));
            CardsHost.ItemsSource = _view;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Startup error: {ex.Message}");
            }
        }

        private async Task InitAsync()
        {
            LoadCatalogFromDisk();

            string? install = GameLocator.FindRepoInstallFolder();
            SubtitleText.Text = install ?? "game not detected — tracking manually";

            _iconIndex = await Task.Run(CosmeticIconIndex.BuildIndex);
            ApplyIcons();

            await SyncFromSaveAsync();
            StartWatcher();

            if (CatalogLooksStale())
                await RebuildCatalogAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _watcher?.Dispose();
            base.OnClosed(e);
        }

        // ===== Catalog persistence =====

        private void LoadCatalogFromDisk()
        {
            try
            {
                if (File.Exists(CatalogPath))
                {
                    var items = JsonSerializer.Deserialize<CosmeticItem[]>(File.ReadAllText(CatalogPath))
                                ?? Array.Empty<CosmeticItem>();
                    _cosmetics.Clear();
                    foreach (CosmeticItem item in items)
                        _cosmetics.Add(item);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't read catalog.json: {ex.Message}");
            }

            PopulateCategories();
            UpdateCounts();
        }

        private void SaveCatalog()
        {
            try
            {
                string json = JsonSerializer.Serialize(_cosmetics, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CatalogPath, json);
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't save catalog.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Cheap staleness check so we only pay for a full asset parse when
        /// the game actually updated (or we've never built a catalog).
        /// </summary>
        private bool CatalogLooksStale()
        {
            if (_cosmetics.Count == 0)
                return true;

            string? dataPath = GameLocator.FindRepoDataFolder();
            if (dataPath == null)
                return false;

            var catalogInfo = new FileInfo(CatalogPath);
            if (!catalogInfo.Exists)
                return true;

            foreach (string level in new[] { "level0", "level1", "level2" })
            {
                var levelInfo = new FileInfo(Path.Combine(dataPath, level));
                if (levelInfo.Exists && levelInfo.LastWriteTimeUtc > catalogInfo.LastWriteTimeUtc)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Rebuilds the catalog straight from the game's Unity data files,
        /// preserving ownership of anything already tracked.
        /// </summary>
        private async Task RebuildCatalogAsync(bool userRequested = false)
        {
            if (_rebuilding)
                return;

            string? dataPath = GameLocator.FindRepoDataFolder();
            if (dataPath == null)
            {
                if (userRequested)
                    SetStatus("Game install not found — can't rescan.");
                return;
            }

            _rebuilding = true;
            RefreshButton.IsEnabled = false;
            SetStatus("Reading cosmetics from game files…");

            try
            {
                DirectAssetReader.ReadResult result =
                    await Task.Run(() => DirectAssetReader.BuildCatalogAsync(dataPath));

                if (result.Items.Count == 0)
                {
                    SetStatus("Catalog scan failed: " + (result.Log.LastOrDefault() ?? "unknown error"));
                    return;
                }

                var oldIds = _cosmetics.Select(c => c.Id).ToHashSet();
                var ownedIds = _cosmetics.Where(c => c.Owned).Select(c => c.Id).ToHashSet();
                bool changed = result.Items.Count != _cosmetics.Count
                               || result.Items.Any(i => !oldIds.Contains(i.Id));

                if (!changed)
                {
                    // Touch the cache so the staleness check stops re-scanning.
                    SaveCatalog();
                    SetStatus("Catalog up to date.");
                    return;
                }

                foreach (CosmeticItem item in result.Items)
                    item.Owned = ownedIds.Contains(item.Id);

                _cosmetics.Clear();
                foreach (CosmeticItem item in result.Items)
                    _cosmetics.Add(item);

                // The game may have cached more icons since we last looked.
                _iconIndex = await Task.Run(CosmeticIconIndex.BuildIndex);
                ApplyIcons();

                PopulateCategories();
                SaveCatalog();
                UpdateCounts();

                int added = result.Items.Count(i => !oldIds.Contains(i.Id));
                SetStatus(added > 0 && oldIds.Count > 0
                    ? $"Catalog updated — {added} new cosmetic{(added == 1 ? "" : "s")} found."
                    : $"Catalog built — {result.Items.Count} cosmetics.");

                await SyncFromSaveAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Catalog scan failed: " + ex.Message);
            }
            finally
            {
                _rebuilding = false;
                RefreshButton.IsEnabled = true;
            }
        }

        // ===== Save-file sync =====

        private void StartWatcher()
        {
            string? root = GameLocator.GetRepoDataRoot();
            if (root == null)
            {
                WatchDot.Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x61));
                return;
            }

            try
            {
                _watcher = new SaveWatcher(root);
                _watcher.SaveChanged += () =>
                    Dispatcher.BeginInvoke(async () => await SyncFromSaveAsync());
                WatchDot.Fill = (Brush)FindResource("SuccessBrush");
            }
            catch (Exception ex)
            {
                SetStatus($"Save watcher failed to start: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads MetaSave.es3 and marks everything the game says is unlocked.
        /// Additive on purpose: it never un-owns items you've checked by hand.
        /// Retries briefly because the game may still hold the file mid-write.
        /// </summary>
        private async Task SyncFromSaveAsync()
        {
            string? root = GameLocator.GetRepoDataRoot();
            if (root == null)
            {
                SetStatus("Save folder not found — run R.E.P.O. once to create it.");
                return;
            }

            string metaPath = Path.Combine(root, "MetaSave.es3");
            if (!File.Exists(metaPath))
            {
                SetStatus("MetaSave.es3 not found yet — waiting for the game to save.");
                return;
            }

            string? json = null;
            for (int attempt = 0; attempt < 4 && json == null; attempt++)
            {
                try
                {
                    json = await Task.Run(() => SaveService.DecryptFile(metaPath).Json);
                }
                catch
                {
                    await Task.Delay(300);
                }
            }

            if (json == null)
            {
                SetStatus("Couldn't read MetaSave.es3 (file busy) — will retry on next save.");
                return;
            }

            List<int>? unlocks;
            try
            {
                unlocks = SaveService.ExtractIntListField(json, "cosmeticUnlocks");
            }
            catch
            {
                unlocks = null;
            }

            if (unlocks == null)
            {
                SetStatus("Save readable, but no cosmeticUnlocks field found.");
                return;
            }

            var unlockedSet = unlocks.ToHashSet();
            int newlyOwned = 0;
            foreach (CosmeticItem item in _cosmetics)
            {
                if (!item.Owned && item.NumericId.HasValue && unlockedSet.Contains(item.NumericId.Value))
                {
                    item.Owned = true;
                    newlyOwned++;
                }
            }

            if (newlyOwned > 0)
            {
                SaveCatalog();
                UpdateCounts();
                if (_hideOwned)
                    _view.Refresh();
                SoundService.PlayChime();
                SetStatus($"Synced from save — {newlyOwned} new unlock{(newlyOwned == 1 ? "" : "s")} ✓");
            }
            else
            {
                SetStatus($"In sync with save • {unlockedSet.Count} unlocked in game • {DateTime.Now:HH:mm}");
            }
        }

        // ===== Card interaction =====

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not CosmeticItem item)
                return;

            item.Owned = !item.Owned;

            if (item.Owned)
            {
                SoundService.PlayCheck();
                PlayCheckPop(button);
            }
            else
            {
                SoundService.PlayUncheck();
            }

            SaveCatalog();
            UpdateCounts();

            if (_hideOwned)
                _view.Refresh();
        }

        /// <summary>
        /// The bounce-in flourish for the check badge. Driven here (not from a
        /// XAML trigger) so it fires only on a genuine click — the Setters own
        /// the resting state, and FillBehavior.Stop lets the animation hand
        /// back cleanly to those Setters instead of leaving a held value that
        /// would smear across recycled scroll containers.
        /// </summary>
        private static void PlayCheckPop(Button button)
        {
            if (button.Template?.FindName("CheckScale", button) is not ScaleTransform scale)
                return;

            var pop = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        // ===== Filters =====

        private bool FilterItem(object obj)
        {
            if (obj is not CosmeticItem item)
                return false;

            if (_hideOwned && item.Owned)
                return false;

            if (_rarityFilter.Count > 0 && !_rarityFilter.Contains(item.Rarity))
                return false;

            if (_categoryFilter != null && item.Category != _categoryFilter)
                return false;

            if (_search.Length > 0
                && !item.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase)
                && !item.Category.Contains(_search, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _search = SearchBox.Text.Trim();
            _view.Refresh();
        }

        private void RarityChip_Changed(object sender, RoutedEventArgs e)
        {
            _rarityFilter.Clear();
            foreach (ToggleButton chip in new[] { ChipCommon, ChipUncommon, ChipRare, ChipUltraRare })
            {
                if (chip.IsChecked == true && chip.Tag is string rarity)
                    _rarityFilter.Add(rarity);
            }
            _view.Refresh();
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingCategories)
                return;
            _categoryFilter = SelectedCategoryRaw();
            _view.Refresh();
        }

        private void HideOwned_Changed(object sender, RoutedEventArgs e)
        {
            _hideOwned = HideOwnedChip.IsChecked == true;
            _view.Refresh();
        }

        private void MuteToggle_Changed(object sender, RoutedEventArgs e)
        {
            SoundService.Muted = MuteToggle.IsChecked == true;
            MuteToggle.Content = SoundService.Muted ? "\U0001F507" : "\U0001F50A";
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RebuildCatalogAsync(userRequested: true);
        }

        /// <summary>Combo shows spaced names; filtering compares the raw ones.</summary>
        private string? SelectedCategoryRaw()
        {
            if (CategoryCombo.SelectedIndex <= 0 || CategoryCombo.SelectedItem is not string display)
                return null;
            return display.Replace(" ", "");
        }

        private void ApplyIcons()
        {
            foreach (CosmeticItem item in _cosmetics)
                item.IconPath = CosmeticIconIndex.Resolve(_iconIndex, item);

            // Decode every icon up front on background threads so scrolling and
            // resizing never pay a decode on the UI thread. Fire-and-forget: the
            // visible cards decode on demand instantly, the rest warm in behind.
            var paths = _cosmetics
                .Select(c => c.IconPath)
                .Where(p => p != null)
                .Select(p => p!)
                .Distinct()
                .ToList();
            _ = IconCache.PreloadAsync(paths);
        }

        private void PopulateCategories()
        {
            _updatingCategories = true;
            try
            {
                string? previous = _categoryFilter;

                CategoryCombo.Items.Clear();
                CategoryCombo.Items.Add("All categories");
                foreach (string category in _cosmetics.Select(c => c.Category).Distinct().OrderBy(c => c))
                    CategoryCombo.Items.Add(CosmeticItem.SpaceOutPascalCase(category));

                int index = previous != null
                    ? CategoryCombo.Items.IndexOf(CosmeticItem.SpaceOutPascalCase(previous))
                    : 0;
                CategoryCombo.SelectedIndex = index >= 0 ? index : 0;
                _categoryFilter = SelectedCategoryRaw();
            }
            finally
            {
                _updatingCategories = false;
            }
        }

        // ===== Header / status =====

        private void UpdateCounts()
        {
            int owned = _cosmetics.Count(c => c.Owned);
            int total = _cosmetics.Count;

            OwnedCountRun.Text = owned.ToString();
            TotalCountRun.Text = total.ToString();
            EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

            double target = total == 0 ? 0 : ProgressBarWidth * owned / total;
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, animation);
        }

        private void SetStatus(string message) => StatusText.Text = message;

        // ===== Native dark title bar =====

        // Window.Background only paints the client area — the title bar is
        // drawn by DWM. This flips the native title bar into dark mode too
        // (same mechanism VS Code and Windows Terminal use). No-ops silently
        // on Windows builds older than 10 20H1.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int enabled = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on early Win10 builds).
                if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
            catch
            {
                // Not on Windows / DWM unavailable — light title bar, no harm.
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
    }
}
