/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Globalization;
using System.Windows.Data;

namespace IllyrianVault.Converters;

// Compares a bound string value against ConverterParameter.
// Used for RadioButton.IsChecked two-way binding in the sidebar nav and titlebar toggles.
// Convert : value == parameter → true (button appears checked)
// ConvertBack: true → return parameter string (updates bound property), false → DoNothing
[ValueConversion(typeof(string), typeof(bool))]
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() ?? Binding.DoNothing : Binding.DoNothing;
}
