using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RepoCosmeticTracker
{
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
}
