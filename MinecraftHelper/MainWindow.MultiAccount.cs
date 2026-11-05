using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MinecraftHelper.Models;
using MinecraftHelper.Services;

namespace MinecraftHelper;

public partial class MainWindow
{
    private bool _gameWindowOwnedByAnotherProfile;

    internal void OpenAccountConfiguration() => NavigateToSection(nameof(TabKopacz));

    // Zmiana nazwy z karty „Profil konta”; zapis pod tą samą blokadą co w panelu Konta.
    private void BtnRenameAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AccountNameWindow(_accountDisplayName) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.AccountName))
            return;

        string newName = dialog.AccountName.Trim();
        try
        {
            new MinerAccountStore().Update(profiles =>
            {
                MinerAccountProfile? profile = profiles.FirstOrDefault(item => string.Equals(item.Id, _accountProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                    profiles.Add(profile = new MinerAccountProfile { Id = _accountProfileId });
                profile.Name = newName;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UpdateStatusBar("Nie udało się zapisać nazwy konta: " + ex.Message, "Red");
            return;
        }

        ReloadAccountProfile();
        RefreshOverlayHud(DateTime.UtcNow);
        UpdateStatusBar($"Konto nazywa się teraz „{newName}”.", "Green");
    }

    // Lista kont pod przyciskiem; wybór przełącza okno ustawień na to konto.
    private void BtnSwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = BtnSwitchAccount, Placement = PlacementMode.Bottom };
        foreach (MinerAccountProfile profile in new MinerAccountStore().Load())
        {
            bool current = string.Equals(profile.Id, _accountProfileId, StringComparison.OrdinalIgnoreCase);
            var item = new MenuItem { Header = profile.Name, IsChecked = current, IsEnabled = !current };
            string id = profile.Id;
            item.Click += async (_, __) =>
            {
                try
                {
                    MinerWorkspaceResponse result = await MinerWorkspaceClient.SelectAsync(id);
                    if (!result.Success)
                        UpdateStatusBar(result.Message, "Red");
                }
                catch (Exception ex)
                {
                    UpdateStatusBar("Nie udało się przełączyć konta: " + ex.GetBaseException().Message, "Red");
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var panel = new MenuItem { Header = "Wszystkie konta…" };
        panel.Click += (_, __) => NavigateToSection(nameof(TabAccounts));
        menu.Items.Add(panel);
        menu.IsOpen = true;
    }

    MinerWorkspacePlacement? IMinerWorkspaceSession.HideWorkspaceWindow() => OnUiThread(() =>
    {
        if (_pendingChanges)
            AutoSaveSettings();
        Rect bounds = RestoreBounds;
        MinerWorkspacePlacement? placement = WindowState == WindowState.Normal
            ? MinerWorkspacePlacement.FromBounds(Left, Top, ActualWidth, ActualHeight, false)
            : MinerWorkspacePlacement.FromBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
        MinimizeToTray(showNotification: false);
        return placement;
    });

    void IMinerWorkspaceSession.ShowWorkspaceWindow(MinerWorkspacePlacement? placement) => OnUiThread(() =>
    {
        ReloadAccountProfile();
        if (placement != null && double.IsFinite(placement.Left) && double.IsFinite(placement.Top)
            && double.IsFinite(placement.Width) && double.IsFinite(placement.Height)
            && placement.Width > 0 && placement.Height > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowState = WindowState.Normal;
            Left = placement.Left;
            Top = placement.Top;
            Width = Math.Max(MinWidth, placement.Width);
            Height = Math.Max(MinHeight, placement.Height);
            OpenAccountConfiguration();
        }
        ShowInTaskbar = true;
        _isMinimizedToTray = false;
        Show();
        if (placement != null)
            WindowState = placement.Maximized ? WindowState.Maximized : WindowState.Normal;
        else if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    });

    void IMinerWorkspaceSession.CloseWorkspaceSession() => RequestWorkspaceExit();

    internal void RequestWorkspaceExit()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RequestWorkspaceExit));
            return;
        }
        if (_isExitRequested)
            return;
        if (_settings != null)
        {
            StopMiningFromRemoteCommand();
            if (_pendingChanges)
                AutoSaveSettings();
        }
        _isExitRequested = true;
        Close();
    }

    // Keeps the resolved game window reserved for this profile. Returns false when
    // another running profile already owns it; the window is then not used here.
    private bool ClaimTargetGameWindow()
    {
        IntPtr window = _targetGameWindowHandle;
        int processId = 0;
        if (window != IntPtr.Zero && IsWindow(window))
        {
            _ = GetWindowThreadProcessId(window, out uint processIdRaw);
            processId = processIdRaw > int.MaxValue ? 0 : (int)processIdRaw;
        }

        if (processId <= 0)
        {
            BackgroundGameWindow.ReleaseLayoutUnlessTarget(IntPtr.Zero);
            _gameWindowClaim.Release();
            SetGameWindowOwnedByAnotherProfile(false);
            return true;
        }

        if (_gameWindowClaim.TryClaim(processId))
        {
            BackgroundGameWindow.ReleaseLayoutUnlessTarget(window);
            SetGameWindowOwnedByAnotherProfile(false);
            return true;
        }

        _targetGameWindowHandle = IntPtr.Zero;
        BackgroundGameWindow.ReleaseLayoutUnlessTarget(IntPtr.Zero);
        // Do not enumerate processes on every tick while the conflict lasts.
        _nextTargetWindowResolveAtUtc = DateTime.UtcNow.Add(TargetWindowResolveRetryInterval);
        SetGameWindowOwnedByAnotherProfile(true);
        return false;
    }

    private void SetGameWindowOwnedByAnotherProfile(bool owned)
    {
        if (_gameWindowOwnedByAnotherProfile == owned)
            return;

        _gameWindowOwnedByAnotherProfile = owned;
        if (owned)
            UpdateStatusBar("Wybrane okno Minecrafta należy do innego konta Kopacza. Wybierz inne okno w tym profilu.", "Red");
    }
}
