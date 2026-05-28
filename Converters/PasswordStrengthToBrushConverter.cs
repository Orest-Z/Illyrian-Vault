using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IllyrianVault.Converters;

[ValueConversion(typeof(int), typeof(SolidColorBrush))]
public sealed class PasswordStrengthToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value is int score ? score switch
        {
            <= 1 => "BrushDanger",
            2 or 3 => "BrushWarning",
            4 => "BrushAccent",
            _ => "BrushSuccess",
        } : "BrushTextTertiary";

        return Application.Current.TryFindResource(key) as SolidColorBrush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
