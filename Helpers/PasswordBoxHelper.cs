/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace IllyrianVault.Helpers;

/// <summary>
/// Attached properties for MVVM binding on WPF's PasswordBox.
///
/// TWO MODES:
///
/// 1. SECURE (preferred for login): bind PasswordBox.SecurePassword (a SecureString)
///    to a SecureString property on the ViewModel.  The password never exists as a
///    plain System.String on the managed heap — it stays in the PasswordBox's kernel
///    DPAPI buffer and is extracted to a pinned byte[] only at the moment the unlock
///    command fires (see SecureMemory.PinFromSecureString).
///
///    XAML:
///      helpers:PasswordBoxHelper.BindSecurePassword="True"
///      helpers:PasswordBoxHelper.SecurePassword="{Binding SecurePassword, Mode=TwoWay}"
///
/// 2. LEGACY string binding (kept for RegisterViewModel strength-score UX):
///    helpers:PasswordBoxHelper.BindPassword="True"
///    helpers:PasswordBoxHelper.BoundPassword="{Binding NewPassword, Mode=TwoWay}"
/// </summary>
public static class PasswordBoxHelper
{
    // ── Secure mode ───────────────────────────────────────────────────────────

    public static readonly DependencyProperty SecurePasswordProperty =
        DependencyProperty.RegisterAttached(
            "SecurePassword", typeof(SecureString), typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(null, OnSecurePasswordChanged));

    public static readonly DependencyProperty BindSecurePasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindSecurePassword", typeof(bool), typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindSecurePasswordChanged));

    public static SecureString? GetSecurePassword(DependencyObject d) =>
        (SecureString?)d.GetValue(SecurePasswordProperty);
    public static void SetSecurePassword(DependencyObject d, SecureString? v) =>
        d.SetValue(SecurePasswordProperty, v);

    public static bool GetBindSecurePassword(DependencyObject d) =>
        (bool)d.GetValue(BindSecurePasswordProperty);
    public static void SetBindSecurePassword(DependencyObject d, bool v) =>
        d.SetValue(BindSecurePasswordProperty, v);

    private static void OnBindSecurePasswordChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if ((bool)e.NewValue)
            box.PasswordChanged += OnSecurePasswordBoxChanged;
        else
            box.PasswordChanged -= OnSecurePasswordBoxChanged;
    }

    private static void OnSecurePasswordChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // No-op: the ViewModel updates the SecureString property; we only need
        // ViewModel → View sync on initial load, which is rare (usually empty).
        // View → ViewModel is the primary direction (handled in OnSecurePasswordBoxChanged).
    }

    private static void OnSecurePasswordBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;

        // PasswordBox.SecurePassword returns a NEW COPY of the SecureString on
        // every call (confirmed in .NET source). The ViewModel's SecurePassword
        // setter disposes the old copy, preventing unbounded accumulation.
        box.SetCurrentValue(SecurePasswordProperty, box.SecurePassword);
    }

    // ── Legacy string mode ────────────────────────────────────────────────────

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
        box.SetCurrentValue(BoundPasswordProperty, box.Password);
        _updating = false;
    }
}
