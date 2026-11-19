using DesktopOrbit.Core.Layout;

namespace DesktopOrbit.Tests;

public class RingGeometryTests
{
    private static RingGeometry Make(int sectors, double gap = 0) => new(200, 72, sectors, gap);

    [Theory]
    [InlineData(0, -1, 0)]   // up
    [InlineData(1, 0, 1)]    // right
    [InlineData(0, 1, 2)]    // down
    [InlineData(-1, 0, 3)]   // left
    public void HitTest_MapsCardinalDirections(double x, double y, int expected)
    {
        RingHit hit = Make(4).HitTest(x * 120, y * 120);
        Assert.True(hit.IsSector);
        Assert.Equal(expected, hit.Index);
    }

    [Fact]
    public void HitTest_OutsideRadius_ReturnsOutside()
    {
        RingHit hit = Make(6).HitTest(0, 300);
        Assert.Equal(RingHitArea.Outside, hit.Area);
    }

    [Fact]
    public void HitTest_InsideHub_ReturnsHub()
    {
        RingHit hit = Make(6).HitTest(0, 10);
        Assert.Equal(RingHitArea.Hub, hit.Area);
    }

    [Fact]
    public void HitTest_GapBetweenSectors_IsGap()
    {
        RingGeometry geometry = Make(4, 20);
        double diagonal = 100 / Math.Sqrt(2);
        RingHit hit = geometry.HitTest(diagonal, -diagonal);
        Assert.Equal(RingHitArea.Gap, hit.Area);
    }

    [Fact]
    public void SectorCenter_FirstSector_PointsUp()
    {
        RingGeometry geometry = Make(4);
        Assert.Equal(270, geometry.SectorCenterDegrees(0), 3);
    }

    [Theory]
    [InlineData(120, 0)]
    [InlineData(0, -150)]
    [InlineData(-90, 90)]
    public void SingleSector_AcceptsAnyAngle(double x, double y)
    {
        RingHit hit = Make(1).HitTest(x, y);
        Assert.True(hit.IsSector);
        Assert.Equal(0, hit.Index);
    }

    [Fact]
    public void ZeroSectors_NeverReturnsSector()
    {
        RingHit hit = Make(0).HitTest(0, 120);
        Assert.NotEqual(RingHitArea.Sector, hit.Area);
    }

    [Fact]
    public void SectorAngles_RespectGap()
    {
        RingGeometry geometry = Make(4, 10);
        (double start, double end) = geometry.SectorAnglesDegrees(0);
        Assert.Equal(80, end - start, 3);
    }
}
