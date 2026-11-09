using System;
using System.Windows;
using System.Windows.Controls;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Osobne okno logów, otwierane przyciskiem z zakładki Konta.
internal sealed class MiningLogsWindow : Window
{
    public MiningLogsWindow(Window owner, MiningLogService logService, string profileId)
    {
        Owner = owner;
        Title = "Logi — Kopacz";
        Width = Math.Min(1040, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(680, SystemParameters.WorkArea.Height - 40);
        MinWidth = 780;
        MinHeight = 420;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        var root = new Grid();
        root.Children.Add(new GlassBlobBackground { IsAnimationEnabled = false });
        root.Children.Add(new MiningLogsPanel(logService, profileId));
        Content = root;
    }
}

public partial class MainWindow
{
    private MiningLogsWindow? _miningLogsWindow;

    private void OpenMiningLogsWindow()
    {
        if (_miningLogsWindow != null)
        {
            if (_miningLogsWindow.WindowState == WindowState.Minimized)
                _miningLogsWindow.WindowState = WindowState.Normal;
            _miningLogsWindow.Activate();
            return;
        }

        _miningLogsWindow = new MiningLogsWindow(this, _miningLogService, _accountProfileId);
        _miningLogsWindow.Closed += (_, __) => _miningLogsWindow = null;
        _miningLogsWindow.Show();
    }
}
