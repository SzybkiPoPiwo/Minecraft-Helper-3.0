using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftHelper.Services;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace MinecraftHelper;

public partial class MainWindow
{
    private GoldDepositFlow? _goldDepositFlow;
    private bool _goldDepositPending;
    private bool _goldDepositBusy;
    private bool _goldDepositParked;
    private byte[]? _goldDepositEmptyCursor;
    private ChestMarkerLayout _goldDepositLayout;
    private Drawing.Rectangle _goldDepositArea;
    private GoldDepositAction _goldDepositClickAction;
    private Drawing.Point _goldDepositClickPoint;
    private DateTime _goldDepositClickAt;
    private string _goldDepositSummary = "";
    private int _goldDepositRows = 6;
    private int _goldDepositColumns = 9;
    private int _goldDepositSelectedSlot = 11;
    private bool IsGoldDepositActive => _inventoryCleanupStage == InventoryCleanupStage.DepositGold || _goldDepositFlow != null;

    private GoldDepositOptions GoldOptions() => new(
        TxtGoldDepositCommand.Text.Trim(), TxtGoldDepositTitle.Text.Trim(),
        _goldDepositRows, _goldDepositSelectedSlot,
        ParseNonNegativeInt(TxtGoldDepositDelay.Text), ChkGoldDepositIngots.IsChecked == true,
        ChkGoldDepositBlocks.IsChecked == true, ChkGoldDepositHotbar.IsChecked == true, _goldDepositColumns);

    private void LoadGoldDepositSettings()
    {
        ChkGoldDepositEnabled.IsChecked = _settings.GoldDepositEnabled;
        TxtGoldDepositCommand.Text = _settings.GoldDepositCommand ?? "";
        TxtGoldDepositTitle.Text = _settings.GoldDepositMenuTitle ?? "Bazar";
        _goldDepositRows = Math.Clamp(_settings.GoldDepositRows, 1, 6);
        _goldDepositColumns = Math.Clamp(_settings.GoldDepositColumns, 1, 9);
        _goldDepositSelectedSlot = Math.Clamp(_settings.GoldDepositTargetSlot, 1, _goldDepositRows * _goldDepositColumns);
        TxtGoldDepositDelay.Text = Math.Clamp(_settings.GoldDepositOpenDelayMs, 500, 30000).ToString(CultureInfo.InvariantCulture);
        ChkGoldDepositIngots.IsChecked = _settings.GoldDepositIngots;
        ChkGoldDepositBlocks.IsChecked = _settings.GoldDepositBlocks;
        ChkGoldDepositHotbar.IsChecked = _settings.GoldDepositIncludeHotbar;
        RefreshGoldDepositSlotGrid();
        UpdateGoldDepositSettingsUi();
    }

