using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOrbit.Core.Layout;
using DesktopOrbit.Core.Models;

namespace DesktopOrbit.App.UI;

public sealed class RingNode
{
    public required MenuItem Item { get; init; }
    public bool IsMore { get; init; }
    public bool IsEmptyHint { get; init; }
    public ImageSource? Icon { get; set; }
}

public sealed class RingMenuView : FrameworkElement
{
    private readonly double _shadowMarginDip;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private OrbitTheme _theme;
    private double _hubRatio;
    private double _gapDegrees;
    private double _hoverExpand;

    private IReadOnlyList<RingNode> _nodes = Array.Empty<RingNode>();
    private double[] _hoverEase = Array.Empty<double>();
    private int _hover = -1;
    private int _selected;
    private bool _animating;
    private TimeSpan _lastFrame;
    private string _title = "桌面";
    private string _subtitle = string.Empty;
    private string _query = string.Empty;
    private bool _searchFocused;

    public RingMenuView(OrbitTheme theme, double shadowMarginDip, double hubRatio, double gapDegrees, double hoverExpand)
    {
        _theme = theme;
        _shadowMarginDip = shadowMarginDip;
        _hubRatio = hubRatio;
        _gapDegrees = gapDegrees;
        _hoverExpand = hoverExpand;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;

        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += OnFrame;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    public int HoverIndex => _hover;
    public int SelectedIndex => _selected;
    public double OpenProgress => 1;
    public bool AnimationsEnabled => _theme.Animations;
    public IReadOnlyList<RingNode> Nodes => _nodes;
    public double OuterRadius
    {
        get
        {
            double diameter = Math.Min(ActualWidth, ActualHeight);
            return Math.Max(1, diameter / 2 - _shadowMarginDip);
        }
    }

    public void ApplyTheme(OrbitTheme theme, double hubRatio, double gapDegrees, double hoverExpand)
    {
        _theme = theme;
        _hubRatio = hubRatio;
        _gapDegrees = gapDegrees;
        _hoverExpand = hoverExpand;
        InvalidateVisual();
    }

    public void SetContent(string title, string subtitle, IReadOnlyList<RingNode> nodes)
    {
        _title = title;
        _subtitle = subtitle;
        _nodes = nodes;
        _hover = -1;
        _selected = 0;
        _hoverEase = new double[nodes.Count];
        InvalidateVisual();
    }

    public void SetQuery(string query, bool focused)
    {
        _query = query;
        _searchFocused = focused;
        InvalidateVisual();
    }

    public void SetHover(int index)
    {
        if (index == _hover) return;
        _hover = index;
        if (!_theme.Animations)
        {
            for (int i = 0; i < _hoverEase.Length; i++) _hoverEase[i] = i == index ? 1 : 0;
            InvalidateVisual();
            return;
        }
        StartAnimation();
    }

    public void SetSelected(int index)
    {
        if (_nodes.Count == 0) return;
        int wrapped = ((index % _nodes.Count) + _nodes.Count) % _nodes.Count;
        if (wrapped == _selected) return;
        _selected = wrapped;
        InvalidateVisual();
    }

    public void SetIcon(int index, ImageSource? icon)
    {
        if (index < 0 || index >= _nodes.Count) return;
        _nodes[index].Icon = icon;
        InvalidateVisual();
    }

    public void PlayOpenAnimation()
    {
        if (!_theme.Animations)
        {
            InvalidateVisual();
            return;
        }
        StartAnimation();
    }

    public RingGeometry BuildGeometry(int sectorCount)
    {
        double center = Math.Min(ActualWidth, ActualHeight) / 2;
        double outer = Math.Max(1, center - _shadowMarginDip);
        return new RingGeometry(outer, outer * _hubRatio, sectorCount, _gapDegrees);
    }

    public RingHit HitTestPoint(Point point)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        RingGeometry geometry = BuildGeometry(_nodes.Count);
        return geometry.HitTest(point.X - center.X, point.Y - center.Y);
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        _clock.Restart();

        // A DispatcherTimer is used instead of CompositionTarget.Rendering: the
        // rendering event only fires while the window is actively compositing, and
        // a menu that opens into an occluded or not-yet-composited state would
        // otherwise freeze mid-animation and stay visibly translucent forever.
        _timer.Interval = TimeSpan.FromMilliseconds(15);
        _timer.Start();
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        _timer.Stop();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double deltaSeconds = _clock.Elapsed.TotalSeconds - _lastFrame.TotalSeconds;
        _lastFrame = _clock.Elapsed;
        if (deltaSeconds <= 0 || deltaSeconds > 0.25) deltaSeconds = 0.016;

        bool settled = true;

        for (int i = 0; i < _hoverEase.Length; i++)
        {
            double target = i == _hover ? 1 : 0;
            double current = _hoverEase[i];
            if (Math.Abs(target - current) < 0.01)
            {
                _hoverEase[i] = target;
                continue;
            }
            _hoverEase[i] = current + (target - current) * Math.Min(1, deltaSeconds * 16);
            settled = false;
        }

        InvalidateVisual();
        if (settled) StopAnimation();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = OuterRadius;
        double hub = outer * _hubRatio;

        if (outer <= 4) return;

        if (_theme.Glow)
        {
            dc.DrawEllipse(null, _theme.GlowPen, center, outer + 6, outer + 6);
        }

        var geometry = new RingGeometry(outer, hub, _nodes.Count, _gapDegrees);

        for (int i = 0; i < _nodes.Count; i++)
        {
            double ease = i < _hoverEase.Length ? _hoverEase[i] : 0;
            double expand = _hoverExpand * ease;
            double sectorOuter = outer + expand;
            double sectorInner = hub + expand * 0.35;

            (double start, double end) = geometry.SectorAnglesDegrees(i);
            Geometry path = BuildSectorGeometry(center, sectorInner, sectorOuter, start, end);
            Brush fill = i % 2 == 0 ? _theme.SectorBrushA : _theme.SectorBrushB;
            dc.DrawGeometry(fill, ease > 0.02 ? _theme.HoverPen : _theme.SectorPen, path);

            if (ease > 0.02)
            {
                dc.PushOpacity(ease * 0.92);
                dc.DrawGeometry(_theme.HoverBrush, null, path);
                dc.Pop();
            }

            if (i == _selected && _nodes.Count > 1)
            {
                dc.DrawGeometry(null, _theme.AccentPen, path);
            }

            DrawSectorContent(dc, center, geometry, i, sectorInner, sectorOuter, ease);
        }

        DrawHub(dc, center, hub);
    }

