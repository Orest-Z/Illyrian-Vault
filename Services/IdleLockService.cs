/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;
using System.Windows.Threading;

namespace IllyrianVault.Services;

public sealed class IdleLockService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action          _onIdle;
    private TimeSpan                 _timeout;

    public IdleLockService(Action onIdle, TimeSpan timeout)
    {
        _onIdle  = onIdle;
        _timeout = timeout;
        _timer   = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += OnTick;
    }

    private DateTime _lastActivity = DateTime.UtcNow;

    public void Start()
    {
        // Hook all currently open windows and any that open later so modal dialogs
        // (e.g. AddEntryWindow) also reset the idle timer.
        foreach (Window w in Application.Current.Windows)
            AttachWindow(w);

        Application.Current.Activated += OnAppActivated;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Application.Current.Activated -= OnAppActivated;

        foreach (Window w in Application.Current.Windows)
            DetachWindow(w);
    }

    private void OnAppActivated(object? sender, EventArgs e)
    {
        // A new modal window just got focus — attach activity listeners to it.
        foreach (Window w in Application.Current.Windows)
            AttachWindow(w);
        _lastActivity = DateTime.UtcNow;
    }

    private void AttachWindow(Window w)
    {
        // Detach first to avoid double-hooking if Start() is called more than once.
        w.PreviewMouseDown -= OnActivity;
        w.PreviewKeyDown   -= OnActivity;
        w.PreviewMouseDown += OnActivity;
        w.PreviewKeyDown   += OnActivity;
    }

    private void DetachWindow(Window w)
    {
        w.PreviewMouseDown -= OnActivity;
        w.PreviewKeyDown   -= OnActivity;
    }

    public void SetTimeout(TimeSpan timeout) => _timeout = timeout;

    private void OnActivity(object sender, System.Windows.Input.InputEventArgs e) =>
        _lastActivity = DateTime.UtcNow;

    private void OnTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastActivity >= _timeout)
            _onIdle();
    }

    public void Dispose() => Stop();
}