    private void RefreshGoldDepositSlotGrid()
    {
        if (PanelGoldDepositSlots == null) return;
        _goldDepositRows = Math.Clamp(_goldDepositRows, 1, 6);
        _goldDepositColumns = Math.Clamp(_goldDepositColumns, 1, 9);
        _goldDepositSelectedSlot = Math.Clamp(_goldDepositSelectedSlot, 1, _goldDepositRows * _goldDepositColumns);
        PanelGoldDepositSlots.Rows = _goldDepositRows;
        PanelGoldDepositSlots.Columns = _goldDepositColumns;
        BorderGoldDepositSlots.Width = _goldDepositColumns * 40 + 12;
        PanelGoldDepositSlots.Children.Clear();
        for (int slot = 1; slot <= _goldDepositRows * _goldDepositColumns; slot++)
        {
            bool selected = slot == _goldDepositSelectedSlot;
            var button = new Button
            {
                Content = slot.ToString(CultureInfo.InvariantCulture),
                Tag = slot,
                Width = 36,
                Height = 28,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(selected ? GlassPalette.MapLegacyColor(23, 50, 74) : GlassPalette.MapLegacyColor(30, 42, 57)),
                BorderBrush = new SolidColorBrush(selected ? GlassPalette.MapLegacyColor(46, 168, 255) : GlassPalette.MapLegacyColor(62, 83, 110)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Foreground = new SolidColorBrush(selected ? GlassPalette.MapLegacyColor(56, 214, 180) : GlassPalette.MapLegacyColor(216, 226, 240)),
                ToolTip = $"Pole {slot} — rząd {(slot - 1) / _goldDepositColumns + 1}, kolumna {(slot - 1) % _goldDepositColumns + 1}"
            };
            button.Click += GoldDepositSlot_Click;
            PanelGoldDepositSlots.Children.Add(button);
        }
        int row = (_goldDepositSelectedSlot - 1) / _goldDepositColumns + 1;
        int column = (_goldDepositSelectedSlot - 1) % _goldDepositColumns + 1;
        TxtGoldDepositSelectedSlot.Text = $"{_goldDepositSelectedSlot} (rząd {row}, kolumna {column})";
        TxtGoldDepositGridSize.Text = $"Układ: {_goldDepositRows} × {_goldDepositColumns} • liczba pól: {_goldDepositRows * _goldDepositColumns}";
        BtnGoldDepositAddRow.IsEnabled = _goldDepositRows < 6;
        BtnGoldDepositRemoveRow.IsEnabled = _goldDepositRows > 1;
        BtnGoldDepositAddColumn.IsEnabled = _goldDepositColumns < 9;
        BtnGoldDepositRemoveColumn.IsEnabled = _goldDepositColumns > 1;
    }

    private void GoldDepositSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int slot } || slot < 1 || slot > _goldDepositRows * _goldDepositColumns) return;
        _goldDepositSelectedSlot = slot;
        RefreshGoldDepositSlotGrid();
        GoldDepositSetting_Changed(sender, e);
    }

    private void BtnGoldDepositAddRow_Click(object sender, RoutedEventArgs e) => ChangeGoldDepositRows(1, sender, e);
    private void BtnGoldDepositRemoveRow_Click(object sender, RoutedEventArgs e) => ChangeGoldDepositRows(-1, sender, e);
    private void BtnGoldDepositAddColumn_Click(object sender, RoutedEventArgs e) => ChangeGoldDepositColumns(1, sender, e);
    private void BtnGoldDepositRemoveColumn_Click(object sender, RoutedEventArgs e) => ChangeGoldDepositColumns(-1, sender, e);

    private void ChangeGoldDepositRows(int change, object sender, RoutedEventArgs e)
    {
        int rows = Math.Clamp(_goldDepositRows + change, 1, 6);
        if (rows == _goldDepositRows) return;
        _goldDepositRows = rows;
        RefreshGoldDepositSlotGrid();
        GoldDepositSetting_Changed(sender, e);
    }

    private void ChangeGoldDepositColumns(int change, object sender, RoutedEventArgs e)
    {
        int columns = Math.Clamp(_goldDepositColumns + change, 1, 9);
        if (columns == _goldDepositColumns) return;
        _goldDepositColumns = columns;
        RefreshGoldDepositSlotGrid();
        GoldDepositSetting_Changed(sender, e);
    }

    private void SaveGoldDepositSettings()
    {
        GoldDepositOptions options = GoldOptions();
        _settings.GoldDepositEnabled = ChkGoldDepositEnabled.IsChecked == true;
        _settings.GoldDepositCommand = options.Command;
        _settings.GoldDepositMenuTitle = options.MenuTitle;
        _settings.GoldDepositRows = Math.Clamp(options.Rows, 1, 6);
        _settings.GoldDepositColumns = Math.Clamp(options.Columns, 1, 9);
        _settings.GoldDepositTargetSlot = Math.Clamp(options.TargetSlot, 1, _settings.GoldDepositRows * _settings.GoldDepositColumns);
        _settings.GoldDepositOpenDelayMs = Math.Clamp(options.OpenDelayMs, 500, 30000);
        _settings.GoldDepositIngots = options.Ingots;
        _settings.GoldDepositBlocks = options.Blocks;
        _settings.GoldDepositIncludeHotbar = options.IncludeHotbar;
    }

    private void UpdateGoldDepositSettingsUi()
    {
        if (PanelGoldDepositSettings == null) return;
        bool enabled = ChkInventoryCleanupEnabled.IsChecked == true && ChkGoldDepositEnabled.IsChecked == true;
        PanelGoldDepositSettings.IsEnabled = enabled;
        PanelGoldDepositSettings.Visibility = ChkGoldDepositEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateGoldDepositTestButtons();
    }

    private void GoldDepositSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi) return;
        UpdateGoldDepositSettingsUi();
        if (ChkGoldDepositEnabled.IsChecked != true)
        {
            if (IsGoldDepositActive) StopGoldDeposit("Wyłączono opcję wpłaty złota.");
            else _goldDepositPending = false;
            CancelGoldDepositReadTest();
        }
        else if (ChkGoldDepositEnabled.IsChecked == true && _goldDepositFlow == null)
        {
            string error = GoldOptions().Validate();
            SetGoldStatus(error.Length == 0 ? "Konfiguracja gotowa. Najpierw użyj testu odczytu menu i małej ilości złota." : error, error.Length > 0);
        }
        MarkDirty();
    }

    private void SetGoldStatus(string message, bool error = false)
    {
        LogStatus("Złoto", message, error ? "Red" : "Default");
        if (!string.Equals(TxtGoldDepositStatus.Text, message, StringComparison.Ordinal)) TxtGoldDepositStatus.Text = message;
        TxtGoldDepositStatus.Foreground = error ? Brushes.OrangeRed : Brushes.DeepSkyBlue;
    }

    private void ResetGoldDepositState()
    {
        _goldDepositFlow = null;
        _goldDepositPending = false;
        _goldDepositParked = false;
        _goldDepositEmptyCursor = null;
        _goldDepositClickAction = GoldDepositAction.None;
        _goldDepositManualTest = null;
        _goldDepositReadTestGeneration++;
        _goldDepositReadTestActive = false;
        // A pending OCR task keeps its busy flag until its finally block. Its
        // generation/flow identity checks prevent input after STOP or a restart.
    }

    private void LogGoldDeposit(string status, string details)
    {
        _ = _miningLogService.RecordAutomationEvent(GetMiningRunId(_inventoryCleanupOwner),
            _goldDepositManualTest != null ? "Test wpłaty złota" : GetInventoryCleanupOwnerLabel(_inventoryCleanupOwner), "gold-deposit", status, details, out _);
        RefreshMiningLogsSummary();
    }

    private void StopGoldDeposit(string reason)
    {
        if (_goldDepositManualTest != null) { FinishGoldDepositManualTest(reason, success: false); return; }
        InventoryCleanupOwner owner = _inventoryCleanupOwner;
        LogGoldDeposit(MiningLogStatuses.Aborted, reason);
        RecordAbortedInventoryCleanup("Wpłata złota: " + reason);
        // Do not close an uncertain GUI or click a possibly carried item.
        StopMiningRuntime(owner);
        EndMiningLogRun(owner, "Kopanie zatrzymane: wpłata złota — " + reason);
        SetGoldStatus(reason, error: true);
        UpdateInventoryCleanupStatus("Wpłata złota zatrzymana: " + reason, "Red");
        UpdateStatusBar("Kopacz zatrzymany — sprawdź wpłatę złota i przedmiot na kursorze.", "Red");
    }

    private bool GoldInputAllowed() => _targetGameWindowHandle != IntPtr.Zero
        && IsGameInputAvailableFor(_targetGameWindowHandle)
        && _inventoryCleanupStage == InventoryCleanupStage.DepositGold
        && (_goldDepositManualTest != null
            ? _goldDepositManualTest.Started && _goldDepositManualTest.StopReason.Length == 0
                && _goldDepositManualTest.TargetWindow == _targetGameWindowHandle && !HasOtherGoldTestAutomation()
            : (_inventoryCleanupOwner == InventoryCleanupOwner.Kopacz533 ? _kopacz533RuntimeEnabled : _kopacz633RuntimeEnabled));

    // async void z timera: każdy wyjątek musi zostać złapany tutaj, inaczej zamknąłby program.
    private async void ProcessGoldDeposit(DateTime now)
    {
        try
        {
            await ProcessGoldDepositCoreAsync(now);
        }
        catch (Exception ex)
        {
            AppLog.Error("Złoto", "Nieoczekiwany błąd wpłaty złota", ex);
            StopGoldDeposit("Nieoczekiwany błąd wpłaty złota: " + ex.GetBaseException().Message);
        }
    }

    private async Task ProcessGoldDepositCoreAsync(DateTime now)
    {
        if (_goldDepositBusy) return;
        if (!GoldInputAllowed()) { StopGoldDeposit("Utrata fokusu lub zatrzymanie kanału kopania."); return; }
        if (_goldDepositFlow == null)
        {
            _goldDepositFlow = new GoldDepositFlow(GoldOptions(), now);
            LogGoldDeposit(MiningLogStatuses.Completed, "Rozpoczęto wpłatę złota po skanie EQ; " + _goldDepositFlow.Options.Command);
        }
        GoldDepositFlow flow = _goldDepositFlow;
        int generation = _inventoryCleanupGeneration;
        try
        {
            if (_goldDepositClickAction != GoldDepositAction.None)
            {
                if (now < _goldDepositClickAt) return;
                if (!TryCaptureTargetClient(out Drawing.Bitmap? clickImage, out Drawing.Rectangle clickArea))
                    throw new InvalidOperationException("Nie udało się potwierdzić GUI przed kliknięciem.");
                using (clickImage)
                {
                    if (!TryGetGameCursorPosition(out POINT cursor)) throw new InvalidOperationException("Nie odczytano położenia myszy przed kliknięciem.");
                    var expectedPointer = new Drawing.Point(_goldDepositArea.Left + _goldDepositClickPoint.X, _goldDepositArea.Top + _goldDepositClickPoint.Y);
                    string geometryError = GoldDepositClickGuard.ValidateGeometry(_goldDepositArea, clickArea, expectedPointer,
                        new Drawing.Point(cursor.X, cursor.Y), IsGameGuiOpen(flow.Options.Columns), InventoryMarkerDetector.IsChestHeaderVisible(clickImage!, _goldDepositLayout));
                    if (geometryError.Length > 0) throw new InvalidOperationException(geometryError);
                    if (!EnsureF3TesseractEngine()) throw new InvalidOperationException("Brak OCR do potwierdzenia menu przed kliknięciem.");
                    IntPtr target = _targetGameWindowHandle;
                    ChestMarkerLayout trustedLayout = _goldDepositLayout;
                    _goldDepositBusy = true;
                    string titleError = await Task.Run(() =>
                    {
                        using var title = clickImage!.Clone(trustedLayout.HeaderSample, Imaging.PixelFormat.Format32bppArgb);
                        return flow.Options.ValidateMenuTitle(RunOcrOnBitmap(title, Tesseract.PageSegMode.SingleLine));
                    });
                    if (generation != _inventoryCleanupGeneration || !ReferenceEquals(flow, _goldDepositFlow)) return;
                    if (flow.HasExpired(DateTime.UtcNow)) throw new InvalidOperationException("Przekroczono limit czasu wpłaty przed kliknięciem.");
                    if (titleError.Length > 0) throw new InvalidOperationException(titleError);
                    if (target != _targetGameWindowHandle || !GoldInputAllowed()) throw new InvalidOperationException("Utracono fokus lub zmieniono okno podczas sprawdzania kliknięcia.");
                    if (!TryGetWindowClientRectOnScreen(target, out RECT currentRect) || !TryGetGameCursorPosition(out POINT currentPointer))
                        throw new InvalidOperationException("Nie potwierdzono okna i myszy bezpośrednio przed kliknięciem.");
                    var currentArea = new Drawing.Rectangle(currentRect.Left, currentRect.Top, currentRect.Right - currentRect.Left, currentRect.Bottom - currentRect.Top);
                    geometryError = GoldDepositClickGuard.ValidateGeometry(_goldDepositArea, currentArea, expectedPointer,
                        new Drawing.Point(currentPointer.X, currentPointer.Y), IsGameGuiOpen(flow.Options.Columns), headerVisible: true);
                    if (geometryError.Length > 0) throw new InvalidOperationException(geometryError);
                    if (!GoldInputAllowed() || !SendMouseClick(leftButton: true, holdPulseMode: false))
                        throw new InvalidOperationException("Nie wykonano kliknięcia wpłaty.");
                    flow.ClickExecuted(DateTime.UtcNow);
                }
                _goldDepositClickAction = GoldDepositAction.None;
                _goldDepositParked = false;
                return;
            }
            if (now < flow.NextAt) return;
            if (flow.AwaitsTyping && !ChatInputGate.IsReady(now)) return;
            GoldDepositObservation? observation = null;
            if (flow.NeedsScreen)
            {
                if (!IsGameGuiOpen(flow.Options.Columns))
                    observation = new(false, false, true, null, Array.Empty<DetectedInventoryItem>(), "GUI jest zamknięte.");
                else
                {
                    if (!_goldDepositParked)
                    {
                        if (!TryCaptureTargetClient(out Drawing.Bitmap? parkImage, out Drawing.Rectangle parkArea))
                            throw new InvalidOperationException("Brak obrazu menu wpłaty.");
                        using (parkImage)
                        {
                            if (!TryDetectGoldMenuLayout(parkImage!, flow.Options, out ChestMarkerLayout parkLayout))
                            {
                                observation = new(true, false, false, null, Array.Empty<DetectedInventoryItem>(), DescribeGoldMenuGridError(parkImage!, flow.Options.TargetSlot, flow.Options.Columns));
                            }
                            else
                            {
                                _goldDepositLayout = parkLayout;
                                _goldDepositArea = parkArea;
                                if (!SetGameCursorPosition(parkArea.Left + parkLayout.CursorPark.X, parkArea.Top + parkLayout.CursorPark.Y))
                                    throw new InvalidOperationException("Nie udało się odsunąć kursora przed odczytem.");
                                _goldDepositParked = true;
                                _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(120);
                                return;
                            }
                        }
                    }
                    if (observation == null)
                    {
                        if (!TryGetGameCursorPosition(out POINT parked) || parked.X != _goldDepositArea.Left + _goldDepositLayout.CursorPark.X || parked.Y != _goldDepositArea.Top + _goldDepositLayout.CursorPark.Y)
                            throw new InvalidOperationException("Mysz została poruszona podczas automatycznej wpłaty.");
                        if (!EnsureF3TesseractEngine() || !TryCaptureTargetClient(out Drawing.Bitmap? image, out Drawing.Rectangle area))
                            throw new InvalidOperationException("Brak OCR lub obrazu menu wpłaty.");
                        byte[]? empty = _goldDepositEmptyCursor;
                        _goldDepositBusy = true;
                        using (image)
                        {
                            var result = await Task.Run(() => ReadGoldObservation(image!, flow.Options, empty));
                            if (generation != _inventoryCleanupGeneration || !ReferenceEquals(flow, _goldDepositFlow)) return;
                            if (!GoldInputAllowed() || area != _goldDepositArea || !TryGetGameCursorPosition(out POINT after) || after.X != parked.X || after.Y != parked.Y)
                                throw new InvalidOperationException("Zmieniono okno lub mysz podczas odczytu menu.");
                            observation = result.Screen;
                            if (observation.MenuValid)
                            {
                                _goldDepositEmptyCursor ??= result.CursorPatch;
                                _goldDepositLayout = result.Layout;
                            }
                        }
                        _goldDepositBusy = false;
                        now = DateTime.UtcNow;
                    }
                }
            }
            GoldDepositStep step = flow.Tick(now, observation);
            SetGoldStatus(flow.Message);
            UpdateInventoryCleanupStatus("Wpłata złota: " + flow.Message, "Orange");
            switch (step.Action)
            {
                case GoldDepositAction.CloseInitialGui:
                    if (IsGameGuiOpen(flow.Options.Columns)) SendKeyTap(VK_ESCAPE);
                    break;
                case GoldDepositAction.OpenChat: SendChatOpenKeyTap(); break;
                case GoldDepositAction.TypeCommand:
                    if (!SendTextByKeyboard(flow.Options.Command)) throw new InvalidOperationException("Nie udało się wpisać komendy wpłaty.");
                    break;
                case GoldDepositAction.SubmitCommand: SendKeyTap(VK_RETURN); break;
                case GoldDepositAction.PickSource:
                case GoldDepositAction.ReturnRejected:
                case GoldDepositAction.Deposit:
                    Drawing.Point point = step.Action == GoldDepositAction.Deposit
                        ? _goldDepositLayout.GetMenuSlotCenter(flow.Options.TargetSlot)
                        : _goldDepositLayout.PlayerInventory.GetSlotCenter(step.SourceSlot);
                    if (!GoldInputAllowed() || !SetGameCursorPosition(_goldDepositArea.Left + point.X, _goldDepositArea.Top + point.Y))
                        throw new InvalidOperationException("Nie udało się przygotować przeniesienia złota.");
                    _goldDepositClickPoint = point;
                    _goldDepositClickAction = step.Action;
                    _goldDepositClickAt = now.AddMilliseconds(80);
                    _goldDepositParked = false;
                    break;
                case GoldDepositAction.CloseMenu: SendKeyTap(VK_ESCAPE); _goldDepositParked = false; break;
                case GoldDepositAction.Stop: StopGoldDeposit(flow.Message); return;
                case GoldDepositAction.Complete:
                    if (_goldDepositManualTest != null) { FinishGoldDepositManualTest(flow.Message, success: true); return; }
                    _goldDepositSummary = flow.Message;
                    LogGoldDeposit(MiningLogStatuses.Completed, flow.Message);
                    _goldDepositPending = false;
                    _goldDepositFlow = null;
                    _inventoryCleanupStage = GetInventoryCleanupPostCommandStage();
                    SetGoldStatus(flow.Message);
                    break;
            }
            _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(20);
        }
        catch (Exception ex)
        {
            if (generation == _inventoryCleanupGeneration && ReferenceEquals(flow, _goldDepositFlow)) StopGoldDeposit(ex.Message);
        }
        finally { _goldDepositBusy = false; UpdateGoldDepositTestButtons(); }
    }

    private (GoldDepositObservation Screen, byte[] CursorPatch, ChestMarkerLayout Layout) ReadGoldObservation(Drawing.Bitmap image, GoldDepositOptions options, byte[]? emptyCursor)
    {
        var invalid = new GoldDepositObservation(true, false, false, null, Array.Empty<DetectedInventoryItem>());
        if (!TryDetectGoldMenuLayout(image, options, out ChestMarkerLayout layout))
            return (invalid with { Error = DescribeGoldMenuGridError(image, options.TargetSlot, options.Columns) }, Array.Empty<byte>(), default);
        using var title = image.Clone(layout.HeaderSample, Imaging.PixelFormat.Format32bppArgb);
        string text = RunOcrOnBitmap(title, Tesseract.PageSegMode.SingleLine);
        Drawing.Point target = layout.GetMenuSlotCenter(options.TargetSlot);
        DetectedInventoryItem? bank = InventoryMarkerDetector.ReadMarkedItem(image, new Drawing.Point(target.X - 8 * layout.Scale, target.Y - 8 * layout.Scale), layout.Scale);
        string identityError = options.ValidateMenuIdentity(text, bank?.ItemId);
        if (identityError.Length > 0)
            return (invalid with { Error = identityError }, Array.Empty<byte>(), layout);
        Drawing.Point park = layout.CursorPark;
        var cursorRect = new Drawing.Rectangle(park.X - 8 * layout.Scale, park.Y - 8 * layout.Scale, 16 * layout.Scale, 16 * layout.Scale);
        byte[] patch = ReadGoldPatch(image, cursorRect);
        var cursorItem = InventoryMarkerDetector.ReadMarkedItem(image, cursorRect.Location, layout.Scale);
        bool empty = emptyCursor == null || GoldPatchMatches(patch, emptyCursor);
        var items = InventoryMarkerDetector.ReadMarkedItems(image, layout.PlayerInventory, options.IncludeHotbar)
            .Where(item => GoldDepositOptions.IsGold(item.ItemId)).ToArray();
        return (new(true, true, empty, empty ? null : cursorItem, items), patch, layout);
    }

    private static bool TryDetectGoldMenuLayout(Drawing.Bitmap image, GoldDepositOptions options, out ChestMarkerLayout layout) =>
        InventoryMarkerDetector.TryDetectChestMenu(image, options.Columns, (options.TargetSlot - 1) / options.Columns + 1, out layout);

    private static string DescribeGoldMenuGridError(Drawing.Bitmap image, int targetSlot, int columns)
    {
        if (InventoryMarkerDetector.TryDetectChestMenu(image, columns, 1, out ChestMarkerLayout layout))
            return $"Rozpoznano menu z {layout.Rows} rzędami, ale wybrane pole {targetSlot} (rząd {(targetSlot - 1) / columns + 1}) jest poza menu. Tytułu i ikony wpłaty jeszcze nie sprawdzono.";
        return "Nie odczytano ramki menu i siatki EQ. Tytułu i ikony wpłaty jeszcze nie sprawdzono. Menu gry musi być widoczne i niezasłonięte innymi oknami.";
    }

    private static byte[] ReadGoldPatch(Drawing.Bitmap image, Drawing.Rectangle rect)
    {
        using var patch = image.Clone(rect, Imaging.PixelFormat.Format32bppArgb);
        Imaging.BitmapData data = patch.LockBits(new Drawing.Rectangle(0, 0, patch.Width, patch.Height), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[patch.Width * patch.Height * 4];
            for (int y = 0; y < patch.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * patch.Width * 4, patch.Width * 4);
            return bytes;
        }
        finally { patch.UnlockBits(data); }
    }

    private static bool GoldPatchMatches(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return false;
        int changed = 0;
        for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 8) changed++;
        return changed <= a.Length / 200;
    }

    private async void BtnGoldDepositTest_Click(object sender, RoutedEventArgs e)
    {
        if (HasOtherGoldTestAutomation() || IsGoldDepositActive || _goldDepositBusy || _goldDepositReadTestActive) { SetGoldStatus("Najpierw zatrzymaj kopanie i pozostałe testy.", true); return; }
        GoldDepositOptions options = GoldOptions();
        string error = options.Validate();
        if (error.Length > 0) { SetGoldStatus(error, true); return; }
        IntPtr target = _targetGameWindowHandle;
        if (target == IntPtr.Zero) { SetGoldStatus("Najpierw wybierz okno Minecrafta.", true); return; }
        int generation = ++_goldDepositReadTestGeneration;
        bool ownsWorker = false;
        _goldDepositReadTestActive = true;
        UpdateGoldDepositTestButtons();
        try
        {
            SetGoldStatus("Test odczytu za 3 s: otwórz menu komendą i odsuń mysz poza GUI. Bez wpłacania i klikania.");
            await Task.Delay(3000);
            if (generation != _goldDepositReadTestGeneration || !_goldDepositReadTestActive) return;
            if (target != _targetGameWindowHandle || GetForegroundWindow() != target || HasOtherGoldTestAutomation()) { SetGoldStatus("Test anulowany — brak fokusu, zmieniono okno lub uruchomiono automat.", true); return; }
            if (!EnsureF3TesseractEngine() || !TryCaptureTargetClient(out Drawing.Bitmap? image, out _)) { SetGoldStatus("Brak OCR lub obrazu gry.", true); return; }
            _goldDepositBusy = ownsWorker = true;
            using (image)
            {
                var result = await Task.Run(() => ReadGoldObservation(image!, options, null));
                if (generation != _goldDepositReadTestGeneration || !_goldDepositReadTestActive) return;
                if (target != _targetGameWindowHandle || GetForegroundWindow() != target) { SetGoldStatus("Test odczytu anulowany — zmieniono okno lub fokus.", true); return; }
                SetGoldStatus(result.Screen.MenuValid ? $"Menu potwierdzone (wykryto {result.Layout.Rows} rzędów). Pole wpłaty {options.TargetSlot}; wykryto {result.Screen.Items.Count(i => options.IsSelected(i.ItemId))} stosów złota. Niczego nie przeniesiono." : result.Screen.Error, !result.Screen.MenuValid);
            }
        }
        catch (Exception ex) { if (generation == _goldDepositReadTestGeneration) SetGoldStatus("Test: " + ex.Message, true); }
        finally
        {
            if (ownsWorker) _goldDepositBusy = false;
            if (generation == _goldDepositReadTestGeneration) _goldDepositReadTestActive = false;
            UpdateGoldDepositTestButtons();
        }
    }
}
