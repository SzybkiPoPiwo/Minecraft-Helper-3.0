using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MinecraftHelper.Services;

internal enum AutoGuardDecision
{
    Skip,
    WaitAfterThrow,
    Tap
}

// Pure decision rules for the PVP extras. They contain no Win32 calls so the
// test suite can drive every branch without touching the desktop.
internal static class PvpInputPolicies
{
    public const int JitterMaxDispersion = 20;
    public const int LagFilterMinPayloadBytes = 35;

    // After each AUTO LPM click the crosshair is nudged by up to ±dispersion
    // pixels on both axes, with the given chance (0–100%).
    public static bool TryRollJitter(Random random, int chancePercent, int dispersion, out int deltaX, out int deltaY)
    {
        deltaX = 0;
        deltaY = 0;
        chancePercent = Math.Clamp(chancePercent, 0, 100);
        dispersion = Math.Clamp(dispersion, 0, JitterMaxDispersion);
        if (chancePercent == 0 || dispersion == 0 || random.Next(100) >= chancePercent)
            return false;

        deltaX = random.Next(-dispersion, dispersion + 1);
        deltaY = random.Next(-dispersion, dispersion + 1);
        return deltaX != 0 || deltaY != 0;
    }

    // Auto Guard taps PPM only while AUTO LPM is really clicking. A recent
    // pearl/snowball throw postpones it, and a PPM held by the player always wins.
    public static AutoGuardDecision DecideAutoGuard(
        bool leftClickingNow,
        bool userHoldsRightButton,
        long nowTimestamp,
        long throwPauseUntilTimestamp)
    {
        if (!leftClickingNow)
            return AutoGuardDecision.Skip;
        if (nowTimestamp < throwPauseUntilTimestamp)
            return AutoGuardDecision.WaitAfterThrow;
        if (userHoldsRightButton)
            return AutoGuardDecision.Skip;
        return AutoGuardDecision.Tap;
    }

    // WinDivert filter that holds back the game's outgoing data packets while
    // letting small keep-alive/ACK segments through, so the connection survives.
    public static string BuildLagSwitchFilter(IEnumerable<int> remotePorts)
    {
        var builder = new StringBuilder();
        builder.Append("outbound and tcp.PayloadLength > ")
            .Append(LagFilterMinPayloadBytes.ToString(CultureInfo.InvariantCulture))
            .Append(" and (");
        foreach (int port in remotePorts.Where(port => port is > 0 and <= 65535).Distinct().OrderBy(port => port))
            builder.Append("tcp.DstPort == ").Append(port.ToString(CultureInfo.InvariantCulture)).Append(" or ");
        builder.Append("false)");
        return builder.ToString();
    }
}
