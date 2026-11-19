using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopOrbit.App.UI;
using DesktopOrbit.Core.Config;
using DesktopOrbit.Core.Models;
using DesktopOrbit.Shell.Shell;

namespace DesktopOrbit.App;

/// <summary>
/// Off-screen render used to validate the ring visuals without a display session.
/// Writes a PNG of the ring and a second one with the search panel visible.
/// </summary>
internal static class RenderProbe
{
    public static void Run(string outputPath)
    {
        AppConfig config = AppConfig.CreateDefault();
        config.Appearance.Animations = false;

        var theme = new OrbitTheme(config.Appearance, config.Menu);
        var lister = new FolderLister(config.General);
        var iconCache = new ShellIconCache(ShellIconCache.DefaultDirectory);
        var loader = new IconLoader(iconCache);

        List<MenuItem> items = lister.ListDesktop().Take(8).ToList();
        if (items.Count == 0)
        {
            items = new List<MenuItem>
            {
                new() { Path = "C:\\Windows", Label = "Windows", Kind = ItemKind.Folder },
                new() { Path = "C:\\Users", Label = "Users", Kind = ItemKind.Folder }
            };
        }

        int pixelSize = (int)(config.Appearance.IconSizeDip * 1.5);
        var nodes = new List<RingNode>();
        foreach (MenuItem item in items)
        {
            var node = new RingNode { Item = item };
            try
            {
                node.Icon = loader.LoadAsync(item.Path, item.IsFolder, pixelSize, !item.IsFolder, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
            nodes.Add(node);
        }

        var view = new RingMenuView(theme, config.Menu.ShadowMarginDip, config.Menu.HubRadiusRatio,
            config.Menu.SectorGapDeg, config.Menu.HoverExpandPx);
        view.SetContent("桌面", $"{nodes.Count} 项 · 预览", nodes);
        view.SetHover(1);
        view.SetSelected(1);

        const double size = 520;
        view.Width = size;
        view.Height = size;

        Render(view, outputPath);

        var panel = new SearchPanelView(theme);
        panel.Width = 340;
        panel.SetResults(new[]
        {
            Row("季度报告-2026.docx", @"D:\Work\Reports"),
            Row("桌面整理清单.md", @"D:\Work"),
            Row("QuarterlyReview.xlsx", @"D:\Work\Finance")
        }, "报告");

        string panelPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".",
            Path.GetFileNameWithoutExtension(outputPath) + "-search.png");
        Render(panel, panelPath, 340, 162);
    }

    private static DesktopOrbit.Shell.Search.SearchResult Row(string label, string folder) => new()
    {
        Path = System.IO.Path.Combine(folder, label),
        Label = label,
        Folder = folder,
        Kind = ItemKind.File,
        Score = 100
    };

    private static void Render(FrameworkElement element, string path, double width = 0, double height = 0)
    {
        double w = width > 0 ? width : element.Width;
        double h = height > 0 ? height : element.Height;

        element.Measure(new Size(w, h));
        element.Arrange(new Rect(0, 0, w, h));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
