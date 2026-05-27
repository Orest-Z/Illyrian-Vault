using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MahApps.Metro.IconPacks;

namespace IllyriaVault.Converters;

// Converts bool (IsFavorite) → PackIconMaterialKind so one icon element handles both states.
[ValueConversion(typeof(bool), typeof(PackIconMaterialKind))]
public sealed class FavoriteIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? PackIconMaterialKind.Star : PackIconMaterialKind.StarOutline;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