    private void DrawSectorContent(DrawingContext dc, Point center, RingGeometry geometry, int index,
        double inner, double outer, double ease)
    {
        RingNode node = _nodes[index];
        double midAngle = geometry.SectorCenterRadians(index);
        double sectorSpan = Math.PI * 2 / Math.Max(1, _nodes.Count);

        double iconRadius = inner + (outer - inner) * 0.40;
        double labelRadius = inner + (outer - inner) * 0.70;

        var iconCenter = new Point(
            center.X + Math.Cos(midAngle) * iconRadius,
            center.Y + Math.Sin(midAngle) * iconRadius);

        double iconSize = _theme.IconSizeDip;

        if (node.Icon is not null)
        {
            dc.DrawImage(node.Icon, new Rect(iconCenter.X - iconSize / 2, iconCenter.Y - iconSize / 2, iconSize, iconSize));
        }
        else
        {
            var placeholder = new SolidColorBrush(Color.FromArgb(52, _theme.Text.R, _theme.Text.G, _theme.Text.B));
            placeholder.Freeze();
            dc.DrawEllipse(placeholder, null, iconCenter, iconSize * 0.34, iconSize * 0.34);
        }

        if (!_theme.ShowLabels) return;

        double ringWidth = outer - inner;
        double chord = 2 * labelRadius * Math.Sin(sectorSpan / 2) * 0.9;
        double maxWidth = Math.Max(34, Math.Min(Math.Min(chord, ringWidth * 1.05), 118));

        string label = TrimLabel(node.Item.Label, _theme.LabelMaxChars);
        FormattedText text = BuildText(label, _theme.Typeface, 11.0, _theme.TextBrush, maxWidth);
        text.MaxTextWidth = maxWidth;
        text.Trimming = TextTrimming.CharacterEllipsis;

        var labelCenter = new Point(
            center.X + Math.Cos(midAngle) * labelRadius,
            center.Y + Math.Sin(midAngle) * labelRadius);

        var plate = new Rect(
            labelCenter.X - text.Width / 2 - 5,
            labelCenter.Y - text.Height / 2 - 1,
            text.Width + 10,
            text.Height + 2);
        var plateBrush = new SolidColorBrush(Color.FromArgb(196, _theme.Background.R, _theme.Background.G, _theme.Background.B));
        plateBrush.Freeze();
        dc.DrawRoundedRectangle(plateBrush, null, plate, 4, 4);

        dc.DrawText(text, new Point(labelCenter.X - text.Width / 2, labelCenter.Y - text.Height / 2));
    }

