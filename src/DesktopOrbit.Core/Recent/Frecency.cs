namespace DesktopOrbit.Core.Recent;

public static class Frecency
{
    public const double DefaultHalfLifeDays = 14.0;

    public static double Score(int useCount, DateTime lastUsedUtc, DateTime nowUtc, double halfLifeDays = DefaultHalfLifeDays)
    {
        if (useCount <= 0) return 0;
        if (halfLifeDays <= 0) return useCount;

        double days = (nowUtc - lastUsedUtc).TotalDays;
        if (days < 0) days = 0;
        return useCount * Math.Pow(0.5, days / halfLifeDays);
    }
}
