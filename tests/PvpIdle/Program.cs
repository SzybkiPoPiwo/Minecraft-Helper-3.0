using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MinecraftHelper.Models;
using MinecraftHelper.Services;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        try
        {
            MinecraftHelper.MainWindow.VerifyLongIdle();
            MinecraftHelper.MainWindow.VerifyBackgroundIdle();
            MinecraftHelper.MainWindow.VerifyActivationAndReleases();
            MinecraftHelper.MainWindow.VerifyEveryWorkflow();
            MinecraftHelper.MainWindow.VerifyHiddenAndVisibleUi();
            VerifyPixelClassifier();
            VerifyProductionIntegration();
            Console.WriteLine($"PASS: {_checks} checks; one simulated hour of idle, bind activation, GUI guards and cursor pixels. No windows, native input, hooks, screen capture or user data.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + description);
        _checks++;
    }

    private static void VerifyPixelClassifier()
    {
        foreach (int size in new[] { 1, 9, 32, 64, 128 })
        foreach (Drawing.Color background in new[] { Drawing.Color.Black, Drawing.Color.White })
        {
            using var bitmap = new Drawing.Bitmap(size, size, DrawingImaging.PixelFormat.Format32bppArgb);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap)) graphics.Clear(background);
            Check(!CursorVisibilityProbe.BitmapChangesBackground(bitmap, background), "unchanged background is blank");
            var different = background.ToArgb() == Drawing.Color.Black.ToArgb() ? Drawing.Color.White : Drawing.Color.Black;
            foreach (Drawing.Point pixel in new[] { new Drawing.Point(0, 0), new Drawing.Point(size / 2, size / 2), new Drawing.Point(size - 1, size - 1) })
            {
                bitmap.SetPixel(pixel.X, pixel.Y, different);
                Check(CursorVisibilityProbe.BitmapChangesBackground(bitmap, background), "one visible pixel at any location is not blank");
                bitmap.SetPixel(pixel.X, pixel.Y, background);
                Check(!CursorVisibilityProbe.BitmapChangesBackground(bitmap, background), "pixel classifier releases its bitmap lock");
            }
            // Rendering code keeps its Graphics alive after releasing the HDC.
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                Check(!CursorVisibilityProbe.BitmapChangesBackground(bitmap, background), "classification is safe with a live Graphics object");
        }

        var random = new Random(20261023);
        using var sample = new Drawing.Bitmap(17, 19, DrawingImaging.PixelFormat.Format32bppArgb);
        for (int pass = 0; pass < 30; pass++)
        {
            for (int y = 0; y < sample.Height; y++)
            for (int x = 0; x < sample.Width; x++)
                sample.SetPixel(x, y, Drawing.Color.FromArgb(random.Next(256), random.Next(256), random.Next(256), random.Next(256)));
            foreach (Drawing.Color background in new[] { Drawing.Color.Black, Drawing.Color.White })
            {
                bool expected = false;
                for (int y = 0; y < sample.Height && !expected; y++)
                for (int x = 0; x < sample.Width; x++)
                    if (sample.GetPixel(x, y).ToArgb() != background.ToArgb()) { expected = true; break; }
                Check(CursorVisibilityProbe.BitmapChangesBackground(sample, background) == expected,
                    "bulk pixel read agrees with the old per-pixel classifier");
            }
        }
        foreach (MethodInfo method in typeof(CursorVisibilityProbe).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
        {
            var import = method.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>();
            if (import != null)
                Check(method.Name is "GetCursorInfo" or "GetSystemMetrics" or "DrawIconEx", "cursor probe only imports read/render APIs");
        }
    }

    private static void VerifyProductionIntegration()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MinecraftHelper.slnx"))) directory = directory.Parent;
        if (directory == null) throw new DirectoryNotFoundException("Repository root not found.");
        string source = File.ReadAllText(Path.Combine(directory.FullName, "MinecraftHelper.Pvp", "MainWindow.xaml.cs"));
        int idle = source.IndexOf("if (TryHandleIdleRuntimeTick(now, targetFocused))", StringComparison.Ordinal);
        int cursor = source.IndexOf("_cachedCursorVisible = IsInventoryCursorVisible();", StringComparison.Ordinal);
        Check(idle >= 0 && cursor > idle, "idle gate runs before any continuous cursor probe");
        string tick = source[source.IndexOf("private void RuntimeTimerTickCore()", StringComparison.Ordinal)..];
        int backgroundIdle = tick.IndexOf("if (TryHandleBackgroundIdleRuntimeTick(now))", StringComparison.Ordinal);
        Check(backgroundIdle >= 0 && backgroundIdle < tick.IndexOf("TryRestoreSavedTargetWindow(now);", StringComparison.Ordinal), "background idle skips process/window discovery");
        Check(backgroundIdle < tick.IndexOf("UpdateTargetGameFocus()", StringComparison.Ordinal)
            && backgroundIdle < tick.IndexOf("bool targetFocused = UpdateTargetGameFocus();", StringComparison.Ordinal), "background idle precedes all focus/handle probes");
        Check(tick.IndexOf("bool targetFocused = UpdateTargetGameFocus();", StringComparison.Ordinal)
            < tick.IndexOf("HandleClickerRuntime(blockedByCursor);", StringComparison.Ordinal), "wake-up still checks focus before clicker activation");
        // The target is the game process: a recreated (F11/fullscreen) or briefly hidden
        // window must neither drop the target nor stop clicks.
        string status = source[source.IndexOf("private void RefreshTargetStatus()", StringComparison.Ordinal)..];
        status = status[..status.IndexOf("private static string GetWindowTitle", StringComparison.Ordinal)];
        Check(!status.Contains("_targetGameWindowHandle = IntPtr.Zero", StringComparison.Ordinal)
            && !status.Contains("_targetGameProcessId = 0", StringComparison.Ordinal), "status refresh never clears the chosen game");
        Check(source.Contains("GameWindowFinder.GetWindowProcessId(foreground) != _targetGameProcessId", StringComparison.Ordinal)
            && source.Contains("GameWindowFinder.IsProcessRunning(_targetGameProcessId)", StringComparison.Ordinal)
            && !source.Contains("GetForegroundWindow() == _targetGameWindowHandle", StringComparison.Ordinal), "focus follows the game process, not one window handle");
        string scheduler = File.ReadAllText(Path.Combine(directory.FullName, "MinecraftHelper.Pvp", "Services", "LightweightClickScheduler.cs"));
        Check(scheduler.Contains("GameWindowFinder.IsProcessForeground(targetProcessId)", StringComparison.Ordinal)
            && !scheduler.Contains("GetForegroundWindow() == targetWindow", StringComparison.Ordinal), "scheduled clicks check the game process in the foreground");
        Check(source.Contains("(_isTargetFocused || _backgroundIdleRuntimeSuspended)", StringComparison.Ordinal), "unknown background focus does not slow bind polling");
        Check(source.Contains("CursorVisibilityProbe.IsGuiCursorVisible(failClosed: true)", StringComparison.Ordinal), "scheduled clicks still fail closed on cursor errors");
        Check(source.Contains("if (IsVisible && WindowState != WindowState.Minimized", StringComparison.Ordinal), "focus/status refresh avoids hidden WPF controls");
        Check(source.Contains("BindKeyCache.TryGetValue", StringComparison.Ordinal), "key names are cached rather than parsed on every poll");
        string probe = File.ReadAllText(Path.Combine(directory.FullName, "MinecraftHelper.Pvp", "Services", "CursorVisibilityProbe.cs"));
        Check(!probe.Contains("GetPixel(", StringComparison.Ordinal) && probe.Contains("LockBits(", StringComparison.Ordinal), "cursor analysis does not make per-pixel GDI calls");
    }
}

