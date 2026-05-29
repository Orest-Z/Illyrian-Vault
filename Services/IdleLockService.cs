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
        if (Application.Current.MainWindow is { } w)
        {
            w.PreviewMouseDown += OnActivity;
            w.PreviewKeyDown   += OnActivity;
        }
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        if (Application.Current.MainWindow is { } w)
        {
            w.PreviewMouseDown -= OnActivity;
            w.PreviewKeyDown   -= OnActivity;
        }
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
