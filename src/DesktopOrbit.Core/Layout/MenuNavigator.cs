using DesktopOrbit.Core.Models;

namespace DesktopOrbit.Core.Layout;

public sealed record NavigationLevel(string Title, string? Path, IReadOnlyList<MenuItem> Items);

public sealed class MenuNavigator
{
    private readonly List<NavigationLevel> _levels = new();

    public MenuNavigator()
    {
        _levels.Add(new NavigationLevel("桌面", null, Array.Empty<MenuItem>()));
    }

    public int Depth => _levels.Count;
    public NavigationLevel Current => _levels[^1];
    public bool CanGoBack => _levels.Count > 1;

    public IReadOnlyList<string> Breadcrumbs => _levels.Select(level => level.Title).ToArray();

    public void Reset(NavigationLevel level)
    {
        _levels.Clear();
        _levels.Add(level);
    }

    public void Push(NavigationLevel level)
    {
        _levels.Add(level);
    }

    public bool Back()
    {
        if (_levels.Count <= 1) return false;
        _levels.RemoveAt(_levels.Count - 1);
        return true;
    }
}
