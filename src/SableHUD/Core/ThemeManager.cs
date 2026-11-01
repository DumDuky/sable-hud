using System.Windows;
using System.Windows.Media;

namespace SableHUD.Core;

public sealed record ThemeDef(
    string Id,
    string Name,
    bool Light,
    string Bg,
    string BgTop,
    string BgSoft,
    string Card,
    string Stroke,
    string StrokeStrong,
    string Text,
    string TextMuted,
    string TextDim,
    string Accent,
    string AccentHover,
    string AccentPressed,
    string OnAccent);

/// <summary>
/// Four monochrome presets. LiMusic ships colourful palettes (Catppuccin, Tokyo Night,
/// Neon...); Sable keeps the idea of swappable themes but holds the whole app to ink,
/// paper and graphite, per the design brief.
/// </summary>
public static class Themes
{
    public static readonly ThemeDef Noir = new(
        "Noir", "Noir", false,
        Bg: "#F50A0A0C", BgTop: "#FF121216", BgSoft: "#FF101013", Card: "#FF141418",
        Stroke: "#1AFFFFFF", StrokeStrong: "#2BFFFFFF",
        Text: "#FFF7F7F8", TextMuted: "#FFA2A2AA", TextDim: "#FF70707A",
        Accent: "#FFF7F7F8", AccentHover: "#FFE4E4E9", AccentPressed: "#FFCDCDD4",
        OnAccent: "#FF0A0A0C");

    public static readonly ThemeDef Graphite = new(
        "Graphite", "Graphite", false,
        Bg: "#F517181B", BgTop: "#FF202226", BgSoft: "#FF1C1E22", Card: "#FF222429",
        Stroke: "#1AFFFFFF", StrokeStrong: "#2EFFFFFF",
        Text: "#FFF3F3F5", TextMuted: "#FFA7A9B1", TextDim: "#FF767983",
        Accent: "#FFF5F5F7", AccentHover: "#FFE2E3E7", AccentPressed: "#FFCBCDD4",
        OnAccent: "#FF121316");

    public static readonly ThemeDef Ivory = new(
        "Ivory", "Ivory", true,
        Bg: "#FBF8F4EF", BgTop: "#FFFFFFFF", BgSoft: "#FFF2EEE7", Card: "#FFFFFEFB",
        Stroke: "#1A171614", StrokeStrong: "#2E171614",
        Text: "#FF171614", TextMuted: "#FF585550", TextDim: "#FF6F6B63",
        Accent: "#FF171614", AccentHover: "#FF33312D", AccentPressed: "#FF4A4741",
        OnAccent: "#FFFAF8F3");

    public static readonly ThemeDef Porcelain = new(
        "Porcelain", "Porcelain", true,
        Bg: "#FBFAF9F7", BgTop: "#FFFFFFFF", BgSoft: "#FFF1F1EF", Card: "#FFFFFFFF",
        Stroke: "#16111112", StrokeStrong: "#29111112",
        Text: "#FF111112", TextMuted: "#FF5C5C63", TextDim: "#FF707077",
        Accent: "#FF101012", AccentHover: "#FF2C2C30", AccentPressed: "#FF434348",
        OnAccent: "#FFFFFFFF");

    public static readonly IReadOnlyList<ThemeDef> All = new[] { Noir, Graphite, Ivory, Porcelain };

    public static ThemeDef Get(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Noir;

    public static ThemeDef Next(string? id)
    {
        var i = All.ToList().FindIndex(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        return All[(i + 1 + All.Count) % All.Count];
    }
}

public static class ThemeManager
{
    public static ThemeDef Current { get; private set; } = Themes.Noir;

    public static event Action? Changed;

    public static void Apply(string? id)
    {
        Current = Themes.Get(id);
        var res = Application.Current?.Resources;
        if (res is null) return;

        Set(res, "Bg", Current.Bg);
        Set(res, "BgSoft", Current.BgSoft);
        Set(res, "CardBg", Current.Card);
        Set(res, "Text", Current.Text);
        Set(res, "TextMuted", Current.TextMuted);
        Set(res, "TextDim", Current.TextDim);
        Set(res, "Accent", Current.Accent);
        Set(res, "AccentHover", Current.AccentHover);
        Set(res, "AccentPressed", Current.AccentPressed);
        Set(res, "OnAccent", Current.OnAccent);
        Set(res, "Stroke", Current.Stroke);
        Set(res, "StrokeStrong", Current.StrokeStrong);
        Set(res, "CursorRing", Current.Text);
        Set(res, "ScrollThumb", Current.Light ? "#33000000" : "#40FFFFFF");

        var hover = Current.Light ? "#0D000000" : "#12FFFFFF";
        var pressed = Current.Light ? "#1A000000" : "#1CFFFFFF";
        var cursor = Current.Light ? "#0F000000" : "#14FFFFFF";
        res["Hover"] = Brush(hover);
        res["Pressed"] = Brush(pressed);
        res["CursorBg"] = Brush(cursor);
        res["ArtTint"] = Current.Light ? Parse("#FFE8E3DA") : Parse("#FF2B2B31");
        res["ArtShade"] = Current.Light ? Parse("#FFF8F5EF") : Parse("#FF0B0B0E");

        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        grad.GradientStops.Add(new GradientStop(Parse(Current.BgTop), 0));
        grad.GradientStops.Add(new GradientStop(Parse(Current.Bg), 1));
        grad.Freeze();
        res["BgGradient"] = grad;

        var spark = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        var tint = Parse(Current.Text);
        spark.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, tint.R, tint.G, tint.B), 0));
        spark.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1));
        spark.Freeze();
        res["SparkFill"] = spark;

        Changed?.Invoke();
    }

    private static void Set(ResourceDictionary res, string key, string hex) => res[key] = Brush(hex);

    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush(Parse(hex));
        b.Freeze();
        return b;
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string Argb(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
