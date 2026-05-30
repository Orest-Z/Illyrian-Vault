/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using IllyrianVault.Models;
using IllyrianVault.Services;
using IllyrianVault.ViewModels;
using MahApps.Metro.IconPacks;

namespace IllyrianVault.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel  _vm;
    private readonly IdleLockService _idleLock;

    // ── Win32 hit-test and maximize constants ────────────────────────────────────
    private const int WM_NCHITTEST     = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int HTCLIENT         = 1;
    private const int HTLEFT           = 10;
    private const int HTRIGHT          = 11;
    private const int HTTOP            = 12;
    private const int HTTOPLEFT        = 13;
    private const int HTTOPRIGHT       = 14;
    private const int HTBOTTOM         = 15;
    private const int HTBOTTOMLEFT     = 16;
    private const int HTBOTTOMRIGHT    = 17;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor, rcWork;
        public int  dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ── Constructor ──────────────────────────────────────────────────────────────
    public MainWindow(string username)
    {
        // Cap maximize dimensions to the work area BEFORE InitializeComponent creates
        // the HWND. WPF's own WM_GETMINMAXINFO handler enforces MaxWidth/MaxHeight at
        // HWND-creation time, so the window can never grow over the taskbar even on
        // the very first maximize — no state-toggle hack required.
        var wa = SystemParameters.WorkArea;
        MaxWidth  = wa.Width;
        MaxHeight = wa.Height;

        InitializeComponent();
        _vm = new MainViewModel(App.Database, App.Encryption, App.SessionKey, username);
        DataContext = _vm;

        _idleLock = new IdleLockService(
            () => _vm.LockCommand.Execute(null),
            TimeSpan.FromMinutes(5));

        _vm.LockRequested          += OnLockRequested;
        _vm.NewEntryRequested      += OnNewEntryRequested;
        _vm.ExportRequested        += OnExportRequested;
        _vm.ToastRequested         += ShowToast;
        _vm.IdleTimeoutChanged     += t => _idleLock.SetTimeout(t);
        _vm.ConfirmDeleteRequested += () =>
            MessageBox.Show(
                "Delete this entry? This action cannot be undone.",
                "Illyrian Vault — Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

        Loaded += async (_, _) =>
        {
            StartWelcomeOverlay(username);
            await _vm.LoadEntriesCommand.ExecuteAsync(null);
        };
    }

    private void StartWelcomeOverlay(string username)
    {
        var welcomeKey = App.Localization.Current == AppLanguage.Sq ? "WelcomeBack" : "WelcomeBack";
        var label = TryFindResource(welcomeKey) as string ?? "Welcome back";
        WelcomeText.Text = $"{label}, {username}";

        var anim = new DoubleAnimation
        {
            From        = 1.0,
            To          = 0.0,
            BeginTime   = TimeSpan.FromMilliseconds(800),
            Duration    = new Duration(TimeSpan.FromMilliseconds(1200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        anim.Completed += (_, _) => WelcomeOverlay.Visibility = Visibility.Collapsed;
        WelcomeOverlay.BeginAnimation(OpacityProperty, anim);
    }

    // ── WndProc hook: resize + maximize rect ─────────────────────────────────────
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource src)
        {
            src.AddHook(WndProc);
            _idleLock.Start();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST when WindowState == WindowState.Normal:
                handled = true;
                return GetResizeHit(lParam);

            case WM_GETMINMAXINFO:
                AdjustMaximizedRect(hwnd, lParam);
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    private IntPtr GetResizeHit(IntPtr lParam)
    {
        long xy = (long)lParam;
        int  sx = (short)(xy & 0xFFFF);
        int  sy = (short)((xy >> 16) & 0xFFFF);

        // PointFromScreen converts physical pixels → WPF DIPs, handling DPI correctly.
        var    pos = PointFromScreen(new Point(sx, sy));
        double w   = ActualWidth;
        double h   = ActualHeight;
        const double grip = 8;

        bool l = pos.X < grip,      t = pos.Y < grip;
        bool r = w - pos.X < grip,  b = h - pos.Y < grip;

        if (t && l) return (IntPtr)HTTOPLEFT;
        if (t && r) return (IntPtr)HTTOPRIGHT;
        if (b && l) return (IntPtr)HTBOTTOMLEFT;
        if (b && r) return (IntPtr)HTBOTTOMRIGHT;
        if (t)      return (IntPtr)HTTOP;
        if (b)      return (IntPtr)HTBOTTOM;
        if (l)      return (IntPtr)HTLEFT;
        if (r)      return (IntPtr)HTRIGHT;
        return (IntPtr)HTCLIENT;
    }

    // Use the monitor's work area (taskbar excluded) so maximized window stays inside it.
    private static void AdjustMaximizedRect(IntPtr hwnd, IntPtr lParam)
    {
        var mmi     = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref info);
            var wa = info.rcWork;
            mmi.ptMaxPosition = new POINT { x = wa.left,             y = wa.top };
            mmi.ptMaxSize     = new POINT { x = wa.right - wa.left,  y = wa.bottom - wa.top };
        }
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    // ── State change: adjust rounded border and icon for maximize ────────────────
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool maximized           = WindowState == WindowState.Maximized;
        OuterBorder.Margin       = maximized ? new Thickness(0)   : new Thickness(20);
        OuterBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
        MaximizeIcon.Kind        = maximized
            ? PackIconMaterialKind.FullscreenExit
            : PackIconMaterialKind.Fullscreen;
    }

    // ── New Entry dialog ──────────────────────────────────────────────────────────
    private async void OnNewEntryRequested()
    {
        var addVm  = new AddEntryViewModel(App.Encryption, App.SessionKey);
        var dialog = new AddEntryWindow(addVm) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _vm.InsertNewEntryAsync(addVm.BuildEntry());
    }

    // ── Lock ──────────────────────────────────────────────────────────────────────
    private void OnLockRequested()
    {
        _idleLock.Stop();
        var auth = new AuthWindow();
        auth.Show();
        Close();
    }

    // ── Export ────────────────────────────────────────────────────────────────────
    private void OnExportRequested()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title            = "Export Vault",
            Filter           = "CSV (*.csv)|*.csv|Encrypted JSON (*.ivjson)|*.ivjson",
            FileName         = $"illyrian-vault-export-{DateTime.Now:yyyy-MM-dd}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;

        if (dialog.FilterIndex == 1)
        {
            var warn = MessageBox.Show(
                "This file will contain ALL your passwords in plain text.\n\n" +
                "Do NOT save it to a cloud-synced folder (OneDrive, Dropbox, Desktop on a managed PC).\n\n" +
                "Are you sure you want to continue?",
                "Illyrian Vault — Plain Text Export Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (warn != MessageBoxResult.Yes) return;

            var rows = _vm.AllEntries.Select(evm =>
            {
                var pw = !string.IsNullOrEmpty(evm.Model.EncryptedPassword)
                    ? App.Encryption.Decrypt(evm.Model.EncryptedPassword, App.SessionKey)
                    : string.Empty;
                return (evm.Model, pw);
            });
            ExportService.ExportCsv(rows, dialog.FileName);
        }
        else
        {
            ExportService.ExportEncryptedJson(
                _vm.AllEntries.Select(e => e.Model), dialog.FileName, App.SessionKey, App.Encryption);
        }

        MessageBox.Show("Export saved successfully.", "Illyrian Vault",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Copy toast ────────────────────────────────────────────────────────────────
    private void ShowToast(string message)
    {
        ToastText.Text = message;

        var fadeIn  = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(1500),
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeIn,  ToastBorder);
        Storyboard.SetTarget(fadeOut, ToastBorder);
        Storyboard.SetTargetProperty(fadeIn,  new PropertyPath(OpacityProperty));
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }

    // ── Title bar ─────────────────────────────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeClick(sender, e);
            return;
        }
        if (WindowState == WindowState.Normal)
            DragMove();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) =>
        Close();
}
