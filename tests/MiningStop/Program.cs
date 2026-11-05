using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls;
using MinecraftHelper;
using MinecraftHelper.Models;

// Default tests create no window, settings, filesystem logs or desktop input.
// The explicit native smoke option only creates/disposes a message-only window.
internal static partial class Program
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--hud-layout-smoke"))
            {
                VerifyNativeHudLayout();
                Console.WriteLine($"PASS: {_passed} WPF HUD layout checks; own offscreen windows, no desktop input sent.");
                return 0;
            }
            if (args.Contains("--focus-input-smoke"))
            {
                VerifyNativeFocusObserver();
                VerifyNativeHudIsolation();
                VerifyNativeTextCarrier();
                Console.WriteLine($"PASS: {_passed} checks; native Raw Input lifecycle and independent hidden HUD; no desktop input sent.");
                return 0;
            }
            VerifyStopEdges();
            VerifyPanelStartChannel();
            VerifyBackgroundFocusIntent();
            VerifyBackgroundMouseBaseline();
            VerifyBackgroundTextInput();
            VerifyChatInputBarrier();
            VerifyAppLog();
            VerifyDiscordSettings();
            VerifyMarkerResourcePack();
            VerifyEmergencyDamageSoundArming();
            VerifyEmergencyReconnectOptIn();
            VerifyEmergencyProtectionCheckboxEditing();
            VerifyEmergencyDisconnectSequence();
            VerifyDamageSoundFingerprint();
            VerifyBundledDamageSoundReferences();
            VerifyCommandCountdownPause();
            VerifyCobbleXCommandRepetition();
            VerifyMiningLogMessages();
            VerifyEmergencyMiningLogMessages();
            foreach (string miner in new[] { "533", "633" })
                VerifyMiner(miner);
            VerifyReconnectScreenClassification();
            VerifyReconnectTeleportTiming();
            VerifyReconnectOverlayStages();
            VerifyReconnectGuards();
            VerifyDisconnectScreenWait();
            VerifyPriority();
            Console.WriteLine($"PASS: {_passed} checks; no user settings, logs or desktop input changed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyStopEdges()
    {
        bool wasDown = false;
        Check(Press(true, false, ref wasDown), "first stop press is consumed");
        Check(!Press(true, false, ref wasDown), "held stop bind does not repeat/restart");
        Check(!Press(false, false, ref wasDown), "release does not toggle");
        Check(Press(true, false, ref wasDown), "a new press is recognized");
        wasDown = false;
        Check(!Press(true, true, ref wasDown), "injected drop/control DOWN cannot stop mining");
        Check(!Press(false, false, ref wasDown), "injected release does not stop mining");
        Check(Press(true, false, ref wasDown), "physical press after injected release works");
    }

    private static bool Press(bool down, bool injected, ref bool wasDown)
    {
        object[] args = { down, injected, wasDown };
        bool result = (bool)typeof(MainWindow).GetMethod("ConsumeMiningStopPress", Static)!.Invoke(null, args)!;
        wasDown = (bool)args[2];
        return result;
    }

    private static void VerifyPanelStartChannel()
    {
        MainWindow host = NewWindow();
        var enabled533 = new CheckBox();
        var enabled633 = new CheckBox();
        var direction = new ComboBox();
        for (int i = 0; i < 4; i++) direction.Items.Add(i.ToString());
        typeof(MainWindow).GetField("ChkKopacz533Enabled", Instance)!.SetValue(host, enabled533);
        typeof(MainWindow).GetField("ChkKopacz633Enabled", Instance)!.SetValue(host, enabled633);
        typeof(MainWindow).GetField("CbKopacz633Direction", Instance)!.SetValue(host, direction);
        // Start validation only compares the handle to zero; no native call/input.
        typeof(MainWindow).GetField("_targetGameWindowHandle", Instance)!.SetValue(host, new IntPtr(123));
        string Owner() => typeof(MainWindow).GetMethod("GetMiningStartOwner", Instance)!.Invoke(host, null)!.ToString()!;
        MethodInfo blockerMethod = typeof(MainWindow).GetMethod("GetMiningStartBlocker", Instance)!;
        Type ownerType = blockerMethod.GetParameters()[0].ParameterType;
        string? BlockerFor(string requested) => (string?)blockerMethod.Invoke(host, new[] { Enum.Parse(ownerType, requested) });
        string? Blocker() => BlockerFor("None");

        enabled533.IsChecked = true;
        enabled633.IsChecked = true;
        foreach (int selected in new[] { 1, 2 })
        {
            direction.SelectedIndex = selected;
            Check(Owner() == "Kopacz633" && Blocker() == null,
                "START chooses movement instead of stationary mining when both channels are enabled; direction " + selected);
        }
        direction.SelectedIndex = 3;
        Check(Blocker() != null, "the removed rectangle mode is no longer a valid Kopacz 6/3/3 direction");
        Check(BlockerFor("Kopacz533") == null, "!start 5/3/3 does not need a Kopacz 6/3/3 direction");
        enabled533.IsChecked = false;
        Check(BlockerFor("Kopacz533") == null, "!start 5/3/3 is allowed even when its channel is unticked (it gets ticked on start)");
        enabled533.IsChecked = true;

        direction.SelectedIndex = 0;
        Check(Owner() == "Kopacz633" && Blocker()?.Contains("kierunek Kopacza 6/3/3", StringComparison.Ordinal) == true,
            "an incomplete movement setup is reported instead of silently falling back to 5/3/3");
        enabled533.IsChecked = false;
        direction.SelectedIndex = 2;
        Check(Owner() == "Kopacz633" && Blocker() == null, "START still supports only the movement channel enabled");

        enabled533.IsChecked = true;
        enabled633.IsChecked = false;
        direction.SelectedIndex = 0;
        Check(Owner() == "Kopacz533" && Blocker() == null,
            "START preserves stationary mining when only 5/3/3 is enabled without requiring a movement direction");
        enabled533.IsChecked = false;
        direction.SelectedIndex = 2;
        Check(Owner() == "None" && Blocker()?.Contains("zaznacz kanał", StringComparison.Ordinal) == true,
            "a remembered movement direction cannot start a disabled channel");
    }

    private static void VerifyBackgroundFocusIntent()
    {
        Type type = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.BackgroundMiningFocusIntent")!;
        object intent = Activator.CreateInstance(type, nonPublic: true)!;
        void Mouse(ushort flags, bool destination, int at) =>
            type.GetMethod("ObserveMouse")!.Invoke(intent, new object[] { flags, destination, at });
        void Key(ushort key, bool up, int at) =>
            type.GetMethod("ObserveKeyboard")!.Invoke(intent, new object[] { key, up, at });
        bool Switch(int at) => (bool)type.GetMethod("IsSwitchRequested")!.Invoke(intent, new object[] { at })!;
        bool Return(bool focused, int at) => (bool)type.GetMethod("ShouldReturnForeground")!.Invoke(intent, new object[] { focused, at })!;

        Check(!Switch(100), "background focus stays protected without a device click");
        Mouse(0, true, 100);
        Check(!Switch(101), "hovering over Minecraft does not authorize foreground input");
        Mouse(0x0400, true, 110);
        Mouse(0x0002, true, 120);
        Check(!Switch(121), "scrolling or releasing a held mining button cannot end background mode");
        Mouse(0x0001, false, 130);
        Check(!Switch(131), "clicking Kopacz or another application does not authorize a game activation");
        Mouse(0x0001, true, 140);
        Check(Switch(141), "a real click directed at the game permits a foreground transition");
        Mouse(0x0002, true, 150);
        Check(Switch(151), "a quick click is recognized after the button was released between macro ticks");
        Check(!Switch(1141), "old clicks cannot authorize a later hover activation");
        Mouse(0x0004, true, 1200);
        Check(Switch(1201), "a real right click can activate the game");
        Mouse(0x0001, false, 1210);
        Check(!Switch(1211), "a click in another application clears a pending return to the game");

        Key(0xA4, false, 1300);
        Check(!Switch(1301), "holding Alt alone does not permit hover activation");
        Key(0x09, false, 1310);
        Key(0x09, true, 1320);
        Key(0xA4, true, 1330);
        Check(Switch(1331), "a complete Alt+Tab gesture is recognized after releasing its keys");
        Check(!Switch(2331), "Alt+Tab permission expires rather than allowing arbitrary later focus steals");
        Key(0x5B, false, 2400);
        Check(!Switch(2401), "the Windows key alone does not authorize a hover activation");
        Key(0x27, false, 2410);
        Key(0x5B, true, 2420);
        Check(Switch(2421), "Windows desktop navigation permits an intentional return to the game");

        int beforeWrap = int.MaxValue - 20;
        Mouse(1, true, beforeWrap);
        Check(Switch(unchecked(beforeWrap + 50)), "focus intent survives the TickCount signed wrap");
        Check(!Switch(unchecked(beforeWrap + 1001)), "focus intent still expires across the TickCount wrap");
        Check(!Switch(beforeWrap - 1), "a future queued timestamp is not accepted");

        intent = Activator.CreateInstance(type, nonPublic: true)!;
        Check(!Return(true, 100), "foreground arrival waits briefly for Raw Input without enabling global input");
        Mouse(1, true, 110);
        Check(!Return(true, 120), "a click queued after foreground activation still permits manual control");
        Mouse(1, false, 130);
        Check(!Return(true, 140), "an unsolicited activation starts its own event delivery grace period");
        Check(Return(true, 190), "hover activation is reverted when no intentional device event arrives");
        Check(Return(true, 210), "failed focus restoration can be retried without ending background mode");
        Check(!Return(false, 220), "leaving the game clears an unsolicited activation");
        Check(!Return(true, 300), "a later activation gets a fresh input delivery grace period");
        Key(0xA4, false, 400);
        Key(0x09, false, 410);
        Key(0xA4, true, 3000);
        Check(Switch(3010), "holding the Alt+Tab selector open still permits its final selected window");
    }

    private static void VerifyNativeFocusObserver()
    {
        Type type = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.BackgroundMiningFocusObserver")!;
        // Repeat to verify a new session can register after the previous one is disposed.
        for (int session = 0; session < 2; session++)
        {
            using var observer = (IDisposable?)type.GetMethod("TryCreate")!.Invoke(null, new object[] { IntPtr.Zero });
            Check(observer != null, "mouse and keyboard Raw Input registration succeeds");
            Check(!(bool)type.GetProperty("IsSwitchRequested")!.GetValue(observer)!, "a new native observer has no phantom physical click");
            Check(!(bool)type.GetMethod("ShouldReturnForeground")!.Invoke(observer, new object[] { false })!, "inactive game needs no native foreground return");
        }
    }

    private static void VerifyBackgroundMouseBaseline()
    {
        Type type = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.BackgroundGameWindow")!;
        MethodInfo calculate = type.GetMethod("CalculateFocusMousePosition", Static)!;
        foreach (var value in new[]
        {
            (OldWidth: 854, OldHeight: 480, NewHeight: 480, X: 427, Y: 240),
            (OldWidth: 1920, OldHeight: 1080, NewHeight: 480, X: 960, Y: -60),
            (OldWidth: 855, OldHeight: 481, NewHeight: 480, X: 427, Y: 239),
            (OldWidth: 854, OldHeight: 480, NewHeight: 1080, X: 427, Y: 840)
        })
        {
            var point = (System.Drawing.Point)calculate.Invoke(null, new object[] { value.OldWidth, value.OldHeight, value.NewHeight })!;
            Check(point.X == value.X && point.Y == value.Y, "mouse baseline preserves old viewport coordinates across resize");
            int previousLwjglY = value.OldHeight - 1 - value.OldHeight / 2;
            int newLwjglY = value.NewHeight - 1 - point.Y;
            Check(point.X == value.OldWidth / 2 && newLwjglY == previousLwjglY,
                "rebasing cancels accumulated movement without introducing an inverted-Y camera delta");
        }

        MethodInfo pack = type.GetMethod("PositionLParam", Static)!;
        IntPtr packed = (IntPtr)pack.Invoke(null, new object[] { 960, -60 })!;
        Check(unchecked((short)packed.ToInt64()) == 960 && unchecked((short)(packed.ToInt64() >> 16)) == -60,
            "virtual positions outside a resized client retain signed WM_MOUSEMOVE coordinates");
    }

    private static void VerifyBackgroundTextInput()
    {
        Type type = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.BackgroundTextInput")!;
        MethodInfo send = type.GetMethod("SendCharacter", Static)!;
        var messages = new System.Collections.Generic.List<(uint Message, IntPtr WParam, IntPtr LParam)>();
        bool Send(char ch, Func<uint, IntPtr, IntPtr, bool> post) => (bool)send.Invoke(null, new object[] { ch, post })!;
        bool Record(uint message, IntPtr wParam, IntPtr lParam)
        {
            messages.Add((message, wParam, lParam));
            return true;
        }
        const string text = "/repair Zażółć 123";
        foreach (char ch in text)
            Check(Send(ch, Record), "background text sends an independent pressed character: " + ch);
        Check(messages.Count == text.Length * 3, "each character has one down, one exact UTF-16 character and one release");
        for (int i = 0; i < text.Length; i++)
        {
            var down = messages[i * 3];
            var character = messages[i * 3 + 1];
            var up = messages[i * 3 + 2];
            Check(down.Message == 0x0100 && down.WParam == (IntPtr)0xE7 && down.LParam == (IntPtr)1,
                "text uses an unmapped carrier instead of physical letter keys/modifiers");
            Check(character.Message == 0x0102 && character.WParam == (IntPtr)text[i] && character.LParam == (IntPtr)1,
                "leading slash, spaces, uppercase and Unicode survive unchanged");
            Check(up.Message == 0x0101 && up.WParam == down.WParam && (up.LParam.ToInt64() & 0xC0000000L) == 0xC0000000L,
                "every text carrier is released before the next character or Enter");
        }

        messages.Clear();
        Check(!Send('/', (m, w, l) => { Record(m, w, l); return m != 0x0102; }), "a failed character post is reported");
        Check(messages.Count == 3 && messages[2].Message == 0x0101, "a failed character still releases its carrier");
        messages.Clear();
        Check(!Send('/', (m, w, l) => { Record(m, w, l); return false; }) && messages.Count == 1,
            "a rejected key-down sends no orphan character");
        Check(!Send('/', (m, w, l) => m != 0x0101), "a failed release is reported rather than claiming text success");
        messages.Clear();
        try
        {
            Send('/', (m, w, l) => { Record(m, w, l); return m == 0x0102 ? throw new InvalidOperationException("test") : true; });
            Check(false, "test callback should throw");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            Check(messages.Count == 3 && messages[2].Message == 0x0101, "an interrupted character send still releases its carrier");
        }
    }

    private static void VerifyChatInputBarrier()
    {
        Type type = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.ChatInputBarrier")!;
        var requests = new System.Collections.Generic.List<long>();
        bool accept = true;
        Func<long, bool> request = token => { requests.Add(token); return accept; };
        object Create(int confirmations) => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { request, confirmations }, null)!;
        object barrier = Create(2);
        void Arm(DateTime at) => type.GetMethod("Arm")!.Invoke(barrier, new object[] { at });
        void Ack(long token, DateTime at) => type.GetMethod("Acknowledge")!.Invoke(barrier, new object[] { token, at });
        bool Ready(DateTime at) => (bool)type.GetMethod("IsReady")!.Invoke(barrier, new object[] { at })!;
        bool TimedOut() => (bool)type.GetProperty("TimedOut")!.GetValue(barrier)!;

        DateTime t = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        Check(Ready(t), "an unarmed barrier never delays typing");
        Arm(t);
        Check(requests.Count == 1 && !Ready(t.AddMilliseconds(500)), "text waits until the game confirms it received the chat key");
        Ack(requests[0], t.AddMilliseconds(600));
        Check(!Ready(t.AddMilliseconds(700)) && requests.Count == 1, "text waits at least one tick gap after the chat key was received");
        Check(!Ready(t.AddMilliseconds(720)) && requests.Count == 2, "after the gap a second frame confirmation is requested");
        Ack(requests[0], t.AddMilliseconds(730));
        Check(!Ready(t.AddMilliseconds(740)), "a stale confirmation does not release the barrier");
        Ack(requests[1], t.AddMilliseconds(750));
        Check(Ready(t.AddMilliseconds(760)) && !TimedOut(), "after the second confirmation the chat is open and text may be typed");

        requests.Clear();
        Arm(t);
        Check(!Ready(t.AddSeconds(4.9)) && Ready(t.AddSeconds(5)) && TimedOut(), "a frozen game releases the barrier after the timeout");
        accept = false;
        Arm(t);
        Check(Ready(t), "a rejected confirmation request does not block typing forever");

        // Gra na wierzchu (SendInput): T może dojść do gry dopiero po pierwszym WM_NULL,
        // więc tekst czeka na trzecie potwierdzenie, każde po przerwie na tick.
        accept = true;
        requests.Clear();
        barrier = Create(3);
        Arm(t);
        Ack(requests[0], t.AddMilliseconds(10));
        Check(!Ready(t.AddMilliseconds(130)) && requests.Count == 2, "foreground input requests a second confirmation after a tick gap");
        Ack(requests[1], t.AddMilliseconds(140));
        Check(!Ready(t.AddMilliseconds(200)), "foreground input is not released after two confirmations");
        Check(!Ready(t.AddMilliseconds(260)) && requests.Count == 3, "foreground input requests a third confirmation after another gap");
        Ack(requests[2], t.AddMilliseconds(270));
        Check(Ready(t.AddMilliseconds(280)) && !TimedOut(), "foreground text is released after the third confirmation");
    }

    // Bez Initialize dziennik działa tylko w pamięci — test nie dotyka plików użytkownika.
    private static void VerifyAppLog()
    {
        Type log = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.AppLog")!;
        void Info(string source, string message) => log.GetMethod("Info")!.Invoke(null, new object[] { source, message });
        int Count(string source) => ((IEnumerable)log.GetMethod("Snapshot")!.Invoke(null, null)!)
            .Cast<object>().Count(entry => (string)entry.GetType().GetProperty("Source")!.GetValue(entry)! == source);

        string source = "test-" + Guid.NewGuid().ToString("N")[..6];
        Info(source, "pierwszy");
        Info(source, "pierwszy");
        Check(Count(source) == 1, "repeated status message from one source is logged once");
        Info(source, "drugi");
        Info(source, "pierwszy");
        Check(Count(source) == 3, "a changed message is logged again");

        string flood = "flood-" + Guid.NewGuid().ToString("N")[..6];
        for (int i = 0; i < 100; i++)
            Info(flood, "wiadomość " + i);
        Check(Count(flood) <= 40, "one source cannot flood the log within a second");

        object entry = ((IEnumerable)log.GetMethod("Snapshot")!.Invoke(null, null)!).Cast<object>().Last();
        string formatted = (string)log.GetMethod("Format")!.Invoke(null, new[] { entry })!;
        Check(formatted.Contains("INFO", StringComparison.Ordinal) && formatted.Contains($"[{flood}]", StringComparison.Ordinal),
            "log lines carry level and source");
        Check(log.GetProperty("FilePath")!.GetValue(null) == null, "test log stays in memory without a file");
    }

    // Tylko obiekty w pamięci — test nie zapisuje discord.json użytkownika.
    private static void VerifyDiscordSettings()
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        Type settingsType = assembly.GetType("MinecraftHelper.Services.DiscordSettings")!;
        Type store = assembly.GetType("MinecraftHelper.Services.DiscordSettingsStore")!;
        object settings = Activator.CreateInstance(settingsType, nonPublic: true)!;
        void SetProp(string name, object value) => settingsType.GetProperty(name)!.SetValue(settings, value);
        object? GetProp(string name) => settingsType.GetProperty(name)!.GetValue(settings);

        const string token = "MTIzNDU2Nzg5.test-token.secret";
        store.GetMethod("SetToken", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new[] { settings, token });
        string stored = (string)GetProp("TokenProtected")!;
        Check(stored.Length > 0 && !stored.Contains("secret", StringComparison.Ordinal), "the bot token is never stored as plain text");
        Check((string?)store.GetMethod("GetToken", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new[] { settings }) == token, "the encrypted token is readable by the same Windows user");

        SetProp("OwnerUserIds", "123456789012345678, 22;abc 33");
        var owners = ((System.Collections.Generic.IEnumerable<string>)settingsType.GetMethod("GetOwnerIds")!.Invoke(settings, null)!).ToArray();
        Check(owners.Length == 3 && owners.Contains("22") && !owners.Contains("abc"), "owner list keeps only numeric Discord IDs");

        SetProp("ChannelId", "987654321");
        Check(!(bool)GetProp("IsConfigured")!, "a disabled bot is never considered ready");
        SetProp("Enabled", true);
        Check((bool)GetProp("IsConfigured")!, "an enabled bot with token and numeric channel is ready");
        SetProp("ChannelId", "kanał");
        Check(!(bool)GetProp("IsConfigured")!, "a non-numeric channel ID is rejected");

        // Okno „Komendy bota”: wyłączone komendy, aliasy i !pomoc z jednego katalogu.
        Type commands = assembly.GetType("MinecraftHelper.Services.DiscordCommands")!;
        object? Find(string typed) => commands.GetMethod("Find")!.Invoke(null, new object[] { typed });
        string CommandId(object command) => (string)command.GetType().GetProperty("Id")!.GetValue(command)!;
        Check(Find("wyjdź") is { } leaveAlias && CommandId(leaveAlias) == "leave" && Find("wejdz") is { } joinAlias && CommandId(joinAlias) == "join"
            && Find("ZRZUT") is { } ssAlias && CommandId(ssAlias) == "ss" && Find("nieznana") == null,
            "commands resolve by id and Polish aliases, case-insensitively");
        Check((bool)commands.GetMethod("IsHelp")!.Invoke(null, new object[] { "help" })! && Find("pomoc") == null,
            "help is always available and cannot be switched off");
        object commandSettings = Activator.CreateInstance(settingsType, nonPublic: true)!;
        var disabledList = (System.Collections.Generic.List<string>)settingsType.GetProperty("DisabledCommands")!.GetValue(commandSettings)!;
        bool Enabled(string id) => (bool)settingsType.GetMethod("IsCommandEnabled")!.Invoke(commandSettings, new object[] { id })!;
        Check(Enabled("leave") && Enabled("join"), "new commands are enabled by default");
        disabledList.Add("leave");
        Check(!Enabled("leave") && !Enabled("LEAVE") && Enabled("join"), "a disabled command stays off, others keep working");
        string commandJson = System.Text.Json.JsonSerializer.Serialize(commandSettings, settingsType);
        object reloaded = System.Text.Json.JsonSerializer.Deserialize(commandJson, settingsType)!;
        Check(!(bool)settingsType.GetMethod("IsCommandEnabled")!.Invoke(reloaded, new object[] { "leave" })!, "disabled commands survive saving to discord.json");
        settingsType.GetProperty("DisabledCommands")!.SetValue(reloaded, null);
        Check((bool)settingsType.GetMethod("IsCommandEnabled")!.Invoke(reloaded, new object[] { "leave" })!, "a missing list (older file) enables every command");
        object help = assembly.GetType("MinecraftHelper.Services.DiscordBotHost")!.GetMethod("BuildHelp", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { commandSettings })!;
        string helpText = (string)help.GetType().GetMethod("ToPlainText")!.Invoke(help, null)!;
        Check(helpText.Contains("!join [konto]", StringComparison.Ordinal) && !helpText.Contains("!leave [konto]", StringComparison.Ordinal)
            && helpText.Contains("Wyłączone w Kopaczu", StringComparison.Ordinal),
            "help lists only enabled commands and names the disabled ones");
        string botSource = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "Services", "DiscordBotHost.cs"));
        int enabledGate = botSource.IndexOf("if (!settings.IsCommandEnabled(command.Id))", StringComparison.Ordinal);
        Check(enabledGate > 0 && enabledGate < botSource.IndexOf("switch (command.Id)", StringComparison.Ordinal), "a disabled command is rejected before it runs");

        // Pierwszy zapis tokenu z wyłączonym przełącznikiem zostawiał bota milczącego.
        string discordUi = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.Discord.cs"));
        string discordXaml = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.xaml"));
        int autoEnable = discordUi.IndexOf("if (!settings.Enabled && PwdDiscordToken.Password.Trim().Length > 0)", StringComparison.Ordinal);
        int save = discordUi.IndexOf("DiscordSettingsStore.Save(settings);", autoEnable < 0 ? 0 : autoEnable, StringComparison.Ordinal);
        Check(autoEnable >= 0 && save > autoEnable && discordUi.Contains("ChkDiscordEnabled.IsChecked = true;", StringComparison.Ordinal),
            "saving a newly typed token switches the bot on before writing settings");
        Check(discordXaml.Contains("KeyDown=\"PwdDiscordToken_KeyDown\"", StringComparison.Ordinal)
            && discordXaml.Contains("PasswordChanged=\"PwdDiscordToken_PasswordChanged\"", StringComparison.Ordinal)
            && discordUi.Contains("SaveDiscordSettingsFromUi($\"Wysłano test jako {botName} i zapisano token.\")", StringComparison.Ordinal),
            "Enter, an unsaved-token hint and a successful test all commit the typed token");
        SetProp("CommandPrefix", "  ");
        Check((string)GetProp("Prefix")! == "!", "an empty prefix falls back to !");
        string json = System.Text.Json.JsonSerializer.Serialize(settings, settingsType);
        Check(!json.Contains("IsConfigured", StringComparison.Ordinal) && !json.Contains("\"Prefix\"", StringComparison.Ordinal), "computed Discord properties are not written to discord.json");

        MethodInfo invite = store.GetMethod("TryBuildInviteUrl", BindingFlags.Static | BindingFlags.Public)!;
        string botId = "1430123456789012345";
        string tokenWithId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(botId)).TrimEnd('=') + ".GaBc12.secret";
        string? inviteUrl = (string?)invite.Invoke(null, new object?[] { tokenWithId });
        Check(inviteUrl == $"https://discord.com/oauth2/authorize?client_id={botId}&scope=bot&permissions=101376", "the invite link uses the bot ID from the token and the 4 needed permissions");
        Check(invite.Invoke(null, new object?[] { "nie-token" }) == null && invite.Invoke(null, new object?[] { null }) == null, "an invalid or missing token gives no invite link");

        VerifyDiscordLogReport(assembly);
    }

    // Paczka zasobów budowana przez Kopacza: znaczniki, chroniony cobble, etykiety [MH] i wersja.
    // Sztuczne tekstury zamiast plików gry — test działa bez Minecrafta.
    private static void VerifyMarkerResourcePack()
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        Type pack = assembly.GetType("MinecraftHelper.Services.MarkerResourcePack")!;
        static byte[] Png(int width, int height, uint color)
        {
            var pixels = Enumerable.Repeat(color, width * height).ToArray();
            var source = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 72, 72, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        static uint[] Pixels(byte[] png, out int width)
        {
            var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(new MemoryStream(png),
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            System.Windows.Media.Imaging.BitmapSource frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
            width = frame.PixelWidth;
            var result = new uint[frame.PixelWidth * frame.PixelHeight];
            frame.CopyPixels(result, frame.PixelWidth * 4, 0);
            return result;
        }

        byte[] item = Png(16, 16, 0xFF808080);
        byte[] inventory = Png(256, 256, 0xFFC6C6C6);
        Func<string, byte[]?> textures = path => path == "gui/container/inventory.png" ? inventory : path.StartsWith("items/", StringComparison.Ordinal) ? item : null;
        var baseFiles = new (string, byte[])[] { ("assets/minecraft/textures/items/diamond.png", Png(16, 16, 0xFF000000)), ("assets/minecraft/models/block/stone.json", new byte[] { 123, 125 }) };
        byte[] zipBytes = (byte[])pack.GetMethod("Build", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object?[] { textures, baseFiles })!;

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes));
        byte[] Entry(string name)
        {
            using Stream stream = zip.GetEntry(name)!.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        uint[,] expected = (uint[,])pack.GetMethod("BuildItemMarker", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { 0 })!;
        uint[] diamond = Pixels(Entry("assets/minecraft/textures/items/diamond.png"), out int width);
        bool markerOk = true;
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                markerOk &= diamond[y * width + 13 + x] == expected[y, x];
        Check(markerOk && diamond[8 * width + 8] == 0xFF808080, "the generated pack marks diamond with code 0 in the top-right 3x3 and keeps the game texture");
        Check(zip.Entries.Count(entry => entry.FullName == "assets/minecraft/textures/items/diamond.png") == 1, "marked textures replace the copied default ones instead of duplicating them");
        Check(zip.GetEntry("assets/minecraft/models/block/stone.json") != null, "default models from the game are copied into the pack");

        uint[] cobble = Pixels(Entry("assets/minecraft/textures/blocks/cobblestone.png"), out _);
        Check(cobble.All(pixel => pixel == 0xFFFFAEC9), "cobblestone is the solid pink protected by Auto EQ (255,174,201)");
        uint[] inv = Pixels(Entry("assets/minecraft/textures/gui/container/inventory.png"), out int invWidth);
        Check(inv[1 * invWidth + 1] == 0xFFFF00FF && inv[1 * invWidth + 2] == 0xFF00FFFF, "inventory.png carries the GUI corner markers");
        Check(System.Text.Encoding.UTF8.GetString(Entry("assets/minecraft/lang/pl_PL.lang")).Contains("selectServer.title=[MH] SERVER LIST"), "reconnect labels [MH] are written for pl_PL");
        Check(Entry("assets/minecraft/sounds/damage/hit1.ogg").Length > 1000 && Entry("pack.png").Length > 100, "damage sounds and pack icon come from the program");
        Check(System.Text.Encoding.UTF8.GetString(Entry("pack.mcmeta")).Contains("\"pack_format\": 1"), "pack.mcmeta targets Minecraft 1.8 (pack_format 1)");

        int? version = (int?)pack.GetMethod("ReadGeneratorVersion", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object[] { new MemoryStream(zipBytes) });
        Check(version == (int)pack.GetField("GeneratorVersion")!.GetValue(null)!, "the pack records the generator version used to detect outdated packs");
        Check(pack.GetMethod("ReadGeneratorVersion")!.Invoke(null, new object[] { new MemoryStream(new byte[] { 1, 2, 3 }) }) == null, "a damaged file is not mistaken for a current pack");

        bool failed = false;
        try
        {
            pack.GetMethod("Build")!.Invoke(null, new object?[] { (Func<string, byte[]?>)(_ => null), null });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is FileNotFoundException)
        {
            failed = true;
        }
        Check(failed, "a game without the needed textures gives a clear error instead of a broken pack");
    }

    // !dane: ostatnia sesja z dziennika, wyrzucone przedmioty, co zostało i wyjścia z serwera.
    private static void VerifyDiscordLogReport(Assembly assembly)
    {
        Type entryType = assembly.GetType("MinecraftHelper.Services.MiningLogEntry")!;
        Type itemType = assembly.GetType("MinecraftHelper.Services.MiningLogItemDetail")!;
        var entries = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(entryType))!;
        DateTimeOffset now = new(2026, 10, 29, 14, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 29, 14, 0, 0)));
        object Add(params (string Name, object Value)[] values)
        {
            object entry = Activator.CreateInstance(entryType, nonPublic: true)!;
            foreach ((string name, object value) in values)
                entryType.GetProperty(name)!.SetValue(entry, value);
            entries.Add(entry);
            return entry;
        }

        Add(("Kind", "mining-run"), ("SessionId", "old"), ("Timestamp", now.AddDays(-1)), ("Status", "completed"), ("CompletedAt", (DateTimeOffset?)now.AddDays(-1).AddHours(1)));
        Add(("Kind", "automation-event"), ("MiningRunId", "old"), ("Timestamp", now.AddDays(-1).AddMinutes(30)), ("EventType", "emergency-damage-sound-detected"), ("Details", "Rozpoznano dźwięk obrażeń"));
        Add(("Kind", "mining-run"), ("SessionId", "run"), ("Owner", "Kopacz 633"), ("Timestamp", now.AddHours(-2)), ("Status", "in-progress"));
        object eq = Add(("Kind", "inventory-session"), ("MiningRunId", "run"), ("Timestamp", now.AddHours(-1)), ("Status", "completed"),
            ("ItemCount", 192), ("StackCount", 3), ("CobbleXCreated", true), ("RemainingStacks", 2),
            ("CobbleXEnabled", true), ("FullCobblestoneStacks", 4), ("RequiredCobblestoneStacks", 9));
        var items = (System.Collections.IList)entryType.GetProperty("Items")!.GetValue(eq)!;
        foreach ((string label, int count) in new[] { ("Andezyt", 128), ("Diorit", 64) })
        {
            object item = Activator.CreateInstance(itemType, nonPublic: true)!;
            itemType.GetProperty("Label")!.SetValue(item, label);
            itemType.GetProperty("ItemCount")!.SetValue(item, count);
            items.Add(item);
        }
        Add(("Kind", "automation-event"), ("MiningRunId", "run"), ("Timestamp", now.AddMinutes(-30)), ("EventType", "emergency-damage-sound-detected"), ("Details", "Test: rozpoznano dźwięk obrażeń"));

        MethodInfo build = assembly.GetType("MinecraftHelper.Services.DiscordLogReport")!.GetMethod("Build", BindingFlags.Static | BindingFlags.Public)!;
        string Plain(object embed) => (string)embed.GetType().GetMethod("ToPlainText")!.Invoke(embed, null)!;
        string report = Plain(build.Invoke(null, new object[] { "Alt 1", entries, now })!);
        Check(report.Contains("Kopacz 633") && report.Contains("🟢 trwa") && report.Contains("[👤 Alt 1]"), "!dane reports the newest mining session of that account as running");
        Check(report.Contains("Otwarcia EQ: 1") && report.Contains("CobbleX: 1") && report.Contains("192 szt."), "!dane counts EQ, CobbleX and discarded items of that session");
        Check(report.Contains("Andezyt — **128**") && report.IndexOf("Andezyt", StringComparison.Ordinal) < report.IndexOf("Diorit", StringComparison.Ordinal), "!dane lists discarded item types, largest first");
        Check(report.Contains("zostało 2 stacki") && report.Contains("cobble 64: 4/9"), "!dane shows what was left after the last EQ");
        Check(report.Contains("192 szt.\n3 stacki") && !report.Contains("stos"), "Discord cards say „stacki”, never the „stos.” abbreviation");
        MethodInfo stacks = build.DeclaringType!.GetMethod("Stacks", BindingFlags.Static | BindingFlags.NonPublic)!;
        string[] stackWords = new[] { 1, 2, 5, 12, 22, 36 }.Select(count => (string)stacks.Invoke(null, new object[] { count })!).ToArray();
        Check(stackWords.SequenceEqual(new[] { "1 stack", "2 stacki", "5 stacków", "12 stacków", "22 stacki", "36 stacków" }), "stack counts use Polish plural forms");
        Check(report.Contains("Wyjścia z serwera: brak ✅"), "a damage-sound test is not reported as a server exit");
        Check(report.Contains("Ostatnie wyjście z serwera: " + now.AddDays(-1).AddMinutes(30).LocalDateTime.ToString("dd.MM HH:mm")), "!dane names the last real server exit from earlier sessions");

        MethodInfo fieldsMethod = build.DeclaringType!.GetMethod("BuildSessionFields", BindingFlags.Static | BindingFlags.Public)!;
        string Fields(object list) => string.Join("\n", ((System.Collections.IEnumerable)list).Cast<object>()
            .Select(field => $"{field.GetType().GetProperty("Name")!.GetValue(field)}: {field.GetType().GetProperty("Value")!.GetValue(field)}"));
        string summary = Fields(fieldsMethod.Invoke(null, new object[] { entries, "run", now })!);
        Check(summary.Contains("Otwarcia EQ: 1") && summary.Contains("CobbleX: 1") && summary.Contains("192 szt.") && summary.Contains("Andezyt — **128**"),
            "the end-of-session notification summarizes that session since its start");
        Check(summary.Contains("Wyjścia z serwera: brak"), "the session summary ignores damage-sound tests");
        Check(Fields(fieldsMethod.Invoke(null, new object[] { entries, "brak", now })!).Length == 0, "an unknown session gives no summary");

        string empty = Plain(build.Invoke(null, new object[] { "Alt 2", Activator.CreateInstance(entries.GetType())!, now })!);
        Check(empty.Contains("Brak zapisanych sesji"), "!dane handles an account without logs");

        Type embedType = assembly.GetType("MinecraftHelper.Services.DiscordEmbed")!;
        object card = Activator.CreateInstance(embedType, new object[] { new string('x', 300), 0xE5484D })!;
        embedType.GetMethod("Field")!.Invoke(card, new object?[] { "Puste", " ", false });
        string payload = embedType.GetMethod("ToPayload")!.Invoke(card, new object?[] { "zrzut.jpg" })!.ToString()!;
        Check(!payload.Contains("Puste") && payload.Contains("attachment://zrzut.jpg") && !payload.Contains(new string('x', 257)),
            "embeds skip empty fields, keep the screenshot inside the card and respect Discord's title limit");

        object grid = Activator.CreateInstance(embedType, new object[] { "Start", 0x30A46C })!;
        foreach (string name in new[] { "Tryb", "Kierunek", "Auto EQ" })
            embedType.GetMethod("Field")!.Invoke(grid, new object?[] { name, "wartość", true });
        embedType.GetMethod("Field")!.Invoke(grid, new object?[] { "Stan", "**gotowy**", true });
        var gridJson = (System.Text.Json.Nodes.JsonObject)embedType.GetMethod("ToPayload")!.Invoke(grid, new object?[] { null })!;
        string[] values = gridJson["fields"]!.AsArray().Select(field => (string)field!["value"]!).ToArray();
        Check(values.Length == 5 && values[0] == "`wartość`" && values[2] == "​" && values[4] == "**gotowy**",
            "inline values become separate chips in two columns, own formatting stays untouched");

        // „Testuj powiadomienia”: przykład dla każdego rodzaju, karty z tego samego kodu co prawdziwe.
        Type kindType = assembly.GetType("MinecraftHelper.Services.DiscordAlertKind")!;
        var samples = ((System.Collections.IEnumerable)assembly.GetType("MinecraftHelper.Services.DiscordNotificationSamples")!
            .GetProperty("All", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!).Cast<object>().ToArray();
        object SampleProp(object sample, string name) => sample.GetType().GetProperty(name)!.GetValue(sample)!;
        Check(Enum.GetValues(kindType).Cast<object>().All(kind => samples.Any(sample => SampleProp(sample, "Kind").Equals(kind))),
            "the notification test has a sample for every notification kind");

        Type settingsType2 = assembly.GetType("MinecraftHelper.Services.DiscordSettings")!;
        object alertSettings = Activator.CreateInstance(settingsType2, nonPublic: true)!;
        settingsType2.GetProperty("OwnerUserIds")!.SetValue(alertSettings, "123456789012345678");
        MethodInfo buildAlert = assembly.GetType("MinecraftHelper.Services.DiscordNotifier")!.GetMethod("BuildAlert", BindingFlags.Static | BindingFlags.Public)!;
        string? Content(string kind, bool urgent)
        {
            object result = buildAlert.Invoke(null, new object?[] { alertSettings, Enum.Parse(kindType, kind), "Konto główne", "Tytuł", "Opis", urgent, null, null })!;
            return (string?)result.GetType().GetField("Item2")!.GetValue(result);
        }
        Check(Content("Protection", false)!.StartsWith("<@123456789012345678> **Tytuł** — Konto główne", StringComparison.Ordinal), "alarms ping the owner with the headline above the card");
        Check(Content("Reconnect", false) == null && Content("Reconnect", true) != null && Content("MiningStartStop", false) == null,
            "routine notifications stay silent, a failed reconnect pings");
    }

    private static void VerifyNativeHudIsolation()
    {
        // A real foreign HWND, without touching the Minecraft window or its input.
        IntPtr foreignWindow = GetShellWindow();
        // An isolated test desktop may have no Explorer shell. Its desktop HWND
        // is still a real system-owned window suitable for this owner rejection.
        if (foreignWindow == IntPtr.Zero)
            foreignWindow = GetDesktopWindow();
        _ = GetWindowThreadProcessId(foreignWindow, out uint processId);
        Check(foreignWindow != IntPtr.Zero && processId != (uint)Environment.ProcessId,
            "native HUD test has a valid foreign owner candidate");
        Type type = typeof(MainWindow).Assembly.GetType("MinecraftHelper.OverlayHudWindow")!;
        var hud = (System.Windows.Window)Activator.CreateInstance(type, nonPublic: true)!;
        try
        {
            MethodInfo setOwner = type.GetMethod("SetOwnerWindowHandle")!;
            setOwner.Invoke(hud, new object[] { foreignWindow });
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(hud).EnsureHandle();
            Check(GetWindow(handle, 4) == IntPtr.Zero, "miner HUD rejects a foreign owner before native creation");
            setOwner.Invoke(hud, new object[] { foreignWindow });
            Check(GetWindow(handle, 4) == IntPtr.Zero, "miner HUD also rejects reattaching a foreign owner after creation");
            Check(!hud.IsVisible && !hud.IsActive, "HUD isolation test never shows or activates its window");
        }
        finally
        {
            hud.Close();
        }
    }

    private static void VerifyNativeTextCarrier()
    {
        // Translate only our private packet in a message-only HWND. This must
        // not synthesize printable text in addition to the explicit WM_CHAR.
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("MinecraftHelper.TextCarrierTest")
        {
            ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0
        });
        var packet = new NativeMessage
        {
            Window = source.Handle, Message = 0x0100, WParam = (IntPtr)0xE7, LParam = (IntPtr)1
        };
        _ = TranslateMessage(ref packet);
        bool printable = false;
        while (PeekMessage(out NativeMessage generated, source.Handle, 0x0102, 0x0102, 1))
            printable |= generated.WParam.ToInt64() is >= 32 and not 127;
        Check(!printable, "the native VK_PACKET carrier generates no duplicate printable character");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private static void VerifyEmergencyDamageSoundArming()
    {
        MethodInfo method = typeof(MainWindow).GetMethod("ShouldMonitorEmergencyDamageSound", Static)!;
        bool Should(bool enabled, bool calibrated, bool minerActive, bool manualTest) =>
            (bool)method.Invoke(null, new object[] { enabled, calibrated, minerActive, manualTest })!;

        Check(!Should(false, true, true, false), "damage listener stays off when option is disabled");
        Check(!Should(true, false, true, false), "damage listener stays off without calibration");
        Check(!Should(true, true, false, false), "damage listener stays off without active miner");
        Check(Should(true, true, true, false), "damage listener arms only with enabled calibrated active miner");
        Check(Should(false, false, false, true), "manual safe test can listen without miner");
    }

    private static void VerifyEmergencyDisconnectSequence()
    {
        MethodInfo calculateY = typeof(MainWindow).GetMethod("CalculateMinecraftDisconnectButtonClientY", Static)!;
        int Y(int width, int height) => (int)calculateY.Invoke(null, new object[] { width, height })!;
        Check(Y(1920, 1080) == 612, "disconnect button centre is correct at 1920x1080 GUI x3");
        Check(Y(1280, 720) == 522, "disconnect button centre is correct at 1280x720 GUI x3");
        Check(Y(854, 480) == 348, "disconnect button centre respects Minecraft minimum GUI dimensions");
        Check(Y(0, 0) == 0, "disconnect coordinate rejects an empty client area");

        // Home w GUI, pole Direct Connect i Disconnect liczą tę samą skalę co gra: Large (3)
        // obniżone w małym oknie. Stała skala 3 psuła test /home przy oknie konta 854×480.
        MethodInfo guiScale = typeof(MainWindow).GetMethod("GetMinecraftGuiScale", BindingFlags.Static | BindingFlags.NonPublic)!;
        int Scale(int width, int height) => (int)guiScale.Invoke(null, new object[] { width, height })!;
        Check(Scale(854, 480) == 2, "an 854x480 account window uses GUI scale 2 even with Large selected");
        Check(Scale(1280, 720) == 3 && Scale(1920, 1017) == 3, "larger windows keep GUI scale Large (3)");
        Check(Scale(800, 600) == 2 && Scale(639, 480) == 1, "GUI scale drops when the window is too small for the next step");

        MainWindow window = NewWindow();
        Check(!(bool)Call(window, "IsMinecraftGuiLikelyOpenForEmergency")!, "normal mining needs one ESC");
        Set(window, "_inventoryCleanupStage", EnumValue("InventoryCleanupStage", "WaitForInventory"));
        Check((bool)Call(window, "IsMinecraftGuiLikelyOpenForEmergency")!, "open Auto EQ receives a closing ESC first");
        Set(window, "_inventoryCleanupStage", EnumValue("InventoryCleanupStage", "None"));
        Set(window, "_kopacz533CommandStage", EnumValue("Kopacz533CommandStage", "TypeCommand"));
        Check((bool)Call(window, "IsMinecraftGuiLikelyOpenForEmergency")!, "open miner chat receives a closing ESC first");
        Set(window, "_kopacz533CommandStage", EnumValue("Kopacz533CommandStage", "None"));
        Set(window, "_autoReconnectStage", EnumValue("AutoReconnectStage", "EmergencyVerifyInventory"));
        Check((bool)Call(window, "IsMinecraftGuiLikelyOpenForEmergency")!, "emergency pickaxe verification closes EQ before Disconnect");
    }

    private static void VerifyEmergencyReconnectOptIn()
    {
        foreach (bool protection in new[] { false, true })
        foreach (bool reconnect in new[] { false, true })
        {
            var saved = new AppSettings
            {
                EmergencyDamageSoundEnabled = protection,
                EmergencyReconnectEnabled = reconnect
            };
            var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved))!;
            MainWindow window = NewWindow();
            Set(window, "_settings", loaded);
            Call(window, "EnsureSettingsConsistency");
            Check(loaded.EmergencyDamageSoundEnabled == protection && loaded.EmergencyReconnectEnabled == reconnect,
                $"normalization preserves independent protection/reconnect choices {protection}/{reconnect}");
            var protectionBox = new CheckBox { IsChecked = protection };
            var reconnectBox = new RadioButton { IsChecked = reconnect };
            var periodicBox = new CheckBox();
            Set(window, "ChkEmergencyDamageSoundEnabled", protectionBox);
            Set(window, "RbProtectionReturn", reconnectBox);
            Set(window, "ChkAutoReconnectEnabled", periodicBox);
            Check((bool)Call(window, "IsEmergencyReconnectEnabled")! == (protection && reconnect),
                "emergency reconnect requires both protection and explicit reconnect opt-in");
            foreach (bool periodic in new[] { false, true })
            {
                periodicBox.IsChecked = periodic;
                Check((bool)Call(window, "IsPeriodicAutoReconnectEnabled")! == (protection && reconnect && periodic),
                    "periodic Auto EQ control cannot reconnect while disconnect-only is selected");
            }

            Set(window, "_autoReconnectGeneration", 4);
            Set(window, "_emergencyReconnectActive", true);
            Check((bool)Call(window, "CanContinueEmergencyReconnect", 4)! == (protection && reconnect),
                "disconnect completion respects the current selected mode");
            if (!protection || !reconnect)
            {
                Check(!(bool)Call(window, "BeginEmergencyReconnectAfterDisconnect", DateTime.UtcNow, 4)!,
                    "disabled reconnect returns before opening profiles, starting countdown or sending input");
                Check(Get(window, "_autoReconnectStage")!.ToString() == "None",
                    "disconnect-only never creates a reconnect stage");
            }
            Set(window, "_autoReconnectGeneration", 5);
            Check(!(bool)Call(window, "CanContinueEmergencyReconnect", 4)!, "late Disconnect result cannot undo cancellation");
            Set(window, "_emergencyReconnectActive", false);
            Check(!(bool)Call(window, "CanContinueEmergencyReconnect", 5)!, "stopped emergency procedure cannot restart from a completed Disconnect");
        }

        MainWindow toggled = NewWindow();
        var enabled = new RadioButton { IsChecked = true };
        Set(toggled, "ChkEmergencyDamageSoundEnabled", new CheckBox { IsChecked = true });
        Set(toggled, "RbProtectionReturn", enabled);
        Set(toggled, "_emergencyReconnectActive", true);
        Set(toggled, "_autoReconnectGeneration", 12);
        Check((bool)Call(toggled, "CanContinueEmergencyReconnect", 12)!, "configured reconnect can continue after successful Disconnect");
        enabled.IsChecked = false;
        Check(!(bool)Call(toggled, "CanContinueEmergencyReconnect", 12)!, "unchecking during Disconnect blocks the pending continuation immediately");
        Set(toggled, "_autoReconnectGeneration", 13);
        enabled.IsChecked = true;
        Check(!(bool)Call(toggled, "CanContinueEmergencyReconnect", 12)!, "off/on during Disconnect cannot revive the old alarm");
    }

    private static void VerifyDamageSoundFingerprint()
    {
        Type detector = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.DamageSoundDetector")!;
        MethodInfo create = detector.GetMethod("CreateFingerprint", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo similarity = detector.GetMethod("GetBestSimilarity", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo level = detector.GetMethod("CalculateLevelDb", BindingFlags.Static | BindingFlags.NonPublic)!;
        const int sampleRate = 48000;
        var samples = new float[4096];
        for (int i = 0; i < samples.Length; i++)
        {
            double time = i / (double)sampleRate;
            samples[i] = (float)(0.35 * Math.Sin(2 * Math.PI * 620 * time)
                + 0.18 * Math.Sin(2 * Math.PI * 1420 * time));
        }

        var fingerprint = (double[])create.Invoke(null, new object[] { samples, sampleRate })!;
        Check(fingerprint.Length == 32, "damage fingerprint has stable persisted size");
        Check(Array.TrueForAll(fingerprint, double.IsFinite), "damage fingerprint contains only finite values");

        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(typeof(double[]));
        IList templates = (IList)Activator.CreateInstance(listType)!;
        templates.Add((double[])fingerprint.Clone());
        double match = (double)similarity.Invoke(null, new object[] { fingerprint, templates })!;
        Check(match > 0.999, "identical damage fingerprint is recognized");

        double levelDb = (double)level.Invoke(null, new object[] { samples })!;
        Check(levelDb > -20 && levelDb < -3, "audible calibration sample has a plausible dB level");
        double silenceDb = (double)level.Invoke(null, new object[] { new float[4096] })!;
        Check(silenceDb <= -90, "silence stays below the detector floor");
    }

    private static void VerifyEmergencyProtectionCheckboxEditing()
    {
        foreach (bool protection in new[] { false, true })
        foreach (bool reconnect in new[] { false, true })
        foreach (bool periodic in new[] { false, true })
        {
            MainWindow window = NewWindow();
            var testBox = new CheckBox { IsChecked = true, IsEnabled = false };
            var reconnectBox = new RadioButton { IsChecked = reconnect };
            var periodicBox = new CheckBox { IsChecked = periodic, IsEnabled = false };
            Set(window, "ChkEmergencyDamageSoundEnabled", new CheckBox { IsChecked = protection });
            Set(window, "ChkEmergencyDamageSoundTestMode", testBox);
            Set(window, "RbProtectionReturn", reconnectBox);
            Set(window, "ChkAutoReconnectEnabled", periodicBox);
            Set(window, "TxtAutoReconnectAvailability", new TextBlock());
            Call(window, "UpdateEmergencyProtectionOptionAvailability");
            Check(testBox.IsEnabled && periodicBox.IsEnabled,
                "saved protection preferences stay editable with dependencies disabled");
            Check(reconnectBox.IsChecked == reconnect && periodicBox.IsChecked == periodic,
                "refreshing availability never changes a checkbox selection");
            Check((bool)Call(window, "IsPeriodicAutoReconnectEnabled")! == (protection && reconnect && periodic),
                "editable checkbox does not bypass the automatic reconnect guards");
            if (periodic && !reconnect && protection)
                Check(((TextBlock)Get(window, "TxtAutoReconnectAvailability")!).Text.Contains("tylko Disconnect", StringComparison.Ordinal),
                    "checked but inactive periodic control explains its dependency");
        }

        MainWindow modeWindow = NewWindow();
        var settings = new AppSettings();
        var modeBox = new CheckBox { IsChecked = true };
        var dirtyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        Set(modeWindow, "_settings", settings);
        Set(modeWindow, "ChkEmergencyDamageSoundEnabled", new CheckBox { IsChecked = true });
        Set(modeWindow, "ChkEmergencyDamageSoundTestMode", modeBox);
        Set(modeWindow, "RbProtectionReturn", new RadioButton { IsChecked = false });
        Set(modeWindow, "ChkAutoReconnectEnabled", new CheckBox { IsChecked = false });
        Set(modeWindow, "TxtEmergencyDamageSoundStatus", new TextBlock());
        Set(modeWindow, "TxtEmergencyReconnectStatus", new TextBlock());
        Set(modeWindow, "TxtSettingsSaved", new TextBlock());
        Set(modeWindow, "EllSettingsSaved", new System.Windows.Shapes.Ellipse());
        Set(modeWindow, "_dirtyTimer", dirtyTimer);
        try
        {
            foreach (bool testMode in new[] { false, true, false })
            {
                modeBox.IsChecked = testMode;
                Call(modeWindow, "ChkEmergencyDamageSoundSetting_Changed", modeBox, new System.Windows.RoutedEventArgs());
                Check(modeBox.IsChecked == testMode && settings.EmergencyDamageSoundTestMode == testMode,
                    "test mode choice applies directly without a modal or reverting the checkbox");
                Check((bool)Get(modeWindow, "_pendingChanges")!, "test mode choice schedules its normal settings save");
            }
        }
        finally
        {
            dirtyTimer.Stop();
        }
    }

    private static void VerifyBundledDamageSoundReferences()
    {
        DirectoryInfo directory = FindRepositoryDirectory();
        string soundDirectory = Path.Combine(directory.FullName, "MinecraftHelper", "Assets", "Sounds", "Damage");
        string[] files = new[] { "hit1.ogg", "hit2.ogg", "hit3.ogg", "hit4.ogg" }
            .Select(name => Path.Combine(soundDirectory, name))
            .ToArray();
        Type detector = typeof(MainWindow).Assembly.GetType("MinecraftHelper.Services.DamageSoundDetector")!;
        MethodInfo load = detector.GetMethod("LoadReferenceFiles", BindingFlags.Static | BindingFlags.Public)!;
        object result = load.Invoke(null, new object[] { files })!;
        Type resultType = result.GetType();
        int sourceFileCount = (int)resultType.GetProperty("SourceFileCount")!.GetValue(result)!;
        IEnumerable templates = (IEnumerable)resultType.GetProperty("Templates")!.GetValue(result)!;
        int templateCount = templates.Cast<object>().Count();
        Check(sourceFileCount == 4, "all four fixed Minecraft damage sounds are loaded");
        Check(templateCount >= 8, "fixed damage sounds produce multiple alignment-tolerant fingerprints");
    }

    private static void VerifyCommandCountdownPause()
    {
        DateTime started = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (string miner in new[] { "533", "633" })
        {
            MainWindow window = NewWindow();
            object owner = EnumValue("InventoryCleanupOwner", "Kopacz" + miner);
            Set(window, "_kopacz" + miner + "RuntimeEnabled", true);
            Set(window, "_nextKopacz" + miner + "CommandAtUtc", started.AddSeconds(18));

            Call(window, "PauseKopaczCommandCountdown", owner, started);
            Check((bool)Call(window, "IsKopaczCommandCountdownPaused", owner)!, miner + " countdown paused");
            Check((int)Call(window, "GetKopaczCommandRemainingSeconds", owner, started.AddMinutes(2))! == 18,
                miner + " countdown does not elapse during full EQ/CX/eating");

            Call(window, "ResumeKopaczCommandCountdown", started.AddSeconds(42));
            Check(!(bool)Call(window, "IsKopaczCommandCountdownPaused", owner)!, miner + " countdown resumed");
            Check((DateTime)Get(window, "_nextKopacz" + miner + "CommandAtUtc")! == started.AddSeconds(60),
                miner + " deadline moved by paused duration");
            Check((int)Call(window, "GetKopaczCommandRemainingSeconds", owner, started.AddSeconds(42))! == 18,
                miner + " resumes from preserved remainder");

            // A cancelled miner must clear pause bookkeeping without moving a
            // deadline that no longer belongs to an active runtime.
            Call(window, "PauseKopaczCommandCountdown", owner, started.AddSeconds(42));
            Set(window, "_kopacz" + miner + "RuntimeEnabled", false);
            Call(window, "ResumeKopaczCommandCountdown", started.AddSeconds(50));
            Check((DateTime)Get(window, "_nextKopacz" + miner + "CommandAtUtc")! == started.AddSeconds(60),
                miner + " cancellation does not reschedule a dead runtime");
        }

        DateTime nearMaximum = DateTime.MaxValue.AddTicks(-5);
        DateTime saturated = (DateTime)typeof(MainWindow).GetMethod("AddWithoutOverflow", Static)!
            .Invoke(null, new object[] { nearMaximum, TimeSpan.FromSeconds(1) })!;
        Check(saturated == DateTime.MaxValue, "deadline extension cannot overflow DateTime");
    }

    private static void VerifyCobbleXCommandRepetition()
    {
        var defaults = new MinecraftHelper.Models.AppSettings();
        Check(!defaults.CobbleXRepeatThreeTimes, "old and new settings keep a single CX send by default");
        defaults.CobbleXRepeatThreeTimes = true;
        string json = System.Text.Json.JsonSerializer.Serialize(defaults);
        Check(System.Text.Json.JsonSerializer.Deserialize<MinecraftHelper.Models.AppSettings>(json)!.CobbleXRepeatThreeTimes,
            "triple CX checkbox survives saving and loading settings");

        foreach (int target in new[] { 1, 3 })
        foreach (bool gold in new[] { false, true })
        foreach (bool food in new[] { false, true })
        {
            MainWindow window = NewWindow();
            Set(window, "ChkCobbleXRepeatThreeTimes", new System.Windows.Controls.CheckBox { IsChecked = target == 3 });
            Set(window, "ChkGoldDepositEnabled", new System.Windows.Controls.CheckBox { IsChecked = gold });
            Check((int)Call(window, "GetConfiguredCobbleXCommandCount")! == target, "CX checkbox selects one or three commands");
            Set(window, "_inventoryCleanupCobbleXCommandTargetCount", target);
            Set(window, "_goldDepositPending", gold);
            Set(window, "_inventoryCleanupEatAfterCleanupPending", food);
            for (int sent = 1; sent <= target; sent++)
            {
                DateTime now = new DateTime(2026, 10, 25, 12, 0, sent, DateTimeKind.Utc);
                Set(window, "_inventoryCleanupStage", EnumValue("InventoryCleanupStage", "SubmitCobbleXCommand"));
                Set(window, "_inventoryCleanupCobbleXCommandPending", true);
                Check((bool)Call(window, "AdvanceCobbleXCommandSequence", now)!, "CX send is advanced exactly once");
                Check((int)Get(window, "_inventoryCleanupCobbleXCommandSendCount")! == sent && (bool)Get(window, "_inventoryCleanupCobbleXCommandSent")!, "actual CX send count is recorded");
                string expectedStage = sent < target ? "OpenCobbleXChat" : gold ? "DepositGold" : food ? "SelectFoodSlot" : "ResumeMining";
                Check(Get(window, "_inventoryCleanupStage")!.ToString() == expectedStage, "gold, food and mining wait until the complete CX series");
                Check((bool)Get(window, "_inventoryCleanupCobbleXCommandPending")! == (sent < target), "CX is pending only while a repetition remains");
                Check((DateTime)Get(window, "_nextInventoryCleanupStageAtUtc")! > now, "CX repetition waits on the timer instead of a blocking loop");
            }
            Check(!(bool)Call(window, "AdvanceCobbleXCommandSequence", DateTime.UtcNow)!, "completed CX series cannot produce a fourth send");
        }
    }

    private static void VerifyMiningLogMessages()
    {
        Check(InventoryStatus("completed", remainingStacks: 0, dropPasses: 0) == "Zakończono — EQ było czyste",
            "clean EQ message without drop passes");
        Check(InventoryStatus("completed", remainingStacks: 0, dropPasses: 2) == "Zakończono — EQ wyczyszczone • 2 przebiegi",
            "clean EQ message after dropping");
        Check(InventoryStatus("completed", remainingStacks: 1, dropPasses: 3) == "Nie wszystko wyrzucono • pozostał 1 stos • 3 przebiegi",
            "remaining stack singular message");
        Check(InventoryStatus("completed", remainingStacks: 2, dropPasses: 3) == "Nie wszystko wyrzucono • pozostały 2 stosy • 3 przebiegi",
            "remaining stacks paucal message");
        Check(InventoryStatus("completed", remainingStacks: 5, dropPasses: 3) == "Nie wszystko wyrzucono • pozostało 5 stosów • 3 przebiegi",
            "remaining stacks plural message");
        Check(InventoryStatus("aborted", remainingStacks: 0, dropPasses: 1) == "Przerwano — sprawdź powód",
            "aborted EQ message");
        Check(InventoryStatus("interrupted", remainingStacks: 0, dropPasses: 1) == "Niedokończono — program lub makro zatrzymane",
            "interrupted EQ message");
        Check(InventoryStatus("in-progress", remainingStacks: 0, dropPasses: 0) == "Auto EQ w toku",
            "active EQ message");
    }

    private static string InventoryStatus(string status, int remainingStacks, int dropPasses)
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        Type entryType = assembly.GetType("MinecraftHelper.Services.MiningLogEntry")!;
        object entry = Activator.CreateInstance(entryType)!;
        entryType.GetProperty("Kind")!.SetValue(entry, "inventory-session");
        entryType.GetProperty("Status")!.SetValue(entry, status);
        entryType.GetProperty("RemainingStacks")!.SetValue(entry, remainingStacks);
        entryType.GetProperty("DropPasses")!.SetValue(entry, dropPasses);

        Type windowType = assembly.GetType("MinecraftHelper.MiningLogsPanel")!;
        MethodInfo method = windowType.GetMethod("GetStatusLabel", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, new[] { entry })!;
    }

    private static void VerifyEmergencyMiningLogMessages()
    {
        (string EventType, string Expected)[] events =
        {
            ("emergency-protection-started", "Ochrona awaryjna • start"),
            ("emergency-reconnect-joined", "Ochrona awaryjna • dołączono"),
            ("emergency-pickaxe-detected", "Kontrola kilofa • wykryto"),
            ("emergency-pickaxe-missing", "Kontrola kilofa • brak"),
            ("emergency-home-command-sent", "Ochrona awaryjna • home"),
            ("emergency-mining-resumed", "Ochrona awaryjna • wznowiono kopanie"),
            ("emergency-shutdown", "Ochrona awaryjna • zamknięcie programu"),
            ("emergency-protection-finished", "Ochrona awaryjna • wynik")
        };

        Assembly assembly = typeof(MainWindow).Assembly;
        Type entryType = assembly.GetType("MinecraftHelper.Services.MiningLogEntry")!;
        Type windowType = assembly.GetType("MinecraftHelper.MiningLogsPanel")!;
        MethodInfo method = windowType.GetMethod("GetEventLabel", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach ((string eventType, string expected) in events)
        {
            object entry = Activator.CreateInstance(entryType)!;
            entryType.GetProperty("Kind")!.SetValue(entry, "automation-event");
            entryType.GetProperty("EventType")!.SetValue(entry, eventType);
            Check((string)method.Invoke(null, new[] { entry })! == expected,
                "emergency log label " + eventType);
        }
    }

    private static void VerifyMiner(string miner)
    {
        object owner = EnumValue("InventoryCleanupOwner", "Kopacz" + miner);
        Type stages = Nested("InventoryCleanupStage");
        foreach (object stage in Enum.GetValues(stages))
        {
            if (stage.ToString() == "None") continue;
            MainWindow window = NewWindow();
            Set(window, "_kopacz" + miner + "RuntimeEnabled", true);
            Set(window, "_kopacz" + miner + "ResumeMiningPending", true);
            Set(window, "_kopacz" + miner + "PendingCommand", "/pending");
            Set(window, "_inventoryCleanupGeneration", 7);
            Set(window, "_inventoryCleanupOwner", owner);
            Set(window, "_inventoryCleanupStage", stage);
            Set(window, "_inventoryCleanupPendingCobbleXCommand", "/cx");
            Set(window, "_inventoryCleanupCobbleXCommandPending", true);
            Set(window, "_inventoryCleanupCobbleXCommandTargetCount", 3);
            Set(window, "_inventoryCleanupCobbleXCommandSendCount", 1);
            Set(window, "_inventoryCleanupEatAfterCleanupPending", true);
            ((IList)Get(window, "_inventoryCleanupTargets")!).Add(new System.Drawing.Point(5, 5));

            Check((bool)Call(window, "HasMiningWork", owner)!, miner + " can stop " + stage);
            Call(window, "StopMiningRuntime", owner);
            Check(!(bool)Get(window, "_kopacz" + miner + "RuntimeEnabled")!, "runtime stopped");
            Check(!(bool)Get(window, "_kopacz" + miner + "ResumeMiningPending")!, "resume cancelled");
            Check((string)Get(window, "_kopacz" + miner + "PendingCommand")! == "", "command cancelled");
            Check(Get(window, "_inventoryCleanupStage")!.ToString() == "None", "EQ stage cancelled");
            Check(Get(window, "_inventoryCleanupOwner")!.ToString() == "None", "EQ owner reset");
            Check(((IList)Get(window, "_inventoryCleanupTargets")!).Count == 0, "remaining drops removed");
            Check(!(bool)Get(window, "_inventoryCleanupCobbleXCommandPending")!, "CX cancelled");
            Check((int)Get(window, "_inventoryCleanupCobbleXCommandSendCount")! == 0 && (int)Get(window, "_inventoryCleanupCobbleXCommandTargetCount")! == 1,
                "STOP cancels the remaining CX repetitions and resets the send counter");
            Check(!(bool)Call(window, "AdvanceCobbleXCommandSequence", DateTime.UtcNow)!, "cancelled CX series cannot be revived by a delayed callback");
            Check(!(bool)Get(window, "_inventoryCleanupEatAfterCleanupPending")!, "eating cancelled");
            Check((DateTime)Get(window, "_nextInventoryCleanupAtUtc")! == DateTime.MaxValue, "no rescheduled EQ");
            Check((int)Get(window, "_inventoryCleanupGeneration")! == 8, "old scan invalidated");
            Check(!(bool)Call(window, "HasMiningWork", owner)!, "stop does not leave pending work");

            // Restart the same miner while an old worker is finishing. The same
            // owner/stage must not make the old generation valid again.
            Set(window, "_kopacz" + miner + "RuntimeEnabled", true);
            Set(window, "_inventoryCleanupOwner", owner);
            Set(window, "_inventoryCleanupStage", EnumValue("InventoryCleanupStage", "WaitForInventory"));
            Check(!(bool)Call(window, "IsInventoryCleanupScanCurrent", 7, owner)!, "late result rejected after restart");
            Check((bool)Call(window, "IsInventoryCleanupScanCurrent", 8, owner)!, "current result accepted");
        }

        foreach (object stage in Enum.GetValues(Nested("Kopacz" + miner + "CommandStage")))
        {
            MainWindow window = NewWindow();
            Set(window, "_kopacz" + miner + "RuntimeEnabled", true);
            Set(window, "_kopacz" + miner + "CommandStage", stage);
            Call(window, "StopMiningRuntime", owner);
            Check(Get(window, "_kopacz" + miner + "CommandStage")!.ToString() == "None", "cancel command stage " + stage);
        }
    }

    // Ekran rozłączenia: klik dopiero po 10 s ciągłej widoczności; inny ekran zeruje licznik.
    private static void VerifyDisconnectScreenWait()
    {
        MainWindow window = NewWindow();
        object disconnected = EnumValue("AutoReconnectScreenKind", "Disconnected");
        DateTime start = new(2026, 10, 29, 15, 0, 0, DateTimeKind.Utc);
        void Schedule(double seconds) => Call(window, "ScheduleBlockedDisconnectButtonClick", disconnected, start.AddSeconds(seconds));
        string Stage() => Get(window, "_autoReconnectStage")!.ToString()!;

        Schedule(0);
        Check(Stage() == "AnalyzeScreen" && (DateTime)Get(window, "_nextAutoReconnectActionAtUtc")! == start.AddSeconds(10),
            "a disconnect screen is re-checked after 10 s instead of being clicked blindly");
        Schedule(4);
        Check(Stage() == "AnalyzeScreen" && (DateTime)Get(window, "_nextAutoReconnectActionAtUtc")! == start.AddSeconds(10),
            "seeing the same screen again does not restart the 10 s wait");
        Schedule(10);
        Check(Stage() == "WaitForDisconnectButtonUnlock", "after 10 s of the same disconnect screen the button is clicked");

        Set(window, "_autoReconnectDisconnectScreenSinceUtc", DateTime.MinValue);
        Set(window, "_autoReconnectPendingScreenKind", EnumValue("AutoReconnectScreenKind", "Unknown"));
        Schedule(30);
        Check(Stage() == "AnalyzeScreen", "a disconnect screen that appears again starts a fresh 10 s wait");

        // Test „Pełny cykl”: kopanie rusza tylko po udanym powrocie; błąd albo STOP reconnectu je anuluje.
        object kopacz633 = EnumValue("InventoryCleanupOwner", "Kopacz633");
        Set(window, "_fullCycleTestStartOwner", kopacz633);
        Check(Call(window, "TakeFullCycleTestOwner")!.ToString() == "Kopacz633" && Get(window, "_fullCycleTestStartOwner")!.ToString() == "None",
            "the full-cycle test hands its miner over exactly once");
        string source = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.xaml.cs"));
        int stopStart = source.IndexOf("private void StopAutoReconnect(", StringComparison.Ordinal);
        int clear = source.IndexOf("_fullCycleTestStartOwner = InventoryCleanupOwner.None;", stopStart, StringComparison.Ordinal);
        int firstLog = source.IndexOf("RecordAutomationLogEvent(", stopStart, StringComparison.Ordinal);
        Check(stopStart > 0 && clear > stopStart && clear < firstLog, "every reconnect ending (failure, STOP) cancels the pending mining start first");
        int success = source.IndexOf("Reconnect zakończony: EQ potwierdzone", StringComparison.Ordinal);
        int take = source.LastIndexOf("TakeFullCycleTestOwner()", success, StringComparison.Ordinal);
        Check(take > 0 && success - take < 400, "only the confirmed reconnect success takes the miner before stopping the reconnect");

        // !join: ten sam start po reconnekcie co test pełnego cyklu, z wynikiem na Discordzie.
        int discordCapture = source.IndexOf("bool reportDiscordJoinFailure =", stopStart, StringComparison.Ordinal);
        int discordFailure = source.IndexOf("ReportDiscordJoinResult(false, message);", stopStart, StringComparison.Ordinal);
        Check(discordCapture > stopStart && discordCapture < firstLog && discordFailure > firstLog,
            "an interrupted !join reports why on Discord once — after the log entry, whose reconnect card it replaces");
        string discord = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.Discord.cs"));
        Check(discord.Contains("if (IsDiscordJoinAnnouncing && eventType is MiningLogEventTypes.AutoReconnectStarted or MiningLogEventTypes.AutoReconnectFinished)", StringComparison.Ordinal)
            && discord.IndexOf("if (IsDiscordJoinAnnouncing)", discord.IndexOf("private void NotifyDiscordMiningStarted", StringComparison.Ordinal), StringComparison.Ordinal) > 0,
            "one !join produces one result card, not separate reconnect and mining-start cards");
        string serverCommands = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.ServerCommands.cs"));
        Check(serverCommands.Contains("screenshotDelay: disconnected ? ScreenshotAfterDisconnect : TimeSpan.Zero", StringComparison.Ordinal)
            && serverCommands.Contains("screenshotDelay: started ? ScreenshotAfterInventoryClose : TimeSpan.Zero", StringComparison.Ordinal)
            && discord.Contains("screenshotDelay: disconnected ? ScreenshotAfterDisconnect : TimeSpan.Zero", StringComparison.Ordinal),
            "result screenshots wait until the game shows the outcome (off the server, inventory closed)");
        string notifier = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "Services", "DiscordNotifier.cs"));
        int delay = notifier.IndexOf("await Task.Delay(screenshotDelay)", StringComparison.Ordinal);
        Check(delay > 0 && delay < notifier.IndexOf("MinerLivePreviewService.TryCapture(gameWindow", StringComparison.Ordinal), "the delay runs before the capture, off the UI thread");
        string tests = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.ProtectionTests.cs"));
        int afterCycle = tests.IndexOf("private void StartMiningAfterFullCycleTest(", StringComparison.Ordinal);
        Check(afterCycle > 0 && tests.IndexOf("ReportDiscordJoinResult(started, message);", afterCycle, StringComparison.Ordinal) > afterCycle,
            "a successful !join reports the mining start on Discord");
        string server = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.ServerCommands.cs"));
        int joinStart = server.IndexOf("private (bool Joining, string Message) JoinServerFromRemoteCommand(", StringComparison.Ordinal);
        int joinBegin = server.IndexOf("BeginFullAutoReconnect(manual: true);", joinStart, StringComparison.Ordinal);
        Check(joinStart > 0 && new[] { "GetMiningStartBlocker(requested)", "GetRemoteGameInputBlocker()", "GetSelectedAutoReconnectServerProfile()", "IsProtectionOrReconnectRunning()" }
                .All(guard => server.IndexOf(guard, joinStart, StringComparison.Ordinal) is int at && at > joinStart && at < joinBegin),
            "!join checks mining, background input, return profile and running procedures before reconnecting");
        int leaveStart = server.IndexOf("private (bool Leaving, string Message) LeaveServerFromRemoteCommand()", StringComparison.Ordinal);
        int leaveRun = server.IndexOf("_ = LeaveServerAsync();", leaveStart, StringComparison.Ordinal);
        Check(leaveStart > 0 && server.IndexOf("GetRemoteGameInputBlocker()", leaveStart, StringComparison.Ordinal) < leaveRun
                && server.IndexOf("StopMiningFromRemoteCommand();", leaveStart, StringComparison.Ordinal) < leaveRun
                && server.IndexOf("StopAutoReconnect(", leaveStart, StringComparison.Ordinal) < leaveRun,
            "!leave never types into another app, stops mining and any reconnect before disconnecting");

        MethodInfo classify = typeof(MainWindow).GetMethod("ClassifyAutoReconnectScreen", Static)!;
        Check(classify.Invoke(null, new object?[] { "Disconnected by Server Wystąpił błąd z użytkownikiem! Back to server list" })!.ToString() == "Disconnected",
            "BlazingPack 'Disconnected by Server' screen is a disconnect screen, not the server list");
    }

    private static void VerifyReconnectGuards()
    {
        MainWindow window = NewWindow();
        IntPtr foreground = (IntPtr)typeof(MainWindow).GetMethod("GetForegroundWindow", Static)!.Invoke(null, null)!;
        Set(window, "_targetGameWindowHandle", foreground);
        object owner = EnumValue("InventoryCleanupOwner", "Kopacz533");
        Set(window, "_autoReconnectResumeKopacz533", true);
        foreach (object stage in Enum.GetValues(Nested("AutoReconnectStage")))
        {
            if (stage.ToString() == "None") continue;
            Set(window, "_autoReconnectStage", stage);
            Set(window, "_autoReconnectGeneration", 4);
            Check((bool)Call(window, "HasMiningWork", owner)!, "mining stop available during reconnect " + stage);
            Set(window, "_autoReconnectGeneration", 5);
            Check(!(bool)Call(window, "IsAutoReconnectResultCurrent", 4, stage, foreground)!, "late reconnect result rejected " + stage);
        }
        Set(window, "_autoReconnectStage", EnumValue("AutoReconnectStage", "None"));
        Check(!(bool)Call(window, "HasMiningWork", owner)!, "completed reconnect is not active work");
    }

    private static void VerifyReconnectScreenClassification()
    {
        MethodInfo classify = typeof(MainWindow).GetMethod("ClassifyAutoReconnectScreen", Static)!;
        object Classify(string text) => classify.Invoke(null, new object?[] { text })!;

        Check(Classify("Play Multiplayer Offline 34 xLajtHC.pl thinking").ToString() == "ServerList",
            "BlazingPack Play Multiplayer screen is recognized as server list");
        Check(Classify("Play Multiplayer [MH] DIRECT CONNECT").ToString() == "ServerList",
            "tagged Direct Connect button is recognized on the server list");
        Check(Classify("Play --- Multi_player").ToString() == "ServerList",
            "server-list title tolerates OCR separators");
        Check(Classify("Direct Connect Server Address").ToString() == "DirectConnect",
            "Direct Connect screen keeps priority over server-list text");

        // Menu główne ma napis „Multiplayer” — nie może być wzięte za listę serwerów
        // (kliknięcie „Direct Connect” przy dole trafiłoby w okolice „Quit Game”).
        Check(Classify("Singleplayer Multiplayer discord.gg/BlazingPack Options... Quit Game Copyright Mojang AB. Do not distribute! Minecraft 1.8.8 (BlazingPack.pl)").ToString() == "MainMenu",
            "BlazingPack title screen is recognized as the main menu, not the server list");
        Check(Classify("Gra jednoosobowa Gra wieloosobowa Opcje... Wyjdź z gry").ToString() == "MainMenu",
            "Polish main menu is recognized");
        Check(Classify("Multiplayer Quit Game").ToString() == "MainMenu", "partial OCR of the main menu is still the main menu");
        Check(Classify("Play Multiplayer Join Server Direct Connect Add server Edit Delete Refresh Cancel").ToString() == "ServerList",
            "the full server-list screen stays a server list");
        Check(Classify("[MH] SERVER LIST Join Server Direct Connect Add server Singleplayer").ToString() == "ServerList",
            "server-list buttons win over a stray main-menu word");
        Check(Classify("Connection Lost Disconnected").ToString() == "Disconnected",
            "disconnect screen remains recognized");
    }

    private static void VerifyReconnectOverlayStages()
    {
        MethodInfo label = typeof(MainWindow).GetMethod("GetAutoReconnectOverlayStageLabel", Static)!;
        MethodInfo nextAction = typeof(MainWindow).GetMethod("GetAutoReconnectOverlayNextAction", Static)!;
        foreach (object stage in Enum.GetValues(Nested("AutoReconnectStage")))
        {
            if (stage.ToString() == "None")
                continue;

            string value = (string)label.Invoke(null, new[] { stage })!;
            Check(!string.IsNullOrWhiteSpace(value) && value != "AUTO RECONNECT",
                "HUD has a dedicated reconnect label for " + stage);
            string next = (string)nextAction.Invoke(null, new[] { stage, (object)false })!;
            Check(!string.IsNullOrWhiteSpace(next) && next != "Oczekiwanie na kolejny krok",
                "HUD describes the next reconnect action for " + stage);
        }

        object waitForTeleport = EnumValue("AutoReconnectStage", "WaitForTeleport");
        Check((string)nextAction.Invoke(null, new[] { waitForTeleport, (object)false })! == "Slot 1 i wznowienie Kopacza",
            "HUD announces mining resume after a successful home teleport");
        Check((string)nextAction.Invoke(null, new[] { waitForTeleport, (object)true })! == "Bezpieczne zamknięcie Minecraft Helper",
            "HUD announces shutdown after the missing-pickaxe home teleport");
    }

    private static void VerifyReconnectTeleportTiming()
    {
        MethodInfo calculate = typeof(MainWindow).GetMethod("CalculateAutoReconnectTeleportWaitSeconds", Static)!;
        int Wait(int configuredSeconds) => (int)calculate.Invoke(null, new object[] { configuredSeconds })!;

        Check(Wait(10) == 12, "teleport wait adds the two-second safety buffer");
        Check(Wait(1) == 3, "minimum teleport wait keeps the safety buffer");
        Check(Wait(120) == 122, "maximum configured teleport wait keeps the safety buffer");
        Check(Wait(0) == 3, "invalid zero teleport wait is clamped before adding the buffer");
    }

    private static void VerifyPriority()
    {
        DirectoryInfo directory = FindRepositoryDirectory();
        // Klon na Windowsie (core.autocrlf) ma CRLF — wzorce poniżej obejmują kilka linii.
        string source = File.ReadAllText(Path.Combine(directory.FullName, "MinecraftHelper", "MainWindow.xaml.cs")).Replace("\r\n", "\n");
        int core = source.IndexOf("private void RunMacroTickCore()", StringComparison.Ordinal);
        int stop = source.IndexOf("if (!backgroundInput && TryStopMiningFromBinds())", core, StringComparison.Ordinal);
        int reconnect = source.IndexOf("if (_autoReconnectStage != AutoReconnectStage.None)", core, StringComparison.Ordinal);
        int commands = source.IndexOf("bool internalCommandTyping", core, StringComparison.Ordinal);
        Check(stop > core && stop < reconnect && stop < commands, "STOP precedes reconnect and command guards");

        // In background mining the physical keyboard belongs to another app:
        // binds must be treated like internally typed commands.
        int backgroundSession = source.IndexOf("bool backgroundInput = UpdateBackgroundMiningSession(", core, StringComparison.Ordinal);
        int bindSuppression = source.IndexOf("bool internalCommandTyping =\n                backgroundInput ||", core, StringComparison.Ordinal);
        Check(backgroundSession > core && backgroundSession < stop && bindSuppression > stop,
            "background mining suppresses every bind, including STOP");

        int reconnectTick = source.IndexOf("private async void RunAutoReconnectTick", StringComparison.Ordinal);
        int reconnectHudRefresh = source.IndexOf("RefreshOverlayHud(now);", reconnectTick, StringComparison.Ordinal);
        int reconnectFocusGuard = source.IndexOf("if (!IsGameInputAvailableFor(_targetGameWindowHandle))", reconnectTick, StringComparison.Ordinal);
        Check(reconnectHudRefresh > reconnectTick && reconnectHudRefresh < reconnectFocusGuard,
            "reconnect HUD refreshes before timer wait and focus guards");
    }

    private static DirectoryInfo FindRepositoryDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MinecraftHelper", "MainWindow.xaml.cs")))
            directory = directory.Parent;
        return directory ?? throw new InvalidOperationException("Run the test from the repository.");
    }

    private static MainWindow NewWindow()
    {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        foreach (string name in new[]
        {
            "_inventoryCleanupTargets", "_inventoryCleanupInitialItemTypeCounts",
            "_inventoryCleanupInitialItemTypeStackCounts", "_inventoryCleanupItemTypeCounts",
            "_inventoryCleanupItemTypeStackCounts"
        })
        {
            var field = typeof(MainWindow).GetField(name, Instance)!;
            field.SetValue(window, Activator.CreateInstance(field.FieldType));
        }
        return window;
    }

    private static Type Nested(string name) => typeof(MainWindow).GetNestedType(name, BindingFlags.NonPublic)!;
    private static object EnumValue(string type, string value) => Enum.Parse(Nested(type), value);
    private static object? Get(object o, string n) => typeof(MainWindow).GetField(n, Instance)!.GetValue(o);
    private static void Set(object o, string n, object v) => typeof(MainWindow).GetField(n, Instance)!.SetValue(o, v);
    private static object? Call(object o, string n, params object[] args) => typeof(MainWindow).GetMethod(n, Instance)!.Invoke(o, args);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        _passed++;
    }
}
