namespace DesktopOrbit.Core.Layout;

public enum RingHitArea
{
    Hub,
    Sector,
    Gap,
    Outside
}

public readonly record struct RingHit(RingHitArea Area, int Index)
{
    public static readonly RingHit None = new(RingHitArea.Outside, -1);
    public bool IsSector => Area == RingHitArea.Sector;
    public bool IsHub => Area == RingHitArea.Hub;
}

public sealed class RingGeometry
{
    public RingGeometry(double outerRadius, double hubRadius, int sectorCount, double gapDegrees, double startAngleDegrees = -90)
    {
        OuterRadius = Math.Max(1, outerRadius);
        SectorCount = Math.Max(0, sectorCount);
        HubRadius = Math.Clamp(hubRadius, 0, OuterRadius - 1);
        GapDegrees = Math.Max(0, gapDegrees);
        StartAngleDegrees = startAngleDegrees;
    }

    public double OuterRadius { get; }
    public double HubRadius { get; }
    public int SectorCount { get; }
    public double GapDegrees { get; }
    public double StartAngleDegrees { get; }

    public double StepDegrees => SectorCount > 0 ? 360.0 / SectorCount : 0;

    public double EffectiveGapDegrees => SectorCount <= 1 ? 0 : Math.Min(GapDegrees, StepDegrees * 0.8);

    public double RotationDegrees => StartAngleDegrees - StepDegrees / 2;

    public static double Normalize(double degrees)
    {
        double value = degrees % 360.0;
        return value < 0 ? value + 360.0 : value;
    }

    public RingHit HitTest(double x, double y)
    {
        double distance = Math.Sqrt(x * x + y * y);
        if (distance > OuterRadius) return RingHit.None;
        if (distance < HubRadius) return new RingHit(RingHitArea.Hub, -1);
        if (SectorCount == 0) return RingHit.None;

        double angle = Math.Atan2(y, x) * 180.0 / Math.PI;
        double relative = Normalize(angle - RotationDegrees);
        int index = (int)(relative / StepDegrees);
        if (index >= SectorCount) index = SectorCount - 1;
        if (index < 0) index = 0;

        double within = relative - index * StepDegrees;
        double half = EffectiveGapDegrees / 2;
        if (half > 0 && (within < half || within > StepDegrees - half))
        {
            return new RingHit(RingHitArea.Gap, index);
        }

        return new RingHit(RingHitArea.Sector, index);
    }

    public double SectorCenterDegrees(int index)
    {
        if (SectorCount == 0) return StartAngleDegrees;
        return Normalize(RotationDegrees + (index + 0.5) * StepDegrees);
    }

    public double SectorCenterRadians(int index) => SectorCenterDegrees(index) * Math.PI / 180.0;

    public (double Start, double End) SectorAnglesDegrees(int index)
    {
        double half = EffectiveGapDegrees / 2;
        double start = RotationDegrees + index * StepDegrees + half;
        double end = RotationDegrees + (index + 1) * StepDegrees - half;
        return (start, end);
    }

    public double AngleToRadius(int index, double radius) => SectorCenterRadians(index) * radius;
}
