using System.Text.Json.Serialization;

namespace DesktopOrbit.Core.Config;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public GeneralOptions General { get; set; } = new();
    public TriggerOptions Trigger { get; set; } = new();
    public MenuOptions Menu { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();
    public List<SourceConfig> Sources { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();
    public SearchOptions Search { get; set; } = new();
    public ActionOptions Actions { get; set; } = new();

    public static AppConfig CreateDefault() => new()
    {
        Sources =
        {
            new SourceConfig { Id = "desktop", Type = "desktop", Label = "桌面" },
            new SourceConfig { Id = "downloads", Type = "folder", Label = "下载", Path = @"%USERPROFILE%\Downloads" },
            new SourceConfig { Id = "documents", Type = "folder", Label = "文档", Path = @"%USERPROFILE%\Documents" },
            new SourceConfig { Id = "recent", Type = "recent", Label = "最近" }
        },
        Layout = new LayoutConfig
        {
            Sectors =
            {
                new SectorConfig { SourceId = "desktop" },
                new SectorConfig { SourceId = "downloads" },
                new SectorConfig { SourceId = "documents" },
                new SectorConfig { SourceId = "recent" }
            }
        }
    };
}

public sealed class GeneralOptions
{
    public bool StartWithWindows { get; set; }
    public string Language { get; set; } = "zh-CN";
    public bool PauseWhenFullscreen { get; set; } = true;
    public string VaultPath { get; set; } = @"%USERPROFILE%\DesktopVault";
    public bool ShowHiddenItems { get; set; }
    public bool Thumbnails { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
}

public sealed class TriggerOptions
{
    public bool HotkeyEnabled { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public bool LongPressEnabled { get; set; } = true;
    public LongPressOptions LongPress { get; set; } = new();
    public bool MiddleClickOnDesktop { get; set; }
    public bool StrictSwallowClick { get; set; }
}

public sealed class LongPressOptions
{
    public int HoldMs { get; set; } = 350;
    public int MoveSlopPx { get; set; } = 6;
    public bool RequireBlankSpot { get; set; } = true;
    public bool OnlyWhenDesktopForeground { get; set; } = true;
}

public sealed class MenuOptions
{
    public double OuterRadiusDip { get; set; } = 240;
    public double HubRadiusRatio { get; set; } = 0.36;
    public double SectorGapDeg { get; set; } = 1.5;
    public int OpenAnimationMs { get; set; } = 150;
    public int HoverDelayMs { get; set; } = 320;
    public double HoverExpandPx { get; set; } = 6;
    public int MaxSectors { get; set; } = 12;
    public int MaxItemsPerFolder { get; set; } = 200;
    public int IdleTimeoutMs { get; set; } = 0;
    public double ShadowMarginDip { get; set; } = 48;
    public bool ShowLabels { get; set; } = true;
    public int LabelMaxChars { get; set; } = 8;
    public DismissOptions Dismiss { get; set; } = new();
    public DrillOptions Drill { get; set; } = new();
}

/// <summary>
/// How the menu goes away. Both modes can be active at once; each has its own
/// escape hatch so an accidental trigger is cheap to undo.
/// </summary>
public sealed class DismissOptions
{
    public bool CloseOnClickOutside { get; set; } = true;
    public bool CloseOnPointerLeave { get; set; }
    public int PointerLeaveDelayMs { get; set; } = 600;
    public double PointerLeaveBufferDip { get; set; } = 24;
    public bool CloseOnFocusLoss { get; set; } = true;
    public bool CloseOnEscape { get; set; } = true;
}

/// <summary>
/// How a folder sector opens. Click always works; hover drilling is optional and
/// timed by HoverDelayMs.
/// </summary>
public sealed class DrillOptions
{
    public bool OnClick { get; set; } = true;
    public bool OnHover { get; set; } = true;
    public bool RightClickGoesBack { get; set; } = true;
    public bool EscapeGoesBack { get; set; } = true;
}

public sealed class AppearanceOptions
{
    public string BackgroundColor { get; set; } = "#171A22";
    public string SurfaceColor { get; set; } = "#222734";
    public string AccentColor { get; set; } = "#7C5CFF";
    public string AccentColor2 { get; set; } = "#39C6FF";
    public string TextColor { get; set; } = "#F3F5FA";
    public string MutedTextColor { get; set; } = "#98A2B3";
    public double BackgroundOpacity { get; set; } = 0.86;
    public double IconSizeDip { get; set; } = 40;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public bool Animations { get; set; } = true;
    public bool Glow { get; set; } = true;
}

public sealed class SourceConfig
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "folder";
    public string? Path { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string ExpandedPath => string.IsNullOrWhiteSpace(Path) ? string.Empty : Environment.ExpandEnvironmentVariables(Path);
}

public sealed class LayoutConfig
{
    public List<SectorConfig> Sectors { get; set; } = new();
}

public sealed class SectorConfig
{
    public string? SourceId { get; set; }
    public string? Path { get; set; }
    public string? Label { get; set; }
}

public sealed class SearchOptions
{
    public int MaxResults { get; set; } = 50;
    public bool IncludeSubfolders { get; set; } = true;
    public int MaxDepth { get; set; } = 3;
    public int MaxIndexEntries { get; set; } = 20000;
}

public sealed class ActionOptions
{
    public bool DragEnabled { get; set; } = true;
    public bool ConfirmAdminOpen { get; set; } = true;
    public bool ShowRecentInHub { get; set; } = true;
}
