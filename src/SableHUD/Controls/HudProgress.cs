using System.Windows;
using System.Windows.Media;

namespace SableHUD.Controls;

/// <summary>A rounded progress rail. Used for playback and volume.</summary>
public class HudProgress : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(HudProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(HudProgress),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(HudProgress),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumWidthProperty = DependencyProperty.Register(
        nameof(MinimumWidth), typeof(double), typeof(HudProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double MinimumWidth
    {
        get => (double)GetValue(MinimumWidthProperty);
        set => SetValue(MinimumWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinimumWidth : availableSize.Width;
        return new Size(Math.Max(width, MinimumWidth), ActualHeight > 0 ? ActualHeight : 3);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var radius = h / 2.0;

        if (Track is not null)
            dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), radius, radius);

        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0 || Fill is null) return;
        var fw = Math.Max(h, w * v);
        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Min(fw, w), h), radius, radius);
    }
}
