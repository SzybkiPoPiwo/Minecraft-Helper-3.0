using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinecraftHelper.Services;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace MinecraftHelper;

// Zakładka „Enderchest”: krok cyklu Auto EQ — komenda, Shift + LPM na wybranych przedmiotach,
// zamknięcie i powrót do kopania. Decyzje podejmuje EnderchestFlow; tu tylko klawisze i obraz.
public partial class MainWindow
{
    // Przedmioty do przenoszenia (id znacznika, nazwa) — lista do uzupełnienia; pusta wyłącza cały krok.
    private static readonly (string Id, string Label)[] EnderchestCatalog = Array.Empty<(string, string)>();

    private readonly List<CheckBox> _enderchestItemTypeCheckBoxes = new();
    private bool _enderchestFull;
    private EnderchestFlow? _enderchestFlow;
    private bool _enderchestPending;
    private bool _enderchestBusy;
    private bool _enderchestParked;
    private bool _enderchestShiftDown;
    private bool _enderchestTitleConfirmed;
    private bool _enderchestReadTestActive;
    private byte[]? _enderchestEmptyCursor;
    private ChestMarkerLayout _enderchestLayout;
    private Drawing.Rectangle _enderchestArea;
    private Drawing.Point _enderchestClickPoint;
    private string _enderchestSummary = "";

    private bool IsEnderchestActive => _inventoryCleanupStage == InventoryCleanupStage.MoveToEnderchest || _enderchestFlow != null;

    // Złoto przy włączonej wpłacie idzie na wpłatę, nie do enderchesta.
    private EnderchestOptions EnderchestOptionsFromUi()
    {
        bool goldDeposit = ChkGoldDepositEnabled.IsChecked == true;
        var types = new HashSet<string>(_enderchestItemTypeCheckBoxes
            .Where(box => box.IsChecked == true && box.Tag is string)
            .Select(box => (string)box.Tag)
            .Where(id => !(goldDeposit && GoldDepositOptions.IsGold(id))), StringComparer.OrdinalIgnoreCase);
        return new EnderchestOptions(TxtEnderchestCommand.Text.Trim(), TxtEnderchestTitle.Text.Trim(),
            ParseNonNegativeInt(TxtEnderchestDelay.Text), types, GetSelectedInventoryCleanupSlots());
    }

    private bool IsEnderchestReady() => ChkEnderchestEnabled.IsChecked == true && EnderchestOptionsFromUi().Validate().Length == 0;

