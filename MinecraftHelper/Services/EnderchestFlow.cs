using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MinecraftHelper.Services;

internal sealed record EnderchestOptions(string Command, string MenuTitle, int OpenDelayMs,
    IReadOnlySet<string> ItemTypes, IReadOnlySet<int> Slots)
{
    public const int MinimumRows = 3;

    public bool IsSelected(DetectedInventoryItem item) => ItemTypes.Contains(item.ItemId) && Slots.Contains(item.Slot);

    public string Validate() => string.IsNullOrWhiteSpace(Command) || !Command.StartsWith('/')
        || Command.Length > 256 || Command.Contains('\n') || Command.Contains('\r')
        ? "Wpisz pojedynczą komendę zaczynającą się od /."
        : string.IsNullOrWhiteSpace(MenuTitle) || MenuTitle.Count(char.IsLetterOrDigit) < 2 ? "Wpisz oczekiwany tytuł enderchesta (np. Ender Chest)."
        : OpenDelayMs is < 500 or > 30000 ? "Oczekiwanie na enderchest musi wynosić 500–30000 ms."
        : ItemTypes.Count == 0 ? "Wybierz przynajmniej jeden przedmiot do przenoszenia."
        : Slots.Count == 0 ? "W Auto EQ nie zaznaczono żadnego slotu."
        : string.Empty;

    public string ValidateMenuTitle(string title)
    {
        static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        return Normalize(title).Contains(Normalize(MenuTitle), StringComparison.Ordinal)
            ? string.Empty
            : $"Nie potwierdzono tytułu enderchesta. Oczekiwano „{MenuTitle}”, odczytano: „{(string.IsNullOrWhiteSpace(title) ? "brak odczytu" : title.Trim())}”.";
    }
}

internal enum EnderchestAction { None, CloseInitialGui, OpenChat, TypeCommand, SubmitCommand, MoveToSlot, PressShift, ClickSlot, ReleaseShift, CloseMenu, Complete, Stop }
internal readonly record struct EnderchestStep(EnderchestAction Action, int Slot = -1);
internal sealed record EnderchestObservation(bool GuiOpen, bool MenuValid, bool CursorEmpty, IReadOnlyList<DetectedInventoryItem> Items, string Error = "");

// Sam automat: bez wywołań Windows, zrzutów i ustawień. Przenosi Shift + LPM, po jednym stosie,
// i po każdym sprawdza, czy stos zniknął. Pełny albo nieotwarty enderchest to nie awaria:
// reszta zostaje w EQ i kopanie idzie dalej. Zatrzymanie tylko przy przedmiocie na kursorze.
internal sealed class EnderchestFlow
{
    private enum Phase { CloseInitial, OpenChat, Type, Submit, Inspect, PressShift, Click, ReleaseShift, Verify, Closed, Done, Failed }

    private const int MaximumMoves = 36;
    private const int MaximumObservations = 3;

    private Phase _phase;
    private readonly DateTime _started;
    private DetectedInventoryItem _source;
    private int _observations;

    public EnderchestOptions Options { get; }
    public DateTime NextAt { get; private set; }
    public int MovedStacks { get; private set; }
    public int MovedItems { get; private set; }
    public string Message { get; private set; } = "Przygotowanie enderchesta";

    /// <summary>Niepusty: czego nie udało się zrobić; kopanie i tak idzie dalej.</summary>
    public string Warning { get; private set; } = "";

    /// <summary>Stos nie zmieścił się: do następnego startu kopania komenda nie ma sensu.</summary>
    public bool IsFull { get; private set; }

    public bool NeedsScreen => _phase is Phase.Inspect or Phase.Verify or Phase.Closed;
    public bool AwaitsTyping => _phase == Phase.Type;
    public bool HasExpired(DateTime now) => now - _started > TimeSpan.FromMinutes(2);

    public EnderchestFlow(EnderchestOptions options, DateTime now)
    {
        Options = options;
        _started = NextAt = now;
        string error = options.Validate();
        if (error.Length > 0)
        {
            Warning = error;
            Message = error;
            _phase = Phase.Done;
        }
    }

