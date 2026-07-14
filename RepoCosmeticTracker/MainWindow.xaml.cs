using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        private string? _updateUrl;

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
            _ = CheckForUpdatesAsync();

            LoadCatalogFromDisk();

            string? install = GameLocator.FindRepoInstallFolder();
            SubtitleText.Text = install ?? "game not detected tracking manually";

            _iconIndex = await Task.Run(CosmeticIconIndex.BuildIndex);
            await ApplyIconsAndBakeCardsAsync();

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
        private async Task RebuildCatalogAsync(bool userRequested = false)
        {
            if (_rebuilding)
                return;

            string? dataPath = GameLocator.FindRepoDataFolder();
            if (dataPath == null)
            {
                if (userRequested)
                    SetStatus("Game install not found can't rescan.");
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

                PopulateCategories();

                HashSet<int>? unlocked = await ReadUnlockedIdsAsync();
                if (unlocked != null)
                {
                    foreach (CosmeticItem item in _cosmetics)
                        if (!item.Owned && item.NumericId.HasValue && unlocked.Contains(item.NumericId.Value))
                            item.Owned = true;
                }

                SaveCatalog();

                _iconIndex = await Task.Run(CosmeticIconIndex.BuildIndex);
                await ApplyIconsAndBakeCardsAsync();

                int added = result.Items.Count(i => !oldIds.Contains(i.Id));
                SetStatus(added > 0 && oldIds.Count > 0
                    ? $"Catalog updated {added} new cosmetic{(added == 1 ? "" : "s")} found."
                    : $"Catalog built {result.Items.Count} cosmetics.");
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

        private async Task<HashSet<int>?> ReadUnlockedIdsAsync()
        {
            string? root = GameLocator.GetRepoDataRoot();
            if (root == null)
            {
                SetStatus("Save folder not found run R.E.P.O. once to create it.");
                return null;
            }

            string metaPath = Path.Combine(root, "MetaSave.es3");
            if (!File.Exists(metaPath))
            {
                SetStatus("MetaSave.es3 not found yet waiting for the game to save.");
                return null;
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
                SetStatus("Couldn't read MetaSave.es3 (file busy) will retry on next save.");
                return null;
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
                return null;
            }

            return unlocks.ToHashSet();
        }

        private async Task SyncFromSaveAsync()
        {
            HashSet<int>? unlockedSet = await ReadUnlockedIdsAsync();
            if (unlockedSet == null)
                return;

            int newlyOwned = 0;
            foreach (CosmeticItem item in _cosmetics)
            {
                if (!item.Owned && item.NumericId.HasValue && unlockedSet.Contains(item.NumericId.Value))
                {
                    item.Owned = true;
                    RebakeCard(item);
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
                SetStatus($"Synced from save {newlyOwned} new unlock{(newlyOwned == 1 ? "" : "s")} ✓");
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
            RebakeCard(item);

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
        private static void PlayCheckPop(Button button)
        {
            if (button.Template?.FindName("CheckBadge", button) is not Border badge
                || button.Template?.FindName("CheckScale", button) is not ScaleTransform scale)
                return;

            var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550))));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750))));
            badge.BeginAnimation(UIElement.OpacityProperty, opacity);

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

        private async Task CheckForUpdatesAsync()
        {
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            UpdateChecker.UpdateInfo? info = await UpdateChecker.CheckAsync(currentVersion);
            if (info == null)
                return;

            _updateUrl = info.Url;
            UpdateBanner.Content = $"⬆ v{info.Version} available";
            UpdateBanner.Visibility = Visibility.Visible;
        }

        private void UpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            if (_updateUrl == null)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });
            }
            catch
            {
                // Nothing more useful to do
            }
        }

        /// <summary>Combo shows spaced names; filtering compares the raw ones.</summary>
        private string? SelectedCategoryRaw()
        {
            if (CategoryCombo.SelectedIndex <= 0 || CategoryCombo.SelectedItem is not string display)
                return null;
            return display.Replace(" ", "");
        }

        private async Task ApplyIconsAndBakeCardsAsync()
        {
            foreach (CosmeticItem item in _cosmetics)
                item.IconPath = CosmeticIconIndex.Resolve(_iconIndex, item);

            var bakeOrder = _cosmetics
                .OrderByDescending(c => c.RarityRank)
                .ThenBy(c => c.DisplayName)
                .ToList();

            int total = bakeOrder.Count;
            EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

            string[] rarities = { "Common", "Uncommon", "Rare", "UltraRare" };
            var rarityCountText = new Dictionary<string, TextBlock>
            {
                ["Common"] = CommonCountText,
                ["Uncommon"] = UncommonCountText,
                ["Rare"] = RareCountText,
                ["UltraRare"] = UltraRareCountText
            };

            int trueOwned = bakeOrder.Count(c => c.Owned);
            var rarityTrueTotal = rarities.ToDictionary(r => r, r => bakeOrder.Count(c => c.Rarity == r));
            var rarityTrueOwned = rarities.ToDictionary(r => r, r => bakeOrder.Count(c => c.Rarity == r && c.Owned));
            var rarityBakedSoFar = rarities.ToDictionary(r => r, _ => 0);

            // Clear any held animation from a previous UpdateCounts, otherwise
            // it outranks the per-card Width sets below and the bar freezes.
            ProgressFill.BeginAnimation(WidthProperty, null);

            TotalCountRun.Text = total.ToString();
            OwnedCountRun.Text = "0";
            ProgressFill.Width = 0;
            foreach (string r in rarities)
                rarityCountText[r].Text = $" 0/{rarityTrueTotal[r]}";

            var paths = bakeOrder
                .Select(c => c.IconPath)
                .Where(p => p != null)
                .Select(p => p!)
                .Distinct()
                .ToList();
            if (paths.Count > 0)
                SetStatus($"Decoding {paths.Count} icons…");
            await IconCache.PreloadAsync(paths);

            int i = 0;
            foreach (CosmeticItem item in bakeOrder)
            {
                item.CardBitmap = CardRenderer.Render(item, IconCache.Get(item.IconPath));
                AnimateReveal(item);
                i++;

                if (rarityBakedSoFar.ContainsKey(item.Rarity))
                    rarityBakedSoFar[item.Rarity]++;

                int shownOwned = (int)Math.Round(trueOwned * (double)i / total);
                OwnedCountRun.Text = shownOwned.ToString();
                ProgressFill.Width = ProgressBarWidth * shownOwned / total;

                foreach (string r in rarities)
                {
                    int rTotal = rarityTrueTotal[r];
                    int shown = rTotal == 0
                        ? 0
                        : (int)Math.Round(rarityTrueOwned[r] * (double)rarityBakedSoFar[r] / rTotal);
                    rarityCountText[r].Text = $" {shown}/{rTotal}";
                }

                SetStatus($"Rendering cards… {i}/{total}");
                await Task.Delay(4);
            }

            UpdateCounts();
        }

        private void RebakeCard(CosmeticItem item) => item.CardBitmap = CardRenderer.Render(item, IconCache.Get(item.IconPath));


        private void AnimateReveal(CosmeticItem item)
        {
            if (CardsHost.ItemContainerGenerator.ContainerFromItem(item) is not UIElement container)
                return;

            var scale = new ScaleTransform(0.7, 0.7);
            container.RenderTransformOrigin = new Point(0.5, 0.5);
            container.RenderTransform = scale;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop };
            container.BeginAnimation(OpacityProperty, fade);

            var pop = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
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

            SetRarityCount(CommonCountText, "Common");
            SetRarityCount(UncommonCountText, "Uncommon");
            SetRarityCount(RareCountText, "Rare");
            SetRarityCount(UltraRareCountText, "UltraRare");
        }

        private void SetRarityCount(TextBlock target, string rarity)
        {
            int rarityOwned = 0;
            int rarityTotal = 0;
            foreach (CosmeticItem item in _cosmetics)
            {
                if (item.Rarity != rarity) continue;
                rarityTotal++;
                if (item.Owned) rarityOwned++;
            }
            target.Text = $" {rarityOwned}/{rarityTotal}";
        }

        private void SetStatus(string message) => StatusText.Text = message;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int enabled = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
            catch
            {
                // Not on Windows / DWM unavailable
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
    }
}
