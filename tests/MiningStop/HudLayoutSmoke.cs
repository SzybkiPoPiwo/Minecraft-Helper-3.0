using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MinecraftHelper;
using Drawing = System.Drawing;

internal static partial class Program
{
    private static void VerifyNativeHudLayout()
    {
        // The coordinator/default engine can stay never-shown when startup
        // selects another account. It must still be closable at workspace exit.
        var hiddenEngineWindow = new Window { ShowInTaskbar = false, ShowActivated = false };
        bool hiddenClosed = false;
        hiddenEngineWindow.Closed += (_, _) => hiddenClosed = true;
        hiddenEngineWindow.Hide();
        hiddenEngineWindow.Close();
        Check(hiddenClosed && !hiddenEngineWindow.IsVisible, "a never-shown engine window closes without requiring a UI startup");

        Assembly assembly = typeof(MainWindow).Assembly;
        Type type = assembly.GetType("MinecraftHelper.OverlayHudWindow")!;
        Type entryType = assembly.GetType("MinecraftHelper.OverlayHudEntry")!;
        object corner = Enum.Parse(assembly.GetType("MinecraftHelper.OverlayCorner")!, "BottomRight");
        object tone = Enum.Parse(assembly.GetType("MinecraftHelper.OverlayHudTone")!, "Active");
        var area = new Drawing.Rectangle(-30000, -30000, 1000, 720);
        IntPtr foreground = LayoutGetForegroundWindow();
        var first = (Window)Activator.CreateInstance(type, nonPublic: true)!;
        var second = (Window)Activator.CreateInstance(type, nonPublic: true)!;
        var third = (Window)Activator.CreateInstance(type, nonPublic: true)!;
        Array Entries(string body, bool large)
        {
            Array entries = Array.CreateInstance(entryType, 1);
            entries.SetValue(Activator.CreateInstance(entryType, "Test account · Kopacz", body, tone, large), 0);
            return entries;
        }
        void Layout(Window hud, Drawing.Rectangle bounds) => type.GetMethod("SetMinerLayout")!.Invoke(hud, new object[] { bounds, corner, 16 });
        void Update(Window hud, Array entries) => type.GetMethod("UpdateEntries")!.Invoke(hud, new object[] { entries });
        Drawing.Rectangle Bounds(Window hud)
        {
            Check(LayoutGetWindowRect(new WindowInteropHelper(hud).Handle, out LayoutRect bounds), "actual HUD has native bounds");
            return Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }
        try
        {
            foreach (Window hud in new[] { first, second })
            {
                hud.Left = area.Left;
                hud.Top = area.Top;
                type.GetMethod("SetAnimationsEnabled")!.Invoke(hud, new object[] { false });
                Layout(hud, area);
            }
            Update(first, Entries("Stan: Kopanie", false));
            Update(second, Entries("Stan: Reconnect\nPowrót za: 5 s", true));
            Drawing.Rectangle a = Bounds(first), b = Bounds(second);
            Check(first.IsVisible && second.IsVisible && !a.IntersectsWith(b) && a.Bottom == b.Bottom,
                "real WPF HUDs with different sizes appear side by side");
            Check(area.Contains(a) && area.Contains(b), "real WPF HUDs fit the chosen physical work area");
            Update(first, Entries("Stan: Kopanie\nPraca: 10 s\nAuto EQ: za 5 s\nKomenda: za 1 s", true));
            Check(!Bounds(first).IntersectsWith(Bounds(second)), "WPF HUD size changes trigger immediate collision avoidance");
            type.GetMethod("SetAnimationsEnabled")!.Invoke(second, new object[] { true });
            Layout(second, new Drawing.Rectangle(area.Left, area.Top, 100, 100));
            Check(!second.IsVisible && (bool)type.GetProperty("IsLayoutSuppressed")!.GetValue(second)!,
                "a full monitor immediately hides the HUD rather than stacking it");
            Layout(second, area);
            Update(second, Entries("Stan: Kopanie", false));
            Check(second.IsVisible && !Bounds(first).IntersectsWith(Bounds(second)), "HUD returns after space becomes available");
            Update(first, Array.CreateInstance(entryType, 0));
            third.Left = area.Left;
            third.Top = area.Top;
            type.GetMethod("SetAnimationsEnabled")!.Invoke(third, new object[] { false });
            Layout(third, area);
            Update(third, Entries("Stan: Kopanie", true));
            Update(first, Entries("Stan: Kopanie", true));
            Check(!Bounds(first).IntersectsWith(Bounds(third)) && !Bounds(first).IntersectsWith(Bounds(second)),
                "a quickly returning HUD rechecks space occupied while it was hidden");
            Check(LayoutGetForegroundWindow() == foreground && !first.IsActive && !second.IsActive,
                "showing, moving and hiding WPF HUDs never activates them");
            Check(GetWindow(new WindowInteropHelper(first).Handle, 4) == IntPtr.Zero
                && GetWindow(new WindowInteropHelper(second).Handle, 4) == IntPtr.Zero, "WPF HUDs remain ownerless");
        }
        finally { third.Close(); second.Close(); first.Close(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct LayoutRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool LayoutGetWindowRect(IntPtr window, out LayoutRect bounds);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr LayoutGetForegroundWindow();
}
