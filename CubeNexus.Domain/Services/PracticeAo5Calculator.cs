using CubeNexus.Domain.Entities;

namespace CubeNexus.Domain.Services;

/// <summary>Tính Ao5 theo chuẩn WCA dùng chung cho Practice và Elo seeding.</summary>
public static class PracticeAo5Calculator
{
    public const int DnfSentinel = int.MaxValue;

    public static int GetDisplayTimeMs(int rawTimeMs, bool isDnf, PenaltyType? penalty)
    {
        if (isDnf)
            return DnfSentinel;

        return rawTimeMs + (penalty?.TimeAdditionMs ?? 0);
    }

    public static int GetDisplayTimeMs(PracticeSolve solve)
        => GetDisplayTimeMs(solve.TimeMs, solve.IsDnf, solve.PenaltyType);

    /// <summary>Thời gian hiển thị cho UI. -1 nếu DNF.</summary>
    public static int ToUiDisplayTimeMs(int displayTimeMs)
        => displayTimeMs == DnfSentinel ? -1 : displayTimeMs;

    public static int? CalculateAo5(IReadOnlyList<int> displayTimesMs)
    {
        if (displayTimesMs.Count != 5)
            return null;

        var times = displayTimesMs.ToList();
        var dnfCount = times.Count(t => t == DnfSentinel);
        if (dnfCount >= 2)
            return null;

        times.Sort();
        return (int)times.Skip(1).Take(3).Average();
    }

    public static int? CalculateAo5(IReadOnlyList<PracticeSolve> window)
    {
        if (window.Count != 5)
            return null;

        return CalculateAo5(window.Select(GetDisplayTimeMs).ToList());
    }
}