namespace MinecraftHelper
{
    // Runs the production idle gate without constructing a Window. The only
    // replacements are the input/output host and other workflows' state flags.
    public partial class MainWindow
    {
        private readonly PvpSettings _settings = new();
        private readonly Scheduler _autoClickScheduler = new();
        private readonly MouseTracker _physicalMouseTracker = new();
        private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);
        private readonly CheckBox ChkAutoLeftEnabled = new() { IsChecked = true };
        private readonly CheckBox ChkAutoRightEnabled = new() { IsChecked = true };
        private readonly CheckBox ChkAppleCutterEnabled = new() { IsChecked = false };
        private readonly CheckBox ChkAutoArmorEnabled = new() { IsChecked = true };
        private readonly CheckBox ChkBindyEnabled = new() { IsChecked = true };
        private readonly TextBox TxtAutoLeftKey = new() { Text = "R" };
        private readonly TextBox TxtAutoRightKey = new() { Text = "MouseX1" };
        private readonly TextBox TxtAppleCutterBind = new() { Text = "Insert" };
        private readonly TextBox TxtAutoArmorBind = new();
        private readonly TextBox TxtPvpAutoBlockBind = new() { Text = "MouseX2" };
        private readonly TextBox TxtPvpSnowballBind = new();
        private readonly TextBox TxtPvpPearlBind = new();
        private readonly TextBox TxtPvpRodBind = new();
        private bool _autoLeftRuntimeEnabled = false;
        private bool _autoRightRuntimeEnabled = false;
        private bool _autoArmorCalibrationPending = false;
        private bool _suppressBindToggleUntilRelease = false;
        private bool _cachedCursorVisible = true;
        private bool _physicalMouseTrackerWarningShown = false;
        private DateTime _nextPhysicalMouseTrackerRetryUtc;
        private DateTime _nextCursorProbeUtc = DateTime.MaxValue;
        private BindyStage _bindyStage;
        private bool IsAutoArmorRunning { get; set; }
        private bool IsAppleCutterRunning { get; set; }
        private bool IsPvpQuickActionRunning { get; set; }
        private bool IsVisible { get; set; }
        private WindowState WindowState { get; set; }
        private string _lastOverlaySignature = string.Empty;
        private int _syncs;
        private int _uiRefreshes;
        private int _overlayRefreshes;
        private bool _dabHeld;
        private int _dabWrites;
        private bool _autoLeftBindWasDown;
        private bool _isTargetFocused = false;
        private enum BindyStage { None, OpenChat }
        private sealed class Scheduler { public int Stops; public void Stop() => Stops++; }
        private sealed class MouseTracker
        {
            public bool IsMonitoring;
            public int Stops;
            public void Stop() { Stops++; IsMonitoring = false; }
        }

