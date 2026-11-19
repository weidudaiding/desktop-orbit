using System.Text.Json;
using DesktopOrbit.Core.Config;

namespace DesktopOrbit.Tests;

public class ConfigTests
{
    [Fact]
    public void DefaultConfig_HasDesktopAndRecent()
    {
        AppConfig config = AppConfig.CreateDefault();
        Assert.Contains(config.Sources, source => source.Type == "desktop");
        Assert.Contains(config.Sources, source => source.Type == "recent");
        Assert.NotEmpty(config.Layout.Sectors);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        AppConfig config = AppConfig.CreateDefault();
        config.Menu.OuterRadiusDip = 321;
        config.Appearance.AccentColor = "#123456";
        config.Trigger.LongPress.HoldMs = 512;

        string json = JsonSerializer.Serialize(config, ConfigStore.JsonOptions);
        AppConfig? restored = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(321, restored!.Menu.OuterRadiusDip);
        Assert.Equal("#123456", restored.Appearance.AccentColor);
        Assert.Equal(512, restored.Trigger.LongPress.HoldMs);
    }

    [Fact]
    public void Deserializer_IgnoresUnknownProperties()
    {
        const string json = """
        { "version": 1, "unknown": true, "menu": { "outerRadiusDip": 260 } }
        """;
        AppConfig? config = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions);
        Assert.NotNull(config);
        Assert.Equal(260, config!.Menu.OuterRadiusDip);
    }

    [Fact]
    public void SourcePath_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("DESKTOPORBIT_TEST_DIR", @"D:\Orbit");
        var source = new SourceConfig { Type = "folder", Path = @"%DESKTOPORBIT_TEST_DIR%\sub" };
        Assert.Equal(@"D:\Orbit\sub", source.ExpandedPath);
    }

    [Fact]
    public void LoadOrCreate_CreatesDefaultFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DesktopOrbitTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "config.json");
        try
        {
            AppConfig config = ConfigStore.LoadOrCreate(path, out bool created);
            Assert.True(created);
            Assert.True(File.Exists(path));
            Assert.NotEmpty(config.Sources);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
