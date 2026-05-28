using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IllyrianVault.Converters;

/// <summary>
/// Converts a strength score (0–5) to a filled or unfilled bar brush.
/// ConverterParameter = "1".."5" (the bar's 1-based index).
/// </summary>
[ValueConversion(typeof(int), typeof(SolidColorBrush))]
public sealed class PasswordStrengthToBarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int score    = value is int i ? i : 0;
        int barIndex = int.TryParse(parameter?.ToString(), out int p) ? p : 1;

        if (score >= barIndex)
        {
            string key = score switch
            {
                <= 1 => "BrushDanger",
                2 or 3 => "BrushWarning",
                4 => "BrushAccent",
                _ => "BrushSuccess",
            };
            return Application.Current.TryFindResource(key) as SolidColorBrush ?? Brushes.Gray;
        }

        return Application.Current.TryFindResource("BrushElevated") as SolidColorBrush
               ?? Brushes.DimGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
