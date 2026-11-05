using MinecraftHelper.Services;

namespace MultiAccountTests;

internal static partial class Program
{
    private static void CheckWorkspaceSelection() => CheckWorkspaceSelectionAsync().GetAwaiter().GetResult();

    private static async Task CheckWorkspaceSelectionAsync()
    {
        Check(MinerWorkspacePlacement.FromBounds(double.NaN, double.NaN, 0, 0, false) == null,
            "Niepokazane jeszcze okno z domyślnymi współrzędnymi NaN nie ma zapisanego położenia.");
        foreach (var bounds in new[]
        {
            (Left: double.NaN, Top: 0.0, Width: 1280.0, Height: 820.0),
            (Left: 0.0, Top: double.PositiveInfinity, Width: 1280.0, Height: 820.0),
            (Left: 0.0, Top: 0.0, Width: double.NaN, Height: 820.0),
            (Left: 0.0, Top: 0.0, Width: 1280.0, Height: double.NegativeInfinity),
            (Left: 0.0, Top: 0.0, Width: -1.0, Height: 820.0),
            (Left: 0.0, Top: 0.0, Width: 1280.0, Height: 0.0)
        })
            Check(MinerWorkspacePlacement.FromBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height, false) == null,
                "Nieprawidłowy rozmiar/położenie nie może wejść do żądania pokazania okna.");
        Check(MinerWorkspacePlacement.FromBounds(-1800, -100, 1280, 820, true)
            == new MinerWorkspacePlacement(-1800, -100, 1280, 820, true),
            "Prawidłowe położenie na monitorze o ujemnych współrzędnych i maksymalizacja muszą być zachowane.");

        var fixture = new WorkspaceFixture();
        MinerWorkspaceSelection workspace = fixture.Create();
        Check((await workspace.EnsureAsync("alpha")).Success, "START powinien móc przygotować ukryty silnik konta.");
        Check(fixture.Visible.SetEquals(new[] { "default" }) && fixture.Commands.Count == 0,
            "Przygotowanie konta nie może przełączać widocznej konfiguracji.");
        int readyCalls = fixture.ReadyCalls;
        Check(!(await workspace.SelectAsync("missing")).Success && !(await workspace.EnsureAsync("../alpha")).Success,
            "Nieznane i zniekształcone identyfikatory kont muszą być odrzucane.");
        Check(fixture.ReadyCalls == readyCalls && fixture.Commands.Count == 0, "Odrzucone konto nie uruchamia procesu ani nie ukrywa bieżącego okna.");
        Check((await workspace.SelectAsync("ALPHA")).Success && fixture.Visible.SetEquals(new[] { "alpha" }),
            "Wybór konta pokazuje jego pełny interfejs zamiast kolejnego równoległego okna.");
        Check(fixture.LastPlacement == fixture.Placement, "Wybrane konto przejmuje pozycję i rozmiar bieżącego okna.");
        Check(fixture.Commands.Take(2).SequenceEqual(new[] { "default:workspace-hide", "alpha:workspace-show" }),
            "Najpierw ukrywamy stare okno, dopiero potem pokazujemy nowe.");
        Check((await workspace.SelectAsync("beta")).Success && fixture.Visible.SetEquals(new[] { "beta" }),
            "Kolejne konto przełącza ten sam obszar roboczy.");
        Check((await workspace.SelectAsync(null)).ProfileId == "beta", "Zasobnik przywraca ostatnio wybrane konto.");
        Check(fixture.LastPlacement == null, "Przywrócenie bieżącego konta zachowuje jego aktualną pozycję zamiast starej kopii.");
        var explicitPlacement = new MinerWorkspacePlacement(20, 30, 1400, 900, false);
        Check((await workspace.SelectAsync("beta", explicitPlacement)).Success && fixture.LastPlacement == explicitPlacement,
            "Jawnie przekazana pozycja tego samego konta ma pierwszeństwo.");
        Check(fixture.Commands.All(command => !command.EndsWith(":stop") && !command.EndsWith(":workspace-close")),
            "Przełączanie ustawień nie zatrzymuje kopania na pozostałych kontach.");
        Check(fixture.Settings["alpha"] == "alpha-settings" && fixture.Settings["beta"] == "beta-settings",
            "Przełączanie nie kopiuje i nie nadpisuje ustawień kont.");

        await Task.WhenAll(workspace.SelectAsync("alpha"), workspace.SelectAsync("beta"), workspace.SelectAsync("default"));
        Check(fixture.MaxVisible == 1 && fixture.Visible.SetEquals(new[] { "default" }),
            "Równoczesne żądania muszą zachować dokładnie jedno widoczne okno.");
        Check((await workspace.SelectAsync("default")).Success, "Konto główne działa nawet gdy accounts.json go nie zawiera.");