    private static string TrimLabel(string label, int maxChars)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;

        string stem = System.IO.Path.GetFileNameWithoutExtension(label);
        string candidate = string.IsNullOrEmpty(stem) ? label : stem;
        return candidate.Length <= maxChars ? candidate : candidate[..maxChars] + "…";
    }

    private void DrawHub(DrawingContext dc, Point center, double hubRadius)
    {
        dc.DrawEllipse(_theme.HubBrush, _theme.HubPen, center, hubRadius, hubRadius);

        double usable = hubRadius * 1.42;
        double top = center.Y - hubRadius * 0.52;

        bool searching = !string.IsNullOrEmpty(_query);

        if (searching)
        {
            // While typing the hub becomes the query field: the row is drawn as a
            // filled pill so the typed text always has a dark background, and the
            // caret makes it obvious that input is going somewhere.
            DrawQueryRow(dc, center, hubRadius);
            return;
        }

        if (!string.IsNullOrEmpty(_title))
        {
            FormattedText title = BuildText(_title, _theme.TypefaceBold, 12.5, _theme.TextBrush, usable);
            title.MaxTextWidth = usable;
            title.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(title, new Point(center.X - title.Width / 2, top));
        }

        if (!string.IsNullOrEmpty(_subtitle))
        {
            FormattedText subtitle = BuildText(_subtitle, _theme.Typeface, 9.5, _theme.MutedBrush, usable);
            subtitle.MaxTextWidth = usable;
            subtitle.MaxTextHeight = 26;
            subtitle.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(subtitle, new Point(center.X - subtitle.Width / 2, top + 18));
        }

        FormattedText hint = BuildText("输入即搜索", _theme.Typeface, 10.5, _theme.MutedBrush, usable);
        dc.DrawText(hint, new Point(center.X - hint.Width / 2, center.Y + hubRadius * 0.10));
    }

    private void DrawQueryRow(DrawingContext dc, Point center, double hubRadius)
    {
        double width = hubRadius * 1.62;
        double height = 34;
        var row = new Rect(center.X - width / 2, center.Y - height / 2, width, height);

        var fieldBrush = new SolidColorBrush(Color.FromArgb(235, 0x0B, 0x0D, 0x12));
        fieldBrush.Freeze();
        var fieldPen = new Pen(_theme.AccentBrush, 1.4);
        fieldPen.Freeze();

        dc.DrawRoundedRectangle(fieldBrush, fieldPen, row, height / 2, height / 2);

        FormattedText query = BuildText(_query, _theme.TypefaceBold, 13.5, _theme.TextBrush, width - 34);
        query.MaxTextWidth = width - 34;
        query.Trimming = TextTrimming.CharacterEllipsis;

        double textX = row.Left + 14;
        double textY = row.Top + (height - query.Height) / 2;
        dc.DrawText(query, new Point(textX, textY));

        double caretX = Math.Min(row.Right - 16, textX + query.Width + 1.5);
        var caretPen = new Pen(_theme.Accent2Brush, 1.4);
        caretPen.Freeze();
        dc.DrawLine(caretPen, new Point(caretX, row.Top + 9), new Point(caretX, row.Bottom - 9));
    }

    private FormattedText BuildText(string text, Typeface typeface, double size, Brush brush, double maxWidth)
    {
        var formatted = new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    private static Brush MakeFaded(Brush source, double opacity)
    {
        if (source is SolidColorBrush solid)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(solid.Color.A * opacity), solid.Color.R, solid.Color.G, solid.Color.B));
            brush.Freeze();
            return brush;
        }
        return source;
    }

    private static Geometry BuildSectorGeometry(Point center, double inner, double outer, double startDegrees, double endDegrees)
    {
        double start = startDegrees * Math.PI / 180.0;
        double end = endDegrees * Math.PI / 180.0;
        bool large = Math.Abs(endDegrees - startDegrees) > 180;

        Point outerStart = Polar(center, start, outer);
        Point outerEnd = Polar(center, end, outer);
        Point innerEnd = Polar(center, end, inner);
        Point innerStart = Polar(center, start, inner);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(outerStart, true, true);
            context.ArcTo(outerEnd, new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            context.LineTo(innerEnd, true, false);
            context.ArcTo(innerStart, new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point Polar(Point center, double radians, double radius) =>
        new(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
}
