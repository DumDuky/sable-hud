using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SableHUD.Controls;

/// <summary>
/// The single button primitive used across the HUD. It has no keyboard focus of its own:
/// the controller navigation layer moves <see cref="IsCursor"/>, which renders as a soft
/// outer ring so the active item is readable from a couch.
/// </summary>
public class HudButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(HudButton));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(HudButton), new PropertyMetadata(16.0));

    public static readonly DependencyProperty IconMarginProperty = DependencyProperty.Register(
        nameof(IconMargin), typeof(Thickness), typeof(HudButton), new PropertyMetadata(new Thickness(0, 0, 8, 0)));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(HudButton), new PropertyMetadata(new CornerRadius(14)));

    public static readonly DependencyProperty IsCursorProperty = DependencyProperty.Register(
        nameof(IsCursor), typeof(bool), typeof(HudButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(HudButton), new PropertyMetadata(false));

    public static readonly DependencyProperty SolidProperty = DependencyProperty.Register(
        nameof(Solid), typeof(bool), typeof(HudButton), new PropertyMetadata(false));

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsCursor
    {
        get => (bool)GetValue(IsCursorProperty);
        set => SetValue(IsCursorProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool Solid
    {
        get => (bool)GetValue(SolidProperty);
        set => SetValue(SolidProperty, value);
    }

    public HudButton()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public void Invoke()
    {
        if (!IsEnabled) return;
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }
}
