using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RepoCosmeticTracker.Models;

namespace RepoCosmeticTracker.Services
{
    public static class CardRenderer
    {
        public const double CardWidth = 150;
        public const double CardHeight = 182;
        private const double RenderScale = 2.0;

        private static readonly FontFamily UiFont = new("Segoe UI");

        public static RenderTargetBitmap Render(CosmeticItem item, ImageSource? icon)
        {
            FrameworkElement visual = BuildVisual(item, icon);

            visual.Measure(new Size(CardWidth, CardHeight));
            visual.Arrange(new Rect(0, 0, CardWidth, CardHeight));

            var rtb = new RenderTargetBitmap(
                (int)(CardWidth * RenderScale), (int)(CardHeight * RenderScale),
                96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static FrameworkElement BuildVisual(CosmeticItem item, ImageSource? icon)
        {
            Brush rarityBrush = LookupBrush($"Rarity{item.Rarity}Brush", "RarityUnknownBrush");
            var cardBg = (Brush)Application.Current.FindResource("CardBrush");
            var textBrush = (Brush)Application.Current.FindResource("TextBrush");
            var textDimBrush = (Brush)Application.Current.FindResource("TextDimBrush");
            var successBrush = (Brush)Application.Current.FindResource("SuccessBrush");

            var iconArea = new Grid();
            if (icon != null)
            {
                iconArea.Children.Add(new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(4) });
            }
            else
            {
                iconArea.Children.Add(new TextBlock
                {
                    Text = "?",
                    FontFamily = UiFont,
                    FontSize = 30,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x48)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            Grid.SetRow(iconArea, 0);

            var nameText = new TextBlock
            {
                Text = item.DisplayName,
                FontFamily = UiFont,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 32,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = textBrush
            };
            Grid.SetRow(nameText, 1);

            var metaStack = new StackPanel();
            metaStack.Children.Add(new TextBlock
            {
                Text = item.CategoryDisplay,
                FontFamily = UiFont,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Foreground = textDimBrush
            });
            metaStack.Children.Add(new TextBlock
            {
                Text = item.Rarity,
                FontFamily = UiFont,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = rarityBrush
            });
            Grid.SetRow(metaStack, 2);

            var innerGrid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            innerGrid.Children.Add(iconArea);
            innerGrid.Children.Add(nameText);
            innerGrid.Children.Add(metaStack);

            var card = new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                CornerRadius = new CornerRadius(12),
                Background = cardBg,
                BorderThickness = new Thickness(1.4),
                BorderBrush = rarityBrush,
                Opacity = item.Owned ? 0.35 : 1.0,
                Child = innerGrid
            };

            if (!item.Owned) return card;

            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = successBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                Child = new TextBlock
                {
                    Text = "✓",
                    FontFamily = UiFont,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x24, 0x1A)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };

            var outer = new Grid { Width = CardWidth, Height = CardHeight };
            outer.Children.Add(card);
            outer.Children.Add(badge);
            return outer;
        }

        private static Brush LookupBrush(string key, string fallbackKey)
        {
            if (Application.Current.TryFindResource(key) is Brush brush)
                return brush;
            return (Brush)Application.Current.FindResource(fallbackKey);
        }
    }
}
