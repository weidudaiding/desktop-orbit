using System;
using System.Windows.Interop;
using DesktopOrbit.Shell.Platform;

namespace DesktopOrbit.App.Platform;

/// <summary>
/// Message-only window that owns the global hotkey and tray icon callbacks.
/// Keeping both on a message-only HWND avoids activation side effects.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private HotkeyRegistration? _hotkey;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("DesktopOrbit.MessageWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            ParentWindow = PlatformConstants.MessageWindowParent
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    public event Action? HotkeyPressed;
    public event Action? TrayActivated;
    public event Action? TrayMenuRequested;
    public event Action<int>? TrayCommand;

    public TrayIcon? Tray { get; private set; }

    public bool TryRegisterHotkey(string chord)
    {
        _hotkey ??= new HotkeyRegistration(Handle);
        return _hotkey.TryRegister(chord);
    }

    public void UnregisterHotkey() => _hotkey?.Unregister();

    public TrayIcon AttachTray()
    {
        Tray = new TrayIcon(Handle);
        Tray.Add();
        Tray.Activated += () => TrayActivated?.Invoke();
        Tray.MenuRequested += () => TrayMenuRequested?.Invoke();
        Tray.CommandInvoked += id => TrayCommand?.Invoke(id);
        return Tray;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == PlatformConstants.WmHotkey && wParam.ToInt32() == HotkeyRegistration.HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == TrayIcon.WmTrayCallback && Tray is not null)
        {
            if (Tray.HandleMessage(msg, lParam))
            {
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        Tray?.Dispose();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
