using System;
using System.Windows;

namespace SableHUD;

/// <summary>Resolves pack URIs for the generated assets.</summary>
public static class Assets
{
    public static Uri IconUri => new("pack://application:,,,/Assets/app.ico");
    public static Uri MarkUri => new("pack://application:,,,/Assets/mark.png");
}