    public EnderchestStep Tick(DateTime now, EnderchestObservation? screen = null)
    {
        if (_phase == Phase.Failed) return new(EnderchestAction.Stop);
        if (_phase == Phase.Done) return new(EnderchestAction.Complete);
        if (HasExpired(now)) return Fail("Przekroczono limit czasu enderchesta.");
        if (now < NextAt) return default;
        if (NeedsScreen && screen == null) return default;

        switch (_phase)
        {
            case Phase.CloseInitial: return Advance(Phase.OpenChat, EnderchestAction.CloseInitialGui, now, 180, "Otwieranie czatu");
            case Phase.OpenChat: return Advance(Phase.Type, EnderchestAction.OpenChat, now, 180, "Wpisywanie komendy enderchesta");
            case Phase.Type: return Advance(Phase.Submit, EnderchestAction.TypeCommand, now, 110, "Wysyłanie komendy enderchesta");
            case Phase.Submit: return Advance(Phase.Inspect, EnderchestAction.SubmitCommand, now, Options.OpenDelayMs, "Oczekiwanie na enderchest");
            // Klawisz i klik w osobnych krokach: gra musi zdążyć zobaczyć wciśnięty Shift przed kliknięciem i po nim.
            case Phase.PressShift: return Advance(Phase.Click, EnderchestAction.PressShift, now, 70, Message);
            case Phase.Click: return Advance(Phase.ReleaseShift, EnderchestAction.ClickSlot, now, 70, Message, _source.Slot);
            case Phase.ReleaseShift: return Advance(Phase.Verify, EnderchestAction.ReleaseShift, now, 300, "Sprawdzanie przeniesienia");
            case Phase.Inspect: return Inspect(now, screen!);
            case Phase.Verify: return Verify(now, screen!);
            case Phase.Closed:
                if (screen!.GuiOpen)
                    return Observe(now) ? Fail("Enderchest nie został zamknięty.") : default;
                return Finish();
        }
        return default;
    }

    private EnderchestStep Inspect(DateTime now, EnderchestObservation screen)
    {
        if (!screen.MenuValid)
        {
            if (!Observe(now)) return default;
            return GiveUp(now, screen, screen.GuiOpen
                ? "Nie potwierdzono enderchesta: " + screen.Error
                : "Enderchest się nie otworzył — sprawdź komendę i czas oczekiwania.");
        }
        if (!screen.CursorEmpty) return Fail("Kursor nie jest pusty. Nie rozpoczęto przenoszenia.");

        DetectedInventoryItem[] targets = screen.Items.Where(Options.IsSelected).OrderBy(item => item.Slot).ToArray();
        if (targets.Length == 0) return Advance(Phase.Closed, EnderchestAction.CloseMenu, now, 250, "Zamykanie enderchesta");
        if (MovedStacks >= MaximumMoves) return GiveUp(now, screen, $"Osiągnięto limit {MaximumMoves} przeniesień w jednym cyklu.");

        _source = targets[0];
        return Advance(Phase.PressShift, EnderchestAction.MoveToSlot, now, 80, $"Przenoszenie ze slotu EQ {_source.Slot + 1}", _source.Slot);
    }

    private EnderchestStep Verify(DateTime now, EnderchestObservation screen)
    {
        if (!screen.MenuValid)
            return Observe(now) ? GiveUp(now, screen, "Enderchest zmienił się podczas przenoszenia. " + screen.Error) : default;
        if (!screen.CursorEmpty) return Fail("Po przeniesieniu na kursorze został przedmiot.");

        if (screen.Items.Any(item => item.Slot == _source.Slot && item.ItemId == _source.ItemId))
        {
            if (!Observe(now))
                return default;
            IsFull = true;
            return GiveUp(now, screen, "Enderchest jest pełny — przedmioty zostają w EQ, komenda wróci przy następnym starcie kopania.");
        }

        MovedStacks++;
        MovedItems += _source.Quantity;
        _phase = Phase.Inspect;
        _observations = 0;
        NextAt = now.AddMilliseconds(100);
        Message = $"Przeniesiono {MovedStacks} stosów / {MovedItems} szt.";
        return default;
    }

    private bool Observe(DateTime now)
    {
        if (++_observations >= MaximumObservations)
            return true;
        NextAt = now.AddMilliseconds(200);
        return false;
    }

    private EnderchestStep GiveUp(DateTime now, EnderchestObservation screen, string warning)
    {
        Warning = warning;
        return screen.GuiOpen ? Advance(Phase.Closed, EnderchestAction.CloseMenu, now, 250, warning) : Finish();
    }

    private EnderchestStep Finish()
    {
        _phase = Phase.Done;
        string moved = $"Enderchest: przeniesiono {MovedItems} szt. z {MovedStacks} stosów.";
        Message = Warning.Length > 0 ? $"{moved} {Warning}" : moved;
        return new(EnderchestAction.Complete);
    }

    private EnderchestStep Advance(Phase phase, EnderchestAction action, DateTime now, int delay, string message, int slot = -1)
    {
        _phase = phase;
        _observations = 0;
        NextAt = now.AddMilliseconds(delay);
        Message = message;
        return new(action, slot);
    }

    private EnderchestStep Fail(string error)
    {
        _phase = Phase.Failed;
        Message = error;
        return new(EnderchestAction.Stop);
    }
}
