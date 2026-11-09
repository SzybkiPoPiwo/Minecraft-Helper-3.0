using System.Windows;
using System.Windows.Media;

namespace MinecraftHelper;

// Kapitulacja: osobny bind na sytuacje, w których makra nie da się wyłączyć normalnie,
// np. przedmiot otworzył GUI i blokada kursora ignoruje bind clickera.
public partial class MainWindow
{
    private bool _panicBindWasDown;

    private void BtnPanicNow_Click(object sender, RoutedEventArgs e) => StopAllMacros();

    // Te same przerwania co przy utracie fokusu: bez dodatkowego wejścia do gry
    // (bez powrotu na miecz i zamykania EQ), a trzymane przyciski są puszczane.
    private void StopAllMacros()
    {
        bool anythingRunning = HasActiveRuntimeWork();

        if (IsAppleCutterRunning)
            CancelAppleCutter("Ścinanie jabłek przerwane — kapitulacja.", Brushes.OrangeRed);
        if (IsPvpQuickActionRunning)
            CancelPvpQuickAction("Akcja PVP przerwana — kapitulacja.", Brushes.OrangeRed, restoreSword: false);
        if (IsAutoArmorRunning)
            CancelAutoArmor("Auto zbroja przerwana — kapitulacja. Sprawdź EQ.", Brushes.OrangeRed, closeInventory: false);
        if (_bindyStage != BindyStage.None)
            ResetBindyStage();

        StopClickerRuntimesForExclusivePointerMacro();
        _autoGuardRuntimeEnabled = false;
        StopPvpExtras();

        if (!anythingRunning)
        {
            UpdateStatusBar("KAPITULACJA: nic nie działało.", "Orange");
            return;
        }

        // Trzymany bind trybu „Hold” nie może uruchomić makra w następnym cyklu.
        _suppressBindToggleUntilRelease = true;
        UpdateStatusBar("KAPITULACJA: wszystkie makra wyłączone.", "Orange");
    }
}
