using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Drawing = System.Drawing;

namespace MinecraftHelper.Services;

internal sealed record GoldDepositOptions(string Command, string MenuTitle, int Rows, int TargetSlot,
    int OpenDelayMs, bool Ingots, bool Blocks, bool IncludeHotbar, int Columns = 9)
{
    public bool IsSelected(string id) => (Ingots && id == "gold_ingot") || (Blocks && id == "gold_block");
    public static bool IsGold(string id) => id is "gold_ingot" or "gold_block";
    public string Validate() => string.IsNullOrWhiteSpace(Command) || !Command.StartsWith('/')
        || Command.Length > 256 || Command.Contains('\n') || Command.Contains('\r')
        ? "Wpisz pojedynczą komendę zaczynającą się od /."
        : string.IsNullOrWhiteSpace(MenuTitle) || MenuTitle.Count(char.IsLetterOrDigit) < 2 ? "Wpisz oczekiwany tytuł menu (np. Bazar)."
        : Rows is < 1 or > 6 || Columns is < 1 or > 9 || TargetSlot < 1 || TargetSlot > Rows * Columns ? "Niepoprawny układ menu lub pole wpłaty."
        : OpenDelayMs is < 500 or > 30000 ? "Oczekiwanie na menu musi wynosić 500–30000 ms."
        : !Ingots && !Blocks ? "Wybierz sztabki lub bloki złota." : string.Empty;

    public string ValidateMenuTitle(string title)
    {
        static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        if (!Normalize(title).Contains(Normalize(MenuTitle), StringComparison.Ordinal))
            return $"Nie potwierdzono tytułu menu. Oczekiwano „{MenuTitle}”, OCR odczytał: „{(string.IsNullOrWhiteSpace(title) ? "brak odczytu" : title.Trim())}”.";
        return string.Empty;
    }

    public string ValidateMenuIdentity(string title, string? bankItemId)
    {
        string titleError = ValidateMenuTitle(title);
        if (titleError.Length > 0) return titleError + " Ikony wpłaty jeszcze nie sprawdzono.";
        if (bankItemId == null || !IsGold(bankItemId))
            return $"Tytuł menu potwierdzony. W polu {TargetSlot} (rząd {(TargetSlot - 1) / Columns + 1}, kolumna {(TargetSlot - 1) % Columns + 1}) nie rozpoznano ikony złota z TXT. Odczyt: {bankItemId ?? "brak znacznika"}.";
        return string.Empty;
    }
}

internal enum GoldDepositAction { None, CloseInitialGui, OpenChat, TypeCommand, SubmitCommand, PickSource, Deposit, ReturnRejected, CloseMenu, Complete, Stop }
internal readonly record struct GoldDepositStep(GoldDepositAction Action, int SourceSlot = -1);
internal sealed record GoldDepositObservation(bool GuiOpen, bool MenuValid, bool CursorEmpty,
    DetectedInventoryItem? CursorItem, IReadOnlyList<DetectedInventoryItem> Items, string Error = "");

internal static class GoldDepositClickGuard
{
    public static string ValidateGeometry(Drawing.Rectangle expectedArea, Drawing.Rectangle actualArea,
        Drawing.Point expectedPointer, Drawing.Point actualPointer, bool guiOpen, bool headerVisible)
    {
        if (actualArea != expectedArea) return "Okno Minecrafta przesunięto lub zmieniono jego rozmiar przed kliknięciem.";
        if (!guiOpen) return "Menu zamknięto przed kliknięciem.";
        int dx = actualPointer.X - expectedPointer.X;
        int dy = actualPointer.Y - expectedPointer.Y;
        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
            return $"Poruszono mysz przed kliknięciem (przesunięcie {dx}, {dy} px).";
        return headerVisible ? "" : "Nie potwierdzono ramki wcześniej rozpoznanego menu przed kliknięciem.";
    }
}

internal enum GoldDepositTestDecision { Countdown, Run, Cancelled }

internal sealed class GoldDepositManualTestSession
{
    public IntPtr TargetWindow { get; }
    public DateTime ReadyAt { get; }
    public GoldDepositFlow Flow { get; }
    public bool Started { get; private set; }
    public string StopReason { get; private set; } = "";

    public GoldDepositManualTestSession(GoldDepositOptions options, IntPtr targetWindow, DateTime now)
    {
        TargetWindow = targetWindow;
        ReadyAt = now.AddSeconds(3);
        Flow = new GoldDepositFlow(options, ReadyAt);
    }

