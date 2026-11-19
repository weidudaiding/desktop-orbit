using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopOrbit.Shell.Search;

namespace DesktopOrbit.App.UI;

/// <summary>
/// Search results list. Drawn with a near-opaque surface: over a bright wallpaper
/// or a white document, a translucent panel makes both the rows and the query
/// unreadable, which was the original complaint.
/// </summary>
public sealed class SearchPanelView : FrameworkElement
{
    private const double RowHeight = 38;
    private const double HeaderHeight = 30;
    private const int MaxVisibleRows = 5;

    private readonly OrbitTheme _theme;
    private IReadOnlyList<SearchResult> _results = Array.Empty<SearchResult>();
    private string _query = string.Empty;
    private int _selected;

    public SearchPanelView(OrbitTheme theme)
    {
        _theme = theme;
        IsHitTestVisible = false;
    }

    public int SelectedIndex => _selected;
    public int Count => _results.Count;
    public IReadOnlyList<SearchResult> Results => _results;

    public void ApplyTheme(OrbitTheme theme)
    {
        InvalidateVisual();
    }

    public void SetResults(IReadOnlyList<SearchResult> results, string query)
    {
        _results = results;
        _query = query ?? string.Empty;
        if (_selected >= _results.Count) _selected = Math.Max(0, _results.Count - 1);

        // The panel is hosted in a Canvas, which only re-arranges children whose
        // measure is invalidated. Without this the new DesiredSize never reaches
        // ActualWidth and every background rectangle collapses to zero width while
        // the text still renders - a nearly invisible panel.
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void MoveSelection(int delta)
    {
        if (_results.Count == 0) return;
        int next = ((_selected + delta) % _results.Count + _results.Count) % _results.Count;
        if (next == _selected) return;
        _selected = next;
        InvalidateVisual();
    }

    public int HitTestRow(Point point)
    {
        if (_results.Count == 0) return -1;

        int offset = ComputeOffset();
        int visible = Math.Min(_results.Count, MaxVisibleRows);
        double y = point.Y - HeaderHeight - 4;

        if (y < 0) return -1;

        int row = (int)(y / RowHeight);
        if (row < 0 || row >= visible) return -1;

        int index = row + offset;
        return index < _results.Count ? index : -1;
    }

    private int ComputeOffset()
    {
        int visible = Math.Min(_results.Count, MaxVisibleRows);
        int offset = Math.Max(0, _selected - visible + 1);
        if (_selected < offset) offset = _selected;
        return offset;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int visible = Math.Min(Math.Max(0, _results.Count), MaxVisibleRows);
        double height = _results.Count == 0 ? 0 : HeaderHeight + visible * RowHeight + 8;

        double width = double.IsInfinity(availableSize.Width) || double.IsNaN(availableSize.Width)
            ? Width
            : availableSize.Width;

        return new Size(double.IsNaN(width) ? 340 : Math.Max(280, width), height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_results.Count == 0) return;

        // MeasureOverride owns the size; ActualWidth/ActualHeight can lag one frame
        // behind a results change, which offsets the card from the ring's centre.
        double width = DesiredSize.Width > 0 ? DesiredSize.Width : Width;
        if (double.IsNaN(width) || width <= 0) width = 340;

        double height = DesiredSize.Height > 0
            ? DesiredSize.Height
            : HeaderHeight + Math.Min(_results.Count, MaxVisibleRows) * RowHeight + 8;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var shadow = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        shadow.Freeze();
        dc.DrawRoundedRectangle(shadow, null, new Rect(0, 3, width, height), 14, 14);

        var background = new SolidColorBrush(Color.FromArgb(252, 0x10, 0x13, 0x1A));
        background.Freeze();
        var border = new Pen(_theme.AccentBrush, 1.2);
        border.Freeze();
        dc.DrawRoundedRectangle(background, border, new Rect(0, 0, width, height), 14, 14);

        var header = new FormattedText(
            $"{_results.Count} 个结果 · “{ShortQuery(_query)}”",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            _theme.TypefaceBold,
            11.5,
            _theme.Accent2Brush,
            dpi)
        {
            MaxTextWidth = width - 24,
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(header, new Point(14, 8));

        var divider = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
        divider.Freeze();
        dc.DrawLine(divider, new Point(12, HeaderHeight - 1), new Point(width - 12, HeaderHeight - 1));

        int visible = Math.Min(_results.Count, MaxVisibleRows);
        int offset = ComputeOffset();

        for (int i = 0; i < visible; i++)
        {
            int index = i + offset;
            if (index >= _results.Count) break;

            SearchResult result = _results[index];
            var row = new Rect(8, HeaderHeight + 4 + i * RowHeight, width - 16, RowHeight - 4);

            if (index == _selected)
            {
                var highlight = new SolidColorBrush(Color.FromArgb(90, _theme.Accent.R, _theme.Accent.G, _theme.Accent.B));
                highlight.Freeze();
                dc.DrawRoundedRectangle(highlight, null, row, 8, 8);
            }

            var label = new FormattedText(result.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _theme.TypefaceBold, 13, _theme.TextBrush, dpi)
            {
                MaxTextWidth = width - 30,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(label, new Point(16, row.Top + 4));

            var folderBrush = new SolidColorBrush(Color.FromArgb(200, 0x9A, 0xA6, 0xBA));
            folderBrush.Freeze();
            var folder = new FormattedText(Shorten(result.Folder), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _theme.Typeface, 10, folderBrush, dpi)
            {
                MaxTextWidth = width - 30,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(folder, new Point(16, row.Top + 21));
        }

        if (_results.Count > visible)
        {
            var more = new FormattedText($"+{_results.Count - visible} 个结果，↑↓ 翻动",
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _theme.Typeface, 9.5,
                _theme.MutedBrush, dpi);
            dc.DrawText(more, new Point(16, HeaderHeight + 4 + visible * RowHeight - 2));
        }
    }

    private static string ShortQuery(string query) =>
        string.IsNullOrEmpty(query) ? string.Empty : query.Length <= 18 ? query : query[..17] + "…";

    private static string Shorten(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= 52) return path;
        return "…" + path[^50..];
    }
}
