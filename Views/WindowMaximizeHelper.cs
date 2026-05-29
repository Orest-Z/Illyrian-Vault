/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IllyrianVault.Views;

/// <summary>
/// Shared Win32 maximize-rect logic used by every borderless window in the app.
/// Handles WM_GETMINMAXINFO so that maximizing a WindowStyle=None window respects
/// the taskbar work area on any monitor in a multi-monitor setup.
/// </summary>
internal static class WindowMaximizeHelper
{
    private const int WM_GETMINMAXINFO      = 0x0024;
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

    /// <summary>
    /// Call this from a window's OnSourceInitialized to install the hook.
    /// </summary>
    public static void InstallHook(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource src)
            src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                Handle(hwnd, msg, lParam, ref handled));
    }

    /// <summary>
    /// Call this in the window constructor (before InitializeComponent) to cap
    /// MaxWidth/MaxHeight to the primary monitor work area.  The WM_GETMINMAXINFO
    /// hook then refines this for the actual monitor on show/maximize.
    /// </summary>
    public static void CapToWorkArea(Window window)
    {
        var wa = SystemParameters.WorkArea;
        window.MaxWidth  = wa.Width;
        window.MaxHeight = wa.Height;
    }

    private static IntPtr Handle(IntPtr hwnd, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var mmi     = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref info);
            var wa = info.rcWork;
            mmi.ptMaxPosition = new POINT { x = wa.left,            y = wa.top };
            mmi.ptMaxSize     = new POINT { x = wa.right - wa.left, y = wa.bottom - wa.top };
        }
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }
}