    public int RemainingSeconds(DateTime now) => Math.Max(0, (int)Math.Ceiling((ReadyAt - now).TotalSeconds));

    // gameInputAvailable: gra na wierzchu albo aktywna sesja kopania w tle dla tego okna.
    public GoldDepositTestDecision Tick(DateTime now, IntPtr selectedWindow, bool gameInputAvailable, bool stopPressed, bool otherAutomation)
    {
        if (StopReason.Length > 0) return GoldDepositTestDecision.Cancelled;
        if (stopPressed) StopReason = "Test przerwany przyciskiem STOP lub bindem kopacza.";
        else if (TargetWindow == IntPtr.Zero || selectedWindow != TargetWindow) StopReason = "Zmieniono lub utracono wybrane okno Minecrafta.";
        else if (otherAutomation) StopReason = "Uruchomiono inną automatyzację podczas testu wpłaty.";
        else if (now < ReadyAt) return GoldDepositTestDecision.Countdown;
        else if (!gameInputAvailable) StopReason = "Test przerwany — Minecraft nie ma fokusu, a kopanie w tle jest wyłączone lub niedostępne. Sprawdź przedmiot na kursorze.";
        if (StopReason.Length > 0) return GoldDepositTestDecision.Cancelled;
        Started = true;
        return GoldDepositTestDecision.Run;
    }
}

// Pure state machine: no Windows calls, screenshots, sleeps or user settings.
internal sealed class GoldDepositFlow
{
    private enum Phase { CloseInitial, OpenChat, Type, Submit, Inspect, Picked, Deposited, Returned, Closed, Done, Failed }
    private Phase _phase;
    private readonly DateTime _started;
    private DetectedInventoryItem _source;
    private int _beforeCount;
    private int _observations;
    public GoldDepositOptions Options { get; }
    public DateTime NextAt { get; private set; }
    public int ConfirmedStacks { get; private set; }
    public int ConfirmedItems { get; private set; }
    public string Message { get; private set; } = "Przygotowanie wpłaty złota";
    public bool NeedsScreen => _phase is Phase.Inspect or Phase.Picked or Phase.Deposited or Phase.Returned or Phase.Closed;
    public bool Finished => _phase is Phase.Done or Phase.Failed;
    public bool AwaitsTyping => _phase == Phase.Type;
    public bool HasExpired(DateTime now) => now - _started > TimeSpan.FromMinutes(3);

    public GoldDepositFlow(GoldDepositOptions options, DateTime now)
    {
        Options = options;
        _started = NextAt = now;
        string error = options.Validate();
        if (error.Length > 0) { _phase = Phase.Failed; Message = error; }
    }

