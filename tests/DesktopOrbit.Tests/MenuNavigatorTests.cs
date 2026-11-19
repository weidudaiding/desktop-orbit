using DesktopOrbit.Core.Layout;
using DesktopOrbit.Core.Models;

namespace DesktopOrbit.Tests;

public class MenuNavigatorTests
{
    private static MenuItem Folder(string name) => new() { Path = name, Label = name, Kind = ItemKind.Folder };

    [Fact]
    public void NewNavigator_HasOnlyRoot()
    {
        var navigator = new MenuNavigator();
        Assert.Equal(1, navigator.Depth);
        Assert.False(navigator.CanGoBack);
    }

    [Fact]
    public void Push_ThenBack_RestoresParent()
    {
        var navigator = new MenuNavigator();
        navigator.Reset(new NavigationLevel("桌面", null, new[] { Folder("A") }));
        navigator.Push(new NavigationLevel("A", "C:\\A", Array.Empty<MenuItem>()));

        Assert.True(navigator.CanGoBack);
        Assert.Equal("A", navigator.Current.Title);
        Assert.Equal(new[] { "桌面", "A" }, navigator.Breadcrumbs);

        Assert.True(navigator.Back());
        Assert.Equal("桌面", navigator.Current.Title);
        Assert.False(navigator.Back());
    }

    [Fact]
    public void Reset_ClearsStack()
    {
        var navigator = new MenuNavigator();
        navigator.Push(new NavigationLevel("A", "C:\\A", Array.Empty<MenuItem>()));
        navigator.Reset(new NavigationLevel("新", null, Array.Empty<MenuItem>()));
        Assert.Equal(1, navigator.Depth);
        Assert.Equal("新", navigator.Current.Title);
    }
}
