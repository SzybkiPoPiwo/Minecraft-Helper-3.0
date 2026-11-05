using System;
using System.Drawing;

namespace MinecraftHelper.Services;

internal readonly record struct BackgroundCursorSnapshot(
    bool Initialized, Point Position, int BaselineTimestamp, int GuardUntil, int LastCorrectedTimestamp);

// Hardware packets, rather than arbitrary GetCursorPos samples, own the shared
// baseline. A GUI transition only temporarily authorizes correcting other moves.
internal static class BackgroundCursorPolicy
{
    public static BackgroundCursorSnapshot Arm(BackgroundCursorSnapshot snapshot, Point fallback,
        int timestamp, int durationMs = 800)
    {
        bool preserve = snapshot.Initialized && IsGuarded(snapshot, timestamp);
        return new BackgroundCursorSnapshot(true, preserve ? snapshot.Position : fallback,
            preserve ? snapshot.BaselineTimestamp : timestamp,
            unchecked(timestamp + Math.Max(0, durationMs)),
            preserve ? snapshot.LastCorrectedTimestamp : unchecked(timestamp - 1));
    }

    public static BackgroundCursorSnapshot ObserveMouse(BackgroundCursorSnapshot snapshot, Point packetPosition,
        int rawX, int rawY, bool absolute, int timestamp, bool userSelectedGame = false)
    {
        if (!snapshot.Initialized)
            return new BackgroundCursorSnapshot(true, packetPosition, timestamp, timestamp, unchecked(timestamp - 1));

        if (unchecked(timestamp - snapshot.BaselineTimestamp) < 0)
            return snapshot;

        if (userSelectedGame)
            return snapshot with { Position = packetPosition, BaselineTimestamp = timestamp, GuardUntil = timestamp };
        if (timestamp == snapshot.LastCorrectedTimestamp)
            return snapshot;

        double x = (double)packetPosition.X - snapshot.Position.X;
        double y = (double)packetPosition.Y - snapshot.Position.Y;
        double distance = Math.Sqrt(x * x + y * y);
        double deviceDistance = Math.Sqrt((double)rawX * rawX + (double)rawY * rawY);
        bool mixedWarp = !absolute && IsGuarded(snapshot, timestamp)
            && distance > Math.Max(64, deviceDistance * 8);
        Point position = mixedWarp
            ? new Point(AddClamped(snapshot.Position.X, rawX), AddClamped(snapshot.Position.Y, rawY))
            : packetPosition;

        return snapshot with
        {
            Position = position,
            BaselineTimestamp = timestamp,
            LastCorrectedTimestamp = mixedWarp ? timestamp : snapshot.LastCorrectedTimestamp
        };
    }

    public static Point? RestoreTarget(BackgroundCursorSnapshot snapshot, Point current, Point ownGameCenter,
        bool userSelectedGame, int timestamp)
    {
        if (userSelectedGame || !snapshot.Initialized || current == snapshot.Position)
            return null;
        bool atCenter = IsNear(current, ownGameCenter);
        if (IsGuarded(snapshot, timestamp) || atCenter && !IsNear(snapshot.Position, ownGameCenter))
            return snapshot.Position;
        return null;
    }

    private static bool IsGuarded(BackgroundCursorSnapshot snapshot, int timestamp) =>
        unchecked(snapshot.GuardUntil - timestamp) > 0;

    private static bool IsNear(Point point, Point other) =>
        Math.Abs((long)point.X - other.X) <= 2 && Math.Abs((long)point.Y - other.Y) <= 2;

    private static int AddClamped(int value, int delta) =>
        (int)Math.Clamp((long)value + delta, int.MinValue, int.MaxValue);
}
