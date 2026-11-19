using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DesktopOrbit.App.Input;
using DesktopOrbit.App.Platform;
using DesktopOrbit.App.Settings;
using DesktopOrbit.App.UI;
using DesktopOrbit.App.Windows;
using DesktopOrbit.Core.Config;
using DesktopOrbit.Shell.Desktop;
using DesktopOrbit.Shell.Platform;
using DesktopOrbit.Shell.Search;
using DesktopOrbit.Shell.Shell;

namespace DesktopOrbit.App;

public partial class App : Application
{
    private const int TrayOpenMenu = 1001;
    private const int TraySettings = 1006;
    private const int TrayToggleAutostart = 1002;
    private const int TrayOpenConfig = 1003;
    private const int TrayReload = 1004;
    private const int TrayToggleIcons = 1005;
    private const int TrayExit = 1009;

    private const int WmCommand = 0x0111;
    private const int ToggleDesktopIconsCommand = 0x7402;

    private InstanceSignal? _instance;
    private ConfigHolder _holder = null!;
    private MessageWindow _messages = null!;
    private OrbitWindow _orbit = null!;
    private SettingsWindow? _settings;
    private LongPressDetector _longPress = null!;
    private DesktopLocator _locator = null!;
    private RecentStore _recent = null!;
    private SearchIndex _search = null!;
    private FileSystemWatcher? _configWatcher;
    private DateTime _lastConfigReload = DateTime.MinValue;
    private bool _openMenuOnStart;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0].Equals("--render", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                RenderProbe.Run(e.Args[1]);
            }
            catch (Exception ex)
            {
                File.WriteAllText(e.Args[1] + ".error.txt", ex.ToString());
            }

            Shutdown();
            return;
        }

        _instance = new InstanceSignal();
        if (!_instance.IsFirst)
        {
            InstanceSignal.TrySignalExisting();
            Shutdown();
            return;
        }

        _instance.ShowRequested += () => Dispatcher.BeginInvoke(new Action(ToggleMenu), DispatcherPriority.Input);
        _instance.StartListening();

        _openMenuOnStart = e.Args.Any(arg => arg.Equals("--menu", StringComparison.OrdinalIgnoreCase));
        bool openSettingsOnStart = e.Args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppConfig config = ConfigStore.LoadOrCreate(ConfigStore.DefaultPath, out _);
        _holder = new ConfigHolder(config);

        _locator = new DesktopLocator();
        var hitTester = new DesktopHitTester(_locator);
        var iconCache = new ShellIconCache(ShellIconCache.DefaultDirectory);
        var icons = new IconLoader(iconCache);
        var lister = new FolderLister(config.General);
        _recent = new RecentStore(RecentStore.DefaultPath);
        _search = new SearchIndex();

        _orbit = new OrbitWindow(_holder, lister, _recent, icons, _search);

        _messages = new MessageWindow();
        TrayIcon tray = _messages.AttachTray();
        tray.SetTooltip("Desktop Orbit · 长按桌面空白处唤出");

        _messages.HotkeyPressed += ToggleMenu;
        _messages.TrayActivated += ToggleMenu;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.TrayCommand += OnTrayCommand;

        if (config.Trigger.HotkeyEnabled && !_messages.TryRegisterHotkey(config.Trigger.Hotkey))
        {
            tray.SetTooltip($"Desktop Orbit · 热键 {config.Trigger.Hotkey} 被占用");
        }

        _longPress = new LongPressDetector(_holder, _locator, hitTester, () => _orbit.IsMenuOpen, OnLongPress);
        _longPress.Start();

        BuildSearchIndex(config);
        WatchConfig(ConfigStore.DefaultPath);
        EnsureAutostart(config.General.StartWithWindows);

        if (_openMenuOnStart)
        {
            Dispatcher.BeginInvoke(new Action(ToggleMenu), DispatcherPriority.ApplicationIdle);
        }

        if (openSettingsOnStart)
        {
            Dispatcher.BeginInvoke(new Action(ShowSettings), DispatcherPriority.ApplicationIdle);
        }
    }

    private void ToggleMenu()
    {
        if (_orbit.IsMenuOpen)
        {
            _orbit.HideMenu();
            _longPress.SuppressFor(TimeSpan.FromMilliseconds(400));
            return;
        }

        (int x, int y) = SystemProbe.GetCursorPosition();
        _orbit.OpenAt(x, y);
        _longPress.SuppressFor(TimeSpan.FromMilliseconds(400));
    }

    private void OnLongPress(int x, int y)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_orbit.IsMenuOpen) return;
            _orbit.OpenAt(x, y);
            _longPress.SuppressFor(TimeSpan.FromMilliseconds(500));
        }), DispatcherPriority.Input);
    }

    private void ShowTrayMenu()
    {
        var items = new[]
        {
            new TrayMenuItem(TrayOpenMenu, "打开菜单"),
            new TrayMenuItem(TraySettings, "设置…"),
            new TrayMenuItem(TrayReload, "重新载入配置"),
            new TrayMenuItem(TrayOpenConfig, "编辑配置文件"),
            new TrayMenuItem(TrayToggleIcons, "切换桌面图标显示"),
            new TrayMenuItem(TrayToggleAutostart, "开机启动", Checked: IsAutostartEnabled(), SeparatorBefore: true),
            new TrayMenuItem(TrayExit, "退出", SeparatorBefore: true)
        };

        _messages.Tray?.ShowContextMenu(items);
    }

    private void ShowSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_holder);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private void OnTrayCommand(int id)
    {
        switch (id)
        {
            case TrayOpenMenu:
                ToggleMenu();
                break;

            case TraySettings:
                ShowSettings();
                break;

            case TrayReload:
                ReloadConfig();
                break;

            case TrayOpenConfig:
                ShellActions.Open(ConfigStore.DefaultPath);
                break;

            case TrayToggleAutostart:
            {
                bool enabled = !IsAutostartEnabled();
                EnsureAutostart(enabled);
                _holder.Current.General.StartWithWindows = enabled;
                SaveConfig();
                break;
            }

            case TrayToggleIcons:
                ToggleDesktopIcons();
                break;

            case TrayExit:
                Shutdown();
                break;
        }
    }

    private void BuildSearchIndex(AppConfig config)
    {
        var roots = config.Sources
            .Where(source => source.Enabled && source.Type != "recent")
            .Select(source => ResolveSourcePath(source))
            .Where(path => !string.IsNullOrEmpty(path))
            .Concat(FolderLister.DesktopDirectories())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SearchOptions options = config.Search;
        SearchIndex index = _search;
        _ = Task.Run(() => index.Build(roots, options));
    }

    private static string ResolveSourcePath(SourceConfig source)
    {
        return source.Type switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "recent" => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
            _ => source.ExpandedPath
        };
    }

    private void WatchConfig(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) return;

            _configWatcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _configWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(ReloadConfig), DispatcherPriority.Background);
            _configWatcher.Created += (_, _) => Dispatcher.BeginInvoke(new Action(ReloadConfig), DispatcherPriority.Background);
        }
        catch (Exception)
        {
        }
    }

    private void ReloadConfig()
    {
        if ((DateTime.UtcNow - _lastConfigReload).TotalMilliseconds < 300) return;
        _lastConfigReload = DateTime.UtcNow;

        AppConfig config = ConfigStore.LoadOrCreate(ConfigStore.DefaultPath, out _);
        _holder.Update(config);

        if (config.Trigger.HotkeyEnabled)
        {
            _messages.TryRegisterHotkey(config.Trigger.Hotkey);
        }
        else
        {
            _messages.UnregisterHotkey();
        }

        BuildSearchIndex(config);
    }

    private void SaveConfig()
    {
        try
        {
            ConfigStore.Save(ConfigStore.DefaultPath, _holder.Current);
        }
        catch (Exception)
        {
        }
    }

    private static string AutostartLinkPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Desktop Orbit.lnk");

    private static bool IsAutostartEnabled() => File.Exists(AutostartLinkPath);

    private void EnsureAutostart(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(AutostartLinkPath)) File.Delete(AutostartLinkPath);
                return;
            }

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            if (IsAutostartEnabled()) return;

            ShortcutWriter.Create(AutostartLinkPath, exe, Path.GetDirectoryName(exe), "--silent");
        }
        catch (Exception)
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static void ToggleDesktopIcons()
    {
        try
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero)
            {
                IntPtr worker = IntPtr.Zero;
                while (true)
                {
                    worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                    if (worker == IntPtr.Zero) break;
                    defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero) break;
                }
            }

            if (defView != IntPtr.Zero)
            {
                SendMessage(defView, WmCommand, (IntPtr)ToggleDesktopIconsCommand, IntPtr.Zero);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            string logDirectory = Path.Combine(ConfigStore.DefaultDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch (Exception)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _configWatcher?.Dispose();
            _longPress?.Dispose();
            _recent?.Flush();
            _messages?.Dispose();
            _instance?.Dispose();
        }
        catch (Exception)
        {
        }

        base.OnExit(e);
    }
}
