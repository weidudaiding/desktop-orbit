using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopOrbit.App.Input;
using DesktopOrbit.App.UI;
using DesktopOrbit.Core.Config;
using DesktopOrbit.Core.Layout;
using DesktopOrbit.Core.Models;
using DesktopOrbit.Shell.Platform;
using DesktopOrbit.Shell.Search;
using DesktopOrbit.Shell.Shell;
using MenuItem = DesktopOrbit.Core.Models.MenuItem;

namespace DesktopOrbit.App.Windows;

/// <summary>
/// Borderless per-pixel transparent menu. One RingMenuView child does all drawing,
/// an off-screen TextBox owns keyboard and IME input, and a SearchPanelView lists
/// search hits. Everything routes through a single state object to keep hover,
/// selection and drill-down consistent.
/// </summary>
public sealed class OrbitWindow : Window
{
    private readonly ConfigHolder _config;
    private readonly FolderLister _lister;
    private readonly RecentStore _recent;
    private readonly IconLoader _icons;
    private readonly SearchIndex _search;
    private readonly MenuNavigator _navigator = new();
    private readonly Canvas _root;
    private readonly RingMenuView _ring;
    private readonly SearchPanelView _panel;
    private readonly TextBox _input;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _drillTimer;
    private readonly DispatcherTimer _searchTimer;
    private readonly PointerMonitor _pointerMonitor;

    private IntPtr _handle;
    private CancellationTokenSource? _iconCts;
    private CancellationTokenSource? _searchCts;
    private bool _pressed;
    private Point _pressOrigin;
    private bool _suppressFocusLoss;
    private int _pendingDrill = -1;
    private int _pageOffset;
    private readonly Stack<int> _pageHistory = new();
    private int _lastRegionRadius;
    private long _lastInteractionTicks;

    private double _ringSizeDip;
    private double _panelHeightDip;
    private int _windowWidth;
    private int _windowHeight;
    private string _levelTitle = string.Empty;
    private IReadOnlyList<MenuItem> _levelItems = Array.Empty<MenuItem>();
    private double _dpiScale = 1.0;

