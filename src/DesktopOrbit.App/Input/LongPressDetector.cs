using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopOrbit.Core.Config;
using DesktopOrbit.Shell.Desktop;
using DesktopOrbit.Shell.Platform;

namespace DesktopOrbit.App.Input;

/// <summary>
/// Hold-to-open detector that never installs a global hook. It polls the physical
/// mouse button, watches for movement past a slop threshold, and asks the desktop
/// locator whether the press started on the desktop surface. Idle cost is one
/// GetAsyncKeyState + GetCursorPos every 50 ms.
/// </summary>
public sealed class LongPressDetector : IDisposable
{
    private readonly ConfigHolder _config;
    private readonly DesktopLocator _locator;
    private readonly DesktopHitTester _hitTester;
    private readonly Func<bool> _isMenuOpen;
    private readonly Action<int, int> _onTriggered;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _wake = new(false);

    private Task? _worker;
    private long _suppressUntilTicks;

    public LongPressDetector(ConfigHolder config, DesktopLocator locator, DesktopHitTester hitTester,
        Func<bool> isMenuOpen, Action<int, int> onTriggered)
    {
        _config = config;
        _locator = locator;
        _hitTester = hitTester;
        _isMenuOpen = isMenuOpen;
        _onTriggered = onTriggered;
    }

    public void Start()
    {
        if (_worker is not null) return;
        _worker = Task.Run(() => Loop(_cts.Token));
    }

    public void SuppressFor(TimeSpan duration)
    {
        Volatile.Write(ref _suppressUntilTicks, DateTime.UtcNow.Add(duration).Ticks);
    }

    private void Loop(CancellationToken token)
    {
        bool armed = false;
        int downX = 0, downY = 0;
        long downAt = 0;

        while (!token.IsCancellationRequested)
        {
            LongPressOptions options = _config.Current.Trigger.LongPress;
            bool buttonDown = SystemProbe.IsCursorDown(PlatformConstants.VkLeftButton);
            int interval = buttonDown ? 16 : 50;

            if (_wake.Wait(interval) || token.IsCancellationRequested) return;

            try
            {
                if (!_config.Current.Trigger.LongPressEnabled || _isMenuOpen())
                {
                    armed = false;
                    continue;
                }

                if (Interlocked.Read(ref _suppressUntilTicks) > DateTime.UtcNow.Ticks)
                {
                    armed = false;
                    continue;
                }

                bool down = SystemProbe.IsCursorDown(PlatformConstants.VkLeftButton);
                (int x, int y) = SystemProbe.GetCursorPosition();

                if (!down)
                {
                    armed = false;
                    continue;
                }

                if (!armed)
                {
                    armed = true;
                    downX = x;
                    downY = y;
                    downAt = Environment.TickCount64;
                    continue;
                }

                if (Environment.TickCount64 - downAt < Math.Max(120, options.HoldMs)) continue;

                int dx = x - downX;
                int dy = y - downY;
                int slop = Math.Max(2, options.MoveSlopPx);
                if (dx * dx + dy * dy > slop * slop)
                {
                    armed = false;
                    continue;
                }

                if (!_locator.IsDesktopPoint(x, y))
                {
                    armed = false;
                    continue;
                }

                if (options.OnlyWhenDesktopForeground && !_locator.IsExplorerForeground())
                {
                    armed = false;
                    continue;
                }

                if (_config.Current.General.PauseWhenFullscreen && SystemProbe.IsFullscreenOrPresenting())
                {
                    armed = false;
                    continue;
                }

                if (options.RequireBlankSpot && _hitTester.QueryBlankSpot(x, y) == BlankSpotResult.OnIcon)
                {
                    armed = false;
                    continue;
                }

                armed = false;
                _onTriggered(x, y);
            }
            catch (Exception)
            {
                armed = false;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _wake.Set();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _wake.Dispose();
        _cts.Dispose();
    }
}
