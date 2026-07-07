using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace RepoCosmeticTracker.Models
{
    /// <summary>
    /// One row in the checklist. Field names deliberately mirror REPO's own
    /// CosmeticAsset class (assetId/assetName/type/rarity), confirmed via
    /// assembly reflection, so mapping real data onto this later is direct.
    /// "Owned" is editable by hand for now; once we know where ownership
    /// lives in the save data, SaveService can set this automatically instead.
    /// </summary>
    public class CosmeticItem : INotifyPropertyChanged
    {
        private bool _owned;

        public string Id { get; set; } = "";               // CosmeticAsset.assetId
        public int? NumericId { get; set; }                 // index used in the save's "cosmeticUnlocks" list
        public string DisplayName { get; set; } = "";       // CosmeticAsset.assetName
        public string Category { get; set; } = "Uncategorized"; // CosmeticAsset.type (slot, e.g. "Hat", "Eyewear")
        public string Rarity { get; set; } = "";             // CosmeticAsset.rarity (Common/Uncommon/Rare/UltraRare)

        /// <summary>Sort key so UltraRare floats to the top of the grid.</summary>
        [JsonIgnore]
        public int RarityRank => Rarity switch
        {
            "UltraRare" => 3,
            "Rare" => 2,
            "Uncommon" => 1,
            "Common" => 0,
            _ => -1
        };

        private string? _iconPath;

        /// <summary>
        /// Full path to the game's own cached icon PNG for this cosmetic
        /// (resolved at runtime from LocalLow\semiwork\Repo\Cache\Icons —
        /// machine-specific, so never persisted).
        /// </summary>
        [JsonIgnore]
        public string? IconPath
        {
            get => _iconPath;
            set
            {
                if (_iconPath == value) return;
                _iconPath = value;
                OnPropertyChanged();
            }
        }

        /// <summary>"BodyTopOverlay" shown as "Body Top Overlay".</summary>
        [JsonIgnore]
        public string CategoryDisplay => SpaceOutPascalCase(Category);

        private ImageSource? _cardBitmap;

        /// <summary>
        /// The card's whole resting appearance pre-baked into one bitmap by
        /// CardRenderer (see that class for why). Never persisted — rebuilt
        /// at startup and whenever Owned changes.
        /// </summary>
        [JsonIgnore]
        public ImageSource? CardBitmap
        {
            get => _cardBitmap;
            set
            {
                if (_cardBitmap == value) return;
                _cardBitmap = value;
                OnPropertyChanged();
            }
        }

        public static string SpaceOutPascalCase(string value)
            => System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");

        public bool Owned
        {
            get => _owned;
            set
            {
                if (_owned == value) return;
                _owned = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
