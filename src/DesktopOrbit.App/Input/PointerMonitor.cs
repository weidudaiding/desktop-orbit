using System;
using System.Diagnostics;
using DesktopOrbit.Core.Config;
using DesktopOrbit.Shell.Platform;

namespace DesktopOrbit.App.Input;

/// <summary>
/// Watches the cursor while the menu is open and raises the two dismissal signals
/// the menu supports: a click anywhere outside the menu, and the pointer staying
/// outside the menu for longer than a grace period.
///
/// It polls instead of installing a mouse hook. A low-level hook would also deliver
/// clicks to other applications more slowly and trips security software for no
/// benefit here: the menu only needs to know that a click happened, not to swallow
/// it, so the click must reach the window underneath anyway.
/// </summary>
public sealed class PointerMonitor : IDisposable
{
    private readonly Func<AppConfig> _config;
    private readonly Func<(int Left, int Top, int Right, int Bottom)?> _menuBounds;
    private readonly Func<bool> _isMenuOpen;
    private readonly Action _onClickOutside;
    private readonly Action _onPointerLeft;

    private readonly System.Threading.CancellationTokenSource _cts = new();
    private System.Threading.Tasks.Task? _worker;
    private volatile bool _enabled;
    private bool _wasDown;
    private long _outsideSinceTicks;

    public PointerMonitor(Func<AppConfig> config,
        Func<(int Left, int Top, int Right, int Bottom)?> menuBounds,
        Func<bool> isMenuOpen,
        Action onClickOutside,
        Action onPointerLeft)
    {
        _config = config;
        _menuBounds = menuBounds;
        _isMenuOpen = isMenuOpen;
        _onClickOutside = onClickOutside;
        _onPointerLeft = onPointerLeft;
    }

    public void Start()
    {
        if (_worker is not null) return;
        _worker = System.Threading.Tasks.Task.Run(() => Loop(_cts.Token));
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == _enabled) return;
        _enabled = enabled;
        _wasDown = false;
        _outsideSinceTicks = 0;
    }

    private void Loop(System.Threading.CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_enabled || !_isMenuOpen())
                {
                    _wasDown = false;
                    _outsideSinceTicks = 0;
                    token.WaitHandle.WaitOne(120);
                    continue;
                }

                AppConfig config = _config();
                DismissOptions dismiss = config.Menu.Dismiss;
                (int x, int y) = SystemProbe.GetCursorPosition();

                bool down = SystemProbe.IsCursorDown(PlatformConstants.VkLeftButton)
                            || SystemProbe.IsCursorDown(PlatformConstants.VkRightButton);

                bool inside = IsInside(dismiss.PointerLeaveBufferDip, x, y);

                // Mode A: a click that starts outside the menu closes it. The press
                // edge is what matters; the click still reaches the window below.
                if (dismiss.CloseOnClickOutside && down && !_wasDown && !inside)
                {
                    _wasDown = down;
                    _onClickOutside();
                    token.WaitHandle.WaitOne(120);
                    continue;
                }

                _wasDown = down;

                // Mode B: pointer parked outside the menu for longer than the delay.
                if (dismiss.CloseOnPointerLeave && !inside)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (_outsideSinceTicks == 0)
                    {
                        _outsideSinceTicks = now;
                    }
                    else
                    {
                        double elapsedMs = (now - _outsideSinceTicks) * 1000.0 / Stopwatch.Frequency;
                        if (elapsedMs >= Math.Max(0, dismiss.PointerLeaveDelayMs))
                        {
                            _outsideSinceTicks = 0;
                            _onPointerLeft();
                            token.WaitHandle.WaitOne(120);
                            continue;
                        }
                    }
                }
                else
                {
                    _outsideSinceTicks = 0;
                }

                token.WaitHandle.WaitOne(_enabled ? 30 : 120);
            }
            catch (Exception)
            {
                token.WaitHandle.WaitOne(200);
            }
        }
    }

    private bool IsInside(double bufferDip, int x, int y)
    {
        if (_menuBounds() is not { } bounds) return false;

        // The buffer is in DIPs; convert with the monitor scale of the menu window.
        double scale = SystemProbe.GetMonitorDpiScale(
            bounds.Left + (bounds.Right - bounds.Left) / 2,
            bounds.Top + (bounds.Bottom - bounds.Top) / 2);
        int buffer = (int)Math.Round(Math.Max(0, bufferDip) * scale);

        return x >= bounds.Left - buffer && x < bounds.Right + buffer
            && y >= bounds.Top - buffer && y < bounds.Bottom + buffer;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromMilliseconds(800));
        }
        catch (AggregateException)
        {
        }
        _cts.Dispose();
    }
}
