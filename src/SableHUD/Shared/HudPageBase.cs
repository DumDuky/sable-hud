using System.Windows;
using System.Windows.Controls;
using SableHUD.Controls;
using SableHUD.Input;

namespace SableHUD.Shared;

public interface IHudPage
{
    string TabId { get; }
    void Activate();
    void Deactivate();
    void Refresh();
    bool Navigate(NavDir dir);
    void Confirm();
    bool Secondary();
    bool Adjust(int direction);
    bool Back();
}

/// <summary>
/// Shared controller-cursor behaviour. Pages keep a list of buttons in reading
/// order; D-pad up/down walks it, A clicks the highlighted button.
/// </summary>
public class HudPageBase : UserControl, IHudPage
{
    protected readonly List<HudButton> Nav = new();
    private readonly HashSet<HudButton> _hooked = new();
    protected int CursorIndex = -1;

    public virtual string TabId => "";

    public virtual void Activate()
    {
        if (Nav.Count > 0)
            SetCursorIndex(CursorIndex < 0 ? 0 : Math.Min(CursorIndex, Nav.Count - 1));
    }

    public virtual void Deactivate()
    {
    }

    public virtual void Refresh()
    {
    }

    public virtual bool Navigate(NavDir dir)
    {
        switch (dir)
        {
            case NavDir.Up:
            case NavDir.Left:
                MoveCursor(-1);
                return true;
            case NavDir.Down:
            case NavDir.Right:
                MoveCursor(1);
                return true;
        }
        return false;
    }

    public virtual void Confirm()
    {
        Current?.Invoke();
    }

    public virtual bool Secondary() => false;

    public virtual bool Adjust(int direction) => false;

    public virtual bool Back() => false;

    public virtual bool KeepVisible => false;

    protected HudButton? Current =>
        CursorIndex >= 0 && CursorIndex < Nav.Count ? Nav[CursorIndex] : null;

    protected void Register(HudButton button)
    {
        if (!_hooked.Contains(button))
        {
            _hooked.Add(button);
            button.MouseEnter += (_, _) => SetCursor(button);
            button.Click += (_, _) => SetCursor(button);
        }
        if (!Nav.Contains(button)) Nav.Add(button);
    }

    protected void ClearNav()
    {
        foreach (var b in Nav) b.IsCursor = false;
        Nav.Clear();
        CursorIndex = -1;
    }

    protected void SetCursor(HudButton button)
    {
        var index = Nav.IndexOf(button);
        if (index < 0) return;
        SetCursorIndex(index);
    }

    protected void SetCursorIndex(int index)
    {
        if (Nav.Count == 0)
        {
            CursorIndex = -1;
            return;
        }
        index = Math.Clamp(index, 0, Nav.Count - 1);
        for (var i = 0; i < Nav.Count; i++) Nav[i].IsCursor = i == index;
        CursorIndex = index;
    }

    protected void MoveCursor(int delta)
    {
        if (Nav.Count == 0) return;
        var index = CursorIndex < 0 ? 0 : (CursorIndex + delta + Nav.Count) % Nav.Count;
        SetCursorIndex(index);
    }

    protected static void SetGeometry(HudButton button, string key) => button.Icon = Ui.Geo(key);

    protected static void SetIcon(System.Windows.Shapes.Path path, string key) => path.Data = Ui.Geo(key);
}