    public OrbitWindow(ConfigHolder config, FolderLister lister, RecentStore recent, IconLoader icons, SearchIndex search)
    {
        _config = config;
        _lister = lister;
        _recent = recent;
        _icons = icons;
        _search = search;

        AppConfig current = config.Current;
        OrbitTheme theme = new(current.Appearance, current.Menu);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = true;
        Focusable = true;

        _ring = new RingMenuView(theme, current.Menu.ShadowMarginDip, current.Menu.HubRadiusRatio,
            current.Menu.SectorGapDeg, current.Menu.HoverExpandPx);

        _panel = new SearchPanelView(theme);

        _input = new TextBox
        {
            Width = 200,
            Height = 40,
            Opacity = 0.01,
            IsHitTestVisible = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            CaretBrush = Brushes.Transparent,
            AcceptsReturn = false,
            Focusable = true
        };
        InputMethod.SetIsInputMethodEnabled(_input, true);

        _root = new Canvas { Background = Brushes.Transparent, ClipToBounds = false };
        _root.Children.Add(_ring);
        _root.Children.Add(_panel);
        _root.Children.Add(_input);
        Content = _root;

        _input.TextChanged += OnInputChanged;

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _idleTimer.Tick += OnIdleTick;

        _drillTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(80, current.Menu.HoverDelayMs))
        };
        _drillTimer.Tick += OnDrillTimerTick;

        _searchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _searchTimer.Tick += OnSearchTimerTick;

        _pointerMonitor = new PointerMonitor(
            () => _config.Current,
            GetMenuBounds,
            () => IsMenuOpen,
            () => Dispatcher.BeginInvoke(new Action(HideMenu), DispatcherPriority.Input),
            () => Dispatcher.BeginInvoke(new Action(HideMenu), DispatcherPriority.Input));
        _pointerMonitor.Start();

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnLeftButtonDown;
        MouseLeftButtonUp += OnLeftButtonUp;
        MouseRightButtonDown += OnRightButtonDown;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        Deactivated += OnDeactivated;

        config.Changed += OnConfigChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ApplyLayout();
    }

    public bool IsMenuOpen { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        _pointerMonitor.Dispose();
        base.OnClosed(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        WindowNative.HideFromAltTabAndTaskbar(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0010)
        {
            handled = true;
            HideMenu();
        }
        return IntPtr.Zero;
    }

    public void OpenAt(int screenX, int screenY)
    {
        if (IsMenuOpen) return;

        _input.Text = string.Empty;
        ResetNavigation();

        _dpiScale = SystemProbe.GetMonitorDpiScale(screenX, screenY);
        MenuOptions menu = _config.Current.Menu;
        double radiusDip = menu.OuterRadiusDip;
        double marginDip = menu.ShadowMarginDip;
        int sizePx = (int)Math.Round((radiusDip * 2 + marginDip * 2) * _dpiScale);

        // The ring's visual radius is derived from the window size, so the whole
        // window (ring plus shadow margin) must fit inside the monitor work area;
        // clamping by the ring radius alone lets the window bleed off-screen.
        MonitorBounds work = SystemProbe.GetMonitorWorkArea(screenX, screenY);
        double halfPx = sizePx / 2.0;

        int size = sizePx;
        if (size > work.Width) size = work.Width;
        if (size > work.Height) size = work.Height;
        halfPx = size / 2.0;

        int centerX = Math.Clamp(screenX, (int)Math.Ceiling(work.Left + halfPx), (int)Math.Floor(work.Right - halfPx));
        int centerY = Math.Clamp(screenY, (int)Math.Ceiling(work.Top + halfPx), (int)Math.Floor(work.Bottom - halfPx));

        if (centerX > Math.Floor(work.Right - halfPx)) centerX = work.Left + work.Width / 2;
        if (centerY > Math.Floor(work.Bottom - halfPx)) centerY = work.Top + work.Height / 2;

        int left = (int)Math.Round(centerX - halfPx);
        int top = (int)Math.Round(centerY - halfPx);

        _ringSizeDip = size / _dpiScale;
        _panelHeightDip = 0;

        IsMenuOpen = true;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        // Position through WPF in DIPs first: WPF rescales window geometry on
        // WM_DPICHANGED, so a raw SetWindowPos gets overwritten when the window
        // lands on a monitor with a different DPI than the one it was created on.
        double dipSize = size / _dpiScale;
        Width = dipSize;
        Height = dipSize;
        Left = left / _dpiScale;
        Top = top / _dpiScale;

        if (!IsVisible) Show();

        // Enforce the exact physical rect once the window owns its monitor's DPI.
        WindowNative.PlacePhysical(_handle, left, top, size, size, show: true);
        _windowWidth = size;
        _windowHeight = size;
        ApplyLayout();

        _ring.PlayOpenAnimation();
        RestartIdleTimer();
        _pointerMonitor?.SetEnabled(true);
        WindowNative.ForceForeground(_handle);
        Activate();
        _input.Focus();
        Keyboard.Focus(_input);
        LoadIconsForCurrentLevel();

        if (Environment.GetEnvironmentVariable("DESKTOPORBIT_DIAG") == "1")
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    string log = Path.Combine(Path.GetTempPath(), "DesktopOrbit-diag.log");
                    File.AppendAllText(log, DescribeVisibility() + Environment.NewLine);
                }
                catch (Exception)
                {
                }
            }), DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// Diagnostic hook used by the --render/--capture probe: reports the effective
    /// window opacity and layered state so visual regressions can be diagnosed.
    /// </summary>
    public string DescribeVisibility()
    {
        var source = _handle == IntPtr.Zero ? null : HwndSource.FromHwnd(_handle);
        long exStyle = _handle == IntPtr.Zero ? 0 : WindowNative.GetExtendedStyle(_handle);
        string rect = "n/a";
        if (_handle != IntPtr.Zero && GetWindowRect(_handle, out RECT r))
        {
            rect = $"({r.Left},{r.Top})-({r.Right},{r.Bottom}) {r.Right - r.Left}x{r.Bottom - r.Top}";
        }

        return $"windowOpacity={Opacity} ringOpacity={_ring.Opacity} ringOpen={_ring.OpenProgress:0.000} " +
               $"themeAnimations={_ring.AnimationsEnabled} regionRadius={_lastRegionRadius} dpi={_dpiScale:0.###} " +
               $"rect={rect} ringRadius={_ring.OuterRadius:0.0} " +
               $"allowsTransparency={AllowsTransparency} hwnd={_handle} exStyle=0x{exStyle:X} " +
               $"layered={(exStyle & 0x80000) != 0} source={source?.GetType().Name ?? "null"}";
    }

    private void UpdateHitRegion()
    {
        if (_handle == IntPtr.Zero || _windowWidth <= 0) return;

        double radiusPx = (_config.Current.Menu.OuterRadiusDip + 10) * _dpiScale;
        int radius = (int)Math.Round(radiusPx);
        _lastRegionRadius = radius;

        int panelLeft = 0, panelTop = 0, panelWidth = 0, panelHeight = 0;
        if (_panel.Results.Count > 0 && _panelHeightDip > 0)
        {
            double left = Canvas.GetLeft(_panel);
            double top = Canvas.GetTop(_panel);
            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                panelLeft = (int)Math.Round(left * _dpiScale);
                panelTop = (int)Math.Round(top * _dpiScale);
                panelWidth = (int)Math.Round(_panel.Width * _dpiScale);
                panelHeight = (int)Math.Round(_panelHeightDip * _dpiScale);
            }
        }

        WindowNative.SetRingRegion(_handle, _windowWidth, _windowHeight,
            _windowWidth / 2, _windowHeight / 2, radius,
            panelLeft, panelTop, panelWidth, panelHeight);
    }

    public void HideMenu()
    {
        if (!IsMenuOpen) return;

        IsMenuOpen = false;
        _pointerMonitor?.SetEnabled(false);
        _idleTimer.Stop();
        _drillTimer.Stop();
        _searchTimer.Stop();
        _pendingDrill = -1;
        _iconCts?.Cancel();
        _searchCts?.Cancel();
        _input.Text = string.Empty;
        _pressed = false;

        // Never animate Window.Opacity here. WPF gives animations higher priority
        // than a direct property assignment, so a fade that is interrupted (or whose
        // Completed callback lands after a re-open) leaves the window stuck at a
        // partial opacity: the menu stays visible but nearly unreadable over a
        // bright wallpaper.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Hide();
    }

    private void ResetNavigation()
    {
        string title = ResolveDesktopLabel();
        IReadOnlyList<MenuItem> items = _lister.ListDesktop();
        _navigator.Reset(new NavigationLevel(title, null, items));
        ResetPaging();
        ApplyLevel(title, items);
    }

    private string ResolveDesktopLabel()
    {
        string? label = _config.Current.Sources.FirstOrDefault(source => source.Type == "desktop")?.Label;
        return string.IsNullOrWhiteSpace(label) ? "桌面" : label;
    }

    private void ApplyLevel(string title, IReadOnlyList<MenuItem> items)
    {
        _levelTitle = title;
        _levelItems = items;

        int max = Math.Max(2, _config.Current.Menu.MaxSectors);
        if (_pageOffset >= items.Count || _pageOffset < 0)
        {
            _pageOffset = 0;
            _pageHistory.Clear();
        }

        int available = items.Count - _pageOffset;
        int visibleCount = Math.Min(max, available);
        bool hasMore = available > visibleCount;
        if (hasMore) visibleCount = Math.Max(1, max - 1);

        var nodes = new List<RingNode>(visibleCount + 1);
        for (int i = 0; i < visibleCount; i++)
        {
            nodes.Add(new RingNode { Item = items[_pageOffset + i] });
        }

        if (hasMore)
        {
            int hidden = available - visibleCount;
            nodes.Add(new RingNode
            {
                Item = new MenuItem { Path = "orbit://more", Label = $"+{hidden}", Kind = ItemKind.Folder },
                IsMore = true
            });
        }

        _ring.SetContent(title, $"{items.Count} 项", nodes);
        _panel.SetResults(Array.Empty<SearchResult>(), string.Empty);
        _ring.PlayOpenAnimation();
        LoadIconsForCurrentLevel();
    }

    private void ShowNextPage()
    {
        if (_levelItems.Count == 0) return;

        int max = Math.Max(2, _config.Current.Menu.MaxSectors) - 1;
        _pageHistory.Push(_pageOffset);

        _pageOffset += Math.Max(1, max);
        if (_pageOffset >= _levelItems.Count) _pageOffset = 0;

        ApplyLevel(_levelTitle, _levelItems);
    }

    /// <summary>
    /// Steps back one page inside the current folder. Returns false when the first
    /// page is already showing, so the caller can fall through to leaving the folder.
    /// </summary>
    private bool GoBackPage()
    {
        if (_pageHistory.Count == 0)
        {
            if (_pageOffset == 0) return false;

            // Offset was set outside the page stack (e.g. by a layout change); fall
            // back to the first page rather than leaving the folder.
            _pageOffset = 0;
            ApplyLevel(_levelTitle, _levelItems);
            return true;
        }

        _pageOffset = _pageHistory.Pop();
        ApplyLevel(_levelTitle, _levelItems);
        return true;
    }

    private void ResetPaging()
    {
        _pageOffset = 0;
        _pageHistory.Clear();
    }

    private void LoadIconsForCurrentLevel()
    {
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        CancellationToken token = _iconCts.Token;

        AppConfig config = _config.Current;
        int pixelSize = (int)Math.Round(config.Appearance.IconSizeDip * _dpiScale * 1.25);
        bool thumbnails = config.General.Thumbnails;

        IReadOnlyList<RingNode> nodes = _ring.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            RingNode node = nodes[index];
            bool isFolder = node.Item.IsFolder;
            bool wantThumbnail = thumbnails && !isFolder && config.General.Thumbnails;

            ImageSource? cached = _icons.TryGetCached(node.Item.Path, isFolder, pixelSize, wantThumbnail);
            if (cached is not null)
            {
                _ring.SetIcon(index, cached);
                continue;
            }

            _ = LoadIconAsync(index, node.Item.Path, isFolder, pixelSize, wantThumbnail, token);
        }
    }

    private async Task LoadIconAsync(int index, string path, bool isFolder, int pixelSize, bool thumbnail, CancellationToken token)
    {
        try
        {
            ImageSource? image = await _icons.LoadAsync(path, isFolder, pixelSize, thumbnail, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || image is null) return;
            _ring.SetIcon(index, image);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Search results replace the ring in the centre of the same square window.
    /// The window never changes size: growing it while a layered window is visible
    /// fights WPF's DPI handling and leaves the panel clipped by the window region.
    /// </summary>
    private void ApplyLayout()
    {
        double ringSize = _ringSizeDip > 0 ? _ringSizeDip : ActualWidth;
        if (ringSize <= 0) return;

        _ring.Width = ringSize;
        _ring.Height = ringSize;
        Canvas.SetLeft(_ring, 0);
        Canvas.SetTop(_ring, 0);

        bool searching = _panel.Results.Count > 0;

        double panelWidth = Math.Max(280, Math.Min(460, ringSize - 40));
        _panel.Width = panelWidth;
        _panel.InvalidateMeasure();
        _panel.Measure(new Size(panelWidth, double.PositiveInfinity));

        double panelHeight = searching ? _panel.DesiredSize.Height : 0;
        if (double.IsNaN(panelHeight) || panelHeight < 0) panelHeight = 0;
        _panelHeightDip = panelHeight;

        Canvas.SetLeft(_panel, (ringSize - panelWidth) / 2);
        Canvas.SetTop(_panel, Math.Max(8, (ringSize - panelHeight) / 2));

        // Keep the ring readable as orientation, but clearly behind the result card.
        _ring.Opacity = searching ? 0.5 : 1;

        Canvas.SetLeft(_input, ringSize / 2 - 100);
        Canvas.SetTop(_input, ringSize / 2 - 20);

        if (IsMenuOpen) UpdateHitRegion();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_ringSizeDip <= 0) _ringSizeDip = ActualWidth;
        ApplyLayout();
    }

    private void OnConfigChanged(AppConfig config)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var theme = new OrbitTheme(config.Appearance, config.Menu);
            _ring.ApplyTheme(theme, config.Menu.HubRadiusRatio, config.Menu.SectorGapDeg, config.Menu.HoverExpandPx);
            _panel.ApplyTheme(theme);
            _idleTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1500, config.Menu.IdleTimeoutMs));
            _drillTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(80, config.Menu.HoverDelayMs));
            ApplyLayout();
        }), DispatcherPriority.Background);
    }

    private void RestartIdleTimer()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    /// <summary>
    /// Idle guard: closes the menu when it has been open with the pointer inside and
    /// untouched for longer than the configured timeout. Set IdleTimeoutMs to 0 to
    /// disable, which is the default because a menu that vanishes on its own is
    /// surprising during first use.
    /// </summary>
    private void OnIdleTick(object? sender, EventArgs e)
    {
        int timeout = _config.Current.Menu.IdleTimeoutMs;
        if (timeout <= 0)
        {
            _idleTimer.Stop();
            return;
        }

        if (!IsPointerInside()) return;

        if (_lastInteractionTicks == 0) _lastInteractionTicks = Environment.TickCount64;
        if (Environment.TickCount64 - _lastInteractionTicks >= timeout)
        {
            _idleTimer.Stop();
            HideMenu();
        }
    }

    private (int Left, int Top, int Right, int Bottom)? GetMenuBounds()
    {
        if (_handle == IntPtr.Zero || !IsMenuOpen) return null;
        if (!GetWindowRect(_handle, out RECT rect)) return null;
        return (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private bool IsPointerInside()
    {
        if (_handle == IntPtr.Zero) return false;
        var (x, y) = SystemProbe.GetCursorPosition();
        if (!GetWindowRect(_handle, out RECT rect)) return false;
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        RestartIdleTimer();
        _lastInteractionTicks = Environment.TickCount64;
        Point point = e.GetPosition(this);

        if (_pressed)
        {
            if (IsDragThresholdPassed(point)) BeginDragFromPress();
            return;
        }

        if (!string.IsNullOrEmpty(_input.Text))
        {
            Point inPanel = TranslateToPanel(point);
            if (inPanel.Y >= 0 && inPanel.Y <= _panel.ActualHeight)
            {
                int row = _panel.HitTestRow(inPanel);
                if (row >= 0 && row != _panel.SelectedIndex)
                {
                    _panel.MoveSelection(row - _panel.SelectedIndex);
                }
            }
            return;
        }

        RingHit hit = _ring.HitTestPoint(point);
        if (hit.IsSector && hit.Index < _ring.Nodes.Count)
        {
            if (_ring.HoverIndex != hit.Index)
            {
                _ring.SetHover(hit.Index);
                _ring.SetSelected(hit.Index);
                _drillTimer.Stop();
                if (_config.Current.Menu.Drill.OnHover && _ring.Nodes[hit.Index].Item.IsFolder)
                {
                    _pendingDrill = hit.Index;
                    _drillTimer.Start();
                }
                else
                {
                    _pendingDrill = -1;
                }
            }
            return;
        }

        _ring.SetHover(-1);
        _drillTimer.Stop();
        _pendingDrill = -1;
    }

    private void OnDrillTimerTick(object? sender, EventArgs e)
    {
        _drillTimer.Stop();
        int index = _pendingDrill;
        _pendingDrill = -1;
        if (index < 0) return;

        RingHit hit = _ring.HitTestPoint(Mouse.GetPosition(this));
        if (!hit.IsSector || hit.Index != index) return;
        DrillInto(index);
    }

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(this);

        if (!string.IsNullOrEmpty(_input.Text))
        {
            int row = _panel.HitTestRow(TranslateToPanel(point));
            if (row >= 0)
            {
                _panel.MoveSelection(row - _panel.SelectedIndex);
                InvokeSearchSelection();
                e.Handled = true;
                return;
            }
        }

        RingHit hit = _ring.HitTestPoint(point);
        if (hit.IsHub)
        {
            _input.Focus();
            Keyboard.Focus(_input);
            return;
        }

        if (!hit.IsSector)
        {
            // Mode A: a click inside the window but outside the ring counts as
            // "outside" and closes immediately. Clicks beyond the window are caught
            // by the pointer monitor.
            if (_config.Current.Menu.Dismiss.CloseOnClickOutside) HideMenu();
            return;
        }

        _pressed = true;
        _pressOrigin = point;
        _ring.SetSelected(hit.Index);
        _drillTimer.Stop();
        _pendingDrill = -1;
        CaptureMouse();
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        ReleaseMouseCapture();

        Point point = e.GetPosition(this);
        RingHit hit = _ring.HitTestPoint(point);
        if (!hit.IsSector || hit.Index >= _ring.Nodes.Count) return;

        RingNode node = _ring.Nodes[hit.Index];
        if (node.IsEmptyHint) return;

        if (node.Item.IsFolder && (_config.Current.Menu.Drill.OnClick || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            DrillInto(hit.Index);
            return;
        }

        ActivateItem(node.Item);
    }

    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (!string.IsNullOrEmpty(_input.Text))
        {
            _input.Text = string.Empty;
            return;
        }

        if (!_config.Current.Menu.Drill.RightClickGoesBack) return;

        if (!GoBackOneStep()) HideMenu();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!string.IsNullOrEmpty(_input.Text)) _panel.MoveSelection(e.Delta > 0 ? -1 : 1);
        else _ring.SetSelected(_ring.SelectedIndex + (e.Delta > 0 ? -1 : 1));
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool searching = !string.IsNullOrEmpty(_input.Text);

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (searching)
                {
                    _input.Text = string.Empty;
                }
                else if (!_config.Current.Menu.Drill.EscapeGoesBack)
                {
                    if (_config.Current.Menu.Dismiss.CloseOnEscape) HideMenu();
                }
                else if (!GoBackOneStep())
                {
                    if (_config.Current.Menu.Dismiss.CloseOnEscape) HideMenu();
                }
                return;

            case Key.Back:
                if (!searching && _config.Current.Menu.Drill.EscapeGoesBack)
                {
                    e.Handled = true;
                    GoBackOneStep();
                }
                return;

            case Key.Left when !searching:
                e.Handled = true;
                _ring.SetSelected(_ring.SelectedIndex - 1);
                _ring.SetHover(_ring.SelectedIndex);
                return;

            case Key.Right when !searching:
                e.Handled = true;
                _ring.SetSelected(_ring.SelectedIndex + 1);
                _ring.SetHover(_ring.SelectedIndex);
                return;

            case Key.Up when searching:
                e.Handled = true;
                _panel.MoveSelection(-1);
                return;

            case Key.Down when searching:
                e.Handled = true;
                _panel.MoveSelection(1);
                return;

            case Key.Enter:
                e.Handled = true;
                if (searching) InvokeSearchSelection();
                else InvokeRingSelection();
                return;

            case Key.Tab when searching:
                e.Handled = true;
                DragSearchSelection();
                return;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!IsMenuOpen || _suppressFocusLoss || _pressed) return;
        if (!_config.Current.Menu.Dismiss.CloseOnFocusLoss) return;

        // If every other dismissal path is switched off, keep focus loss as a safety
        // net so the menu can never become permanently stuck on screen.
        DismissOptions dismiss = _config.Current.Menu.Dismiss;
        bool anyOtherPath = dismiss.CloseOnClickOutside || dismiss.CloseOnPointerLeave || dismiss.CloseOnEscape;
        if (!anyOtherPath || dismiss.CloseOnFocusLoss) HideMenu();
    }

    private void DrillInto(int index)
    {
        if (index < 0 || index >= _ring.Nodes.Count) return;

        RingNode node = _ring.Nodes[index];
        if (node.IsMore)
        {
            ShowNextPage();
            return;
        }

        if (!node.Item.IsFolder) return;

        string path = node.Item.Path;
        IReadOnlyList<MenuItem> items = _lister.ListFolder(path, _config.Current.Menu.MaxItemsPerFolder);

        _drillTimer.Stop();
        _pendingDrill = -1;

        if (items.Count == 0)
        {
            _levelTitle = node.Item.Label;
            _levelItems = items;
            ResetPaging();
            _ring.SetContent(node.Item.Label, "空文件夹", new[]
            {
                new RingNode
                {
                    Item = new MenuItem { Path = path, Label = "（空）", Kind = ItemKind.Folder },
                    IsEmptyHint = true
                }
            });
            return;
        }

        _navigator.Push(new NavigationLevel(node.Item.Label, path, items));
        ResetPaging();
        ApplyLevel(node.Item.Label, items);
    }

    private void GoBack()
    {
        if (!_navigator.Back()) return;

        NavigationLevel level = _navigator.Current;
        _drillTimer.Stop();
        _pendingDrill = -1;
        ResetPaging();
        ApplyLevel(level.Title, level.Items);
    }

    /// <summary>
    /// Back semantics used by right-click and Escape: leave the current page first,
    /// then leave the folder. Returning to the parent folder straight from page 3
    /// loses the user's place and reads as a jump.
    /// </summary>
    private bool GoBackOneStep()
    {
        if (GoBackPage()) return true;
        if (!_navigator.CanGoBack) return false;
        GoBack();
        return true;
    }

    private void ActivateItem(MenuItem item)
    {
        _recent.Record(item.Path);
        if (ShellActions.Open(item.Path)) HideMenu();
    }

    private void InvokeRingSelection()
    {
        int index = _ring.SelectedIndex;
        if (index < 0 || index >= _ring.Nodes.Count) return;

        RingNode node = _ring.Nodes[index];
        if (node.IsEmptyHint) return;

        if (node.Item.IsFolder)
        {
            DrillInto(index);
            return;
        }

        ActivateItem(node.Item);
    }

    private void InvokeSearchSelection()
    {
        IReadOnlyList<SearchResult> results = _panel.Results;
        int index = _panel.SelectedIndex;
        if (index < 0 || index >= results.Count) return;

        SearchResult result = results[index];
        _recent.Record(result.Path);

        if (Directory.Exists(result.Path))
        {
            _input.Text = string.Empty;
            IReadOnlyList<MenuItem> items = _lister.ListFolder(result.Path, _config.Current.Menu.MaxItemsPerFolder);
            if (items.Count == 0) return;

            _navigator.Push(new NavigationLevel(result.Label, result.Path, items));
            ResetPaging();
            ApplyLevel(result.Label, items);
            return;
        }

        if (ShellActions.Open(result.Path)) HideMenu();
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        _ring.SetQuery(_input.Text, true);

        if (string.IsNullOrEmpty(_input.Text))
        {
            _searchTimer.Stop();
            _panel.SetResults(Array.Empty<SearchResult>(), string.Empty);
            Dispatcher.BeginInvoke(new Action(RefreshPanelLayout), DispatcherPriority.Loaded);
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RefreshPanelLayout()
    {
        _panel.UpdateLayout();
        ApplyLayout();
    }

    private void OnSearchTimerTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();

        string query = _input.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            _panel.SetResults(Array.Empty<SearchResult>(), string.Empty);
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        int max = _config.Current.Search.MaxResults;
        SearchIndex index = _search;

        Task.Run(() => index.Query(query, max), token).ContinueWith(task =>
        {
            if (token.IsCancellationRequested || task.IsCanceled || task.IsFaulted) return;
            _panel.SetResults(task.Result, query);
            RefreshPanelLayout();

            if (Environment.GetEnvironmentVariable("DESKTOPORBIT_DIAG") == "1")
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "DesktopOrbit-diag.log"),
                        $"[search] {DescribeVisibility()} panelOpacity={_panel.Opacity} query={query}{Environment.NewLine}");
                }
                catch (Exception)
                {
                }
            }
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool IsDragThresholdPassed(Point current)
    {
        double dx = current.X - _pressOrigin.X;
        double dy = current.Y - _pressOrigin.Y;
        return dx * dx + dy * dy >= 25;
    }

    private void BeginDragFromPress()
    {
        RingHit hit = _ring.HitTestPoint(_pressOrigin);
        _pressed = false;
        ReleaseMouseCapture();

        if (!hit.IsSector || hit.Index >= _ring.Nodes.Count) return;
        if (!_config.Current.Actions.DragEnabled) return;

        RingNode node = _ring.Nodes[hit.Index];
        if (node.IsEmptyHint) return;

        DragItem(node.Item);
    }

    private void DragItem(MenuItem item)
    {
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) return;

        _suppressFocusLoss = true;
        if (_handle != IntPtr.Zero)
        {
            WindowNative.SetNoActivate(_handle, false);
            Activate();
        }

        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 1, 0, 0, 0 }));
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            _recent.Record(item.Path);
        }
        catch (Exception)
        {
        }
        finally
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _suppressFocusLoss = false;
                RestartIdleTimer();
                HideMenu();
            }), DispatcherPriority.Background);
        }
    }

    private void DragSearchSelection()
    {
        IReadOnlyList<SearchResult> results = _panel.Results;
        int index = _panel.SelectedIndex;
        if (index < 0 || index >= results.Count) return;

        SearchResult result = results[index];
        DragItem(new MenuItem { Path = result.Path, Label = result.Label, Kind = result.Kind });
    }

    private Point TranslateToPanel(Point inWindow)
    {
        double left = Canvas.GetLeft(_panel);
        double top = Canvas.GetTop(_panel);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        return new Point(inWindow.X - left, inWindow.Y - top);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