        // The PVP extras (Auto Guard, freeze, lag...) are idle in these scenarios.
        private bool HasActivePvpExtrasWork() => false;
        private bool IsPvpExtrasBindDown() => false;
        private bool IsConfiguredBindKeyDown(string key) => !string.IsNullOrWhiteSpace(key) && _down.Contains(key);
        private void SetAutoLeftDabHold(bool value) { if (_dabHeld != value) { _dabHeld = value; _dabWrites++; } }
        private void SynchronizeBindStatesWhileGameIsInactive() { _syncs++; _autoLeftBindWasDown = _down.Contains(TxtAutoLeftKey.Text); }
        private void RefreshClickerStateUi(DateTime _, bool blockedByCursor, bool targetFocused) => _uiRefreshes++;
        private void RefreshOverlayHud(DateTime _) { _overlayRefreshes++; _lastOverlaySignature = string.Empty; }

        public MainWindow()
        {
            _settings.BindyEntries.Add(new BindyEntry { Key = "OemPlus", Command = "/schowek" });
            _settings.BindyEntries.Add(new BindyEntry { Key = "F6", Command = "/repair" });
        }

        internal static void VerifyLongIdle()
        {
            var host = new MainWindow();
            DateTime now = DateTime.UtcNow;
            for (int tick = 0; tick < 240_000; tick++)
            {
                if (!host.TryHandleIdleRuntimeTick(now.AddMilliseconds(tick * 15L), targetFocused: true))
                    throw new InvalidOperationException("Idle gate stopped handling an idle tick.");
            }
            Program.Check(host._autoClickScheduler.Stops == 1, "one simulated hour stops the scheduler only on idle entry");
            Program.Check(host._physicalMouseTracker.Stops == 0 && host._dabWrites == 0, "idle does not register hooks or inject releases");
            Program.Check(host._uiRefreshes == 0 && host._overlayRefreshes == 0, "hidden idle does not update UI or HUD");
            Program.Check(host._syncs == 240_000, "key releases are still consumed throughout long idle");
            Program.Check(!host._cachedCursorVisible && host._nextCursorProbeUtc == DateTime.MinValue, "idle never reuses a pre-idle cursor observation");
            Program.Check(host._nextPhysicalMouseTrackerRetryUtc == DateTime.MinValue && !host._physicalMouseTrackerWarningShown, "idle clears tracker retry state");
        }

