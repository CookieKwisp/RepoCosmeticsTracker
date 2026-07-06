using System.ComponentModel;
using System.Runtime.CompilerServices;

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
