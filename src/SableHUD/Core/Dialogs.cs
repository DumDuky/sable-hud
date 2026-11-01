using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace SableHUD.Core;

/// <summary>Small native dialogs owned by the WPF window so a topmost HUD cannot hide them.</summary>
public static class Dialogs
{
    private sealed class Owner : WinForms.IWin32Window
    {
        public Owner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    public static string? PickFolder(Window? owner, string? start, string description)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = start ?? "",
        };

        var result = owner is null
            ? dialog.ShowDialog()
            : dialog.ShowDialog(new Owner(new WindowInteropHelper(owner).Handle));

        return result == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public static string? PickExe(Window owner, string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "LiMusic (limusic.exe)|limusic.exe|Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