        internal static void VerifyActivationAndReleases()
        {
            foreach (string key in new[] { "R", "MouseX1", "MouseX2", "OemPlus", "F6" })
            {
                var host = new MainWindow();
                DateTime now = DateTime.UtcNow;
                Program.Check(host.TryHandleIdleRuntimeTick(now, true), "initial idle is handled");
                host._down.Add(key);
                Program.Check(!host.TryHandleIdleRuntimeTick(now.AddMilliseconds(15), true), "activation key wakes normal guarded path: " + key);
                Program.Check(!host._idleRuntimeSuspended && host._nextCursorProbeUtc == DateTime.MinValue, "activation requires fresh GUI/cursor guard");
                host._autoLeftBindWasDown = key == "R";
                host._down.Clear();
                Program.Check(host.TryHandleIdleRuntimeTick(now.AddMilliseconds(30), true), "release returns to idle");
                Program.Check(!host._autoLeftBindWasDown, "idle consumes activation release");
                host._down.Add(key);
                Program.Check(!host.TryHandleIdleRuntimeTick(now.AddMilliseconds(45), true), "same bind can activate again");
            }
            foreach (string key in new[] { "Insert", "UnrelatedKey" })
            {
                var host = new MainWindow();
                host._down.Add(key);
                Program.Check(host.TryHandleIdleRuntimeTick(DateTime.UtcNow, true), "disabled/unknown bind does not wake cursor probe");
            }
            var armor = new MainWindow();
            armor.TxtAutoArmorBind.Text = "F7";
            armor._down.Add("F7");
            Program.Check(!armor.TryHandleIdleRuntimeTick(DateTime.UtcNow, true), "configured armor bind wakes guarded activation");
            var invalidBindy = new MainWindow();
            invalidBindy._settings.BindyEntries[0].Enabled = false;
            invalidBindy._settings.BindyEntries[1].Command = string.Empty;
            invalidBindy._down.UnionWith(new[] { "OemPlus", "F6" });
            Program.Check(invalidBindy.TryHandleIdleRuntimeTick(DateTime.UtcNow, true), "disabled/empty-command BINDY does not wake cursor scanning");
        }