    private void InitializeEnderchestItemTypeGrid()
    {
        PanelEnderchestItemTypes.Children.Clear();
        _enderchestItemTypeCheckBoxes.Clear();
        foreach ((string itemId, string label) in EnderchestCatalog)
        {
            CheckBox checkBox = CreateItemTypeChip(itemId, label);
            checkBox.Checked += EnderchestSetting_Changed;
            checkBox.Unchecked += EnderchestSetting_Changed;
            _enderchestItemTypeCheckBoxes.Add(checkBox);
            PanelEnderchestItemTypes.Children.Add(checkBox);
        }

        bool hasItems = EnderchestCatalog.Length > 0;
        TxtEnderchestNoItems.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        PanelEnderchestTypesFooter.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadEnderchestSettings()
    {
        ChkEnderchestEnabled.IsChecked = _settings.EnderchestEnabled;
        TxtEnderchestCommand.Text = _settings.EnderchestCommand ?? "";
        TxtEnderchestTitle.Text = _settings.EnderchestMenuTitle ?? "";
        TxtEnderchestDelay.Text = Math.Clamp(_settings.EnderchestOpenDelayMs, 500, 30000).ToString(CultureInfo.InvariantCulture);
        var selected = new HashSet<string>(_settings.EnderchestItemTypes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (CheckBox box in _enderchestItemTypeCheckBoxes)
            box.IsChecked = box.Tag is string id && selected.Contains(id);
        UpdateEnderchestSettingsUi();
    }

    private void SaveEnderchestSettings()
    {
        _settings.EnderchestEnabled = ChkEnderchestEnabled.IsChecked == true;
        _settings.EnderchestCommand = TxtEnderchestCommand.Text.Trim();
        _settings.EnderchestMenuTitle = TxtEnderchestTitle.Text.Trim();
        _settings.EnderchestOpenDelayMs = Math.Clamp(ParseNonNegativeInt(TxtEnderchestDelay.Text), 500, 30000);
        _settings.EnderchestItemTypes = _enderchestItemTypeCheckBoxes
            .Where(box => box.IsChecked == true && box.Tag is string)
            .Select(box => (string)box.Tag)
            .ToList();
    }

    private void UpdateEnderchestSettingsUi()
    {
        if (PanelEnderchestSettings == null)
            return;

        SetExpandableSectionState(PanelEnderchestSettings, ChkEnderchestEnabled.IsChecked == true);
        BorderEnderchestNeedsAutoEq.Visibility = ChkInventoryCleanupEnabled.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        TxtEnderchestGoldHint.Visibility = EnderchestCatalog.Length > 0 && ChkGoldDepositEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtEnderchestTypesCount.Text = $"Wybrane: {_enderchestItemTypeCheckBoxes.Count(box => box.IsChecked == true)}";
    }

    private void EnderchestSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi)
            return;

        UpdateEnderchestSettingsUi();
        if (ChkEnderchestEnabled.IsChecked != true)
        {
            if (IsEnderchestActive)
                StopEnderchest("Wyłączono enderchest.");
            else
                _enderchestPending = false;
        }
        else if (_enderchestFlow == null)
        {
            string error = EnderchestOptionsFromUi().Validate();
            SetEnderchestStatus(error.Length == 0 ? "Konfiguracja gotowa. Otwórz enderchest w grze i użyj testu odczytu." : error, error.Length > 0);
        }
        MarkDirty();
    }

