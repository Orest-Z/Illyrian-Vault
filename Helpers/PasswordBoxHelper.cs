using System.Windows;
using System.Windows.Controls;

namespace IllyriaVault.Helpers;

/// <summary>
/// Attached property that enables two-way MVVM data binding on PasswordBox.
///
/// Usage in XAML:
///   helpers:PasswordBoxHelper.BindPassword="True"
///   helpers:PasswordBoxHelper.BoundPassword="{Binding MasterPassword, Mode=TwoWay}"
///
/// Why this exists: WPF's PasswordBox deliberately blocks the binding pipeline
/// because SecureString shouldn't flow through the binding system. This helper
/// provides a controlled, opt-in bridge.
/// </summary>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword", typeof(bool), typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    private static bool _updating;

    public static string GetBoundPassword(DependencyObject d) =>
        (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string v) =>
        d.SetValue(BoundPasswordProperty, v);

    public static bool GetBindPassword(DependencyObject d) =>
        (bool)d.GetValue(BindPasswordProperty);
    public static void SetBindPassword(DependencyObject d, bool v) =>
        d.SetValue(BindPasswordProperty, v);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box || _updating) return;
        box.Password = (string)e.NewValue;
    }

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if ((bool)e.NewValue)
            box.PasswordChanged += OnPasswordBoxChanged;
        else
            box.PasswordChanged -= OnPasswordBoxChanged;
    }

    private static void OnPasswordBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        _updating = true;
        // SetCurrentValue keeps the TwoWay binding expression intact so the ViewModel
        // source property is updated on every keystroke. SetValue would overwrite the
        // binding with a literal and sever the link after the first character.
        box.SetCurrentValue(BoundPasswordProperty, box.Password);
        _updating = false;
    }
}
