using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace RepoCosmeticTracker.Models
{
    public class CosmeticItem : INotifyPropertyChanged
    {
        private bool _owned;

        public string Id { get; set; } = "";               
        public int? NumericId { get; set; }                 
        public string DisplayName { get; set; } = "";       
        public string Category { get; set; } = "Uncategorized"; 
        public string Rarity { get; set; } = "";             

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

        [JsonIgnore]
        public string CategoryDisplay => SpaceOutPascalCase(Category);

        private ImageSource? _cardBitmap;

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
