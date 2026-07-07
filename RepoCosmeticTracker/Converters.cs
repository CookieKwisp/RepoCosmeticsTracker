using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RepoCosmeticTracker.Services;

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
    /// Icon path → decoded bitmap, served from the shared <see cref="IconCache"/>
    /// (pre-warmed on background threads), so binding during scroll/resize is a
    /// pure lookup with no decode work on the UI thread.
    /// </summary>
    public class IconImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => IconCache.Get(value as string);

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
