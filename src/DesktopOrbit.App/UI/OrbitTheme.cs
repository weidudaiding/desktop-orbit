using System;
using System.Windows;
using System.Windows.Media;
using DesktopOrbit.Core.Config;

namespace DesktopOrbit.App.UI;

public sealed class OrbitTheme
{
    public OrbitTheme(AppearanceOptions options, MenuOptions menu)
    {
        Background = Parse(options.BackgroundColor, Color.FromRgb(0x17, 0x1A, 0x22));
        Surface = Parse(options.SurfaceColor, Color.FromRgb(0x22, 0x27, 0x34));
        Accent = Parse(options.AccentColor, Color.FromRgb(0x7C, 0x5C, 0xFF));
        Accent2 = Parse(options.AccentColor2, Color.FromRgb(0x39, 0xC6, 0xFF));
        Text = Parse(options.TextColor, Color.FromRgb(0xF3, 0xF5, 0xFA));
        Muted = Parse(options.MutedTextColor, Color.FromRgb(0x98, 0xA2, 0xB3));

        BackgroundOpacity = Math.Clamp(options.BackgroundOpacity, 0.2, 1.0);
        IconSizeDip = Math.Clamp(options.IconSizeDip, 16, 96);
        ShowLabels = menu.ShowLabels;
        Glow = options.Glow;
        Animations = options.Animations;
        LabelMaxChars = Math.Clamp(menu.LabelMaxChars, 3, 24);

        Font = new FontFamily(string.IsNullOrWhiteSpace(options.FontFamily) ? "Microsoft YaHei UI" : options.FontFamily);
        Typeface = new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        TypefaceBold = new Typeface(Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        BackgroundBrush = Freeze(new SolidColorBrush(WithAlpha(Background, Math.Min(1.0, BackgroundOpacity + 0.08))));
        HubBrush = Freeze(BuildHubBrush(Surface, Background));
        TextBrush = Freeze(new SolidColorBrush(Text));
        MutedBrush = Freeze(new SolidColorBrush(Muted));
        AccentBrush = Freeze(new SolidColorBrush(Accent));
        Accent2Brush = Freeze(new SolidColorBrush(Accent2));

        SectorBrushA = Freeze(BuildSectorBrush(WithAlpha(Surface, 0.96), WithAlpha(Background, 0.90)));
        SectorBrushB = Freeze(BuildSectorBrush(WithAlpha(Surface, 0.90), WithAlpha(Background, 0.84)));
        HoverBrush = Freeze(BuildSectorBrush(WithAlpha(Accent, 0.96), WithAlpha(Accent2, 0.88)));

        SectorPen = Freeze(new Pen(new SolidColorBrush(WithAlpha(Text, 0.10)), 1.0));
        HoverPen = Freeze(new Pen(new SolidColorBrush(WithAlpha(Text, 0.55)), 1.2));
        HubPen = Freeze(new Pen(new SolidColorBrush(WithAlpha(Accent, 0.55)), 1.2));
        GlowPen = Freeze(new Pen(new SolidColorBrush(WithAlpha(Accent, 0.20)), 6.0));
        AccentPen = Freeze(new Pen(new SolidColorBrush(WithAlpha(Accent2, 0.75)), 1.6));
    }

    public Color Background { get; }
    public Color Surface { get; }
    public Color Accent { get; }
    public Color Accent2 { get; }
    public Color Text { get; }
    public Color Muted { get; }
    public double BackgroundOpacity { get; }
    public double IconSizeDip { get; }
    public bool ShowLabels { get; }
    public bool Glow { get; }
    public bool Animations { get; }
    public int LabelMaxChars { get; }

    public FontFamily Font { get; }
    public Typeface Typeface { get; }
    public Typeface TypefaceBold { get; }

    public Brush BackgroundBrush { get; }
    public Brush HubBrush { get; }
    public Brush TextBrush { get; }
    public Brush MutedBrush { get; }
    public Brush AccentBrush { get; }
    public Brush Accent2Brush { get; }
    public Brush SectorBrushA { get; }
    public Brush SectorBrushB { get; }
    public Brush HoverBrush { get; }

    public Pen SectorPen { get; }
    public Pen HoverPen { get; }
    public Pen HubPen { get; }
    public Pen GlowPen { get; }
    public Pen AccentPen { get; }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);

    private static Brush BuildSectorBrush(Color from, Color to)
    {
        var brush = new LinearGradientBrush(from, to, new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 1));
        return brush;
    }

    private static Brush BuildHubBrush(Color surface, Color background)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new System.Windows.Point(0.4, 0.35),
            Center = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.78,
            RadiusY = 0.78
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(surface, 0.99), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(background, 0.97), 1.0));
        return brush;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            object? converted = ColorConverter.ConvertFromString(value);
            return converted is Color color ? color : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }
}
