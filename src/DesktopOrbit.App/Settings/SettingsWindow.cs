using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DesktopOrbit.App.UI;
using DesktopOrbit.Core.Config;
using DesktopOrbit.Core.Models;
using MenuItem = DesktopOrbit.Core.Models.MenuItem;
using DesktopOrbit.Shell.Shell;

namespace DesktopOrbit.App.Settings;

/// <summary>
/// Settings window: a group list on the left, the live controls in the middle and a
/// rendered preview of the ring on the right. Every control writes straight into the
/// shared AppConfig instance, and the preview plus the running menu react to the
/// same Changed event, so what is shown is what the next invocation looks like.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static readonly Color BgColor = Color.FromRgb(0x0E, 0x11, 0x17);
    private static readonly Color SurfaceColor = Color.FromRgb(0x16, 0x1A, 0x23);
    private static readonly Color SurfaceColorAlt = Color.FromRgb(0x1C, 0x21, 0x2C);
    private static readonly Color Line = Color.FromRgb(0x2A, 0x31, 0x40);
    private static readonly Color Text = Color.FromRgb(0xF0, 0xF3, 0xF9);
    private static readonly Color Muted = Color.FromRgb(0x93, 0x9E, 0xB3);
    private static readonly Color Accent = Color.FromRgb(0x7C, 0x5C, 0xFF);

    private readonly ConfigHolder _holder;
    private readonly List<SettingGroup> _groups;
    private readonly StackPanel _groupList;
    private readonly StackPanel _fieldHost;
    private readonly TextBlock _groupTitle;
    private readonly TextBlock _groupDescription;
    private readonly ScrollViewer _fieldScroll;
    private readonly Border _previewHost;
    private readonly TextBlock _status;
    private readonly FileSystemWatcher? _configWatcher;

    private RingMenuView? _previewRing;
    private int _activeGroup;
    private DateTime _lastExternalReload = DateTime.MinValue;
    private DateTime _lastSelfSave = DateTime.MinValue;

    public SettingsWindow(ConfigHolder holder)
    {
        _holder = holder;
        _groups = SettingsCatalog.Build(holder.Current);

        Title = "Desktop Orbit · 设置";
        Width = 1180;
        Height = 760;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(BgColor);
        Foreground = new SolidColorBrush(Text);
        FontFamily = new FontFamily("Microsoft YaHei UI");
        SnapsToDevicePixels = true;

        var root = new Grid { Background = new SolidColorBrush(BgColor) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });

        _groupList = new StackPanel { Margin = new Thickness(10, 14, 10, 14) };
        var navScroll = new ScrollViewer
        {
            Content = _groupList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var nav = new Border
        {
            Background = new SolidColorBrush(SurfaceColor),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navScroll
        };
        Grid.SetColumn(nav, 0);
        root.Children.Add(nav);

        _groupTitle = new TextBlock
        {
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Text)
        };
        _groupDescription = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Muted),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _fieldHost = new StackPanel { Margin = new Thickness(0, 6, 0, 18) };
        _fieldScroll = new ScrollViewer
        {
            Content = _fieldHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var header = new StackPanel { Margin = new Thickness(24, 22, 24, 0) };
        header.Children.Add(_groupTitle);
        header.Children.Add(_groupDescription);

        var centerContent = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        centerContent.Children.Add(header);
        centerContent.Children.Add(_fieldScroll);

        var center = new Border
        {
            Padding = new Thickness(0, 0, 0, 0),
            Child = centerContent
        };
        var centerPad = new Grid();
        centerPad.Children.Add(center);
        Grid.SetColumn(centerPad, 1);
        root.Children.Add(centerPad);

        _previewHost = new Border
        {
            Background = new SolidColorBrush(SurfaceColor),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(16)
        };
        var previewPanel = new DockPanel();
        var previewHeader = new TextBlock
        {
            Text = "实时预览",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Muted),
            Margin = new Thickness(4, 6, 0, 10)
        };
        DockPanel.SetDock(previewHeader, Dock.Top);
        previewPanel.Children.Add(previewHeader);

        _status = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Muted),
            Margin = new Thickness(4, 10, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(_status, Dock.Bottom);
        previewPanel.Children.Add(_status);

        var previewStage = new Grid();
        _previewHost.Child = previewPanel;
        previewPanel.Children.Add(previewStage);
        var previewWrap = new Border { Child = _previewHost };
        Grid.SetColumn(previewWrap, 2);
        root.Children.Add(previewWrap);

        _previewStage = previewStage;
        Content = root;

        _previewStage.SizeChanged += (_, _) => BuildPreview();

        BuildGroupList();
        SelectGroup(0);
        UpdateStatus();

        // A hand-edited config.json should refresh the panel, but ignore our own writes.
        try
        {
            string directory = Path.GetDirectoryName(ConfigStore.DefaultPath) ?? string.Empty;
            _configWatcher = new FileSystemWatcher(directory, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _configWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(ReloadFromDisk));
        }
        catch (Exception)
        {
            _configWatcher = null;
        }
    }

    private readonly Grid _previewStage;



    private void BuildGroupList()
    {
        _groupList.Children.Clear();

        foreach (SettingGroup group in _groups)
        {
            var button = new Button
            {
                Content = $"{group.Glyph}   {group.Title}",
                Height = 38,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 0, 10, 0),
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 13,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Text),
                Background = Brushes.Transparent,
                Template = FlatButtonTemplate()
            };

            int index = _groups.IndexOf(group);
            button.Click += (_, _) => SelectGroup(index);
            _groupList.Children.Add(button);
        }

        var footer = new StackPanel { Margin = new Thickness(2, 18, 2, 0) };
        footer.Children.Add(MakeSecondaryButton("打开配置文件", () => ShellActions.Open(ConfigStore.DefaultPath)));
        footer.Children.Add(MakeSecondaryButton("恢复默认值", RestoreDefaults));
        _groupList.Children.Add(footer);
    }

    private void SelectGroup(int index)
    {
        if (index < 0 || index >= _groups.Count) return;
        _activeGroup = index;

        for (int i = 0; i < _groupList.Children.Count; i++)
        {
            if (_groupList.Children[i] is not Button button) continue;

            bool active = i == index;
            button.Background = active
                ? new SolidColorBrush(Color.FromArgb(58, Accent.R, Accent.G, Accent.B))
                : Brushes.Transparent;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        SettingGroup group = _groups[index];
        _groupTitle.Text = $"{group.Glyph}   {group.Title}";
        _groupDescription.Text = group.Description;

        _fieldHost.Children.Clear();
        foreach (SettingItem item in group.Items)
        {
            _fieldHost.Children.Add(BuildField(item));
        }
    }

    private FrameworkElement BuildField(SettingItem item)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(SurfaceColorAlt),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 12, 11),
            Margin = new Thickness(24, 0, 24, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = item.Label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Text)
        });

        if (!string.IsNullOrEmpty(item.Hint))
        {
            labels.Children.Add(new TextBlock
            {
                Text = item.Hint,
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Muted),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 330
            });
        }

        grid.Children.Add(labels);
        Grid.SetColumn(labels, 0);

        FrameworkElement control = item.Kind switch
        {
            SettingKind.Toggle => BuildToggle(item),
            SettingKind.Number => BuildNumber(item),
            SettingKind.Color => BuildColor(item),
            _ => BuildText(item)
        };

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        container.Child = grid;
        return container;
    }

    private FrameworkElement BuildToggle(SettingItem item)
    {
        bool value = item.Get() as bool? ?? false;

        var track = new Border
        {
            Width = 46,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(value ? Accent : Color.FromRgb(0x33, 0x3A, 0x49)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        var knob = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(3)
        };

        track.Child = knob;

        track.MouseLeftButtonUp += (_, _) =>
        {
            bool next = !(item.Get() as bool? ?? false);
            item.Set(next);
            track.Background = new SolidColorBrush(next ? Accent : Color.FromRgb(0x33, 0x3A, 0x49));
            knob.HorizontalAlignment = next ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Commit();
        };

        return track;
    }

    private FrameworkElement BuildNumber(SettingItem item)
    {
        double value = item.Get() is double number ? number : item.Min;

        var slider = new Slider
        {
            Width = 150,
            Minimum = item.Min,
            Maximum = item.Max,
            SmallChange = item.Step,
            LargeChange = item.Step * 5,
            TickFrequency = item.Step,
            IsSnapToTickEnabled = item.Step > 0,
            Value = Math.Clamp(value, item.Min, item.Max),
            VerticalAlignment = VerticalAlignment.Center
        };

        var readout = new TextBlock
        {
            Text = value.ToString(item.Format, CultureInfo.InvariantCulture),
            Width = 52,
            TextAlignment = TextAlignment.Right,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Text)
        };

        slider.ValueChanged += (_, e) =>
        {
            item.Set(e.NewValue);
            readout.Text = e.NewValue.ToString(item.Format, CultureInfo.InvariantCulture);
            Commit();
        };

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        wrap.Children.Add(slider);
        wrap.Children.Add(readout);
        return wrap;
    }

    private FrameworkElement BuildText(SettingItem item)
    {
        var box = new TextBox
        {
            Width = 190,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Text = item.Get()?.ToString() ?? string.Empty,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x28, 0x34)),
            Foreground = new SolidColorBrush(Text),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Text)
        };

        box.LostFocus += (_, _) =>
        {
            item.Set(box.Text.Trim());
            Commit();
        };

        return box;
    }

    private FrameworkElement BuildColor(SettingItem item)
    {
        string hex = item.Get()?.ToString() ?? "#FFFFFF";
        Color current = ParseColor(hex);

        var swatch = new Border
        {
            Width = 34,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(current),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var box = new TextBox
        {
            Width = 92,
            Height = 26,
            Text = hex,
            FontSize = 11,
            Padding = new Thickness(6, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x28, 0x34)),
            Foreground = new SolidColorBrush(Text),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1)
        };

        box.TextChanged += (_, _) =>
        {
            string text = box.Text.Trim();
            if (text.Length is not (4 or 7) || !text.StartsWith('#')) return;
            item.Set(text);
            swatch.Background = new SolidColorBrush(ParseColor(text));
            Commit();
        };

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        wrap.Children.Add(swatch);
        wrap.Children.Add(new Border { Width = 8 });
        wrap.Children.Add(box);
        return wrap;
    }

    private void Commit()
    {
        _holder.Update(_holder.Current);
        SaveConfig();
        BuildPreview();
        UpdateStatus();
    }

    private void SaveConfig()
    {
        try
        {
            _lastSelfSave = DateTime.UtcNow;
            ConfigStore.Save(ConfigStore.DefaultPath, _holder.Current);
        }
        catch (Exception ex)
        {
            _status.Text = "保存失败：" + ex.Message;
        }
    }

    private void RestoreDefaults()
    {
        AppConfig defaults = AppConfig.CreateDefault();

        AppConfig current = _holder.Current;
        current.General = defaults.General;
        current.Trigger = defaults.Trigger;
        current.Menu = defaults.Menu;
        current.Appearance = defaults.Appearance;
        current.Search = defaults.Search;
        current.Actions = defaults.Actions;

        _holder.Update(current);
        SaveConfig();
        SelectGroup(_activeGroup);
        BuildPreview();
        _status.Text = "已恢复外观与行为默认值（数据源未改动）";
    }

    private void BuildPreview()
    {
        if (_previewStage.ActualWidth < 80 || _previewStage.ActualHeight < 80) return;

        AppConfig config = _holder.Current;
        int maxSectors = Math.Clamp(config.Menu.MaxSectors, 2, 12);
        var items = new List<MenuItem>();
        string[] labels = { "办公", "学习", "游戏", "软件", "下载", "文档", "图片", "视频", "音乐", "项目", "归档", "临时" };

        for (int i = 0; i < maxSectors; i++)
        {
            items.Add(new MenuItem
            {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), labels[i % labels.Length]),
                Label = labels[i % labels.Length],
                Kind = ItemKind.Folder
            });
        }

        var theme = new OrbitTheme(config.Appearance, config.Menu);
        double available = Math.Min(_previewStage.ActualWidth, _previewStage.ActualHeight) - 32;
        double diameterDip = config.Menu.OuterRadiusDip * 2 + config.Menu.ShadowMarginDip * 2;
        double scale = Math.Min(1.0, available / diameterDip);
        double size = diameterDip * scale;

        _previewStage.Children.Clear();

        var ring = new RingMenuView(theme, config.Menu.ShadowMarginDip, config.Menu.HubRadiusRatio,
            config.Menu.SectorGapDeg, config.Menu.HoverExpandPx)
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var nodes = new List<RingNode>();
        foreach (MenuItem item in items)
        {
            nodes.Add(new RingNode { Item = item, Icon = MakeGlyphIcon(item.Label, theme, config.Appearance.IconSizeDip * scale) });
        }

        ring.SetContent("桌面", $"{nodes.Count} 项", nodes);
        if (nodes.Count > 1) ring.SetHover(1);

        _previewStage.Children.Add(ring);

        if (scale < 0.999)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6)
            };
            badge.Child = new TextBlock
            {
                Text = $"预览缩放 {scale:P0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Muted)
            };
            _previewStage.Children.Add(badge);
        }

        _previewRing = ring;
    }

    /// <summary>
    /// The preview draws its own glyph so it never blocks the UI thread on shell
    /// icon extraction for a throwaway synthetic folder list.
    /// </summary>
    private static ImageSource MakeGlyphIcon(string label, OrbitTheme theme, double sizePx)
    {
        double side = Math.Max(12, sizePx);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(theme.Accent, theme.Accent2, 45);
            brush.Freeze();
            dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, side, side * 0.82), side * 0.16, side * 0.16);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), null,
                new Rect(0, 0, side, side * 0.2));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(side), (int)Math.Ceiling(side), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void UpdateStatus()
    {
        AppConfig config = _holder.Current;
        string dismissMode = config.Menu.Dismiss.CloseOnClickOutside && config.Menu.Dismiss.CloseOnPointerLeave
            ? "点击外部 + 移出"
            : config.Menu.Dismiss.CloseOnClickOutside
                ? "点击外部"
                : config.Menu.Dismiss.CloseOnPointerLeave
                    ? "移出关闭"
                    : "仅手动关闭";

        string drillMode = config.Menu.Drill is { OnClick: true, OnHover: true }
            ? $"单击 / 悬停 {config.Menu.HoverDelayMs}ms"
            : config.Menu.Drill.OnClick
                ? "单击进入"
                : config.Menu.Drill.OnHover
                    ? $"悬停 {config.Menu.HoverDelayMs}ms 进入"
                    : "仅双击打开（不进入）";

        _status.Text = $"关闭：{dismissMode}\n进入：{drillMode}\n热键：{config.Trigger.Hotkey}   ·   已保存到 config.json";
    }

    private void ReloadFromDisk()
    {
        // Our own saves also raise Changed; rebuilding the field list on every slider
        // tick would fight the control the user is dragging.
        if ((DateTime.UtcNow - _lastSelfSave).TotalMilliseconds < 1200) return;
        if ((DateTime.UtcNow - _lastExternalReload).TotalMilliseconds < 400) return;
        _lastExternalReload = DateTime.UtcNow;

        AppConfig loaded = ConfigStore.LoadOrCreate(ConfigStore.DefaultPath, out _);
        _holder.Update(loaded);
        SelectGroup(_activeGroup);
        BuildPreview();
        _status.Text = "已从磁盘重新载入 config.json";
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.White;
        try
        {
            object? converted = ColorConverter.ConvertFromString(hex);
            return converted is Color color ? color : Colors.White;
        }
        catch (Exception)
        {
            return Colors.White;
        }
    }

    private Button MakeSecondaryButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Height = 32,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(Muted),
            Background = new SolidColorBrush(SurfaceColorAlt),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Line),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Template = FlatButtonTemplate();
        button.Click += (_, _) => onClick();
        return button;
    }

    private static ControlTemplate FlatButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding(nameof(Control.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding(nameof(Control.BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty,
            new System.Windows.Data.Binding(nameof(Control.Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    protected override void OnClosed(EventArgs e)
    {
        _configWatcher?.Dispose();
        base.OnClosed(e);
    }
}
