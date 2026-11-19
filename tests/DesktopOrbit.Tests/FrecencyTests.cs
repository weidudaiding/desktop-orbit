using DesktopOrbit.Core.Config;
using DesktopOrbit.Core.Recent;

namespace DesktopOrbit.Tests;

public class FrecencyTests
{
    [Fact]
    public void ZeroUses_ScoresZero()
    {
        Assert.Equal(0, Frecency.Score(0, DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void RecentBeatsOld()
    {
        DateTime now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
        double recent = Frecency.Score(3, now.AddDays(-1), now);
        double old = Frecency.Score(3, now.AddDays(-60), now);
        Assert.True(recent > old);
    }

    [Fact]
    public void HalfLife_HalvesScore()
    {
        DateTime now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
        double fresh = Frecency.Score(10, now, now);
        double half = Frecency.Score(10, now.AddDays(-14), now);
        Assert.Equal(fresh / 2, half, 6);
    }
}

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 0x03u, 0x20u)]
    [InlineData("Alt+F5", 0x01u, 0x74u)]
    [InlineData("Win+Shift+D", 0x0Cu, 0x44u)]
    public void ParsesChord(string chord, uint modifiers, uint virtualKey)
    {
        Assert.True(DesktopOrbit.Shell.Platform.HotkeyParser.TryParse(chord, out uint parsedModifiers, out uint parsedKey));
        Assert.Equal(modifiers, parsedModifiers);
        Assert.Equal(virtualKey, parsedKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nope")]
    public void RejectsInvalid(string chord)
    {
        Assert.False(DesktopOrbit.Shell.Platform.HotkeyParser.TryParse(chord, out _, out _));
    }
}
