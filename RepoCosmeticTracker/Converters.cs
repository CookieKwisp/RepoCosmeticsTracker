using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RepoCosmeticTracker
{
    /// <summary>Maps a rarity string ("Rare") to its themed brush from App.xaml.</summary>
    public class RarityBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (Application.Current.TryFindResource($"Rarity{value}Brush") is Brush brush)
                return brush;
            return Application.Current.TryFindResource("RarityUnknownBrush") as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Icon path → decoded bitmap. Decodes at card size (not the PNG's full
    /// resolution) and caches frozen bitmaps, so hundreds of cards stay cheap
    /// on both memory and startup time.
    /// </summary>
    public class IconImageConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || path.Length == 0)
                return null;

            if (Cache.TryGetValue(path, out BitmapImage? cached))
                return cached;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = 160; // 2x the card's icon area, stays crisp
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                Cache[path] = bmp;
                return bmp;
            }
            catch
            {
                // Unreadable/corrupt cache file — card just shows no image.
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>Same mapping but as a raw Color, for effects like the card glow.</summary>
    public class RarityColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (Application.Current.TryFindResource($"Rarity{value}Color") is Color color)
                return color;
            return Application.Current.TryFindResource("RarityUnknownColor") as Color? ?? Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
