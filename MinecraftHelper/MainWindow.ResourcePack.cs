using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Karta „Paczka zasobów” w Ustawieniach: Kopacz sam tworzy paczkę znaczników z plików gry
// i zapisuje ją w resourcepacks (zamiast gotowego ZIP-a z repozytorium).
public partial class MainWindow
{
    // Główny proces przy starcie: tworzy brakującą albo nieaktualną paczkę.
    internal async Task EnsureResourcePackAsync()
    {
        ResourcePackState before = ResourcePackInstaller.GetStatus().State;
        ResourcePackStatus? status = await ResourcePackInstaller.EnsureInstalledAsync();
        RefreshResourcePackStatus(status);
        if (status?.State == ResourcePackState.Current && before is ResourcePackState.Missing or ResourcePackState.Outdated)
            UpdateStatusBar(before == ResourcePackState.Missing
                ? "Utworzono paczkę zasobów Minecraft Helper — włącz ją w grze (Opcje → Paczki zasobów)."
                : "Zaktualizowano paczkę zasobów Minecraft Helper — w grze naciśnij F3 + T.", "Green");
    }

    private void RefreshResourcePackStatus(ResourcePackStatus? status = null)
    {
        if (TxtResourcePackStatus == null)
            return;
        status ??= ResourcePackInstaller.GetStatus();

        TxtResourcePackStatus.Text = status.State switch
        {
            ResourcePackState.Current => "Paczka jest zainstalowana i aktualna.",
            ResourcePackState.Outdated => "Paczka jest nieaktualna — kliknij „Utwórz paczkę ponownie”.",
            ResourcePackState.Missing => "Brak paczki — kliknij „Utwórz paczkę ponownie”.",
            _ => status.Message
        };
        EllResourcePackStatus.Fill = status.State switch
        {
            ResourcePackState.Current => GlassPalette.Ok,
            ResourcePackState.NoGame => GlassPalette.Bad,
            _ => GlassPalette.Warn
        };
        TxtResourcePackPath.Text = status.PackPath == null
            ? string.Empty
            : $"Plik: {status.PackPath}\nTekstury z: {status.SourceJar}";
        BtnResourcePackCreate.IsEnabled = status.State != ResourcePackState.NoGame;
        BtnResourcePackOpenFolder.IsEnabled = status.GameDirectory != null;

        BorderResourcePackWarning.Visibility = status.OldUnpackedFolder ? Visibility.Visible : Visibility.Collapsed;
        TxtResourcePackWarning.Text = "W resourcepacks jest też starsza, rozpakowana wersja (folder „MinecraftHelper_AutoEQ_CobbleX_1.8.8”). "
            + "Wyłącz ją w grze albo usuń folder, żeby nie nakładała się na nową paczkę.";
    }

    private async void BtnResourcePackCreate_Click(object sender, RoutedEventArgs e)
    {
        BtnResourcePackCreate.IsEnabled = false;
        TxtResourcePackStatus.Text = "Tworzę paczkę z plików gry…";
        try
        {
            ResourcePackStatus status = await ResourcePackInstaller.InstallAsync();
            RefreshResourcePackStatus(status);
            UpdateStatusBar(status.Message, status.State == ResourcePackState.Current ? "Green" : "Orange");
        }
        catch (Exception ex)
        {
            RefreshResourcePackStatus();
            UpdateStatusBar("Nie udało się utworzyć paczki zasobów: " + ex.GetBaseException().Message, "Red");
        }
    }

    private void BtnResourcePackOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        ResourcePackStatus status = ResourcePackInstaller.GetStatus();
        if (status.GameDirectory == null)
            return;
        string folder = Path.Combine(status.GameDirectory, "resourcepacks");
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            UpdateStatusBar("Nie udało się otworzyć folderu: " + ex.Message, "Red");
        }
    }
}