        var showFailure = new WorkspaceFixture { FailShow = "alpha" };
        MinerWorkspaceSelection failing = showFailure.Create();
        Check(!(await failing.SelectAsync("alpha")).Success, "Błąd pokazania nowego konta musi wrócić do wywołującego.");
        Check(showFailure.Visible.SetEquals(new[] { "default" }) && showFailure.MaxVisible == 1,
            "Po błędzie pokazania stare okno wraca, a niepewne nowe okno jest wcześniej ukrywane.");
        Check((await failing.SelectAsync(null)).ProfileId == "default", "Nieudane przełączenie nie zmienia konta przywracanego z zasobnika.");

        var hideFailure = new WorkspaceFixture { FailHide = "default" };
        Check(!(await hideFailure.Create().SelectAsync("alpha")).Success && hideFailure.Visible.SetEquals(new[] { "default" }),
            "Brak potwierdzenia ukrycia starego okna blokuje pokazanie drugiego.");
        Check(!hideFailure.Commands.Contains("alpha:workspace-show"), "Nie można pokazać nowego okna po błędzie ukrycia.");

        var cancellation = new WorkspaceFixture { CancelShow = "alpha" };
        try
        {
            _ = await cancellation.Create().SelectAsync("alpha");
            Check(false, "Anulowanie przełączenia powinno zostać zgłoszone.");
        }
        catch (OperationCanceledException)
        {
            Check(cancellation.Visible.SetEquals(new[] { "default" }) && cancellation.MaxVisible == 1,
                "Anulowanie między ukryciem a pokazaniem przywraca poprzednie konto.");
        }

        var cancelHide = new WorkspaceFixture { CancelHide = "default" };
        try
        {
            _ = await cancelHide.Create().SelectAsync("alpha");
            Check(false, "Anulowanie potwierdzenia ukrycia powinno zostać zgłoszone.");
        }
        catch (OperationCanceledException)
        {
            Check(cancelHide.Visible.SetEquals(new[] { "default" }) && !cancelHide.Commands.Contains("alpha:workspace-show"),
                "Utrata potwierdzenia po ukryciu starego okna musi je przywrócić.");
        }

        var notReady = new WorkspaceFixture { NotReady = "alpha" };
        Check(!(await notReady.Create().SelectAsync("alpha")).Success && notReady.Commands.Count == 0,
            "Nieudany start silnika nie może ukrywać działającego interfejsu.");

        await workspace.CloseAsync();
        Check(fixture.Commands.Contains("default:stop") && fixture.Commands.Contains("alpha:stop") && fixture.Commands.Contains("beta:stop"),
            "Zamknięcie obszaru roboczego zatrzymuje wszystkie konta.");
        Check(fixture.Commands.Contains("alpha:workspace-close") && fixture.Commands.Contains("beta:workspace-close")
            && !fixture.Commands.Contains("default:workspace-close"), "Pracownicy zamykają się przed głównym procesem i jego potwierdzeniem.");
        Check(!(await workspace.EnsureAsync("alpha")).Success && !(await workspace.SelectAsync("beta")).Success,
            "Zamykany Kopacz nie może uruchamiać ani pokazywać kolejnych kont.");

        foreach (string command in new[] { "workspace-hide", "workspace-show", "workspace-close" })
            Check(!MinerRemoteCommandRouter.Execute(command, new[] { new FakeSession("alpha", "Alpha", false) }).Success,
                "Polecenia interfejsu pozostają poza zewnętrzną listą komend: " + command);
    }

    private sealed class WorkspaceFixture
    {
        public readonly HashSet<string> Visible = new(StringComparer.OrdinalIgnoreCase) { "default" };
        public readonly List<string> Commands = new();
        public readonly Dictionary<string, string> Settings = new() { ["alpha"] = "alpha-settings", ["beta"] = "beta-settings" };
        public readonly MinerWorkspacePlacement Placement = new(-200, 60, 1280, 820, true);
        public MinerWorkspacePlacement? LastPlacement;
        public string? FailShow, FailHide, CancelShow, CancelHide, NotReady;
        public int ReadyCalls, MaxVisible = 1;
        private readonly object _gate = new();

        public MinerWorkspaceSelection Create() => new(() => new[] { "alpha", "beta" },
            (profile, _) => { ReadyCalls++; return Task.FromResult(profile != NotReady); }, SendAsync);

        private async Task<MinerWorkspaceCommandResponse> SendAsync(string profile, string command,
            MinerWorkspacePlacement? placement, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
            lock (_gate)
            {
                Commands.Add(profile + ":" + command);
                switch (command)
                {
                    case "workspace-hide":
                        if (profile == FailHide) return new(false, "hide failed");
                        Visible.Remove(profile);
                        if (profile == CancelHide) throw new OperationCanceledException();
                        return new(true, "hidden", Placement);
                    case "workspace-show":
                        LastPlacement = placement;
                        Visible.Add(profile);
                        MaxVisible = Math.Max(MaxVisible, Visible.Count);
                        // Failure can occur after the target window already appeared.
                        if (profile == CancelShow) throw new OperationCanceledException();
                        if (profile == FailShow) return new(false, "show failed");
                        return new(true, "shown");
                    case "workspace-close": Visible.Remove(profile); break;
                }
                return new(true, "ok");
            }
        }
    }
}
