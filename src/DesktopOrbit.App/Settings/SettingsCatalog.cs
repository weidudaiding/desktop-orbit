using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopOrbit.Core.Config;

namespace DesktopOrbit.App.Settings;

/// <summary>
/// One editable field in the settings panel. Values are read and written straight
/// through to the live AppConfig instance so the preview and the running menu see
/// changes immediately; the panel only decides when to persist to disk.
/// </summary>
public sealed class SettingItem
{
    public required string Label { get; init; }
    public string? Hint { get; init; }
    public required Func<object?> Get { get; init; }
    public required Action<object?> Set { get; init; }
    public SettingKind Kind { get; init; } = SettingKind.Number;
    public double Min { get; init; }
    public double Max { get; init; }
    public double Step { get; init; } = 1;
    public IReadOnlyList<KeyValuePair<string, object>>? Choices { get; init; }
    public string Format { get; init; } = "0.##";
}

public enum SettingKind
{
    Number,
    Toggle,
    Choice,
    Text,
    Color
}

public sealed class SettingGroup
{
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required string Description { get; init; }
    public List<SettingItem> Items { get; } = new();
}

/// <summary>
/// Declarative description of every user-facing option. Keeping this in one place
/// is what lets the panel and the JSON config stay in sync: each item maps to a
/// real property, not to a copy.
/// </summary>
public static class SettingsCatalog
{
    public static List<SettingGroup> Build(AppConfig config)
    {
        var groups = new List<SettingGroup>();

        var trigger = new SettingGroup
        {
            Title = "唤出与触发",
            Glyph = "⚡",
            Description = "决定圆盘什么时候出现。"
        };
        trigger.Items.Add(new SettingItem
        {
            Label = "启用全局热键",
            Kind = SettingKind.Toggle,
            Get = () => config.Trigger.HotkeyEnabled,
            Set = value => config.Trigger.HotkeyEnabled = value is true
        });
        trigger.Items.Add(new SettingItem
        {
            Label = "热键组合",
            Hint = "例如 Ctrl+Alt+Space、Win+Shift+D",
            Kind = SettingKind.Text,
            Get = () => config.Trigger.Hotkey,
            Set = value => config.Trigger.Hotkey = value as string ?? string.Empty
        });
        trigger.Items.Add(new SettingItem
        {
            Label = "启用长按桌面空白处",
            Kind = SettingKind.Toggle,
            Get = () => config.Trigger.LongPressEnabled,
            Set = value => config.Trigger.LongPressEnabled = value is true
        });
        trigger.Items.Add(new SettingItem
        {
            Label = "长按时长",
            Hint = "按住多久后弹出，单位毫秒",
            Kind = SettingKind.Number,
            Min = 120,
            Max = 2000,
            Step = 10,
            Get = () => (double)config.Trigger.LongPress.HoldMs,
            Set = value => config.Trigger.LongPress.HoldMs = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        trigger.Items.Add(new SettingItem
        {
            Label = "允许位移",
            Hint = "手指抖动超过该像素即视为拖动，取消唤出",
            Kind = SettingKind.Number,
            Min = 2,
            Max = 40,
            Step = 1,
            Get = () => (double)config.Trigger.LongPress.MoveSlopPx,
            Set = value => config.Trigger.LongPress.MoveSlopPx = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        trigger.Items.Add(new SettingItem
        {
            Label = "仅在桌面空白处触发",
            Hint = "关闭后，在图标上长按也会弹出",
            Kind = SettingKind.Toggle,
            Get = () => config.Trigger.LongPress.RequireBlankSpot,
            Set = value => config.Trigger.LongPress.RequireBlankSpot = value is true
        });
        trigger.Items.Add(new SettingItem
        {
            Label = "全屏时暂停",
            Hint = "游戏或演示模式下不弹出",
            Kind = SettingKind.Toggle,
            Get = () => config.General.PauseWhenFullscreen,
            Set = value => config.General.PauseWhenFullscreen = value is true
        });
        groups.Add(trigger);

        var dismiss = new SettingGroup
        {
            Title = "关闭方式",
            Glyph = "✕",
            Description = "两种模式可以同时开启：点外部立即关闭，移出后延时关闭。"
        };
        dismiss.Items.Add(new SettingItem
        {
            Label = "点击外部关闭",
            Hint = "点击圆盘以外的任何位置立即关闭",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Dismiss.CloseOnClickOutside,
            Set = value => config.Menu.Dismiss.CloseOnClickOutside = value is true
        });
        dismiss.Items.Add(new SettingItem
        {
            Label = "移出关闭",
            Hint = "指针离开圆盘后自动关闭",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Dismiss.CloseOnPointerLeave,
            Set = value => config.Menu.Dismiss.CloseOnPointerLeave = value is true
        });
        dismiss.Items.Add(new SettingItem
        {
            Label = "移出死区时间",
            Hint = "离开后等待多久再关闭，避免误关",
            Kind = SettingKind.Number,
            Min = 0,
            Max = 3000,
            Step = 50,
            Get = () => (double)config.Menu.Dismiss.PointerLeaveDelayMs,
            Set = value => config.Menu.Dismiss.PointerLeaveDelayMs = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        dismiss.Items.Add(new SettingItem
        {
            Label = "边缘缓冲",
            Hint = "扩大判定范围，单位 DIP",
            Kind = SettingKind.Number,
            Min = 0,
            Max = 200,
            Step = 4,
            Get = () => config.Menu.Dismiss.PointerLeaveBufferDip,
            Set = value => config.Menu.Dismiss.PointerLeaveBufferDip = Convert.ToDouble(value, CultureInfo.InvariantCulture)
        });
        dismiss.Items.Add(new SettingItem
        {
            Label = "Esc 关闭",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Dismiss.CloseOnEscape,
            Set = value => config.Menu.Dismiss.CloseOnEscape = value is true
        });
        dismiss.Items.Add(new SettingItem
        {
            Label = "失去焦点关闭",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Dismiss.CloseOnFocusLoss,
            Set = value => config.Menu.Dismiss.CloseOnFocusLoss = value is true
        });
        groups.Add(dismiss);

        var drill = new SettingGroup
        {
            Title = "进入子文件夹",
            Glyph = "↳",
            Description = "控制如何展开下一级菜单。"
        };
        drill.Items.Add(new SettingItem
        {
            Label = "单击进入",
            Hint = "左键单击扇区确认进入",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Drill.OnClick,
            Set = value => config.Menu.Drill.OnClick = value is true
        });
        drill.Items.Add(new SettingItem
        {
            Label = "悬停进入",
            Hint = "悬停一段时间后自动进入",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Drill.OnHover,
            Set = value => config.Menu.Drill.OnHover = value is true
        });
        drill.Items.Add(new SettingItem
        {
            Label = "悬停等待时间",
            Hint = "500 - 2000 毫秒之间较自然",
            Kind = SettingKind.Number,
            Min = 100,
            Max = 3000,
            Step = 50,
            Get = () => (double)config.Menu.HoverDelayMs,
            Set = value => config.Menu.HoverDelayMs = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        drill.Items.Add(new SettingItem
        {
            Label = "右键返回上一级",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Drill.RightClickGoesBack,
            Set = value => config.Menu.Drill.RightClickGoesBack = value is true
        });
        drill.Items.Add(new SettingItem
        {
            Label = "Esc 返回上一级",
            Hint = "关闭后在子菜单里按 Esc 直接关闭圆盘",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.Drill.EscapeGoesBack,
            Set = value => config.Menu.Drill.EscapeGoesBack = value is true
        });
        groups.Add(drill);

        var shape = new SettingGroup
        {
            Title = "形状与内容",
            Glyph = "◎",
            Description = "圆盘大小、扇区数量与标签。"
        };
        shape.Items.Add(new SettingItem
        {
            Label = "圆盘半径",
            Hint = "单位 DIP，屏幕缩放 150% 时实际更大",
            Kind = SettingKind.Number,
            Min = 120,
            Max = 420,
            Step = 5,
            Get = () => config.Menu.OuterRadiusDip,
            Set = value => config.Menu.OuterRadiusDip = Convert.ToDouble(value, CultureInfo.InvariantCulture)
        });
        shape.Items.Add(new SettingItem
        {
            Label = "中心圆占比",
            Kind = SettingKind.Number,
            Min = 0.2,
            Max = 0.55,
            Step = 0.01,
            Format = "0.00",
            Get = () => config.Menu.HubRadiusRatio,
            Set = value => config.Menu.HubRadiusRatio = Convert.ToDouble(value, CultureInfo.InvariantCulture)
        });
        shape.Items.Add(new SettingItem
        {
            Label = "扇区间隙",
            Hint = "相邻扇区之间的角度，度",
            Kind = SettingKind.Number,
            Min = 0,
            Max = 12,
            Step = 0.5,
            Get = () => config.Menu.SectorGapDeg,
            Set = value => config.Menu.SectorGapDeg = Convert.ToDouble(value, CultureInfo.InvariantCulture)
        });
        shape.Items.Add(new SettingItem
        {
            Label = "单环扇区上限",
            Hint = "超出后折叠成「+N」翻页",
            Kind = SettingKind.Number,
            Min = 2,
            Max = 20,
            Step = 1,
            Get = () => (double)config.Menu.MaxSectors,
            Set = value => config.Menu.MaxSectors = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        shape.Items.Add(new SettingItem
        {
            Label = "显示文字标签",
            Kind = SettingKind.Toggle,
            Get = () => config.Menu.ShowLabels,
            Set = value => config.Menu.ShowLabels = value is true
        });
        shape.Items.Add(new SettingItem
        {
            Label = "标签最大字数",
            Kind = SettingKind.Number,
            Min = 3,
            Max = 20,
            Step = 1,
            Get = () => (double)config.Menu.LabelMaxChars,
            Set = value => config.Menu.LabelMaxChars = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        shape.Items.Add(new SettingItem
        {
            Label = "悬停放大",
            Hint = "指针所在扇区向外扩张的像素",
            Kind = SettingKind.Number,
            Min = 0,
            Max = 24,
            Step = 1,
            Get = () => config.Menu.HoverExpandPx,
            Set = value => config.Menu.HoverExpandPx = Convert.ToDouble(value, CultureInfo.InvariantCulture)
        });
        groups.Add(shape);

        var look = new SettingGroup
        {
            Title = "外观",
            Glyph = "🎨",
            Description = "配色、图标大小与动画。修改立即生效。"
        };
        look.Items.Add(new SettingItem
        {
            Label = "强调色",
            Kind = SettingKind.Color,
            Get = () => config.Appearance.AccentColor,
            Set = value => config.Appearance.AccentColor = value as string ?? string.Empty
        });
        look.Items.Add(new SettingItem
        {
            Label = "高亮终点色",
            Kind = SettingKind.Color,
            Get = () => config.Appearance.AccentColor2,
            Set = value => config.Appearance.AccentColor2 = value as string ?? string.Empty
        });
        look.Items.Add(new SettingItem
        {
            Label = "背景色",
            Kind = SettingKind.Color,
            Get = () => config.Appearance.BackgroundColor,
            Set = value => config.Appearance.BackgroundColor = value as string ?? string.Empty
        });
        look.Items.Add(new SettingItem
        {
            Label = "图标大小",
            Hint = "单位 DIP",
            Kind = SettingKind.Number,
            Min = 16,
            Max = 72,
            Step = 2,
            Get = () => config.Appearance.IconSizeDip,
            Set = value => config.Appearance.IconSizeDip = Convert.ToDouble(value, CultureInfo.InvariantCulture)
        });
        look.Items.Add(new SettingItem
        {
            Label = "启用动画",
            Kind = SettingKind.Toggle,
            Get = () => config.Appearance.Animations,
            Set = value => config.Appearance.Animations = value is true
        });
        look.Items.Add(new SettingItem
        {
            Label = "外发光",
            Kind = SettingKind.Toggle,
            Get = () => config.Appearance.Glow,
            Set = value => config.Appearance.Glow = value is true
        });
        look.Items.Add(new SettingItem
        {
            Label = "显示缩略图",
            Hint = "图片与视频用缩略图替代图标",
            Kind = SettingKind.Toggle,
            Get = () => config.General.Thumbnails,
            Set = value => config.General.Thumbnails = value is true
        });
        groups.Add(look);

        var search = new SettingGroup
        {
            Title = "搜索",
            Glyph = "🔍",
            Description = "输入即搜索，结果覆盖在圆盘中央。"
        };
        search.Items.Add(new SettingItem
        {
            Label = "结果数量上限",
            Kind = SettingKind.Number,
            Min = 5,
            Max = 200,
            Step = 5,
            Get = () => (double)config.Search.MaxResults,
            Set = value => config.Search.MaxResults = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        search.Items.Add(new SettingItem
        {
            Label = "索引深度",
            Hint = "从配置根目录往下扫描的层数",
            Kind = SettingKind.Number,
            Min = 1,
            Max = 6,
            Step = 1,
            Get = () => (double)config.Search.MaxDepth,
            Set = value => config.Search.MaxDepth = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        search.Items.Add(new SettingItem
        {
            Label = "索引条目上限",
            Kind = SettingKind.Number,
            Min = 1000,
            Max = 100000,
            Step = 1000,
            Get = () => (double)config.Search.MaxIndexEntries,
            Set = value => config.Search.MaxIndexEntries = Convert.ToInt32(value, CultureInfo.InvariantCulture)
        });
        groups.Add(search);

        var system = new SettingGroup
        {
            Title = "系统",
            Glyph = "⚙",
            Description = "启动与常驻行为。"
        };
        system.Items.Add(new SettingItem
        {
            Label = "开机自动启动",
            Kind = SettingKind.Toggle,
            Get = () => config.General.StartWithWindows,
            Set = value => config.General.StartWithWindows = value is true
        });
        system.Items.Add(new SettingItem
        {
            Label = "显示隐藏文件",
            Hint = "包含 Hidden / System 属性的项目",
            Kind = SettingKind.Toggle,
            Get = () => config.General.ShowHiddenItems,
            Set = value => config.General.ShowHiddenItems = value is true
        });
        system.Items.Add(new SettingItem
        {
            Label = "允许拖出文件",
            Kind = SettingKind.Toggle,
            Get = () => config.Actions.DragEnabled,
            Set = value => config.Actions.DragEnabled = value is true
        });
        groups.Add(system);

        return groups;
    }

    public static string Format(SettingItem item, object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool flag => flag ? "开" : "关",
            double number => number.ToString(item.Format, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