    private void BtnEnderchestSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox box in _enderchestItemTypeCheckBoxes)
            box.IsChecked = true;
    }

    private void BtnEnderchestClear_Click(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox box in _enderchestItemTypeCheckBoxes)
            box.IsChecked = false;
    }

    private void SetEnderchestStatus(string message, bool error = false)
    {
        LogStatus("Enderchest", message, error ? "Red" : "Default");
        TxtEnderchestStatus.Text = message;
        TxtEnderchestStatus.Foreground = error ? GlassPalette.Bad : GlassPalette.AccentInk;
    }

    private void ReleaseEnderchestShift()
    {
        if (!_enderchestShiftDown)
            return;
        SendKeyUp(VK_SHIFT);
        _enderchestShiftDown = false;
    }

    private void ResetEnderchestState()
    {
        ReleaseEnderchestShift();
        _enderchestFlow = null;
        _enderchestPending = false;
        _enderchestParked = false;
        _enderchestTitleConfirmed = false;
        _enderchestEmptyCursor = null;
        // Zadanie odczytu w tle trzyma _enderchestBusy do swojego finally; po STOP odrzuca je kontrola generacji.
    }

    private void LogEnderchest(string status, string details)
    {
        _ = _miningLogService.RecordAutomationEvent(GetMiningRunId(_inventoryCleanupOwner),
            GetInventoryCleanupOwnerLabel(_inventoryCleanupOwner), "enderchest", status, details, out _);
        RefreshMiningLogsSummary();
    }

    // Tylko przy niepewnym stanie (np. przedmiot na kursorze) — zwykłe niepowodzenia kończą krok i kopanie idzie dalej.
    private void StopEnderchest(string reason)
    {
        InventoryCleanupOwner owner = _inventoryCleanupOwner;
        ReleaseEnderchestShift();
        LogEnderchest(MiningLogStatuses.Aborted, reason);
        RecordAbortedInventoryCleanup("Enderchest: " + reason);
        StopMiningRuntime(owner);
        EndMiningLogRun(owner, "Kopanie zatrzymane: enderchest — " + reason, MiningLogStatuses.Aborted);
        SetEnderchestStatus(reason, error: true);
        UpdateInventoryCleanupStatus("Enderchest zatrzymany: " + reason, "Red");
        UpdateStatusBar("Kopacz zatrzymany — sprawdź enderchest i przedmiot na kursorze.", "Red");
    }

    private bool EnderchestInputAllowed() => _targetGameWindowHandle != IntPtr.Zero
        && IsGameInputAvailableFor(_targetGameWindowHandle)
        && _inventoryCleanupStage == InventoryCleanupStage.MoveToEnderchest
        && (_inventoryCleanupOwner == InventoryCleanupOwner.Kopacz533 ? _kopacz533RuntimeEnabled : _kopacz633RuntimeEnabled);

    // async void z timera: każdy wyjątek musi zostać złapany tutaj, inaczej zamknąłby program.
    private async void ProcessEnderchest(DateTime now)
    {
        try
        {
            await ProcessEnderchestCoreAsync(now);
        }
        catch (Exception ex)
        {
            AppLog.Error("Enderchest", "Nieoczekiwany błąd enderchesta", ex);
            StopEnderchest("Nieoczekiwany błąd: " + ex.GetBaseException().Message);
        }
    }

    private async Task ProcessEnderchestCoreAsync(DateTime now)
    {
        if (_enderchestBusy)
            return;
        if (!EnderchestInputAllowed())
        {
            StopEnderchest("Utrata fokusu lub zatrzymanie kanału kopania.");
            return;
        }

        if (_enderchestFlow == null)
        {
            _enderchestFlow = new EnderchestFlow(EnderchestOptionsFromUi(), now);
            _enderchestTitleConfirmed = false;
            _enderchestEmptyCursor = null;
            _enderchestParked = false;
        }

        EnderchestFlow flow = _enderchestFlow;
        int generation = _inventoryCleanupGeneration;
        try
        {
            if (now < flow.NextAt || (flow.AwaitsTyping && !ChatInputGate.IsReady(now)))
                return;

            EnderchestObservation? observation = null;
            if (flow.NeedsScreen)
            {
                if (!IsGameGuiOpen())
                {
                    observation = new(false, false, true, Array.Empty<DetectedInventoryItem>(), "GUI jest zamknięte.");
                }
                else if (!_enderchestParked)
                {
                    // Kursor poza slotami: podpowiedź nie zasłania znaczników, a pole kursora jest czytelne.
                    if (!TryCaptureTargetClient(out Drawing.Bitmap? parkImage, out Drawing.Rectangle parkArea))
                        throw new InvalidOperationException("Brak obrazu enderchesta.");
                    using (parkImage)
                    {
                        if (InventoryMarkerDetector.TryDetectChestMenu(parkImage!, 9, EnderchestOptions.MinimumRows, out ChestMarkerLayout layout))
                        {
                            _enderchestLayout = layout;
                            _enderchestArea = parkArea;
                            if (!SetGameCursorPosition(parkArea.Left + layout.CursorPark.X, parkArea.Top + layout.CursorPark.Y))
                                throw new InvalidOperationException("Nie udało się odsunąć kursora.");
                            _enderchestParked = true;
                            _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(120);
                            return;
                        }
                        observation = new(true, false, true, Array.Empty<DetectedInventoryItem>(), "nie widać siatki skrzyni.");
                    }
                }
                else
                {
                    if (!EnsureF3TesseractEngine() || !TryCaptureTargetClient(out Drawing.Bitmap? image, out Drawing.Rectangle area))
                        throw new InvalidOperationException("Brak OCR lub obrazu enderchesta.");
                    byte[]? empty = _enderchestEmptyCursor;
                    bool titleConfirmed = _enderchestTitleConfirmed;
                    _enderchestBusy = true;
                    using (image)
                    {
                        var result = await Task.Run(() => ReadEnderchestObservation(image!, flow.Options, empty, titleConfirmed));
                        if (generation != _inventoryCleanupGeneration || !ReferenceEquals(flow, _enderchestFlow))
                            return;
                        if (!EnderchestInputAllowed() || area != _enderchestArea)
                            throw new InvalidOperationException("Zmieniono okno gry podczas odczytu enderchesta.");
                        observation = result.Screen;
                        if (observation.MenuValid)
                        {
                            _enderchestEmptyCursor ??= result.CursorPatch;
                            _enderchestLayout = result.Layout;
                            _enderchestTitleConfirmed = true;
                        }
                    }
                    _enderchestBusy = false;
                    now = DateTime.UtcNow;
                }
            }

            EnderchestStep step = flow.Tick(now, observation);
            SetEnderchestStatus(flow.Message);
            UpdateInventoryCleanupStatus(flow.Message, "Orange");
            switch (step.Action)
            {
                case EnderchestAction.CloseInitialGui:
                    if (IsGameGuiOpen())
                        SendKeyTap(VK_ESCAPE);
                    break;
                case EnderchestAction.OpenChat:
                    SendChatOpenKeyTap();
                    break;
                case EnderchestAction.TypeCommand:
                    if (!SendTextByKeyboard(flow.Options.Command))
                        throw new InvalidOperationException("Nie udało się wpisać komendy enderchesta.");
                    break;
                case EnderchestAction.SubmitCommand:
                    SendKeyTap(VK_RETURN);
                    break;
                case EnderchestAction.MoveToSlot:
                    _enderchestClickPoint = _enderchestLayout.PlayerInventory.GetSlotCenter(step.Slot);
                    if (!SetGameCursorPosition(_enderchestArea.Left + _enderchestClickPoint.X, _enderchestArea.Top + _enderchestClickPoint.Y))
                        throw new InvalidOperationException("Nie udało się ustawić kursora na przedmiocie.");
                    _enderchestParked = false;
                    break;
                case EnderchestAction.PressShift:
                    SendKeyDown(VK_SHIFT);
                    _enderchestShiftDown = true;
                    break;
                case EnderchestAction.ClickSlot:
                    if (!IsEnderchestClickSafe() || !SendMouseClick(leftButton: true, holdPulseMode: false))
                        throw new InvalidOperationException("Enderchest zamknięto albo poruszono mysz przed kliknięciem.");
                    break;
                case EnderchestAction.ReleaseShift:
                    ReleaseEnderchestShift();
                    break;
                case EnderchestAction.CloseMenu:
                    ReleaseEnderchestShift();
                    SendKeyTap(VK_ESCAPE);
                    _enderchestParked = false;
                    break;
                case EnderchestAction.Stop:
                    StopEnderchest(flow.Message);
                    return;
                case EnderchestAction.Complete:
                    FinishEnderchest(flow);
                    break;
            }
            _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(20);
        }
        catch (Exception ex)
        {
            if (generation == _inventoryCleanupGeneration && ReferenceEquals(flow, _enderchestFlow))
                StopEnderchest(ex.Message);
        }
        finally
        {
            _enderchestBusy = false;
        }
    }

    private void FinishEnderchest(EnderchestFlow flow)
    {
        _enderchestSummary = flow.Message;
        _enderchestFull |= flow.IsFull;
        bool warning = flow.Warning.Length > 0;
        LogEnderchest(warning ? MiningLogStatuses.Aborted : MiningLogStatuses.Completed, flow.Message);
        SetEnderchestStatus(flow.Message, warning);
        if (warning)
            UpdateStatusBar(flow.Message, "Red"); // czerwony pasek trafia też na Discord
        _enderchestPending = false;
        _enderchestFlow = null;
        _inventoryCleanupStage = GetInventoryCleanupPostCommandStage();
    }

    private bool IsEnderchestClickSafe()
    {
        var expected = new Drawing.Point(_enderchestArea.Left + _enderchestClickPoint.X, _enderchestArea.Top + _enderchestClickPoint.Y);
        return EnderchestInputAllowed()
            && IsGameGuiOpen()
            && TryGetGameCursorPosition(out POINT cursor)
            && Math.Abs(cursor.X - expected.X) <= 2
            && Math.Abs(cursor.Y - expected.Y) <= 2;
    }

    private (EnderchestObservation Screen, byte[] CursorPatch, ChestMarkerLayout Layout) ReadEnderchestObservation(
        Drawing.Bitmap image, EnderchestOptions options, byte[]? emptyCursor, bool titleConfirmed)
    {
        if (!InventoryMarkerDetector.TryDetectChestMenu(image, 9, EnderchestOptions.MinimumRows, out ChestMarkerLayout layout))
            return (new(true, false, true, Array.Empty<DetectedInventoryItem>(), "nie widać siatki skrzyni."), Array.Empty<byte>(), default);

        if (!titleConfirmed)
        {
            using var title = image.Clone(layout.HeaderSample, Imaging.PixelFormat.Format32bppArgb);
            string error = options.ValidateMenuTitle(RunOcrOnBitmap(title, Tesseract.PageSegMode.SingleLine));
            if (error.Length > 0)
                return (new(true, false, true, Array.Empty<DetectedInventoryItem>(), error), Array.Empty<byte>(), layout);
        }

        Drawing.Point park = layout.CursorPark;
        var cursorRect = new Drawing.Rectangle(park.X - 8 * layout.Scale, park.Y - 8 * layout.Scale, 16 * layout.Scale, 16 * layout.Scale);
        byte[] patch = ReadGoldPatch(image, cursorRect);
        bool cursorEmpty = emptyCursor == null || GoldPatchMatches(patch, emptyCursor);
        DetectedInventoryItem[] items = InventoryMarkerDetector.ReadMarkedItems(image, layout.PlayerInventory, includeHotbar: true)
            .Where(options.IsSelected)
            .ToArray();
        return (new(true, true, cursorEmpty, items), patch, layout);
    }

    private async void BtnEnderchestReadTest_Click(object sender, RoutedEventArgs e)
    {
        if (HasOtherGoldTestAutomation() || _inventoryCleanupStage != InventoryCleanupStage.None || _enderchestReadTestActive)
        {
            SetEnderchestStatus("Najpierw zatrzymaj kopanie i pozostałe automatyzacje.", true);
            return;
        }

        EnderchestOptions options = EnderchestOptionsFromUi();
        string error = options.Validate();
        IntPtr target = _targetGameWindowHandle;
        if (error.Length > 0 || target == IntPtr.Zero)
        {
            SetEnderchestStatus(error.Length > 0 ? error : "Najpierw wybierz okno Minecrafta.", true);
            return;
        }

        _enderchestReadTestActive = true;
        BtnEnderchestReadTest.IsEnabled = false;
        try
        {
            SetEnderchestStatus("Test odczytu za 3 s: przejdź do gry, otwórz enderchest i odsuń mysz poza okno skrzyni. Niczego nie przeniosę.");
            await Task.Delay(3000);
            if (target != _targetGameWindowHandle || GetForegroundWindow() != target)
            {
                SetEnderchestStatus("Test anulowany — gra nie jest na wierzchu.", true);
                return;
            }
            if (!EnsureF3TesseractEngine() || !TryCaptureTargetClient(out Drawing.Bitmap? image, out _))
            {
                SetEnderchestStatus("Brak OCR lub obrazu gry.", true);
                return;
            }

            using (image)
            {
                var result = await Task.Run(() => ReadEnderchestObservation(image!, options, null, titleConfirmed: false));
                SetEnderchestStatus(result.Screen.MenuValid
                        ? $"Enderchest potwierdzony ({result.Layout.Rows} rzędy). Do przeniesienia: {result.Screen.Items.Count} stosów."
                        : "Nie potwierdzono enderchesta: " + result.Screen.Error,
                    !result.Screen.MenuValid);
            }
        }
        catch (Exception ex)
        {
            SetEnderchestStatus("Test: " + ex.Message, true);
        }
        finally
        {
            _enderchestReadTestActive = false;
            BtnEnderchestReadTest.IsEnabled = true;
        }
    }
}