    public GoldDepositStep Tick(DateTime now, GoldDepositObservation? screen = null)
    {
        if (_phase == Phase.Failed) return new(GoldDepositAction.Stop);
        if (_phase == Phase.Done) return new(GoldDepositAction.Complete);
        if (HasExpired(now)) return Fail("Przekroczono limit czasu wpłaty.");
        if (now < NextAt) return default;
        if (NeedsScreen && screen == null) return default;
        switch (_phase)
        {
            case Phase.CloseInitial: return Advance(Phase.OpenChat, GoldDepositAction.CloseInitialGui, now, 180, "Otwieranie komendy wpłaty");
            case Phase.OpenChat: return Advance(Phase.Type, GoldDepositAction.OpenChat, now, 180, "Wpisywanie komendy wpłaty");
            case Phase.Type: return Advance(Phase.Submit, GoldDepositAction.TypeCommand, now, 110, "Wysyłanie komendy wpłaty");
            case Phase.Submit: return Advance(Phase.Inspect, GoldDepositAction.SubmitCommand, now, Options.OpenDelayMs, "Oczekiwanie na menu wpłaty");
            case Phase.Closed:
                if (screen!.GuiOpen) return WaitOrFail(now, "Menu nie zostało zamknięte.");
                _phase = Phase.Done;
                Message = $"Wpłata zakończona: potwierdzono usunięcie {ConfirmedItems} szt. z {ConfirmedStacks} stosów złota z EQ.";
                return new(GoldDepositAction.Complete);
        }
        if (!screen!.MenuValid) return _phase == Phase.Inspect
            ? WaitOrFail(now, "Nie potwierdzono menu wpłaty: " + screen.Error)
            : Fail("Menu zmieniło się podczas przenoszenia. Sprawdź przedmiot na kursorze. " + screen.Error);

        int count = screen.Items.Where(i => i.ItemId == _source.ItemId).Sum(i => i.Quantity);
        bool sameCursor = screen.CursorItem is DetectedInventoryItem cursor && cursor.ItemId == _source.ItemId && cursor.Quantity == _source.Quantity;
        switch (_phase)
        {
            case Phase.Inspect:
                if (!screen.CursorEmpty) return Fail("Kursor nie jest pusty. Nie rozpoczęto kolejnego przenoszenia.");
                DetectedInventoryItem[] gold = screen.Items.Where(i => Options.IsSelected(i.ItemId))
                    .OrderBy(i => i.ItemId == "gold_block" ? 0 : 1).ThenBy(i => i.Slot).ToArray();
                if (gold.Length == 0) return Advance(Phase.Closed, GoldDepositAction.CloseMenu, now, 250, "Zamykanie menu wpłaty");
                if (ConfirmedStacks >= 36) return Fail("Osiągnięto limit 36 przeniesień w jednym skanie.");
                _source = gold[0];
                _beforeCount = screen.Items.Where(i => i.ItemId == _source.ItemId).Sum(i => i.Quantity);
                return Advance(Phase.Picked, GoldDepositAction.PickSource, now, 250, $"Pobieranie złota z pola EQ {_source.Slot + 1}", _source.Slot);
            case Phase.Picked:
                if (sameCursor && count == _beforeCount - _source.Quantity)
                    return Advance(Phase.Deposited, GoldDepositAction.Deposit, now, 350, "Przenoszenie złota na pole wpłaty", _source.Slot);
                string cursorRead = screen.CursorEmpty ? "pusty kursor"
                    : screen.CursorItem is DetectedInventoryItem carried ? $"{carried.ItemId} ×{carried.Quantity}" : "nierozpoznany przedmiot";
                return WaitOrFail(now, $"Nie potwierdzono pobrania złota. Oczekiwano {_source.ItemId} ×{_source.Quantity}; odczyt: {cursorRead}. Suma w EQ: {count}, oczekiwano {_beforeCount - _source.Quantity}.");
            case Phase.Deposited:
                if (screen.CursorEmpty && count == _beforeCount - _source.Quantity)
                {
                    ConfirmedStacks++;
                    ConfirmedItems += _source.Quantity;
                    _phase = Phase.Inspect;
                    _observations = 0;
                    NextAt = now.AddMilliseconds(100);
                    Message = $"Potwierdzone przeniesienia: {ConfirmedStacks} stosów / {ConfirmedItems} szt. (nie jest to odczyt salda banku)";
                    return default;
                }
                bool rejected = sameCursor && count == _beforeCount - _source.Quantity;
                if (rejected && ++_observations >= 3)
                    return Advance(Phase.Returned, GoldDepositAction.ReturnRejected, now, 250, "Serwer nie przyjął złota — odkładanie do źródłowego pola", _source.Slot);
                if (_observations >= 3) return Fail("Nie potwierdzono wpłaty ani pustego kursora. Sprawdź GUI.");
                return WaitOrFail(now, "Nie potwierdzono wpłaty. Sprawdź przedmiot na kursorze.", increment: !rejected);
            case Phase.Returned:
                if (screen.CursorEmpty && count == _beforeCount)
                    return Fail("Serwer nie przyjął złota. Odłożono je z powrotem do EQ; wpłata przerwana.");
                return WaitOrFail(now, "Nie potwierdzono odłożenia odrzuconego złota. Sprawdź kursor.");
        }
        return default;
    }

    public void ClickExecuted(DateTime now)
    {
        int waitMs = _phase == Phase.Deposited ? 350 : 250;
        if (_phase is Phase.Picked or Phase.Deposited or Phase.Returned)
            NextAt = now.AddMilliseconds(waitMs);
    }

    private GoldDepositStep Advance(Phase phase, GoldDepositAction action, DateTime now, int delay, string message, int slot = -1)
    {
        _phase = phase; _observations = 0; NextAt = now.AddMilliseconds(delay); Message = message;
        return new(action, slot);
    }
    private GoldDepositStep WaitOrFail(DateTime now, string error, bool increment = true)
    {
        if (increment && ++_observations >= 3) return Fail(error);
        NextAt = now.AddMilliseconds(200);
        return default;
    }
    private GoldDepositStep Fail(string error) { _phase = Phase.Failed; Message = error; return new(GoldDepositAction.Stop); }
}
