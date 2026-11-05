using System;

namespace MinecraftHelper.Services;

// The miner opens the inventory only for a moment during Auto EQ, so a live
// "inventory" frame almost always shows a closed GUI. Each miner process keeps the
// last inventory it saw and serves it, with its age, while the GUI is closed.
internal sealed class MinerInventorySnapshot
{
    private readonly object _gate = new();
    private MinerLiveFrame? _latest;

    public static MinerInventorySnapshot Current { get; } = new();

    public void Remember(MinerLiveFrame frame)
    {
        lock (_gate)
            _latest = frame;
    }

    public bool TryGetFallback(DateTimeOffset now, out MinerLiveFrame? frame)
    {
        MinerLiveFrame? latest;
        lock (_gate)
            latest = _latest;

        if (latest == null)
        {
            frame = null;
            return false;
        }

        frame = latest with
        {
            Note = $"Ekwipunek jest zamknięty — to ostatni skan EQ ({DescribeAge(now - latest.CapturedAt)})."
        };
        return true;
    }

    internal static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
            return "przed chwilą";

        int totalMinutes = (int)age.TotalMinutes;
        if (totalMinutes < 60)
            return $"{totalMinutes} min temu";

        return $"{totalMinutes / 60} h {totalMinutes % 60} min temu";
    }
}