        internal static void VerifyBackgroundIdle()
        {
            var host = new MainWindow { _isTargetFocused = true };
            DateTime now = DateTime.UtcNow;
            for (int tick = 0; tick < 240_000; tick++)
            {
                if (!host.TryHandleBackgroundIdleRuntimeTick(now.AddMilliseconds(tick * 15L)))
                    throw new InvalidOperationException("Background idle unexpectedly requested a focus/window probe.");
            }
            Program.Check(host._backgroundIdleRuntimeSuspended && !host._isTargetFocused, "background idle marks focus unknown, not permanently granted");
            Program.Check(host._autoClickScheduler.Stops == 1 && host._uiRefreshes == 0 && host._overlayRefreshes == 0, "one hour without querying windows has no repeated scheduler/UI/HUD work");
            foreach (string key in new[] { "R", "MouseX1", "MouseX2", "OemPlus", "F6" })
            {
                host._down.Add(key);
                Program.Check(!host.TryHandleBackgroundIdleRuntimeTick(now), "background bind requires normal focus checks: " + key);
                Program.Check(!host._backgroundIdleRuntimeSuspended && !host._isTargetFocused, "background activation cannot inherit stale focus");
                Program.Check(host._nextCursorProbeUtc == DateTime.MinValue, "background activation requires fresh GUI guard");
                host._down.Clear();
                Program.Check(host.TryHandleBackgroundIdleRuntimeTick(now), "background key release returns to quiet idle");
            }
            var visible = new MainWindow { IsVisible = true, WindowState = WindowState.Normal };
            Program.Check(!visible.TryHandleBackgroundIdleRuntimeTick(now), "visible helper retains normal target status updates");
            visible.WindowState = WindowState.Minimized;
            Program.Check(visible.TryHandleBackgroundIdleRuntimeTick(now), "minimized helper is allowed to skip window probes");
            foreach (Action<MainWindow> activate in new Action<MainWindow>[]
            {
                h => h._autoLeftRuntimeEnabled = true, h => h._autoRightRuntimeEnabled = true,
                h => h.IsAutoArmorRunning = true, h => h._autoArmorCalibrationPending = true,
                h => h.IsAppleCutterRunning = true, h => h.IsPvpQuickActionRunning = true,
                h => h._bindyStage = BindyStage.OpenChat,
                h => h._suppressBindToggleUntilRelease = true
            })
            {
                var active = new MainWindow();
                activate(active);
                Program.Check(!active.TryHandleBackgroundIdleRuntimeTick(now), "active background workflow cannot bypass focus checks");
            }
        }

        internal static void VerifyEveryWorkflow()
        {
            foreach (string name in new[] { "_autoLeftRuntimeEnabled", "_autoRightRuntimeEnabled", "_autoArmorCalibrationPending", "_suppressBindToggleUntilRelease" })
            {
                var host = new MainWindow();
                typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
                Program.Check(!host.TryHandleIdleRuntimeTick(DateTime.UtcNow, true), "never skip active state " + name);
            }
            foreach (Action<MainWindow> activate in new Action<MainWindow>[]
            {
                h => h.IsAutoArmorRunning = true, h => h.IsAppleCutterRunning = true,
                h => h.IsPvpQuickActionRunning = true,
                h => h._bindyStage = BindyStage.OpenChat
            })
            {
                var host = new MainWindow();
                activate(host);
                Program.Check(!host.TryHandleIdleRuntimeTick(DateTime.UtcNow, true), "never skip a running workflow");
            }
            var inactive = new MainWindow();
            Program.Check(!inactive.TryHandleIdleRuntimeTick(DateTime.UtcNow, false), "focus-loss cleanup remains in the normal loop");
            var cleanup = new MainWindow { _dabHeld = true };
            cleanup._physicalMouseTracker.IsMonitoring = true;
            cleanup.TryHandleIdleRuntimeTick(DateTime.UtcNow, true);
            cleanup.TryHandleIdleRuntimeTick(DateTime.UtcNow.AddSeconds(1), true);
            Program.Check(cleanup._dabWrites == 1 && cleanup._physicalMouseTracker.Stops == 1, "idle transition releases held resources once, not on every tick");
        }

        internal static void VerifyHiddenAndVisibleUi()
        {
            var visible = new MainWindow { IsVisible = true, WindowState = WindowState.Normal };
            visible.TryHandleIdleRuntimeTick(DateTime.UtcNow, true);
            Program.Check(visible._uiRefreshes == 1, "visible window can refresh OFF state");
            visible.WindowState = WindowState.Minimized;
            visible.TryHandleIdleRuntimeTick(DateTime.UtcNow.AddSeconds(1), true);
            Program.Check(visible._uiRefreshes == 1, "minimized window does not refresh controls");
            var overlay = new MainWindow { _lastOverlaySignature = "old completion tile" };
            overlay.TryHandleIdleRuntimeTick(DateTime.UtcNow, true);
            overlay.TryHandleIdleRuntimeTick(DateTime.UtcNow.AddSeconds(1), true);
            Program.Check(overlay._overlayRefreshes == 1, "an existing completion tile can expire, empty HUD is then left alone");
        }
    }
}
