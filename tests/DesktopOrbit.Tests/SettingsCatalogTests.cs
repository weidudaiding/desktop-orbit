using System;
using System.Collections.Generic;
using System.Linq;
using DesktopOrbit.App.Settings;
using DesktopOrbit.Core.Config;

namespace DesktopOrbit.Tests;

public class SettingsCatalogTests
{
    private static AppConfig NewConfig() => AppConfig.CreateDefault();

    private static IEnumerable<SettingItem> AllItems(AppConfig config) =>
        SettingsCatalog.Build(config).SelectMany(group => group.Items);

    [Fact]
    public void AllGroups_HaveItems()
    {
        List<SettingGroup> groups = SettingsCatalog.Build(NewConfig());
        Assert.NotEmpty(groups);
        Assert.All(groups, group => Assert.NotEmpty(group.Items));
    }

    [Fact]
    public void EveryItem_ReadsBackWhatWasWritten()
    {
        AppConfig config = NewConfig();

        foreach (SettingItem item in AllItems(config))
        {
            object? original = item.Get();
            Assert.NotNull(original);

            item.Set(original);
            Assert.Equal(original, item.Get());
        }
    }

    [Fact]
    public void TogglingEverySwitch_ChangesConfig()
    {
        AppConfig config = NewConfig();
        var toggles = AllItems(config).Where(item => item.Kind == SettingKind.Toggle).ToList();

        Assert.NotEmpty(toggles);

        foreach (SettingItem item in toggles)
        {
            bool before = item.Get() is true;
            item.Set(!before);
            Assert.NotEqual(before, item.Get() is true);
        }
    }

    [Fact]
    public void NumericItems_RespectDeclaredBounds()
    {
        AppConfig config = NewConfig();

        foreach (SettingItem item in AllItems(config).Where(item => item.Kind == SettingKind.Number))
        {
            Assert.True(item.Max > item.Min, $"{item.Label} has an empty range");

            item.Set(item.Min);
            double atMin = Convert.ToDouble(item.Get());
            Assert.Equal(item.Min, atMin, 3);

            item.Set(item.Max);
            double atMax = Convert.ToDouble(item.Get());
            Assert.Equal(item.Max, atMax, 3);
        }
    }

    [Fact]
    public void DismissOptions_AreExposedAndWritable()
    {
        AppConfig config = NewConfig();
        List<SettingItem> items = AllItems(config).ToList();

        SettingItem? clickOutside = items.FirstOrDefault(item => item.Label == "点击外部关闭");
        SettingItem? pointerLeave = items.FirstOrDefault(item => item.Label == "移出关闭");
        SettingItem? delay = items.FirstOrDefault(item => item.Label == "移出死区时间");
        SettingItem? buffer = items.FirstOrDefault(item => item.Label == "边缘缓冲");

        Assert.NotNull(clickOutside);
        Assert.NotNull(pointerLeave);
        Assert.NotNull(delay);
        Assert.NotNull(buffer);

        pointerLeave!.Set(true);
        Assert.True(config.Menu.Dismiss.CloseOnPointerLeave);

        delay!.Set(1200.0);
        Assert.Equal(1200, config.Menu.Dismiss.PointerLeaveDelayMs);

        buffer!.Set(40.0);
        Assert.Equal(40, config.Menu.Dismiss.PointerLeaveBufferDip);

        clickOutside!.Set(false);
        Assert.False(config.Menu.Dismiss.CloseOnClickOutside);
    }

    [Fact]
    public void DrillOptions_AreExposedAndWritable()
    {
        AppConfig config = NewConfig();
        List<SettingItem> items = AllItems(config).ToList();

        SettingItem? onHover = items.FirstOrDefault(item => item.Label == "悬停进入");
        SettingItem? onClick = items.FirstOrDefault(item => item.Label == "单击进入");
        SettingItem? wait = items.FirstOrDefault(item => item.Label == "悬停等待时间");

        Assert.NotNull(onHover);
        Assert.NotNull(onClick);
        Assert.NotNull(wait);

        onHover!.Set(false);
        Assert.False(config.Menu.Drill.OnHover);

        onClick!.Set(true);
        Assert.True(config.Menu.Drill.OnClick);

        wait!.Set(800.0);
        Assert.Equal(800, config.Menu.HoverDelayMs);
    }

    [Fact]
    public void NumericIntItems_StoreWholeNumbers()
    {
        AppConfig config = NewConfig();

        SettingItem hold = AllItems(config).First(item => item.Label == "长按时长");
        hold.Set(487.4);
        Assert.Equal(487, config.Trigger.LongPress.HoldMs);

        SettingItem sectors = AllItems(config).First(item => item.Label == "单环扇区上限");
        sectors.Set(9.6);
        Assert.Equal(10, config.Menu.MaxSectors);
    }

    [Fact]
    public void Format_RendersBooleansAndDecimals()
    {
        AppConfig config = NewConfig();
        List<SettingItem> items = AllItems(config).ToList();

        SettingItem toggle = items.First(item => item.Kind == SettingKind.Toggle);
        Assert.Contains(SettingsCatalog.Format(toggle, true), new[] { "开", "关" });

        SettingItem ratio = items.First(item => item.Label == "中心圆占比");
        Assert.Equal("0.36", SettingsCatalog.Format(ratio, 0.36));
    }
}
