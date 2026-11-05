using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using MinecraftHelper.Models;
using MinecraftHelper.Services;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;
using TesseractEngine = Tesseract.TesseractEngine;
using TesseractPix = Tesseract.Pix;
using TesseractPage = Tesseract.Page;
using TesseractEngineMode = Tesseract.EngineMode;
using TesseractPageSegMode = Tesseract.PageSegMode;

namespace MinecraftHelper
{
    public partial class MainWindow : Window, IMinerRemoteSession, IMinerWorkspaceSession
    {
        private readonly SettingsService _settingsService;
        private readonly MiningLogService _miningLogService;
        private readonly string _accountProfileId;
        private string _accountDisplayName;
        private readonly bool _workspaceWorker;
        private readonly MinerRemoteControlServer _remoteControlServer;
        private readonly MinerGameWindowClaim _gameWindowClaim = new();
        private AppSettings _settings;
        private readonly bool _isFirstRun;
        private readonly string _currentAppVersion;
        private readonly bool _showStartupNotice;

        private bool _pendingChanges = false;
        private readonly DispatcherTimer _dirtyTimer;
        private readonly DispatcherTimer _focusTimer;
        private readonly DispatcherTimer _macroTimer;
        private readonly DispatcherTimer _autoReconnectTimer;
        private readonly DispatcherTimer _transientStatusTimer;

        // Use the fast dispatcher cadence only during an active mining or
        // automation sequence. PvP clickers have their own separate process.
        private static readonly TimeSpan MacroTimerActiveInterval = TimeSpan.FromMilliseconds(5);
        // Bez aktywnej pracy wystarczy 50 ms; bind albo START przełącza na 5 ms.
        private static readonly TimeSpan MacroTimerIdleInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan TargetWindowResolveRetryInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan OverlayLayoutRefreshInterval = TimeSpan.FromSeconds(2);
        private readonly DamageSoundDetector _damageSoundDetector;

        private bool _isMinecraftFocused;
        private IntPtr _targetGameWindowHandle = IntPtr.Zero;
        private DateTime _nextTargetWindowResolveAtUtc = DateTime.MinValue;
        private bool _isLoadingUi = true;
        private readonly Dictionary<FrameworkElement, bool> _expandableSectionStates = new();
        private bool _kopacz533RuntimeEnabled;
        private bool _kopacz633RuntimeEnabled;
        private bool _testAutoFishingRuntimeEnabled;
        private bool _emergencyDamageSoundMonitoringForMiner;
        private bool _emergencyDamageSoundManualTestActive;
        private bool _emergencyDamageSoundLiveTestActive;
        private bool _emergencyDamageSoundHandlingAlarm;
        private DateTime _emergencyDamageSoundManualTestUntilUtc = DateTime.MinValue;
        private DateTime _emergencyDamageSoundLastAlarmAtUtc = DateTime.MinValue;
        private bool _emergencyReconnectActive;
        private bool _emergencyReconnectSoundGuardActive;
        private bool _emergencyReconnectResumeKopacz533;
        private bool _emergencyReconnectResumeKopacz633;
        private bool _emergencyReconnectShutdownAfterHome;
        private int _emergencyReconnectInventoryAttempts;

        private bool _kopacz533BindWasDown;
        private bool _kopacz633BindWasDown;
        private bool _testAutoFishingBindWasDown;
        private bool _testAutoFishingCaptureBindWasDown;
        private bool _suppressBindToggleUntilRelease;

        private bool _kopacz533Holding;
        private DateTime _nextKopacz533CommandAtUtc = DateTime.UtcNow;
        private DateTime _nextKopacz533StageAtUtc = DateTime.UtcNow;
        private bool _kopacz533ResumeMiningPending;
        private DateTime _nextKopacz533ResumeAtUtc = DateTime.UtcNow;
        private Kopacz533CommandStage _kopacz533CommandStage = Kopacz533CommandStage.None;
        private bool _kopacz533CommandSequenceCompleted;
        private int _kopacz533CommandIndex;
        private int _kopacz533PendingCommandIndex = -1;
        private string _kopacz533PendingCommand = string.Empty;
        private DateTime _kopacz533RuntimeStartedAtUtc = DateTime.UtcNow;
        private DateTime _nextKopacz633CommandAtUtc = DateTime.UtcNow;
        private DateTime _nextKopacz633StageAtUtc = DateTime.UtcNow;
        private bool _kopacz633ResumeMiningPending;
        private DateTime _nextKopacz633ResumeAtUtc = DateTime.UtcNow;
        private Kopacz633CommandStage _kopacz633CommandStage = Kopacz633CommandStage.None;
        private bool _kopacz633CommandSequenceCompleted;
        private int _kopacz633CommandIndex;
        private int _kopacz633PendingCommandIndex = -1;
        private string _kopacz633PendingCommand = string.Empty;
        private DateTime _kopacz633RuntimeStartedAtUtc = DateTime.UtcNow;
        private bool _kopacz633HoldingAttack;
        private Kopacz633StrafeDirection _kopacz633StrafeDirection = Kopacz633StrafeDirection.None;
        private int _kopacz633UpwardLegIndex;
        private DateTime _kopacz633MovementLegEndAtUtc = DateTime.UtcNow;
        private readonly List<CheckBox> _inventoryCleanupSlotCheckBoxes = new List<CheckBox>();
        private readonly List<CheckBox> _inventoryCleanupItemTypeCheckBoxes = new List<CheckBox>();
        private readonly List<Button> _autoReconnectHomeSlotButtons = new List<Button>();
        private readonly List<Button> _autoReconnectServerProfileButtons = new List<Button>();
        private readonly List<Drawing.Point> _inventoryCleanupTargets = new List<Drawing.Point>();
        private readonly Dictionary<string, int> _inventoryCleanupInitialItemTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _inventoryCleanupInitialItemTypeStackCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _inventoryCleanupItemTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _inventoryCleanupItemTypeStackCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private InventoryCleanupStage _inventoryCleanupStage = InventoryCleanupStage.None;
        private int _inventoryCleanupGeneration;
        private bool _inventoryCleanupScanInProgress;
        private InventoryCleanupOwner _inventoryCleanupOwner = InventoryCleanupOwner.None;
        private DateTime _nextInventoryCleanupAtUtc = DateTime.MaxValue;
        private DateTime _nextInventoryCleanupStageAtUtc = DateTime.UtcNow;
        private InventoryCleanupOwner _inventoryCleanupCommandPauseOwner = InventoryCleanupOwner.None;
        private DateTime _inventoryCleanupCommandPauseStartedAtUtc = DateTime.MinValue;
        private int _inventoryCleanupTargetIndex;
        private int _inventoryCleanupDetectionAttempts;
        private int _inventoryCleanupDropPass;
        private int _inventoryCleanupInitialMarkedStacks;
        private int _inventoryCleanupRemovedStacks;
        private int _inventoryCleanupRemovedItems;
        private int _inventoryCleanupRemainingMarkedStacks;
        private bool _inventoryCleanupCursorParkedForDetection;
        private Drawing.Rectangle _inventoryCleanupClientArea = Drawing.Rectangle.Empty;
        private bool _inventoryCleanupControlDown;
        private bool _inventoryCleanupDropKeyDown;
        private int _inventoryCleanupDropVirtualKey;
        private bool _inventoryCleanupReturnLeftDown;
        private bool _inventoryCleanupReturnBackwardDown;
        private DateTime _inventoryCleanupReturnLeftUntilUtc = DateTime.MinValue;
        private DateTime _inventoryCleanupReturnBackwardUntilUtc = DateTime.MinValue;
        private bool _inventoryCleanupEatAfterCleanupPending;
        private bool _inventoryCleanupEatingRightButtonDown;
        private bool _inventoryCleanupEatingCompleted;
        private string _inventoryCleanupLastResult = "Brak poprzedniego skanu";
        private bool _inventoryCleanupLastResultWarning;
        private int _inventoryCleanupFullCobblestoneStacks;
        private int _inventoryCleanupLastFullCobblestoneStacks;
        private bool _inventoryCleanupCobbleXCommandPending;
        private bool _inventoryCleanupCobbleXCommandSent;
        private bool _inventoryCleanupCobbleXCommandFailed;
        private int _inventoryCleanupCobbleXCommandTargetCount = 1;
        private int _inventoryCleanupCobbleXCommandSendCount;
        private string _inventoryCleanupPendingCobbleXCommand = string.Empty;
        private string _inventoryCleanupLogSessionId = string.Empty;
        private string _inventoryCleanupLogStartError = string.Empty;
        private string _kopacz533MiningRunId = string.Empty;
        private string _kopacz633MiningRunId = string.Empty;
        private DateTime _inventoryCleanupOpenedAtUtc = DateTime.MinValue;
        private MiningLogSummary _latestMiningLogSummary;
        private static readonly (string Id, string Label)[] InventoryCleanupItemTypes =
        {
            ("diamond", "Diament"),
            ("gold_ingot", "Sztabka złota"),
            ("gold_block", "Blok złota"),
            ("iron_ingot", "Sztabka żelaza"),
            ("iron_block", "Blok żelaza"),
            ("emerald", "Emerald"),
            ("emerald_block", "Blok emeraldu"),
            ("obsidian", "Obsydian"),
            ("apple", "Jabłko"),
            ("sand", "Piasek"),
            ("gunpowder", "Proch"),
            ("coal", "Węgiel"),
            ("quartz", "Kwarc"),
            ("book", "Książka"),
            ("ender_pearl", "Ender perła"),
            ("redstone", "Redstone")
        };
        private DateTime _nextTestAutoFishingScanAtUtc = DateTime.UtcNow;
        private DateTime _nextTestAutoFishingActionAtUtc = DateTime.UtcNow;
        private DateTime _nextTestAutoFishingRepairAtUtc = DateTime.MaxValue;
        private DateTime _nextTestAutoFishingRepairStageAtUtc = DateTime.UtcNow;
        private bool _testAutoFishingAwaitSecondClick;
        private bool _testAutoFishingRecastAfterRepairPending;
        private DateTime _testAutoFishingRecastAfterRepairAtUtc = DateTime.UtcNow;
        private bool _testAutoFishingWaitingForCastRegistration;
        private bool _testAutoFishingBaselineReady;
        private DateTime _testAutoFishingBaselineArmedAtUtc = DateTime.UtcNow;
        private DateTime _testAutoFishingNoBobberSinceAtUtc = DateTime.MinValue;
        private DateTime _nextTestAutoFishingPreviewAtUtc = DateTime.UtcNow;
        private double _testAutoFishingLastDetectedBobberX = double.NaN;
        private double _testAutoFishingBaselineBobberY;
        private double _testAutoFishingLastDetectedBobberY = double.NaN;
        private int _testAutoFishingLastDetectedRedPixels;
        private int _testAutoFishingMissedDetections;
        private int _testAutoFishingBiteConfirmationFrames;
        private int _testAutoFishingCaughtCount;
        private DateTime _testAutoFishingLastCatchAtUtc = DateTime.MinValue;
        private DateTime _testAutoFishingRuntimeStartedAtUtc = DateTime.MinValue;
        private TestAutoFishingRepairStage _testAutoFishingRepairStage;
        private DateTime _nextRuntimeTileRefreshAtUtc = DateTime.UtcNow;
        private DateTime _nextOverlayLayoutRefreshAtUtc = DateTime.MinValue;
        private OverlayHudWindow? _overlayHud;
        private bool _overlayLayoutWarningReported;
        private Forms.NotifyIcon? _trayIcon;
        private ContextMenu? _trayMenu;
        private bool _isExitRequested;
        private bool _isMinimizedToTray;
        private bool _isCaptureAreaSelectionInProgress;
        private readonly object _f3TesseractLock = new object();
        private TesseractEngine? _f3TesseractEngine;
        private AutoReconnectStage _autoReconnectStage = AutoReconnectStage.None;
        private int _autoReconnectGeneration;
        private DateTime _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
        private DateTime _nextAutoReconnectHealthCheckAtUtc = DateTime.UtcNow;
        private bool _autoReconnectOcrInProgress;
        private bool _autoReconnectTickInProgress;
        private bool _autoReconnectManualRun;
        private bool _autoReconnectInventoryOnly;
        private bool _autoReconnectResumeKopacz533;
        private bool _autoReconnectResumeKopacz633;
        private string _autoReconnectLogMiningRunId = string.Empty;
        private string _autoReconnectLogOwner = string.Empty;
        private int _autoReconnectAttempt;
        private int _autoReconnectMainMenuClicks;
        private DateTime _autoReconnectDisconnectScreenSinceUtc = DateTime.MinValue;
        private bool _autoReconnectRefreshServerList;
        private DateTime _autoReconnectHomeMenuDeadlineUtc = DateTime.MinValue;
        private int _autoReconnectHomeCommandRetries;
        private int _autoReconnectInventoryFailures;
        private int _autoReconnectLastCountdownSecond = -1;
        private AutoReconnectScreenKind _autoReconnectPendingScreenKind = AutoReconnectScreenKind.Unknown;
        private string _autoReconnectLastOcrText = string.Empty;
        private bool _autoReconnectReturningHomeAfterMissingPickaxe;
        private string _autoReconnectActiveHomeCommand = "/home";
        private bool _autoReconnectActiveHomeHasGui = true;
        private int _autoReconnectActiveHomeGuiDelaySeconds = 1;
        private int _autoReconnectActiveHomeGuiRows = 3;
        private int _autoReconnectActiveHomeGuiColumns = 9;
        private int _autoReconnectActiveHomeSlot = 11;
        private int _autoReconnectActiveTeleportDelaySeconds = 10;
        private string _pendingAutoReconnectProfileDeleteId = string.Empty;
        private DateTime _pendingAutoReconnectProfileDeleteUntilUtc = DateTime.MinValue;
        private const double OverlayScreenMargin = 16;
        private const int MinimumCaptureSelectionSize = 24;
        private const int TestAutoFishingScanIntervalMs = 45;
        private const int TestAutoFishingPreviewIntervalMs = 220;
        private const int TestAutoFishingSecondClickDelayMs = 95;
        private const int TestAutoFishingCastRegistrationDelayMs = 2000;
        private const int TestAutoFishingNoBobberRecastMs = 5000;
        private const int TestAutoFishingBiteArmingDelayMs = 420;
        private const int TestAutoFishingDropThresholdPx = 3;
        private const int TestAutoFishingRequiredBiteFrames = 2;
        private const int TestAutoFishingMinRedPixels = 3;
        private const int TestAutoFishingMissTolerance = 6;
        private const int TestAutoFishingLossTriggerMisses = 3;
        private const int TestAutoFishingRepairDelayAfterOpenChatMs = 180;
        private const int TestAutoFishingRepairDelayAfterTypeCommandMs = 110;
        private const int TestAutoFishingRepairRecastDelayMs = 190;
        private const int AutoReconnectTeleportSafetyBufferSeconds = 2;
        private const int TestAutoFishingRepairIntervalMaxSeconds = 3600;
        // BlazingPack can block Back for about 5 s and the next connection for
        // about 6 s. One extra second avoids clicking on the boundary.
        private const int AutoReconnectBlockedButtonWaitSeconds = 10;
        private const int AutoReconnectServerListRefreshSeconds = 3;
        private const int AutoReconnectSettleAfterJoinSeconds = 3;
        private const int AutoReconnectHomeMenuMaxWaitSeconds = 6;
        private const int AutoReconnectHomeCommandMaxRetries = 1;

        private enum BindTarget
        {
            None,
            Kopacz533,
            Kopacz633,
            TestAutoFishing,
            TestAutoFishingCaptureArea,
            ChatOpen,
            DropItem
        }

        private enum Kopacz533CommandStage
        {
            None,
            OpenChat,
            TypeCommand,
            SubmitCommand
        }

        private enum Kopacz633CommandStage
        {
            None,
            OpenChat,
            TypeCommand,
            SubmitCommand
        }

        private enum TestAutoFishingRepairStage
        {
            None,
            OpenChat,
            TypeCommand,
            SubmitCommand
        }

        private enum AutoReconnectStage
        {
            None,
            EmergencyWaitBeforeReconnect,
            EmergencyOpenInventory,
            EmergencyVerifyInventory,
            HealthOpenInventory,
            HealthVerifyInventory,
            AnalyzeScreen,
            WaitForScreenAnalysis,
            WaitForDisconnectButtonUnlock,
            WaitAfterScreenClick,
            OpenDirectConnect,
            WaitForDirectConnect,
            EnterServerAddress,
            WaitForServerJoin,
            OpenHomeChat,
            TypeHomeCommand,
            SubmitHomeCommand,
            WaitForHomeMenu,
            ClickHomeSlot,
            WaitForTeleport,
            OpenVerificationInventory,
            VerifyAfterTeleport,
            RetryDelay
        }

        private enum AutoReconnectScreenKind
        {
            Unknown,
            Inventory,
            PlayerDead,
            Disconnected,
            ReconnectChoice,
            ServerList,
            DirectConnect,
            Banned,
            AlreadyConnected,
            MainMenu
        }

        private enum Kopacz633StrafeDirection
        {
            None,
            Forward,
            Right,
            Backward,
            Left
        }

        private enum InventoryCleanupStage
        {
            None,
            ReturnToMiningStart,
            OpenInventory,
            WaitForInventory,
            MoveToSlot,
            PressDropModifier,
            PressDropKey,
            ReleaseDropKeys,
            CloseInventory,
            OpenCobbleXChat,
            TypeCobbleXCommand,
            SubmitCobbleXCommand,
            DepositGold,
            SelectFoodSlot,
            StartEating,
            StopEatingAndRestoreTool,
            ResumeMining
        }

        private enum InventoryCleanupOwner
        {
            None,
            Kopacz533,
            Kopacz633
        }

        private sealed class ProcessTargetOption
        {
            public int ProcessId { get; init; }
            public string ProcessName { get; init; } = string.Empty;
            public string WindowTitle { get; init; } = string.Empty;

            public override string ToString()
            {
                return $"{ProcessName} [{ProcessId}] - {WindowTitle}";
            }
        }

        private BindTarget _bindCaptureTarget = BindTarget.None;
        private readonly Dictionary<BindTarget, string> _pendingBindValues = new Dictionary<BindTarget, string>();
        private static readonly Brush BindIdleBorderBrush = new SolidColorBrush(GlassPalette.MapLegacyColor(75, 98, 131));
        private static readonly Brush BindCaptureBorderBrush = new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36));
        private static readonly Brush TileBindBrush = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));
        private static readonly Brush TileOffBrush = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));
        private static readonly object CursorAppearanceCacheLock = new object();
        private static readonly Dictionary<IntPtr, bool> CursorBlankAppearanceCache = new Dictionary<IntPtr, bool>();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DrawIconEx(
            IntPtr hdc,
            int xLeft,
            int yTop,
            IntPtr hIcon,
            int cxWidth,
            int cyWidth,
            uint istepIfAniCur,
            IntPtr hbrFlickerFreeDraw,
            uint diFlags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;
        private const int VK_XBUTTON1 = 0x05;
        private const int VK_XBUTTON2 = 0x06;
        private const int CURSOR_SHOWING = 0x00000001;
        private const int SM_CXCURSOR = 13;
        private const int SM_CYCURSOR = 14;
        private const uint DI_NORMAL = 0x0003;
        private const int VK_1 = 0x31;
        private const int VK_2 = 0x32;
        private const int VK_A = 0x41;
        private const int VK_D = 0x44;
        private const int VK_E = 0x45;
        private const int VK_Q = 0x51;
        private const int VK_W = 0x57;
        private const int VK_S = 0x53;
        private const int VK_T = 0x54;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_MENU = 0x12;
        private const int VK_RETURN = 0x0D;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_F5 = 0x74;
        private const int Kopacz533DelayAfterOpenChatMs = 180;
        private const int Kopacz533DelayAfterTypeCommandMs = 110;
        private const int Kopacz533DelayAfterSubmitResumeMs = 130;
        private const int Kopacz633DelayAfterOpenChatMs = 180;
        private const int Kopacz633DelayAfterTypeCommandMs = 110;
        private const int Kopacz633DelayAfterSubmitResumeMs = 130;
        private const int Kopacz633MsPerBlock = 250;
        private const int InventoryCleanupMinimumIntervalSeconds = 10;
        private const int InventoryCleanupMaximumIntervalSeconds = 3600;
        private const int InventoryCleanupReturnSettleMs = 120;
        private const int InventoryCleanupOpenDelayMs = 350;
        private const int InventoryCleanupDetectionRetryMs = 140;
        private const int InventoryCleanupMaximumDetectionAttempts = 4;
        private const int InventoryCleanupMaximumDropPasses = 3;
        private const int InventoryCleanupTooltipClearDelayMs = 240;
        private const int InventoryCleanupCursorSettleMs = 55;
        private const int InventoryCleanupModifierSettleMs = 100;
        private const int InventoryCleanupDropKeyHoldMs = 100;
        private const int InventoryCleanupBetweenDropsMs = 75;
        private const int InventoryCleanupCloseDelayMs = 120;
        private const int InventoryCleanupResumeDelayMs = 180;
        private const int CobbleXDelayAfterCloseInventoryMs = 180;
        private const int CobbleXDelayAfterOpenChatMs = 180;
        private const int CobbleXDelayAfterTypeCommandMs = 110;
        private const int CobbleXDelayAfterSubmitResumeMs = 130;
        private const int CobbleXDelayBetweenCommandsMs = 500;
        private const int InventoryCleanupFoodSlotSettleMs = 140;
        private const int InventoryCleanupEatingHoldMs = 4000;
        private const int InventoryCleanupToolSlotSettleMs = 180;
        private const int CobbleXMinimumRequiredStacks = 1;
        private const int CobbleXMaximumRequiredStacks = 27;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        public MainWindow()
            : this(null)
        {
        }

        internal MainWindow(string? accountProfileId, bool workspaceWorker = false)
        {
            _workspaceWorker = workspaceWorker;
            _accountProfileId = MinerProfileId.Normalize(accountProfileId);
            _accountDisplayName = new MinerAccountStore()
                .Load()
                .FirstOrDefault(profile => string.Equals(profile.Id, _accountProfileId, StringComparison.OrdinalIgnoreCase))
                ?.Name
                ?? (_accountProfileId == MinerProfileId.Default ? "Konto główne" : _accountProfileId);
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            PreviewMouseDown += MainWindow_PreviewMouseDown;
            Closing += MainWindow_Closing;
            if (!_workspaceWorker)
                InitializeTrayIcon();

            _settingsService = new SettingsService(_accountProfileId);
            InitializeAppLog();
            _miningLogService = new MiningLogService(_accountProfileId);
            _remoteControlServer = new MinerRemoteControlServer(this);
            _remoteControlServer.Start();
            Closed += (_, __) =>
            {
                _remoteControlServer.Dispose();
                _gameWindowClaim.Dispose();
            };
            _isFirstRun = !_settingsService.SettingsFileExists;
            _settings = _settingsService.Load();
            EnsureSettingsConsistency();
            _currentAppVersion = ReleaseNotesCatalog.CurrentVersion;
            Title = _accountProfileId == MinerProfileId.Default
                ? $"Minecraft Helper Kopacz {_currentAppVersion}"
                : $"Minecraft Helper Kopacz {_currentAppVersion} — {_accountDisplayName}";
            _showStartupNotice = !_workspaceWorker && (_isFirstRun
                || !string.Equals(
                    _settings.LastAcknowledgedVersion,
                    _currentAppVersion,
                    StringComparison.OrdinalIgnoreCase));
            _damageSoundDetector = new DamageSoundDetector();
            _damageSoundDetector.ProgressChanged += DamageSoundDetector_ProgressChanged;
            _damageSoundDetector.DamageDetected += DamageSoundDetector_DamageDetected;
            _damageSoundDetector.CaptureFailed += DamageSoundDetector_CaptureFailed;

            _dirtyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _dirtyTimer.Tick += (s, e) =>
            {
                _dirtyTimer.Stop();
                if (_pendingChanges)
                    AutoSaveSettings();
            };

            _focusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _focusTimer.Tick += (_, __) =>
            {
                _isMinecraftFocused = CheckGameFocus();
                RefreshTestAutoFishingPreview(DateTime.UtcNow);
            };

            _macroTimer = new DispatcherTimer
            {
                Interval = MacroTimerIdleInterval
            };
            _macroTimer.Tick += RunMacroTick;

            _autoReconnectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _autoReconnectTimer.Tick += RunAutoReconnectTick;

            _transientStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _transientStatusTimer.Tick += (_, __) =>
            {
                _transientStatusTimer.Stop();
                UpdateStatusBar("Gotowy", "Green");
            };

            DataContext = this;

            _isLoadingUi = true;
            try
            {
                InitializeInventoryCleanupSlotGrid();
                InitializeInventoryCleanupItemTypeGrid();
                InitializeAutoReconnectHomeSlotGrid();
                LoadToUi();
            }
            finally
            {
                _isLoadingUi = false;
            }
            UpdateEnabledStates();
            RefreshOverlayHud(DateTime.UtcNow);
            RefreshMiningLogsSummary();
            MainTabControl.SelectedItem = TabKopacz;

            _focusTimer.Start();
            _macroTimer.Start();
            _autoReconnectTimer.Start();
            _isMinecraftFocused = CheckGameFocus();
            InitializeGlassUi();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            GlassChrome.ApplyTitleBar(this);
            if (_workspaceWorker)
                return;

            // If Windows launches the app minimized (e.g. shortcut setting),
            // do not auto-hide it to tray on startup.
            if (WindowState == WindowState.Minimized && !_isMinimizedToTray)
            {
                WindowState = WindowState.Normal;
                Activate();
            }

            if (_showStartupNotice)
            {
                await Dispatcher.InvokeAsync(
                    ShowStartupNotice,
                    DispatcherPriority.ApplicationIdle);
            }

            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            AppUpdateInfo? update = await UpdateCheckService.CheckForUpdateAsync(_currentAppVersion);
            if (update == null || !IsVisible || _isExitRequested)
                return;

            var dialog = new UpdateAvailableWindow(update)
            {
                Owner = this
            };
            dialog.ShowDialog();

            if (IsVisible && !_isExitRequested)
                Activate();
        }

        private void ShowStartupNotice()
        {
            if (!IsVisible || _isExitRequested)
                return;

            var dialog = new FirstRunGuideWindow(
                _isFirstRun,
                _currentAppVersion,
                _settings.LastAcknowledgedVersion)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _settings.LastAcknowledgedVersion = _currentAppVersion;
                    _settingsService.Save(_settings);
                }
                catch (Exception ex)
                {
                    UpdateStatusBar("Nie udało się zapisać potwierdzenia wersji: " + ex.Message, "Red");
                }
            }

            Activate();
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Minecraft Helper Kopacz",
                Visible = true,
                Icon = TryLoadTrayIcon() ?? System.Drawing.SystemIcons.Application
            };

            // Menu w stylu glass (WPF) zamiast ciemnego ContextMenuStrip — tak jak w Auto Clickerze.
            _trayMenu = GlassTrayMenu.Create(
                ("Otwórz Kopacza", GlassTrayMenu.OpenIcon, RestoreFromTray),
                null,
                ("Zamknij Kopacza", GlassTrayMenu.ExitIcon, ExitFromTray));
            _trayIcon.MouseUp += (_, args) =>
            {
                if (args.Button == Forms.MouseButtons.Right)
                    Dispatcher.BeginInvoke(() => GlassTrayMenu.Show(_trayMenu));
            };
            _trayIcon.DoubleClick += (_, __) => RestoreFromTray();
        }

        private static System.Drawing.Icon? TryLoadTrayIcon()
        {
            try
            {
                // Zasobnik rysuje małą ikonę (16 px, przy skalowaniu 24 px) — bierzemy
                // właściwą klatkę z pliku zamiast zmniejszać domyślną 32×32.
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "miner.ico");
                if (File.Exists(iconPath))
                    return new System.Drawing.Icon(iconPath, Forms.SystemInformation.SmallIconSize);
            }
            catch
            {
                // Ignore and use next fallback.
            }

            try
            {
                string? executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executablePath))
                    return System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            }
            catch
            {
                // Ignore and use default application icon.
            }

            return null;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_isExitRequested)
                return;

            e.Cancel = true;
            MinimizeToTray(showNotification: true);
        }

        private void MinimizeToTray(bool showNotification)
        {
            if (_isMinimizedToTray)
                return;

            Hide();
            ShowInTaskbar = false;
            _isMinimizedToTray = true;

            // Własne powiadomienie glass przy każdym zamknięciu krzyżykiem — przypomina,
            // że kopanie dalej trwa. Ukrycie przez panel kont (przełączanie) go nie pokazuje.
            if (showNotification && _trayIcon != null)
            {
                new TrayToastWindow(
                    "Kopacz działa w tle",
                    "Kopanie i bindy pozostają aktywne. Kliknij tutaj albo dwukrotnie ikonę w zasobniku, aby otworzyć program.",
                    "Assets/miner.ico",
                    RestoreFromTray).Show();
            }
        }

        private async void RestoreFromTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(RestoreFromTray));
                return;
            }

            try
            {
                MinerWorkspaceResponse result = await MinerWorkspaceClient.SelectAsync(null);
                if (!result.Success)
                    UpdateStatusBar(result.Message, "Red");
            }
            catch (Exception ex)
            {
                UpdateStatusBar("Nie udało się otworzyć panelu: " + ex.GetBaseException().Message, "Red");
            }
        }

        private async void ExitFromTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(ExitFromTray));
                return;
            }

            try
            {
                MinerWorkspaceResponse result = await MinerWorkspaceClient.ExitAsync();
                if (!result.Success)
                    UpdateStatusBar(result.Message, "Red");
            }
            catch (Exception ex)
            {
                UpdateStatusBar("Nie udało się zamknąć kont: " + ex.GetBaseException().Message, "Red");
            }
        }

        private static OverlayCorner ParseOverlayCorner(string? raw)
        {
            return raw?.Trim().ToLowerInvariant() switch
            {
                "leftbottom" => OverlayCorner.BottomLeft,
                "righttop" => OverlayCorner.TopRight,
                "lefttop" => OverlayCorner.TopLeft,
                _ => OverlayCorner.BottomRight
            };
        }

        private static string ToOverlayCornerSetting(OverlayCorner corner)
        {
            return corner switch
            {
                OverlayCorner.BottomLeft => "LeftBottom",
                OverlayCorner.TopRight => "RightTop",
                OverlayCorner.TopLeft => "LeftTop",
                _ => "RightBottom"
            };
        }

        private OverlayCorner GetSelectedOverlayCorner()
        {
            int selectedIndex = CbOverlayCorner?.SelectedIndex ?? -1;
            return selectedIndex switch
            {
                1 => OverlayCorner.BottomLeft,
                2 => OverlayCorner.TopRight,
                3 => OverlayCorner.TopLeft,
                0 => OverlayCorner.BottomRight,
                _ => ParseOverlayCorner(_settings.OverlayCorner)
            };
        }

        private int GetSelectedOverlayMonitorIndex()
        {
            int fallback = Math.Max(0, _settings.OverlayMonitorIndex);
            if (CbOverlayMonitor == null)
                return fallback;
            if (CbOverlayMonitor.SelectedIndex >= 0)
                return CbOverlayMonitor.SelectedIndex;
            return fallback;
        }

        private Drawing.Rectangle GetSelectedOverlayWorkArea()
        {
            Forms.Screen[] screens = Forms.Screen.AllScreens;
            if (screens.Length == 0)
                return Forms.SystemInformation.WorkingArea;

            int index = Math.Clamp(GetSelectedOverlayMonitorIndex(), 0, screens.Length - 1);
            return screens[index].WorkingArea;
        }

        private void RefreshOverlayMonitorChoices()
        {
            if (CbOverlayMonitor == null)
                return;

            int selectedIndex = Math.Max(0, _settings.OverlayMonitorIndex);
            if (!_isLoadingUi && CbOverlayMonitor.SelectedIndex >= 0)
                selectedIndex = CbOverlayMonitor.SelectedIndex;

            CbOverlayMonitor.Items.Clear();
            Forms.Screen[] screens = Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                CbOverlayMonitor.Items.Add("Monitor 1");
                CbOverlayMonitor.SelectedIndex = 0;
                return;
            }

            for (int i = 0; i < screens.Length; i++)
            {
                Forms.Screen screen = screens[i];
                var bounds = screen.WorkingArea;
                CbOverlayMonitor.Items.Add($"Monitor {i + 1} ({bounds.Width}x{bounds.Height})");
            }

            CbOverlayMonitor.SelectedIndex = Math.Clamp(selectedIndex, 0, screens.Length - 1);
        }

        private void UpdateOverlayLayout()
        {
            OverlayCorner corner = GetSelectedOverlayCorner();
            bool animationsEnabled = ChkOverlayAnimationsEnabled?.IsChecked ?? _settings.OverlayAnimationsEnabled;

            if (_overlayHud != null)
            {
                _overlayHud.SetAnimationsEnabled(animationsEnabled);
                _overlayHud.SetMinerLayout(GetSelectedOverlayWorkArea(), corner, (int)OverlayScreenMargin);
            }
        }

        private void EnsureSettingsConsistency()
        {
            _settings ??= new AppSettings();
            _settings.LastAcknowledgedVersion ??= string.Empty;

            _settings.Kopacz533Commands ??= new List<MinerCommand>();
            _settings.Kopacz633Commands ??= new List<MinerCommand>();
            _settings.InventoryCleanupSlots ??= Enumerable.Range(0, 27).ToList();
            _settings.InventoryCleanupSlots = _settings.InventoryCleanupSlots
                .Where(slot => slot >= 0 && slot < 27)
                .Distinct()
                .OrderBy(slot => slot)
                .ToList();
            HashSet<string> supportedCleanupItemTypes = InventoryCleanupItemTypes
                .Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _settings.InventoryCleanupItemTypes ??= InventoryCleanupItemTypes.Select(item => item.Id).ToList();
            _settings.InventoryCleanupItemTypes = _settings.InventoryCleanupItemTypes
                .Where(itemId => supportedCleanupItemTypes.Contains(itemId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_settings.InventoryCleanupIntervalSeconds <= 0)
                _settings.InventoryCleanupIntervalSeconds = 120;
            _settings.InventoryCleanupIntervalSeconds = Math.Clamp(
                _settings.InventoryCleanupIntervalSeconds,
                InventoryCleanupMinimumIntervalSeconds,
                InventoryCleanupMaximumIntervalSeconds);
            _settings.CobbleXCommand ??= "/cx";
            if (_settings.CobbleXRequiredFullStacks <= 0)
                _settings.CobbleXRequiredFullStacks = 9;
            _settings.CobbleXRequiredFullStacks = Math.Clamp(
                _settings.CobbleXRequiredFullStacks,
                CobbleXMinimumRequiredStacks,
                CobbleXMaximumRequiredStacks);
            _settings.TestAutoFishingBind ??= string.Empty;
            _settings.TestAutoFishingCaptureBind ??= string.Empty;
            _settings.TestAutoFishingRepairCommand ??= string.Empty;
            _settings.EmergencyDamageSoundDeviceId ??= string.Empty;
            _settings.EmergencyDamageSoundSimilarityPercent = Math.Clamp(_settings.EmergencyDamageSoundSimilarityPercent, 70, 99);
            _settings.EmergencyDamageSoundMinimumDb = Math.Clamp(_settings.EmergencyDamageSoundMinimumDb, -70.0, -10.0);
            _settings.EmergencyDamageSoundTemplates ??= new List<List<double>>();
            _settings.EmergencyDamageSoundTemplates = _settings.EmergencyDamageSoundTemplates
                .Where(template => template != null
                    && template.Count == DamageSoundDetector.FingerprintBandCount
                    && template.All(double.IsFinite))
                .Take(24)
                .Select(template => template.ToList())
                .ToList();
            // Protection and reconnect are separate preferences. In particular,
            // normalization must preserve disconnect-only mode and master OFF.
            _settings.EmergencyReconnectDelaySeconds = Math.Clamp(
                _settings.EmergencyReconnectDelaySeconds <= 0 ? 30 : _settings.EmergencyReconnectDelaySeconds,
                1,
                600);
            _settings.AutoReconnectProfile ??= "Arivi";
            _settings.AutoReconnectServerAddress ??= string.Empty;
            _settings.AutoReconnectHomeCommand ??= "/home";
            if (!string.Equals(_settings.AutoReconnectProfile, "Standard", StringComparison.OrdinalIgnoreCase))
                _settings.AutoReconnectProfile = "Arivi";
            _settings.AutoReconnectHomeSlot = Math.Clamp(_settings.AutoReconnectHomeSlot, 1, 27);
            _settings.AutoReconnectJoinDelaySeconds = Math.Clamp(_settings.AutoReconnectJoinDelaySeconds, 2, 120);
            _settings.AutoReconnectTeleportDelaySeconds = Math.Clamp(_settings.AutoReconnectTeleportDelaySeconds, 1, 120);
            _settings.AutoReconnectWatchdogSeconds = Math.Clamp(_settings.AutoReconnectWatchdogSeconds, 15, 3600);
            _settings.AutoReconnectMaxAttempts = Math.Clamp(_settings.AutoReconnectMaxAttempts, 1, 10);
            EnsureAutoReconnectServerProfiles();
            _settings.TargetProcessName ??= string.Empty;
            _settings.ChatOpenKey = NormalizeMinecraftControlKey(_settings.ChatOpenKey, "T");
            _settings.DropItemKey = NormalizeMinecraftControlKey(_settings.DropItemKey, "Q");
            _settings.OverlayCorner ??= "RightBottom";
            if (_settings.OverlayMonitorIndex < 0)
                _settings.OverlayMonitorIndex = 0;
            if (_settings.TargetProcessId < 0)
                _settings.TargetProcessId = 0;
            if (_settings.TestAutoFishingCaptureX < 0)
                _settings.TestAutoFishingCaptureX = 0;
            if (_settings.TestAutoFishingCaptureY < 0)
                _settings.TestAutoFishingCaptureY = 0;
            if (_settings.TestAutoFishingCaptureWidth < 0)
                _settings.TestAutoFishingCaptureWidth = 0;
            if (_settings.TestAutoFishingCaptureHeight < 0)
                _settings.TestAutoFishingCaptureHeight = 0;
            _settings.TestAutoFishingRepairEverySeconds = Math.Clamp(_settings.TestAutoFishingRepairEverySeconds, 0, TestAutoFishingRepairIntervalMaxSeconds);
        }

        private static List<ProcessTargetOption> GetRunningWindowProcesses()
        {
            var results = new List<ProcessTargetOption>();
            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process process = all[i];
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    string title = (process.MainWindowTitle ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    results.Add(new ProcessTargetOption
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName ?? string.Empty,
                        WindowTitle = title
                    });
                }
                catch
                {
                    // Ignore processes that cannot be inspected.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return results
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.WindowTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private ProcessTargetOption? GetSelectedTargetProcessOption()
        {
            if (CbTargetProcessList?.SelectedItem is ProcessTargetOption option)
                return option;
            return null;
        }

        private string BuildTargetProcessDisplayText()
        {
            string processName = (_settings.TargetProcessName ?? string.Empty).Trim();
            int processId = _settings.TargetProcessId;
            string windowTitle = (_settings.TargetWindowTitle ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(processName))
            {
                string processPart = processId > 0 ? $"{processName} [{processId}]" : processName;
                if (!string.IsNullOrWhiteSpace(windowTitle))
                    return $"{processPart} - {windowTitle}";
                return processPart;
            }

            return string.IsNullOrWhiteSpace(windowTitle) ? "Brak" : windowTitle;
        }

        private static bool LooksLikeLauncherWindow(ProcessTargetOption option)
        {
            return option.ProcessName.Contains("launcher", StringComparison.OrdinalIgnoreCase)
                || option.WindowTitle.Contains("launcher", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshTargetProcessChoices()
        {
            if (CbTargetProcessList == null)
                return;

            ProcessTargetOption? currentSelection = GetSelectedTargetProcessOption();
            List<ProcessTargetOption> options = GetRunningWindowProcesses();

            CbTargetProcessList.Items.Clear();
            bool previousLoading = _isLoadingUi;
            _isLoadingUi = true;
            try
            {
                for (int i = 0; i < options.Count; i++)
                    CbTargetProcessList.Items.Add(options[i]);

                if (options.Count == 0)
                {
                    CbTargetProcessList.IsEnabled = false;
                    CbTargetProcessList.SelectedIndex = -1;
                    return;
                }

                CbTargetProcessList.IsEnabled = true;

                int selectedIndex = -1;
                if (currentSelection != null)
                    selectedIndex = options.FindIndex(o => o.ProcessId == currentSelection.ProcessId);

                if (selectedIndex < 0 && _settings.TargetProcessId > 0)
                    selectedIndex = options.FindIndex(o => o.ProcessId == _settings.TargetProcessId);

                if (selectedIndex < 0 && !string.IsNullOrWhiteSpace(_settings.TargetProcessName))
                {
                    selectedIndex = options.FindIndex(o =>
                        string.Equals(o.ProcessName, _settings.TargetProcessName, StringComparison.OrdinalIgnoreCase));
                }

                if (selectedIndex < 0 && !string.IsNullOrWhiteSpace(_settings.TargetWindowTitle))
                {
                    string configuredTitle = _settings.TargetWindowTitle.Trim();
                    selectedIndex = options.FindIndex(o =>
                        o.WindowTitle.Contains(configuredTitle, StringComparison.OrdinalIgnoreCase)
                        || configuredTitle.Contains(o.WindowTitle, StringComparison.OrdinalIgnoreCase));
                }

                CbTargetProcessList.SelectedIndex = selectedIndex;
            }
            finally
            {
                _isLoadingUi = previousLoading;
            }
        }

        private bool TryResolveTargetWindow(bool allowPendingSelection, out IntPtr windowHandle)
        {
            windowHandle = IntPtr.Zero;

            int targetProcessId = _settings.TargetProcessId;
            string targetProcessName = (_settings.TargetProcessName ?? string.Empty).Trim();
            string targetWindowTitle = (_settings.TargetWindowTitle ?? string.Empty).Trim();

            if (allowPendingSelection && GetSelectedTargetProcessOption() is ProcessTargetOption selectedProcess)
            {
                targetProcessId = selectedProcess.ProcessId;
                targetProcessName = selectedProcess.ProcessName;
                targetWindowTitle = selectedProcess.WindowTitle;
            }

            if (targetProcessId <= 0
                && string.IsNullOrWhiteSpace(targetProcessName)
                && string.IsNullOrWhiteSpace(targetWindowTitle))
            {
                return false;
            }

            if (targetProcessId > 0)
            {
                try
                {
                    using Process configuredProcess = Process.GetProcessById(targetProcessId);
                    bool processNameMatches = string.IsNullOrWhiteSpace(targetProcessName)
                        || string.Equals(configuredProcess.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase);
                    if (processNameMatches && configuredProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        windowHandle = configuredProcess.MainWindowHandle;
                        return true;
                    }
                }
                catch
                {
                    // The saved PID may expire after the game is restarted; resolve by name/title below.
                }
            }

            List<ProcessTargetOption> options = GetRunningWindowProcesses()
                .Where(option => !_gameWindowClaim.IsClaimedByAnotherProfile(option.ProcessId))
                .ToList();
            ProcessTargetOption? match = null;

            if (match == null && !string.IsNullOrWhiteSpace(targetProcessName) && !string.IsNullOrWhiteSpace(targetWindowTitle))
            {
                match = options.FirstOrDefault(option =>
                    string.Equals(option.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase)
                    && (option.WindowTitle.Contains(targetWindowTitle, StringComparison.OrdinalIgnoreCase)
                        || targetWindowTitle.Contains(option.WindowTitle, StringComparison.OrdinalIgnoreCase)));
            }

            if (match == null && !string.IsNullOrWhiteSpace(targetProcessName))
            {
                match = options.FirstOrDefault(option =>
                    string.Equals(option.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null && !string.IsNullOrWhiteSpace(targetWindowTitle))
            {
                match = options.FirstOrDefault(option =>
                    option.WindowTitle.Contains(targetWindowTitle, StringComparison.OrdinalIgnoreCase)
                    || targetWindowTitle.Contains(option.WindowTitle, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
                return false;

            try
            {
                using Process process = Process.GetProcessById(match.ProcessId);
                windowHandle = process.MainWindowHandle;
                return windowHandle != IntPtr.Zero;
            }
            catch
            {
                // Proces gry mógł się zakończyć między wyliczeniem a odczytem — traktujemy jak brak okna.
                windowHandle = IntPtr.Zero;
                return false;
            }
        }

        // FOCUS MINECRAFT
        private bool CheckGameFocus()
        {
            string targetProcessName = (_settings.TargetProcessName ?? string.Empty).Trim();
            int targetProcessId = _settings.TargetProcessId;
            string targetWindowTitle = (_settings.TargetWindowTitle ?? string.Empty).Trim();
            bool focused = false;
            IntPtr focusedWindow = IntPtr.Zero;

            bool hasTargetConfigured =
                targetProcessId > 0
                || !string.IsNullOrWhiteSpace(targetProcessName)
                || !string.IsNullOrWhiteSpace(targetWindowTitle);

            if (hasTargetConfigured)
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow != IntPtr.Zero)
                {
                    StringBuilder windowTitle = new StringBuilder(256);
                    _ = GetWindowText(foregroundWindow, windowTitle, windowTitle.Capacity);
                    string currentWindowTitle = windowTitle.ToString();
                    string currentProcessName = string.Empty;

                    _ = GetWindowThreadProcessId(foregroundWindow, out uint processIdRaw);
                    int processId = processIdRaw > int.MaxValue ? 0 : (int)processIdRaw;
                    if (processId > 0)
                    {
                        try
                        {
                            using Process process = Process.GetProcessById(processId);
                            currentProcessName = process.ProcessName ?? string.Empty;
                        }
                        catch
                        {
                            // Proces zakończył się w międzyczasie — nazwy nie da się już odczytać.
                            currentProcessName = string.Empty;
                        }
                    }

                    if (targetProcessId > 0 && processId == targetProcessId)
                    {
                        if (string.IsNullOrWhiteSpace(targetProcessName)
                            || string.Equals(currentProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                        {
                            focused = true;
                        }
                    }

                    // Several Minecraft clients share the process name (javaw). While the
                    // saved process is alive only its PID matches; name/title fallbacks are
                    // for a restarted game and skip windows owned by other profiles.
                    bool fallbackAllowed = !focused
                        && !_gameWindowClaim.IsClaimedByAnotherProfile(processId)
                        && !(targetProcessId > 0 && MinerGameWindowClaim.IsProcessRunning(targetProcessId));

                    if (fallbackAllowed && !string.IsNullOrWhiteSpace(targetProcessName))
                    {
                        focused = string.Equals(currentProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase);
                    }

                    if (fallbackAllowed && !focused && !string.IsNullOrWhiteSpace(targetWindowTitle) && !string.IsNullOrWhiteSpace(currentWindowTitle))
                    {
                        focused = currentWindowTitle.Contains(targetWindowTitle, StringComparison.OrdinalIgnoreCase);
                    }

                    if (focused)
                    {
                        focusedWindow = foregroundWindow;
                    }
                }
            }

            DateTime now = DateTime.UtcNow;
            if (focusedWindow != IntPtr.Zero)
            {
                _targetGameWindowHandle = focusedWindow;
                _nextTargetWindowResolveAtUtc = DateTime.MinValue;
            }
            else if (!hasTargetConfigured)
            {
                _targetGameWindowHandle = IntPtr.Zero;
                _nextTargetWindowResolveAtUtc = DateTime.MinValue;
            }
            else if (_targetGameWindowHandle == IntPtr.Zero || !IsWindow(_targetGameWindowHandle))
            {
                // Avoid enumerating all desktop processes four times per second
                // while Minecraft is merely in the background. A valid HWND can
                // be reused; a missing one is resolved again after a short delay.
                _targetGameWindowHandle = IntPtr.Zero;
                if (now >= _nextTargetWindowResolveAtUtc)
                {
                    _nextTargetWindowResolveAtUtc = now.Add(TargetWindowResolveRetryInterval);
                    if (TryResolveTargetWindow(allowPendingSelection: false, out IntPtr resolvedWindow))
                    {
                        _targetGameWindowHandle = resolvedWindow;
                        _nextTargetWindowResolveAtUtc = DateTime.MinValue;
                    }
                }
            }

            if (!ClaimTargetGameWindow())
                focused = false;

            bool inBackground = !focused && BackgroundGameWindow.IsActive;
            string focusText = focused ? "✓ Tak" : inBackground ? "◐ W tle" : "✗ Nie";
            if (!string.Equals(TxtMinecraftFocus.Text, focusText, StringComparison.Ordinal))
            {
                Brush focusBrush = focused || inBackground ? TileBindBrush : TileOffBrush;
                TxtMinecraftFocus.Text = focusText;
                TxtMinecraftFocus.Foreground = focusBrush;
                EllMinecraftFocus.Fill = focusBrush;
            }

            return focused;
        }

        private void LoadToUi()
        {
            UpdateStatusBar("Gotowy", "Green");

            _kopacz533RuntimeEnabled = false;
            _kopacz633RuntimeEnabled = false;
            _testAutoFishingRuntimeEnabled = false;
            ResetKopacz533RuntimeState();
            ResetKopacz633RuntimeState();
            ResetTestAutoFishingRuntimeState();
            SetKopacz533MiningHold(false);
            SetKopacz633AttackHold(false);
            SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);

            // KOPACZ
            ChkKopacz533Enabled.IsChecked = _settings.Kopacz533Enabled;
            TxtKopacz533Key.Text = _settings.Kopacz533Key;
            RefreshKopaczCommandsUI(533);

            ChkKopacz633Enabled.IsChecked = _settings.Kopacz633Enabled;
            TxtKopacz633Key.Text = _settings.Kopacz633Key;
            TxtKopacz633Width.Text = _settings.Kopacz633Width.ToString();
            TxtKopacz633WidthUp.Text = _settings.Kopacz633Width.ToString();
            TxtKopacz633LengthUp.Text = _settings.Kopacz633Length.ToString();
            RefreshKopaczCommandsUI(633);

            if (_settings.Kopacz633Direction == "Na wprost")
                CbKopacz633Direction.SelectedIndex = 1;
            else if (_settings.Kopacz633Direction == "Do góry")
                CbKopacz633Direction.SelectedIndex = 2;
            else
                CbKopacz633Direction.SelectedIndex = 0; // także usunięty tryb prostokąta — kierunek trzeba wybrać od nowa
            UpdateKopaczUpwardInfoVisibility();

            ChkInventoryCleanupEnabled.IsChecked = _settings.InventoryCleanupEnabled;
            TxtInventoryCleanupIntervalSeconds.Text = _settings.InventoryCleanupIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            ChkInventoryCleanupAllItemTypes.IsChecked = _settings.InventoryCleanupDiscardAllItemTypes;
            ChkInventoryCleanupEatAfterCleanup.IsChecked = _settings.InventoryCleanupEatAfterCleanup;
            ChkCobbleXEnabled.IsChecked = _settings.CobbleXEnabled;
            ChkCobbleXRepeatThreeTimes.IsChecked = _settings.CobbleXRepeatThreeTimes;
            ChkBackgroundMiningEnabled.IsChecked = _settings.BackgroundMiningEnabled;
            ChkGameShieldEnabled.IsChecked = _settings.BackgroundGameShieldEnabled;
            TxtCobbleXCommand.Text = _settings.CobbleXCommand;
            TxtCobbleXRequiredStacks.Text = _settings.CobbleXRequiredFullStacks.ToString(CultureInfo.InvariantCulture);
            LoadGoldDepositSettings();
            LoadInventoryCleanupSlotsToUi();
            LoadInventoryCleanupItemTypesToUi();
            UpdateInventoryCleanupStatus("Gotowe. Włącz texturepack Minecraft Helper w grze.", "Default");

            TxtTargetWindowTitle.Text = _settings.TargetWindowTitle;
            RefreshTargetProcessChoices();
            TxtCurrentWindowTitle.Text = BuildTargetProcessDisplayText();

            ChkTestAutoFishingEnabled.IsChecked = _settings.TestAutoFishingEnabled;
            TxtTestAutoFishingBind.Text = _settings.TestAutoFishingBind;
            TxtTestAutoFishingCaptureBind.Text = _settings.TestAutoFishingCaptureBind;
            TxtTestAutoFishingRepairCommand.Text = _settings.TestAutoFishingRepairCommand;
            TxtTestAutoFishingRepairEverySeconds.Text = _settings.TestAutoFishingRepairEverySeconds.ToString(CultureInfo.InvariantCulture);
            ChkEmergencyDamageSoundEnabled.IsChecked = _settings.EmergencyDamageSoundEnabled;
            RbProtectionReturn.IsChecked = _settings.EmergencyReconnectEnabled;
            RbProtectionDisconnectOnly.IsChecked = !_settings.EmergencyReconnectEnabled;
            ChkEmergencyDamageSoundTestMode.IsChecked = _settings.EmergencyDamageSoundTestMode;
            SlEmergencyDamageSoundSimilarity.Value = _settings.EmergencyDamageSoundSimilarityPercent;
            TxtEmergencyDamageSoundSimilarityValue.Text = $"{_settings.EmergencyDamageSoundSimilarityPercent}%";
            LoadBundledDamageSoundReferences(showStatus: false);
            RefreshEmergencyDamageSoundDevices();
            UpdateEmergencyDamageSoundReferenceInfo();
            TxtEmergencyReconnectDelaySeconds.Text = _settings.EmergencyReconnectDelaySeconds.ToString(CultureInfo.InvariantCulture);
            ChkAutoReconnectEnabled.IsChecked = _settings.AutoReconnectEnabled;
            CbAutoReconnectProfile.SelectedIndex = string.Equals(_settings.AutoReconnectProfile, "Standard", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            TxtAutoReconnectServerAddress.Text = _settings.AutoReconnectServerAddress;
            TxtAutoReconnectHomeCommand.Text = _settings.AutoReconnectHomeCommand;
            TxtAutoReconnectHomeSlot.Text = _settings.AutoReconnectHomeSlot.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectJoinDelay.Text = _settings.AutoReconnectJoinDelaySeconds.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectTeleportDelay.Text = _settings.AutoReconnectTeleportDelaySeconds.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectWatchdogSeconds.Text = _settings.AutoReconnectWatchdogSeconds.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectMaxAttempts.Text = _settings.AutoReconnectMaxAttempts.ToString(CultureInfo.InvariantCulture);
            RefreshAutoReconnectHomeSlotGrid();
            RefreshAutoReconnectServerProfileCards();
            RefreshEmergencyReconnectProfileSummary();
            UpdateEmergencyReconnectReadyStatus();
            UpdateTestAutoFishingAreaInfo();
            UpdateTestAutoFishingStatusLabel();

            // OVERLAY
            ChkAnimatedBackgroundEnabled.IsChecked = _settings.AnimatedBackgroundEnabled;
            CyberBackground.IsAnimationEnabled = _settings.AnimatedBackgroundEnabled;
            ChkOverlayHudEnabled.IsChecked = _settings.OverlayHudEnabled;
            ChkOverlayAnimationsEnabled.IsChecked = _settings.OverlayAnimationsEnabled;
            TxtChatOpenKey.Text = _settings.ChatOpenKey;
            TxtDropItemKey.Text = _settings.DropItemKey;
            RefreshOverlayMonitorChoices();
            CbOverlayCorner.SelectedIndex = ParseOverlayCorner(_settings.OverlayCorner) switch
            {
                OverlayCorner.BottomLeft => 1,
                OverlayCorner.TopRight => 2,
                OverlayCorner.TopLeft => 3,
                _ => 0
            };

            _pendingBindValues.Clear();
            RefreshBindSaveButtons();
            UpdateOverlayLayout();
        }

        private void InitializeInventoryCleanupSlotGrid()
        {
            PanelInventoryCleanupSlots.Children.Clear();
            _inventoryCleanupSlotCheckBoxes.Clear();

            for (int slot = 0; slot < 27; slot++)
            {
                var checkBox = new CheckBox
                {
                    Content = (slot + 1).ToString(CultureInfo.InvariantCulture),
                    Tag = slot,
                    Style = (Style)FindResource("SlotGridCheckBoxStyle"),
                    ToolTip = $"Slot {slot + 1} — rząd {(slot / 9) + 1}, kolumna {(slot % 9) + 1}"
                };
                checkBox.Checked += InventoryCleanupSlot_Changed;
                checkBox.Unchecked += InventoryCleanupSlot_Changed;
                _inventoryCleanupSlotCheckBoxes.Add(checkBox);
                PanelInventoryCleanupSlots.Children.Add(checkBox);
                UpdateInventoryCleanupOptionVisual(checkBox);
            }
        }

        private void InitializeInventoryCleanupItemTypeGrid()
        {
            PanelInventoryCleanupItemTypes.Children.Clear();
            _inventoryCleanupItemTypeCheckBoxes.Clear();

            foreach ((string itemId, string label) in InventoryCleanupItemTypes)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(6),
                    Background = GlassPalette.NeutralSoft,
                    Padding = new Thickness(3),
                    Margin = new Thickness(0, 0, 7, 0),
                    Child = ItemPixelIcons.Create(itemId, 16)
                });
                content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

                var checkBox = new CheckBox
                {
                    Content = content,
                    Tag = itemId,
                    Style = (Style)FindResource("GlassChip"),
                    ToolTip = label
                };
                checkBox.Checked += InventoryCleanupItemType_Changed;
                checkBox.Unchecked += InventoryCleanupItemType_Changed;
                _inventoryCleanupItemTypeCheckBoxes.Add(checkBox);
                PanelInventoryCleanupItemTypes.Children.Add(checkBox);
                UpdateInventoryCleanupOptionVisual(checkBox);
            }
        }

        private void InitializeAutoReconnectHomeSlotGrid()
        {
            if (PanelAutoReconnectHomeSlots == null)
                return;

            PanelAutoReconnectHomeSlots.Children.Clear();
            _autoReconnectHomeSlotButtons.Clear();

            for (int slot = 1; slot <= 27; slot++)
            {
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
                    ToolTip = $"Slot {slot} — rząd {((slot - 1) / 9) + 1}, kolumna {((slot - 1) % 9) + 1}"
                };
                button.Click += AutoReconnectHomeSlot_Click;
                _autoReconnectHomeSlotButtons.Add(button);
                PanelAutoReconnectHomeSlots.Children.Add(button);
            }

            RefreshAutoReconnectHomeSlotGrid();
        }

        private void AutoReconnectHomeSlot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: int slot })
                return;

            slot = Math.Clamp(slot, 1, 27);
            TxtAutoReconnectHomeSlot.Text = slot.ToString(CultureInfo.InvariantCulture);
            _settings.AutoReconnectHomeSlot = slot;
            RefreshAutoReconnectHomeSlotGrid();

            int row = ((slot - 1) / 9) + 1;
            int column = ((slot - 1) % 9) + 1;
            UpdateAutoReconnectStatus($"Wybrano slot home {slot} (rząd {row}, kolumna {column}).", "Green");

            if (!_isLoadingUi)
                MarkDirty();
        }

        private void RefreshAutoReconnectHomeSlotGrid()
        {
            if (_autoReconnectHomeSlotButtons.Count == 0)
                return;

            int selectedSlot = _settings.AutoReconnectHomeSlot;
            if (TxtAutoReconnectHomeSlot != null &&
                int.TryParse(TxtAutoReconnectHomeSlot.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSlot))
            {
                selectedSlot = parsedSlot;
            }
            selectedSlot = Math.Clamp(selectedSlot, 1, 27);

            foreach (Button button in _autoReconnectHomeSlotButtons)
            {
                bool selected = button.Tag is int slot && slot == selectedSlot;
                button.Background = new SolidColorBrush(selected
                    ? GlassPalette.MapLegacyColor(23, 50, 74)
                    : GlassPalette.MapLegacyColor(30, 42, 57));
                button.BorderBrush = new SolidColorBrush(selected
                    ? GlassPalette.MapLegacyColor(46, 168, 255)
                    : GlassPalette.MapLegacyColor(62, 83, 110));
                button.Foreground = new SolidColorBrush(selected
                    ? GlassPalette.MapLegacyColor(56, 214, 180)
                    : GlassPalette.MapLegacyColor(216, 226, 240));
                button.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

        private void LoadInventoryCleanupSlotsToUi()
        {
            var selectedSlots = new HashSet<int>(_settings.InventoryCleanupSlots ?? Enumerable.Range(0, 27));
            for (int slot = 0; slot < _inventoryCleanupSlotCheckBoxes.Count; slot++)
                _inventoryCleanupSlotCheckBoxes[slot].IsChecked = selectedSlots.Contains(slot);
        }

        private void LoadInventoryCleanupItemTypesToUi()
        {
            var selectedTypes = new HashSet<string>(
                _settings.InventoryCleanupItemTypes ?? InventoryCleanupItemTypes.Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (CheckBox checkBox in _inventoryCleanupItemTypeCheckBoxes)
            {
                string itemId = checkBox.Tag?.ToString() ?? string.Empty;
                checkBox.IsChecked = selectedTypes.Contains(itemId);
            }
        }

        private HashSet<int> GetSelectedInventoryCleanupSlots()
        {
            var slots = new HashSet<int>();
            for (int slot = 0; slot < _inventoryCleanupSlotCheckBoxes.Count; slot++)
            {
                if (_inventoryCleanupSlotCheckBoxes[slot].IsChecked == true)
                    slots.Add(slot);
            }
            return slots;
        }

        private HashSet<string> GetSelectedInventoryCleanupItemTypes()
        {
            var itemTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CheckBox checkBox in _inventoryCleanupItemTypeCheckBoxes)
            {
                if (checkBox.IsChecked == true && checkBox.Tag is string itemId)
                    itemTypes.Add(itemId);
            }
            return itemTypes;
        }

        private static string GetInventoryCleanupItemLabel(string itemId)
        {
            if (string.Equals(itemId, "other", StringComparison.OrdinalIgnoreCase))
                return "Pozostałe przedmioty";

            foreach ((string id, string label) in InventoryCleanupItemTypes)
            {
                if (string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase))
                    return label;
            }

            return itemId;
        }

        private int GetConfiguredInventoryCleanupIntervalSeconds()
        {
            int parsed = ParseNonNegativeInt(TxtInventoryCleanupIntervalSeconds.Text);
            if (parsed <= 0)
                parsed = 120;
            return Math.Clamp(parsed, InventoryCleanupMinimumIntervalSeconds, InventoryCleanupMaximumIntervalSeconds);
        }

        private int GetConfiguredCobbleXRequiredStacks()
        {
            int parsed = ParseNonNegativeInt(TxtCobbleXRequiredStacks.Text);
            if (parsed <= 0)
                parsed = 9;
            return Math.Clamp(parsed, CobbleXMinimumRequiredStacks, CobbleXMaximumRequiredStacks);
        }

        private string GetConfiguredCobbleXCommand()
        {
            return TxtCobbleXCommand.Text.Trim();
        }

        private int GetConfiguredCobbleXCommandCount() => ChkCobbleXRepeatThreeTimes.IsChecked == true ? 3 : 1;

        private bool AdvanceCobbleXCommandSequence(DateTime now)
        {
            if (_inventoryCleanupStage != InventoryCleanupStage.SubmitCobbleXCommand || !_inventoryCleanupCobbleXCommandPending)
                return false;

            _inventoryCleanupCobbleXCommandSendCount++;
            _inventoryCleanupCobbleXCommandSent = true;
            _inventoryCleanupCobbleXCommandPending = _inventoryCleanupCobbleXCommandSendCount < _inventoryCleanupCobbleXCommandTargetCount;
            _inventoryCleanupStage = _inventoryCleanupCobbleXCommandPending
                ? InventoryCleanupStage.OpenCobbleXChat
                : GetInventoryCleanupPostCommandStage();
            _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(_inventoryCleanupCobbleXCommandPending
                ? CobbleXDelayBetweenCommandsMs : CobbleXDelayAfterSubmitResumeMs);
            return true;
        }

        private void UpdateInventoryCleanupStatus(string message, string colorName)
        {
            LogStatus("Auto EQ", message, colorName);
            if (TxtInventoryCleanupStatus == null)
                return;

            TxtInventoryCleanupStatus.Text = message;
            TxtInventoryCleanupStatus.Foreground = colorName == "Red"
                ? new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107))
                : colorName == "Green"
                    ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                    : colorName == "Orange"
                        ? new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36))
                        : new SolidColorBrush(GlassPalette.MapLegacyColor(216, 226, 240));
        }

        private void InventoryCleanupSlot_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
                UpdateInventoryCleanupOptionVisual(checkBox);

            if (!_isLoadingUi)
                MarkDirty();
        }

        private void InventoryCleanupItemType_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
                UpdateInventoryCleanupOptionVisual(checkBox);

            if (!_isLoadingUi)
            {
                RefreshOverlayHud(DateTime.UtcNow);
                MarkDirty();
            }
        }

        private void ChkInventoryCleanupAllItemTypes_Changed(object sender, RoutedEventArgs e)
        {
            RefreshInventoryCleanupItemTypeMode();
            if (!_isLoadingUi)
            {
                RefreshOverlayHud(DateTime.UtcNow);
                MarkDirty();
            }
        }

        private void RefreshInventoryCleanupItemTypeMode()
        {
            bool customSelectionEnabled = ChkInventoryCleanupAllItemTypes?.IsChecked != true;
            if (PanelInventoryCleanupItemTypes != null)
            {
                PanelInventoryCleanupItemTypes.IsEnabled = customSelectionEnabled;
                PanelInventoryCleanupItemTypes.Opacity = customSelectionEnabled ? 1.0 : 0.72;
            }
            if (BtnInventoryCleanupSelectAllItemTypes != null)
                BtnInventoryCleanupSelectAllItemTypes.IsEnabled = customSelectionEnabled;
            if (BtnInventoryCleanupClearItemTypes != null)
                BtnInventoryCleanupClearItemTypes.IsEnabled = customSelectionEnabled;
        }

        private static void UpdateInventoryCleanupOptionVisual(CheckBox checkBox)
        {
            // Chipy typów przedmiotów mają wygląd w stylu GlassChip; tu kolorowane są tylko sloty EQ.
            if (checkBox.Tag is not int)
                return;

            bool slotSelected = checkBox.IsChecked == true;
            checkBox.Background = slotSelected ? GlassPalette.AccentSoft : new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            checkBox.BorderBrush = slotSelected ? GlassPalette.AccentSolid : GlassPalette.Line;
            checkBox.Foreground = slotSelected ? GlassPalette.AccentInk : GlassPalette.Muted;
            checkBox.BorderThickness = new Thickness(slotSelected ? 1.5 : 1);
            checkBox.Opacity = 1.0;
        }

        private void BtnInventoryCleanupSelectAll_Click(object sender, RoutedEventArgs e)
        {
            for (int slot = 0; slot < _inventoryCleanupSlotCheckBoxes.Count; slot++)
                _inventoryCleanupSlotCheckBoxes[slot].IsChecked = true;
            MarkDirty();
        }

        private void BtnInventoryCleanupClearSlots_Click(object sender, RoutedEventArgs e)
        {
            for (int slot = 0; slot < _inventoryCleanupSlotCheckBoxes.Count; slot++)
                _inventoryCleanupSlotCheckBoxes[slot].IsChecked = false;
            MarkDirty();
        }

        private void BtnInventoryCleanupSelectAllItemTypes_Click(object sender, RoutedEventArgs e)
        {
            foreach (CheckBox checkBox in _inventoryCleanupItemTypeCheckBoxes)
                checkBox.IsChecked = true;
            MarkDirty();
        }

        private void BtnInventoryCleanupClearItemTypes_Click(object sender, RoutedEventArgs e)
        {
            foreach (CheckBox checkBox in _inventoryCleanupItemTypeCheckBoxes)
                checkBox.IsChecked = false;
            MarkDirty();
        }

        private void BtnInventoryCleanupTest_Click(object sender, RoutedEventArgs e)
        {
            HashSet<int> enabledSlots = GetSelectedInventoryCleanupSlots();
            HashSet<string> enabledItemTypes = GetSelectedInventoryCleanupItemTypes();
            bool discardEverythingExceptCobblestone = ChkInventoryCleanupAllItemTypes.IsChecked == true;
            bool cobbleXEnabled = ChkCobbleXEnabled.IsChecked == true;
            bool goldEnabled = ChkGoldDepositEnabled.IsChecked == true;
            if (!cobbleXEnabled && !goldEnabled && enabledSlots.Count == 0)
            {
                UpdateInventoryCleanupStatus("Test: zaznacz przynajmniej jeden slot albo włącz CobbleX.", "Orange");
                return;
            }
            if (!cobbleXEnabled && !goldEnabled && !discardEverythingExceptCobblestone && enabledItemTypes.Count == 0)
            {
                UpdateInventoryCleanupStatus("Test: zaznacz przynajmniej jeden typ przedmiotu albo włącz CobbleX.", "Orange");
                return;
            }

            if (!TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out _))
            {
                UpdateInventoryCleanupStatus("Test: nie udało się przechwycić okna gry.", "Red");
                return;
            }

            using (Drawing.Bitmap capturedBitmap = bitmap!)
            {
                if (!InventoryMarkerDetector.TryDetect(capturedBitmap, enabledSlots, enabledItemTypes, out InventoryMarkerDetection detection))
                {
                    UpdateInventoryCleanupStatus("Test: nie znaleziono znaczników GUI. Otwórz ekwipunek, włącz przygotowany texturepack i pozostaw grę widoczną.", "Orange");
                    return;
                }

                if (discardEverythingExceptCobblestone && !detection.SupportsFullInventoryScan)
                {
                    UpdateInventoryCleanupStatus("Test: tryb 'Wyrzucaj wszystko' wymaga widocznej siatki EQ i aktualnych znaczników GUI z paczki Minecraft Helper.", "Orange");
                    return;
                }

                if (!discardEverythingExceptCobblestone && detection.UnknownMarkerSlots.Count > 0)
                {
                    UpdateInventoryCleanupStatus("Test: wykryto starą wersję znaczników. Włącz nowy texturepack z rozpoznawaniem typów przedmiotów.", "Orange");
                    return;
                }

                IReadOnlyList<DetectedInventoryItem> detectedItems = discardEverythingExceptCobblestone
                    ? detection.AllNonCobblestoneItems
                    : detection.Items;
                string items = detectedItems.Count == 0
                    ? "brak wybranych przedmiotów"
                    : string.Join(", ", detectedItems.Select(item => $"{item.Slot + 1} ({GetInventoryCleanupItemLabel(item.ItemId)} x{item.Quantity})"));
                int requiredCobbleStacks = GetConfiguredCobbleXRequiredStacks();
                string cobbleResult = cobbleXEnabled
                    ? $" Cobble 64: {detection.FullCobblestoneSlots.Count}/{requiredCobbleStacks}."
                    : string.Empty;
                UpdateInventoryCleanupStatus($"Test OK (GUI x{detection.Layout.Scale}). Do wyrzucenia: {items}.{cobbleResult}", "Green");
            }
        }

        private bool TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out Drawing.Rectangle clientArea)
        {
            bitmap = null;
            clientArea = Drawing.Rectangle.Empty;
            if (_targetGameWindowHandle == IntPtr.Zero || !TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT clientRect))
                return false;

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;
            if (width < 320 || height < 240)
                return false;

            clientArea = new Drawing.Rectangle(clientRect.Left, clientRect.Top, width, height);
            if (BackgroundGameWindow.IsActive)
            {
                // On another virtual desktop CopyFromScreen would return the user's
                // desktop. clientArea stays in screen coordinates for click maths.
                if (BackgroundGameWindow.TryCaptureClient(_targetGameWindowHandle, out bitmap))
                    return true;

                clientArea = Drawing.Rectangle.Empty;
                return false;
            }

            try
            {
                bitmap = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format32bppArgb);
                using Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(clientArea.Left, clientArea.Top, 0, 0, clientArea.Size, Drawing.CopyPixelOperation.SourceCopy);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Obraz", "Nie udało się zrobić zrzutu okna gry: " + ex.GetBaseException().Message);
                bitmap?.Dispose();
                bitmap = null;
                clientArea = Drawing.Rectangle.Empty;
                return false;
            }
        }

        private void EnsureAutoReconnectServerProfiles()
        {
            _settings.AutoReconnectServerProfiles ??= new List<AutoReconnectServerProfile>();
            if (_settings.AutoReconnectServerProfiles.Count == 0)
            {
                bool legacyHasGui = !string.Equals(_settings.AutoReconnectProfile, "Standard", StringComparison.OrdinalIgnoreCase);
                string legacyName = _settings.AutoReconnectServerAddress.Contains("arivi", StringComparison.OrdinalIgnoreCase)
                    ? "Arivi"
                    : "Domyślny serwer";
                _settings.AutoReconnectServerProfiles.Add(new AutoReconnectServerProfile
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = legacyName,
                    ServerAddress = _settings.AutoReconnectServerAddress,
                    HomeCommand = NormalizeChatCommand(_settings.AutoReconnectHomeCommand),
                    HomeHasGui = legacyHasGui,
                    HomeGuiDelaySeconds = 1,
                    HomeGuiRows = 3,
                    HomeGuiColumns = 9,
                    HomeGuiSlot = Math.Clamp(_settings.AutoReconnectHomeSlot, 1, 27),
                    JoinDelaySeconds = Math.Clamp(_settings.AutoReconnectJoinDelaySeconds, 2, 120),
                    TeleportDelaySeconds = Math.Clamp(_settings.AutoReconnectTeleportDelaySeconds, 1, 120),
                    WatchdogSeconds = Math.Clamp(_settings.AutoReconnectWatchdogSeconds, 15, 3600),
                    MaxAttempts = Math.Clamp(_settings.AutoReconnectMaxAttempts, 1, 10),
                    MissingPickaxeRecoveryEnabled = _settings.AutoReconnectMissingPickaxeRecoveryEnabled
                });
            }

            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AutoReconnectServerProfile profile in _settings.AutoReconnectServerProfiles)
            {
                profile.Id = string.IsNullOrWhiteSpace(profile.Id) || !usedIds.Add(profile.Id)
                    ? Guid.NewGuid().ToString("N")
                    : profile.Id.Trim();
                usedIds.Add(profile.Id);
                profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Serwer" : profile.Name.Trim();
                profile.ServerAddress = (profile.ServerAddress ?? string.Empty).Trim();
                NormalizeAutoReconnectHomeSettings(profile);
                profile.JoinDelaySeconds = Math.Clamp(profile.JoinDelaySeconds, 2, 120);
                profile.TeleportDelaySeconds = Math.Clamp(profile.TeleportDelaySeconds, 1, 120);
                profile.WatchdogSeconds = Math.Clamp(profile.WatchdogSeconds, 15, 3600);
                profile.MaxAttempts = Math.Clamp(profile.MaxAttempts, 1, 10);
            }

            AutoReconnectServerProfile? selected = _settings.AutoReconnectServerProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, _settings.AutoReconnectSelectedServerProfileId, StringComparison.OrdinalIgnoreCase));
            selected ??= _settings.AutoReconnectServerProfiles[0];
            _settings.AutoReconnectSelectedServerProfileId = selected.Id;
            ApplyAutoReconnectServerProfile(selected, updateUi: false);
        }

        private AutoReconnectServerProfile? GetSelectedAutoReconnectServerProfile()
        {
            return _settings.AutoReconnectServerProfiles?.FirstOrDefault(profile =>
                string.Equals(profile.Id, _settings.AutoReconnectSelectedServerProfileId, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyAutoReconnectServerProfile(AutoReconnectServerProfile profile, bool updateUi)
        {
            NormalizeAutoReconnectHomeSettings(profile);
            _settings.AutoReconnectSelectedServerProfileId = profile.Id;
            _settings.AutoReconnectProfile = profile.HomeHasGui ? "Arivi" : "Standard";
            _settings.AutoReconnectServerAddress = profile.ServerAddress.Trim();
            _settings.AutoReconnectHomeCommand = NormalizeChatCommand(profile.HomeCommand);
            _settings.AutoReconnectHomeHasGui = profile.HomeHasGui;
            _settings.AutoReconnectHomeGuiDelaySeconds = Math.Clamp(profile.HomeGuiDelaySeconds, 0, 120);
            _settings.AutoReconnectHomeGuiRows = profile.HomeGuiRows;
            _settings.AutoReconnectHomeGuiColumns = profile.HomeGuiColumns;
            _settings.AutoReconnectHomeSlot = profile.HomeGuiSlot;
            _settings.AutoReconnectJoinDelaySeconds = Math.Clamp(profile.JoinDelaySeconds, 2, 120);
            _settings.AutoReconnectTeleportDelaySeconds = Math.Clamp(profile.TeleportDelaySeconds, 1, 120);
            _settings.AutoReconnectWatchdogSeconds = Math.Clamp(profile.WatchdogSeconds, 15, 3600);
            _settings.AutoReconnectMaxAttempts = Math.Clamp(profile.MaxAttempts, 1, 10);
            _settings.AutoReconnectMissingPickaxeRecoveryEnabled = profile.MissingPickaxeRecoveryEnabled;

            if (!updateUi)
                return;

            CbAutoReconnectProfile.SelectedIndex = profile.HomeHasGui ? 0 : 1;
            TxtAutoReconnectServerAddress.Text = _settings.AutoReconnectServerAddress;
            TxtAutoReconnectHomeCommand.Text = _settings.AutoReconnectHomeCommand;
            TxtAutoReconnectHomeSlot.Text = _settings.AutoReconnectHomeSlot.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectJoinDelay.Text = _settings.AutoReconnectJoinDelaySeconds.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectTeleportDelay.Text = _settings.AutoReconnectTeleportDelaySeconds.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectWatchdogSeconds.Text = _settings.AutoReconnectWatchdogSeconds.ToString(CultureInfo.InvariantCulture);
            TxtAutoReconnectMaxAttempts.Text = _settings.AutoReconnectMaxAttempts.ToString(CultureInfo.InvariantCulture);
            RefreshAutoReconnectHomeSlotGrid();
        }

        private void RefreshAutoReconnectServerProfileCards()
        {
            if (PanelAutoReconnectServerProfiles == null)
                return;

            PanelAutoReconnectServerProfiles.Children.Clear();
            _autoReconnectServerProfileButtons.Clear();
            foreach (AutoReconnectServerProfile profile in _settings.AutoReconnectServerProfiles)
            {
                bool selected = string.Equals(profile.Id, _settings.AutoReconnectSelectedServerProfileId, StringComparison.OrdinalIgnoreCase);
                var content = new StackPanel { Margin = new Thickness(2) };
                content.Children.Add(new TextBlock
                {
                    Text = profile.Name,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = selected ? GlassPalette.AccentInk : GlassPalette.Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                content.Children.Add(new TextBlock
                {
                    Text = profile.ServerAddress,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                content.Children.Add(new TextBlock
                {
                    Text = profile.HomeHasGui ? $"GUI {profile.HomeGuiRows}×{profile.HomeGuiColumns} • slot {profile.HomeGuiSlot}" : "Komenda bez GUI",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(127, 200, 255)),
                    Margin = new Thickness(0, 2, 0, 0)
                });

                var button = new Button
                {
                    Content = content,
                    Tag = profile.Id,
                    Width = 210,
                    Height = 78,
                    Margin = new Thickness(0, 0, 7, 7),
                    Padding = new Thickness(7, 5, 7, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(selected ? GlassPalette.MapLegacyColor(23, 50, 74) : GlassPalette.MapLegacyColor(30, 42, 57)),
                    BorderBrush = new SolidColorBrush(selected ? GlassPalette.MapLegacyColor(46, 168, 255) : GlassPalette.MapLegacyColor(62, 83, 110)),
                    BorderThickness = new Thickness(selected ? 2 : 1)
                };
                button.Click += AutoReconnectServerProfile_Click;
                _autoReconnectServerProfileButtons.Add(button);
                PanelAutoReconnectServerProfiles.Children.Add(button);
            }

            RefreshAutoReconnectSelectedProfileSummary();
            RefreshEmergencyReconnectProfileSummary();
        }

        private void RefreshAutoReconnectSelectedProfileSummary()
        {
            AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
            if (profile == null || TxtAutoReconnectSelectedProfileName == null)
                return;

            TxtAutoReconnectSelectedProfileName.Text = profile.Name;
            TxtAutoReconnectSelectedProfileAddress.Text = profile.ServerAddress;
            TxtAutoReconnectSelectedProfileHome.Text = profile.HomeHasGui
                ? $"{profile.HomeCommand} → GUI {profile.HomeGuiRows}×{profile.HomeGuiColumns}, slot {profile.HomeGuiSlot}, oczekiwanie {profile.HomeGuiDelaySeconds} s"
                : $"{profile.HomeCommand} → bez GUI";
            string missingPickaxeHome = profile.MissingPickaxeHomeHasGui == true
                ? $"{profile.MissingPickaxeHomeCommand} → GUI {profile.MissingPickaxeHomeGuiRows}×{profile.MissingPickaxeHomeGuiColumns}, slot {profile.MissingPickaxeHomeGuiSlot}"
                : $"{profile.MissingPickaxeHomeCommand} → bez GUI";
            TxtAutoReconnectSelectedProfileTiming.Text =
                $"dołączenie {profile.JoinDelaySeconds} s • teleport {profile.TeleportDelaySeconds} s + {AutoReconnectTeleportSafetyBufferSeconds} s buforu • próby {profile.MaxAttempts}" +
                (profile.MissingPickaxeRecoveryEnabled
                    ? $" • brak kilofa: {missingPickaxeHome}"
                    : " • kontrola kilofa wyłączona");
        }

        private void AutoReconnectServerProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string profileId })
                return;
            AutoReconnectServerProfile? profile = _settings.AutoReconnectServerProfiles.FirstOrDefault(item =>
                string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
                return;

            ApplyAutoReconnectServerProfile(profile, updateUi: true);
            RefreshAutoReconnectServerProfileCards();
            _nextAutoReconnectHealthCheckAtUtc = DateTime.UtcNow.AddSeconds(profile.WatchdogSeconds);
            UpdateAutoReconnectStatus($"Wybrano profil serwera: {profile.Name}.", "Green");
            if (!_isLoadingUi)
                MarkDirty();
        }

        private void BtnAutoReconnectAddProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AutoReconnectProfileWindow(null) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ResultProfile == null)
                return;

            _settings.AutoReconnectServerProfiles.Add(dialog.ResultProfile);
            ApplyAutoReconnectServerProfile(dialog.ResultProfile, updateUi: true);
            RefreshAutoReconnectServerProfileCards();
            UpdateAutoReconnectStatus($"Dodano profil serwera: {dialog.ResultProfile.Name}.", "Green");
            MarkDirty();
        }

        private void BtnAutoReconnectEditProfile_Click(object sender, RoutedEventArgs e)
        {
            AutoReconnectServerProfile? selected = GetSelectedAutoReconnectServerProfile();
            if (selected == null)
                return;

            var dialog = new AutoReconnectProfileWindow(selected) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ResultProfile == null)
                return;

            int index = _settings.AutoReconnectServerProfiles.FindIndex(profile =>
                string.Equals(profile.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _settings.AutoReconnectServerProfiles[index] = dialog.ResultProfile;
            ApplyAutoReconnectServerProfile(dialog.ResultProfile, updateUi: true);
            RefreshAutoReconnectServerProfileCards();
            UpdateAutoReconnectStatus($"Zapisano profil serwera: {dialog.ResultProfile.Name}.", "Green");
            MarkDirty();
        }

        private void BtnAutoReconnectDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            AutoReconnectServerProfile? selected = GetSelectedAutoReconnectServerProfile();
            if (selected == null)
                return;
            if (_settings.AutoReconnectServerProfiles.Count <= 1)
            {
                UpdateAutoReconnectStatus("Musi pozostać przynajmniej jeden profil serwera.", "Orange");
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!string.Equals(_pendingAutoReconnectProfileDeleteId, selected.Id, StringComparison.OrdinalIgnoreCase)
                || now > _pendingAutoReconnectProfileDeleteUntilUtc)
            {
                _pendingAutoReconnectProfileDeleteId = selected.Id;
                _pendingAutoReconnectProfileDeleteUntilUtc = now.AddSeconds(5);
                UpdateAutoReconnectStatus($"Kliknij Usuń ponownie w ciągu 5 s, aby usunąć profil {selected.Name}.", "Orange");
                return;
            }

            _settings.AutoReconnectServerProfiles.Remove(selected);
            AutoReconnectServerProfile next = _settings.AutoReconnectServerProfiles[0];
            ApplyAutoReconnectServerProfile(next, updateUi: true);
            RefreshAutoReconnectServerProfileCards();
            _pendingAutoReconnectProfileDeleteId = string.Empty;
            UpdateAutoReconnectStatus($"Usunięto profil: {selected.Name}.", "Green");
            MarkDirty();
        }

        private static string NormalizeChatCommand(string? command)
        {
            string normalized = (command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return "/home";
            return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
        }

        private static void NormalizeAutoReconnectHomeSettings(AutoReconnectServerProfile profile)
        {
            profile.HomeCommand = NormalizeChatCommand(profile.HomeCommand);
            profile.HomeGuiDelaySeconds = Math.Clamp(profile.HomeGuiDelaySeconds, 0, 120);
            profile.HomeGuiRows = Math.Clamp(profile.HomeGuiRows, 1, 6);
            profile.HomeGuiColumns = Math.Clamp(profile.HomeGuiColumns, 1, 9);
            profile.HomeGuiSlot = Math.Clamp(profile.HomeGuiSlot, 1, profile.HomeGuiRows * profile.HomeGuiColumns);

            profile.MissingPickaxeHomeCommand = NormalizeChatCommand(
                string.IsNullOrWhiteSpace(profile.MissingPickaxeHomeCommand)
                    ? profile.HomeCommand
                    : profile.MissingPickaxeHomeCommand);
            profile.MissingPickaxeHomeHasGui ??= profile.HomeHasGui;
            profile.MissingPickaxeHomeGuiDelaySeconds = profile.MissingPickaxeHomeGuiDelaySeconds < 0
                ? profile.HomeGuiDelaySeconds
                : Math.Clamp(profile.MissingPickaxeHomeGuiDelaySeconds, 0, 120);
            profile.MissingPickaxeHomeGuiRows = profile.MissingPickaxeHomeGuiRows <= 0
                ? profile.HomeGuiRows
                : Math.Clamp(profile.MissingPickaxeHomeGuiRows, 1, 6);
            profile.MissingPickaxeHomeGuiColumns = profile.MissingPickaxeHomeGuiColumns <= 0
                ? profile.HomeGuiColumns
                : Math.Clamp(profile.MissingPickaxeHomeGuiColumns, 1, 9);
            int missingPickaxeSlotCount = profile.MissingPickaxeHomeGuiRows * profile.MissingPickaxeHomeGuiColumns;
            profile.MissingPickaxeHomeGuiSlot = profile.MissingPickaxeHomeGuiSlot <= 0
                ? Math.Clamp(profile.HomeGuiSlot, 1, missingPickaxeSlotCount)
                : Math.Clamp(profile.MissingPickaxeHomeGuiSlot, 1, missingPickaxeSlotCount);
        }

        private void ReadAutoReconnectSettingsFromUi()
        {
            AutoReconnectServerProfile? selected = GetSelectedAutoReconnectServerProfile();
            if (selected != null)
                ApplyAutoReconnectServerProfile(selected, updateUi: false);
        }

        private static int CalculateAutoReconnectTeleportWaitSeconds(int configuredSeconds)
        {
            return Math.Clamp(configuredSeconds, 1, 120) + AutoReconnectTeleportSafetyBufferSeconds;
        }

        private void ConfigureActiveAutoReconnectHome(bool missingPickaxeRecovery)
        {
            AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
            if (profile == null)
            {
                _autoReconnectActiveHomeCommand = NormalizeChatCommand(_settings.AutoReconnectHomeCommand);
                _autoReconnectActiveHomeHasGui = _settings.AutoReconnectHomeHasGui;
                _autoReconnectActiveHomeGuiDelaySeconds = Math.Clamp(_settings.AutoReconnectHomeGuiDelaySeconds, 0, 120);
                _autoReconnectActiveHomeGuiRows = Math.Clamp(_settings.AutoReconnectHomeGuiRows, 1, 6);
                _autoReconnectActiveHomeGuiColumns = Math.Clamp(_settings.AutoReconnectHomeGuiColumns, 1, 9);
                _autoReconnectActiveHomeSlot = Math.Clamp(
                    _settings.AutoReconnectHomeSlot,
                    1,
                    _autoReconnectActiveHomeGuiRows * _autoReconnectActiveHomeGuiColumns);
                _autoReconnectActiveTeleportDelaySeconds = Math.Clamp(
                    _settings.AutoReconnectTeleportDelaySeconds,
                    1,
                    120);
                return;
            }

            NormalizeAutoReconnectHomeSettings(profile);
            if (missingPickaxeRecovery)
            {
                _autoReconnectActiveHomeCommand = profile.MissingPickaxeHomeCommand;
                _autoReconnectActiveHomeHasGui = profile.MissingPickaxeHomeHasGui == true;
                _autoReconnectActiveHomeGuiDelaySeconds = profile.MissingPickaxeHomeGuiDelaySeconds;
                _autoReconnectActiveHomeGuiRows = profile.MissingPickaxeHomeGuiRows;
                _autoReconnectActiveHomeGuiColumns = profile.MissingPickaxeHomeGuiColumns;
                _autoReconnectActiveHomeSlot = profile.MissingPickaxeHomeGuiSlot;
                _autoReconnectActiveTeleportDelaySeconds = Math.Clamp(profile.TeleportDelaySeconds, 1, 120);
                return;
            }

            _autoReconnectActiveHomeCommand = profile.HomeCommand;
            _autoReconnectActiveHomeHasGui = profile.HomeHasGui;
            _autoReconnectActiveHomeGuiDelaySeconds = profile.HomeGuiDelaySeconds;
            _autoReconnectActiveHomeGuiRows = profile.HomeGuiRows;
            _autoReconnectActiveHomeGuiColumns = profile.HomeGuiColumns;
            _autoReconnectActiveHomeSlot = profile.HomeGuiSlot;
            _autoReconnectActiveTeleportDelaySeconds = Math.Clamp(profile.TeleportDelaySeconds, 1, 120);
        }

        private void UpdateAutoReconnectStatus(string message, string colorName = "Default")
        {
            LogStatus("Reconnect", message, colorName);
            if (TxtAutoReconnectStatus != null)
            {
                TxtAutoReconnectStatus.Text = message;
                TxtAutoReconnectStatus.Foreground = colorName == "Red"
                    ? new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107))
                    : colorName == "Green"
                        ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                        : colorName == "Orange"
                            ? new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36))
                            : new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193));
            }

            if (_emergencyReconnectActive)
                UpdateEmergencyReconnectStatus(message, colorName);

            UpdateStatusBar(message, colorName);
        }

        private bool CanRunAutoReconnectTest(out string error)
        {
            error = string.Empty;
            if (_inventoryCleanupStage != InventoryCleanupStage.None)
            {
                error = "Poczekaj na zakończenie bieżącego Auto EQ przed uruchomieniem testu reconnectu.";
                return false;
            }
            if (_targetGameWindowHandle == IntPtr.Zero)
            {
                error = "Auto reconnect: najpierw wybierz i zapisz proces Minecrafta.";
                return false;
            }
            return true;
        }

        private void CaptureMiningModeForAutoReconnect()
        {
            _autoReconnectResumeKopacz533 = _kopacz533RuntimeEnabled;
            _autoReconnectResumeKopacz633 = _kopacz633RuntimeEnabled;
            if (_autoReconnectResumeKopacz533)
            {
                _autoReconnectLogMiningRunId = _kopacz533MiningRunId;
                _autoReconnectLogOwner = GetInventoryCleanupOwnerLabel(InventoryCleanupOwner.Kopacz533);
            }
            else if (_autoReconnectResumeKopacz633)
            {
                _autoReconnectLogMiningRunId = _kopacz633MiningRunId;
                _autoReconnectLogOwner = GetInventoryCleanupOwnerLabel(InventoryCleanupOwner.Kopacz633);
            }
            else
            {
                _autoReconnectLogMiningRunId = string.Empty;
                _autoReconnectLogOwner = "Auto reconnect";
            }
        }

        private void PauseMiningForAutoReconnect()
        {
            SetKopacz533MiningHold(false);
            SetKopacz633AttackHold(false);
            SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
        }

        private bool CanContinueEmergencyReconnect(int generation)
        {
            return generation == _autoReconnectGeneration
                && _emergencyReconnectActive
                && IsEmergencyReconnectEnabled();
        }

        private bool BeginEmergencyReconnectAfterDisconnect(DateTime now, int generation)
        {
            if (!CanContinueEmergencyReconnect(generation))
                return false;
            AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
            if (profile == null)
            {
                ClearEmergencyReconnectRuntimeState(stopSoundMonitoring: true);
                UpdateEmergencyReconnectStatus("Nie uruchomiono reconnectu: brak wybranego profilu serwera.", "Red");
                return false;
            }

            ReadAutoReconnectSettingsFromUi();
            ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: false);
            _emergencyReconnectActive = true;
            _emergencyReconnectSoundGuardActive = true;
            _emergencyReconnectShutdownAfterHome = false;
            _emergencyReconnectInventoryAttempts = 0;
            _autoReconnectManualRun = false;
            _autoReconnectInventoryOnly = false;
            _autoReconnectAttempt = 0;
            _autoReconnectMainMenuClicks = 0;
            _autoReconnectDisconnectScreenSinceUtc = DateTime.MinValue;
            _autoReconnectRefreshServerList = false;
            _autoReconnectHomeCommandRetries = 0;
            _autoReconnectJoinCheckByScreen = false;
            _autoReconnectInventoryFailures = 0;
            _autoReconnectReturningHomeAfterMissingPickaxe = false;
            _autoReconnectResumeKopacz533 = _emergencyReconnectResumeKopacz533;
            _autoReconnectResumeKopacz633 = _emergencyReconnectResumeKopacz633;
            _autoReconnectLogOwner = _emergencyReconnectResumeKopacz533
                ? GetInventoryCleanupOwnerLabel(InventoryCleanupOwner.Kopacz533)
                : _emergencyReconnectResumeKopacz633
                    ? GetInventoryCleanupOwnerLabel(InventoryCleanupOwner.Kopacz633)
                    : "Awaryjny reconnect";
            _autoReconnectStage = AutoReconnectStage.EmergencyWaitBeforeReconnect;
            int delaySeconds = Math.Clamp(_settings.EmergencyReconnectDelaySeconds, 1, 600);
            _nextAutoReconnectActionAtUtc = now.AddSeconds(delaySeconds);
            _autoReconnectLastCountdownSecond = -1;
            RecordAutomationLogEvent(
                MiningLogEventTypes.EmergencyProtectionStarted,
                MiningLogStatuses.Completed,
                $"Alarm obrażeń: wyjście z serwera zakończone. Awaryjny reconnect za {delaySeconds} s; profil: {profile.Name}.");
            UpdateEmergencyReconnectStatus(
                $"Rozłączono. Reconnect za {delaySeconds} s. Nasłuch obrażeń pozostaje aktywny do kontroli kilofa.",
                "Orange");
            UpdateEnabledStates();
            return true;
        }

        private void ResetAutoReconnectStageForEmergencyRedetection()
        {
            _autoReconnectGeneration++;
            _autoReconnectStage = AutoReconnectStage.None;
            _autoReconnectOcrInProgress = false;
            _autoReconnectManualRun = false;
            _autoReconnectInventoryOnly = false;
            _autoReconnectInventoryFailures = 0;
            _autoReconnectLastCountdownSecond = -1;
            _autoReconnectPendingScreenKind = AutoReconnectScreenKind.Unknown;
            _autoReconnectJoinCheckByScreen = false;
            _autoReconnectReturningHomeAfterMissingPickaxe = false;
            _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
        }

        private void ClearEmergencyReconnectRuntimeState(bool stopSoundMonitoring)
        {
            _emergencyReconnectActive = false;
            _emergencyReconnectSoundGuardActive = false;
            _emergencyReconnectResumeKopacz533 = false;
            _emergencyReconnectResumeKopacz633 = false;
            _emergencyReconnectShutdownAfterHome = false;
            _emergencyReconnectInventoryAttempts = 0;
            if (stopSoundMonitoring
                && !_kopacz533RuntimeEnabled
                && !_kopacz633RuntimeEnabled
                && _damageSoundDetector.IsRunning)
            {
                StopEmergencyDamageSoundListening(updateStatus: false);
            }
        }

        private void CompleteEmergencyReconnectAndResumeMining()
        {
            bool resumeKopacz533 = _emergencyReconnectResumeKopacz533;
            bool resumeKopacz633 = _emergencyReconnectResumeKopacz633;
            int teleportWaitSeconds = CalculateAutoReconnectTeleportWaitSeconds(
                _autoReconnectActiveTeleportDelaySeconds);
            string plannedMiner = resumeKopacz533 && ChkKopacz533Enabled?.IsChecked == true
                ? "Kopacz 5/3/3"
                : resumeKopacz633 && ChkKopacz633Enabled?.IsChecked == true && IsKopacz633DirectionSelected()
                    ? "Kopacz 6/3/3"
                    : string.Empty;
            if (!string.IsNullOrEmpty(plannedMiner))
            {
                RecordAutomationLogEvent(
                    MiningLogEventTypes.EmergencyMiningResumed,
                    MiningLogStatuses.Completed,
                    $"Odczekano {teleportWaitSeconds} s od wyboru home ({_autoReconnectActiveTeleportDelaySeconds} s z profilu + {AutoReconnectTeleportSafetyBufferSeconds} s bezpieczeństwa). Wybrano slot 1 i wznowiono {plannedMiner}.");
            }
            StopAutoReconnect(
                "Awaryjny reconnect zakończony: wykonano powrót do wybranego home.",
                resumeMining: false,
                warning: false);

            DateTime now = DateTime.UtcNow;
            string resumedMiner = string.Empty;
            if (resumeKopacz533 && ChkKopacz533Enabled?.IsChecked == true)
            {
                SendKeyTap(VK_1);
                _kopacz533RuntimeEnabled = true;
                StopOtherExclusivePointerMacros(keepKopacz533: true);
                StartKopacz533Runtime(now);
                resumedMiner = "Kopacz 5/3/3";
            }
            else if (resumeKopacz633
                && ChkKopacz633Enabled?.IsChecked == true
                && IsKopacz633DirectionSelected())
            {
                SendKeyTap(VK_1);
                _kopacz633RuntimeEnabled = true;
                StopOtherExclusivePointerMacros(keepKopacz633: true);
                StartKopacz633Runtime(now);
                resumedMiner = "Kopacz 6/3/3";
            }

            if (string.IsNullOrEmpty(resumedMiner))
            {
                UpdateEmergencyReconnectStatus(
                    "Powrót do home zakończony, ale nie wznowiono Kopacza — jego kanał lub kierunek jest obecnie wyłączony.",
                    "Orange");
                UpdateStatusBar("Awaryjny reconnect zakończony bez wznowienia Kopacza.", "Orange");
            }
            else
            {
                UpdateEmergencyReconnectStatus($"Powrót zakończony po {teleportWaitSeconds} s. Wybrano slot 1 i wznowiono {resumedMiner}.", "Green");
                UpdateStatusBar($"Awaryjny reconnect zakończony — slot 1 wybrany, wznowiono {resumedMiner}.", "Green");
            }

            RefreshOverlayHud(DateTime.UtcNow);
            UpdateEnabledStates();
            UpdateEmergencyDamageSoundMonitoring();
        }

        private void CompleteEmergencyReconnectAndShutdown()
        {
            int teleportWaitSeconds = CalculateAutoReconnectTeleportWaitSeconds(
                _autoReconnectActiveTeleportDelaySeconds);
            RecordAutomationLogEvent(
                MiningLogEventTypes.EmergencyShutdown,
                MiningLogStatuses.Completed,
                $"Po wyborze awaryjnego home odczekano {teleportWaitSeconds} s ({_autoReconnectActiveTeleportDelaySeconds} s z profilu + {AutoReconnectTeleportSafetyBufferSeconds} s bezpieczeństwa). Zakończono pracę tego konta.");
            StopAutoReconnect(
                "Awaryjny home osiągnięty bez diamentowego kilofa. Kończę pracę tego konta.",
                resumeMining: false,
                warning: false);
            // An account emergency must never shut down the shared workspace
            // and stop miners assigned to other Minecraft clients.
            StopMiningFromRemoteCommand();
            if (BackgroundGameWindow.IsActive)
                EndBackgroundMiningSession(restoreHeldInputsInForeground: false);
            UpdateEmergencyReconnectStatus(
                "Awaryjny home osiągnięty. Praca tego konta zakończona; pozostałe konta działają dalej.",
                "Green");
        }

        private void BeginAutoReconnectHealthCheck(bool manual, bool inventoryOnly)
        {
            if (_autoReconnectStage != AutoReconnectStage.None)
                return;

            ReadAutoReconnectSettingsFromUi();
            ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: false);
            _autoReconnectManualRun = manual;
            _autoReconnectInventoryOnly = inventoryOnly;
            _autoReconnectInventoryFailures = 0;
            _autoReconnectReturningHomeAfterMissingPickaxe = false;
            CaptureMiningModeForAutoReconnect();
            PauseMiningForAutoReconnect();
            RecordAutomationLogEvent(
                MiningLogEventTypes.HealthCheckStarted,
                MiningLogStatuses.Completed,
                $"Rozpoczęto kontrolę EQ ({(manual ? "test ręczny" : "kontrola automatyczna")}). Kopanie zostało chwilowo wstrzymane.");
            _autoReconnectStage = AutoReconnectStage.HealthOpenInventory;
            _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
            UpdateAutoReconnectStatus("Kontrola: zatrzymano kopanie, za chwilę otwieram EQ...", "Orange");
        }

        private void BeginFullAutoReconnect(bool manual)
        {
            if (_autoReconnectStage != AutoReconnectStage.None)
                return;

            ReadAutoReconnectSettingsFromUi();
            ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: false);
            _autoReconnectManualRun = manual;
            _autoReconnectInventoryOnly = false;
            _autoReconnectAttempt = 0;
            _autoReconnectMainMenuClicks = 0;
            _autoReconnectDisconnectScreenSinceUtc = DateTime.MinValue;
            _autoReconnectRefreshServerList = false;
            _autoReconnectHomeCommandRetries = 0;
            _autoReconnectJoinCheckByScreen = false;
            _autoReconnectInventoryFailures = 0;
            _autoReconnectReturningHomeAfterMissingPickaxe = false;
            CaptureMiningModeForAutoReconnect();
            PauseMiningForAutoReconnect();
            AutoReconnectServerProfile? reconnectProfile = GetSelectedAutoReconnectServerProfile();
            string profileLabel = reconnectProfile?.Name?.Trim() ?? _settings.AutoReconnectProfile;
            RecordAutomationLogEvent(
                MiningLogEventTypes.AutoReconnectStarted,
                MiningLogStatuses.Completed,
                $"Uruchomiono {(manual ? "ręczny" : "automatyczny")} reconnect. Profil: {profileLabel}; adres: {_settings.AutoReconnectServerAddress}; powrót: {_autoReconnectActiveHomeCommand}.");
            _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
            _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
            UpdateAutoReconnectStatus("Reconnect: analizuję aktualny ekran...", "Orange");
        }

        private void StopAutoReconnect(string message, bool resumeMining, bool warning)
        {
            _autoReconnectGeneration++;
            // Każde zakończenie inne niż udany powrót (błąd, STOP) anuluje start kopania z testu pełnego cyklu
            // albo z !join — wtedy Discord dostaje powód przerwania.
            // Po udanym powrocie właściciel jest już odebrany (TakeFullCycleTestOwner), więc znacznik
            // Discorda zostaje dla StartMiningAfterFullCycleTest.
            // Znacznik zostaje do zapisu w dzienniku, żeby karta „Reconnect nieudany” nie dublowała
            // wyniku !join — ten wysyłamy po zapisie.
            bool reportDiscordJoinFailure = _fullCycleTestStartOwner != InventoryCleanupOwner.None && _fullCycleStartFromDiscord;
            _fullCycleTestStartOwner = InventoryCleanupOwner.None;
            bool wasEmergencyReconnect = _emergencyReconnectActive;
            bool hadActiveStage = _autoReconnectStage != AutoReconnectStage.None;
            bool wasInventoryOnly = _autoReconnectInventoryOnly;
            bool wasMissingPickaxeRecovery = _autoReconnectReturningHomeAfterMissingPickaxe;
            int attempts = _autoReconnectAttempt;
            if (hadActiveStage)
            {
                RecordAutomationLogEvent(
                    wasEmergencyReconnect
                        ? MiningLogEventTypes.EmergencyProtectionFinished
                        : wasMissingPickaxeRecovery
                            ? MiningLogEventTypes.MissingPickaxeRecoveryFinished
                        : wasInventoryOnly
                            ? MiningLogEventTypes.HealthCheckFinished
                            : MiningLogEventTypes.AutoReconnectFinished,
                    warning ? MiningLogStatuses.Aborted : MiningLogStatuses.Completed,
                    $"{message} Próby połączenia: {attempts}; wznowienie kopania: {(resumeMining ? "tak" : "nie")}.");
            }
            if (reportDiscordJoinFailure)
                ReportDiscordJoinResult(false, message);

            _autoReconnectStage = AutoReconnectStage.None;
            _autoReconnectOcrInProgress = false;
            _autoReconnectManualRun = false;
            _autoReconnectInventoryOnly = false;
            _autoReconnectInventoryFailures = 0;
            _autoReconnectLastCountdownSecond = -1;
            _autoReconnectPendingScreenKind = AutoReconnectScreenKind.Unknown;
            _autoReconnectJoinCheckByScreen = false;
            _autoReconnectReturningHomeAfterMissingPickaxe = false;
            _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
            _nextAutoReconnectHealthCheckAtUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.AutoReconnectWatchdogSeconds, 15, 3600));

            bool targetFocused = IsGameInputAvailableFor(_targetGameWindowHandle);
            if (resumeMining && targetFocused)
            {
                if (_autoReconnectResumeKopacz533 && _kopacz533RuntimeEnabled)
                    SetKopacz533MiningHold(true);
                if (_autoReconnectResumeKopacz633 && _kopacz633RuntimeEnabled)
                    SetKopacz633AttackHold(true);
            }

            _autoReconnectResumeKopacz533 = false;
            _autoReconnectResumeKopacz633 = false;
            _autoReconnectLogMiningRunId = string.Empty;
            _autoReconnectLogOwner = string.Empty;
            if (wasEmergencyReconnect)
            {
                ClearEmergencyReconnectRuntimeState(stopSoundMonitoring: true);
                UpdateEmergencyReconnectStatus(message, warning ? "Red" : "Green");
            }
            UpdateAutoReconnectStatus(message, warning ? "Red" : "Green");
            UpdateEnabledStates();
        }

        private async void RunAutoReconnectTick(object? sender, EventArgs e)
        {
            if (_isLoadingUi || _autoReconnectTickInProgress)
                return;

            DateTime now = DateTime.UtcNow;
            if (_autoReconnectStage == AutoReconnectStage.None)
                return;

            // RunMacroTickCore intentionally stops before refreshing the normal
            // HUD while reconnect owns the input. Keep the reconnect tile and
            // its countdown alive from the slower reconnect timer instead.
            RefreshOverlayHud(now);

            if (!IsGameInputAvailableFor(_targetGameWindowHandle))
            {
                UpdateAutoReconnectStatus("Reconnect wstrzymany: Minecraft musi być aktywnym oknem albo pracować w tle.", "Orange");
                return;
            }
            if (_autoReconnectStage == AutoReconnectStage.EmergencyWaitBeforeReconnect
                && now < _nextAutoReconnectActionAtUtc)
            {
                int remainingSeconds = Math.Max(1, (int)Math.Ceiling((_nextAutoReconnectActionAtUtc - now).TotalSeconds));
                if (remainingSeconds != _autoReconnectLastCountdownSecond)
                {
                    _autoReconnectLastCountdownSecond = remainingSeconds;
                    UpdateAutoReconnectStatus(
                        $"Awaryjny reconnect za {remainingSeconds} s. Nasłuch obrażeń nadal działa.",
                        "Orange");
                }
                return;
            }
            if (_autoReconnectStage == AutoReconnectStage.WaitForDisconnectButtonUnlock
                && now < _nextAutoReconnectActionAtUtc)
            {
                int remainingSeconds = Math.Max(1, (int)Math.Ceiling((_nextAutoReconnectActionAtUtc - now).TotalSeconds));
                if (remainingSeconds != _autoReconnectLastCountdownSecond)
                {
                    _autoReconnectLastCountdownSecond = remainingSeconds;
                    string action = _autoReconnectPendingScreenKind == AutoReconnectScreenKind.ReconnectChoice
                        ? "Reconnect"
                        : "Back to Server List";
                    UpdateAutoReconnectStatus($"Przycisk {action} może być zablokowany. Próba za {remainingSeconds} s...", "Orange");
                }
                return;
            }
            if (now < _nextAutoReconnectActionAtUtc)
                return;

            _autoReconnectTickInProgress = true;
            int generation = _autoReconnectGeneration;
            IntPtr targetWindow = _targetGameWindowHandle;
            try
            {
                switch (_autoReconnectStage)
                {
                    case AutoReconnectStage.EmergencyWaitBeforeReconnect:
                        _autoReconnectLastCountdownSecond = -1;
                        _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                        _nextAutoReconnectActionAtUtc = now;
                        UpdateAutoReconnectStatus("Minął czas oczekiwania. Rozpoznaję ekran i rozpoczynam reconnect...", "Orange");
                        break;

                    case AutoReconnectStage.HealthOpenInventory:
                        PauseMiningForAutoReconnect();
                        SendKeyTap(VK_E);
                        _autoReconnectStage = AutoReconnectStage.HealthVerifyInventory;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(650);
                        UpdateAutoReconnectStatus("Kontrola: sprawdzam znacznik otwartego EQ...", "Orange");
                        break;

                    case AutoReconnectStage.HealthVerifyInventory:
                        var healthScan = await InspectOpenMinecraftInventoryAsync();
                        if (!IsAutoReconnectResultCurrent(generation, AutoReconnectStage.HealthVerifyInventory, targetWindow))
                            return;
                        if (healthScan.Found)
                        {
                            int detectedScale = healthScan.Scale;
                            bool diamondPickaxePresent = healthScan.PickaxePresent;
                            SendKeyTap(VK_E);
                            if (_autoReconnectInventoryOnly)
                            {
                                StopAutoReconnect(
                                    diamondPickaxePresent
                                        ? $"Test EQ OK: wykryto EQ (GUI x{detectedScale}) oraz znacznik diamentowego kilofa."
                                        : $"Test EQ: wykryto EQ (GUI x{detectedScale}), ale nie znaleziono znacznika diamentowego kilofa.",
                                    resumeMining: true,
                                    warning: !diamondPickaxePresent);
                                break;
                            }

                            StopAutoReconnect($"Kontrola OK: wykryto EQ (GUI x{detectedScale}). Kopanie wznowione.", resumeMining: true, warning: false);
                            break;
                        }

                        _autoReconnectInventoryFailures++;
                        SendKeyTap(VK_ESCAPE);
                        if (_autoReconnectInventoryOnly)
                        {
                            StopAutoReconnect("Test EQ nieudany: nie wykryto znaczników paczki Minecraft Helper.", resumeMining: true, warning: true);
                            break;
                        }
                        if (_autoReconnectInventoryFailures < 3)
                        {
                            _autoReconnectStage = AutoReconnectStage.HealthOpenInventory;
                            _nextAutoReconnectActionAtUtc = now.AddMilliseconds(450);
                            UpdateAutoReconnectStatus($"Kontrola EQ: próba {_autoReconnectInventoryFailures + 1}/3...", "Orange");
                            break;
                        }

                        _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(350);
                        UpdateAutoReconnectStatus("Brak potwierdzenia EQ. Uruchamiam procedurę odzyskiwania połączenia...", "Orange");
                        break;

                    case AutoReconnectStage.AnalyzeScreen:
                        await AnalyzeAndHandleAutoReconnectScreenAsync();
                        break;

                    case AutoReconnectStage.WaitForDisconnectButtonUnlock:
                    {
                        // GuiDisconnected 1.8.8: przycisk pod powodem rozłączenia, środek ok. 24 punkty GUI
                        // pod połową wysokości — w pikselach przy faktycznej skali okna (854×480 → x2).
                        double buttonY = 0.59;
                        if (TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT disconnectRect))
                        {
                            int disconnectHeight = disconnectRect.Bottom - disconnectRect.Top;
                            int disconnectScale = GetMinecraftGuiScale(disconnectRect.Right - disconnectRect.Left, disconnectHeight);
                            int scaledHeight = (int)Math.Ceiling(disconnectHeight / (double)disconnectScale);
                            buttonY = (scaledHeight / 2 + 24) / (double)Math.Max(1, scaledHeight);
                        }
                        if (!TryClickClientPoint(0.5, buttonY, bottomOffset: null))
                        {
                            StopAutoReconnect("Nie udało się kliknąć przycisku ekranu rozłączenia.", resumeMining: false, warning: true);
                            break;
                        }
                        _autoReconnectDisconnectScreenSinceUtc = DateTime.MinValue;
                    }

                        if (_autoReconnectPendingScreenKind == AutoReconnectScreenKind.ReconnectChoice)
                        {
                            _autoReconnectAttempt++;
                            _autoReconnectStage = AutoReconnectStage.WaitForServerJoin;
                            _nextAutoReconnectActionAtUtc = now.AddSeconds(Math.Max(AutoReconnectBlockedButtonWaitSeconds, _settings.AutoReconnectJoinDelaySeconds));
                            UpdateAutoReconnectStatus($"Kliknięto Reconnect — próba {_autoReconnectAttempt}/{_settings.AutoReconnectMaxAttempts}...", "Orange");
                        }
                        else
                        {
                            bool proxyCooldown = _autoReconnectPendingScreenKind == AutoReconnectScreenKind.AlreadyConnected;
                            // Po powrocie na listę serwerów: F5 (odświeżenie), chwila przerwy i dopiero dołączanie.
                            _autoReconnectRefreshServerList = true;
                            _autoReconnectStage = AutoReconnectStage.WaitAfterScreenClick;
                            _nextAutoReconnectActionAtUtc = now.AddMilliseconds(proxyCooldown ? 2500 : 800);
                            UpdateAutoReconnectStatus(proxyCooldown
                                ? "Kliknięto powrót. Proxy nadal może zwalniać poprzednią sesję..."
                                : "Kliknięto Back to Server List. Czekam na listę serwerów...", "Orange");
                        }
                        _autoReconnectPendingScreenKind = AutoReconnectScreenKind.Unknown;
                        _autoReconnectLastCountdownSecond = -1;
                        break;

                    case AutoReconnectStage.WaitAfterScreenClick:
                        _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                        _nextAutoReconnectActionAtUtc = now;
                        break;

                    case AutoReconnectStage.OpenDirectConnect:
                        // Minecraft 1.8.8: środek przycisku Direct Connect jest 42 punkty GUI nad
                        // dolną krawędzią — w pikselach razy faktyczna skala (854×480 → x2, nie x3).
                        if (!TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT directConnectRect)
                            || !TryClickClientPoint(0.5, null, bottomOffset: 42 * GetMinecraftGuiScale(
                                directConnectRect.Right - directConnectRect.Left,
                                directConnectRect.Bottom - directConnectRect.Top)))
                        {
                            StopAutoReconnect("Nie udało się kliknąć Direct Connect.", resumeMining: false, warning: true);
                            break;
                        }
                        _autoReconnectStage = AutoReconnectStage.WaitForDirectConnect;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(700);
                        UpdateAutoReconnectStatus("Otwieram Direct Connect...", "Orange");
                        break;

                    case AutoReconnectStage.WaitForDirectConnect:
                        _autoReconnectStage = AutoReconnectStage.EnterServerAddress;
                        _nextAutoReconnectActionAtUtc = now;
                        break;

                    case AutoReconnectStage.EnterServerAddress:
                        if (!TryInspectDirectConnectAddressField(out bool addressAlreadyEntered, out string recognizedAddress))
                        {
                            StopAutoReconnect("Nie udało się sprawdzić pola adresu na ekranie Direct Connect.", resumeMining: false, warning: true);
                            break;
                        }

                        if (!addressAlreadyEntered)
                        {
                            if (string.IsNullOrWhiteSpace(_settings.AutoReconnectServerAddress))
                            {
                                StopAutoReconnect("Pole adresu jest puste, a w konfiguracji nie podano adresu serwera.", resumeMining: false, warning: true);
                                break;
                            }
                            if (!TryClickDirectConnectAddressField())
                            {
                                StopAutoReconnect("Nie udało się aktywować pola adresu serwera.", resumeMining: false, warning: true);
                                break;
                            }
                            SendKeyDown(VK_CONTROL);
                            SendKeyTap(VK_A);
                            SendKeyUp(VK_CONTROL);
                            if (!SendTextByKeyboard(_settings.AutoReconnectServerAddress))
                            {
                                StopAutoReconnect("Nie udało się wpisać adresu serwera.", resumeMining: false, warning: true);
                                break;
                            }
                        }

                        SendKeyTap(VK_RETURN);
                        _autoReconnectAttempt++;
                        _autoReconnectStage = AutoReconnectStage.WaitForServerJoin;
                        _nextAutoReconnectActionAtUtc = now.AddSeconds(_settings.AutoReconnectJoinDelaySeconds);
                        string addressAction = addressAlreadyEntered
                            ? string.IsNullOrWhiteSpace(recognizedAddress)
                                ? "Adres był już wpisany"
                                : $"Adres był już wpisany ({recognizedAddress})"
                            : $"Wpisano {_settings.AutoReconnectServerAddress}";
                        UpdateAutoReconnectStatus($"{addressAction}; zatwierdzono Enterem — próba {_autoReconnectAttempt}/{_settings.AutoReconnectMaxAttempts}...", "Orange");
                        break;

                    case AutoReconnectStage.WaitForServerJoin:
                        if (BackgroundGameWindow.IsActive)
                        {
                            // In the background the Windows cursor belongs to another
                            // desktop. OCR decides: a known menu screen means the join
                            // failed, an unrecognised screen means the game world.
                            _autoReconnectJoinCheckByScreen = true;
                            _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                            _nextAutoReconnectActionAtUtc = now;
                            UpdateAutoReconnectStatus("Kopanie w tle: sprawdzam ekran po czasie oczekiwania na wejście...", "Orange");
                            break;
                        }
                        if (IsInventoryCursorVisible())
                        {
                            if (_autoReconnectAttempt >= _settings.AutoReconnectMaxAttempts)
                            {
                                StopAutoReconnect($"Nie potwierdzono wejścia do gry po {_autoReconnectAttempt} próbach.", resumeMining: false, warning: true);
                                break;
                            }
                            _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                            _nextAutoReconnectActionAtUtc = now;
                            UpdateAutoReconnectStatus("Po czasie oczekiwania nadal widać ekran GUI. Sprawdzam jego typ...", "Orange");
                            break;
                        }
                        ContinueAfterServerJoin(now);
                        break;

                    case AutoReconnectStage.EmergencyOpenInventory:
                        SendKeyTap(VK_E);
                        _autoReconnectStage = AutoReconnectStage.EmergencyVerifyInventory;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(700);
                        UpdateAutoReconnectStatus("Sprawdzam EQ i obecność diamentowego kilofa...", "Orange");
                        break;

                    case AutoReconnectStage.EmergencyVerifyInventory:
                        var emergencyScan = await InspectOpenMinecraftInventoryAsync();
                        if (!IsAutoReconnectResultCurrent(generation, AutoReconnectStage.EmergencyVerifyInventory, targetWindow))
                            return;

                        if (!emergencyScan.Found)
                        {
                            SendKeyTap(VK_ESCAPE);
                            _emergencyReconnectInventoryAttempts++;
                            if (_emergencyReconnectInventoryAttempts < 3)
                            {
                                _autoReconnectStage = AutoReconnectStage.EmergencyOpenInventory;
                                _nextAutoReconnectActionAtUtc = now.AddMilliseconds(500);
                                UpdateAutoReconnectStatus(
                                    $"Nie potwierdzono otwartego EQ. Ponawiam próbę {_emergencyReconnectInventoryAttempts + 1}/3...",
                                    "Orange");
                                break;
                            }

                            StopAutoReconnect(
                                "Awaryjny reconnect zatrzymany: po 3 próbach nie wykryto znaczników EQ. Nie wysłano żadnej komendy /home.",
                                resumeMining: false,
                                warning: true);
                            break;
                        }

                        if (emergencyScan.PickaxePresent)
                        {
                            SendKeyTap(VK_E);
                            _emergencyReconnectInventoryAttempts = 0;
                            _emergencyReconnectSoundGuardActive = false;
                            StopEmergencyDamageSoundListening(updateStatus: false);
                            ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: false);
                            RecordAutomationLogEvent(
                                MiningLogEventTypes.EmergencyPickaxeDetected,
                                MiningLogStatuses.Completed,
                                $"Wykryto diamentowy kilof po {_emergencyReconnectInventoryAttempts + 1} odczytach EQ (GUI x{emergencyScan.Scale}). Wybrano podstawowy home: {_autoReconnectActiveHomeCommand}.");
                            _emergencyReconnectShutdownAfterHome = false;
                            _autoReconnectReturningHomeAfterMissingPickaxe = false;
                            _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
                            _nextAutoReconnectActionAtUtc = now.AddMilliseconds(350);
                            UpdateAutoReconnectStatus(
                                $"Wykryto diamentowy kilof (GUI x{emergencyScan.Scale}). Wysyłam {_autoReconnectActiveHomeCommand}, a po teleporcie wznowię Kopacza.",
                                "Green");
                            break;
                        }

                        _emergencyReconnectInventoryAttempts++;
                        const int emergencyPickaxeScanLimit = 5;
                        if (_emergencyReconnectInventoryAttempts < emergencyPickaxeScanLimit)
                        {
                            _autoReconnectStage = AutoReconnectStage.EmergencyVerifyInventory;
                            _nextAutoReconnectActionAtUtc = now.AddMilliseconds(320);
                            UpdateAutoReconnectStatus(
                                $"Nie odczytano jeszcze znacznika kilofa. Potwierdzam wynik {_emergencyReconnectInventoryAttempts + 1}/{emergencyPickaxeScanLimit}...",
                                "Orange");
                            break;
                        }

                        SendKeyTap(VK_E);
                        _emergencyReconnectSoundGuardActive = false;
                        StopEmergencyDamageSoundListening(updateStatus: false);
                        if (!emergencyScan.HasGuiMarkers)
                        {
                            StopAutoReconnect(
                                "Nie potwierdzono znaczników aktualnej paczki Minecraft Helper. Nie uznaję tego za brak kilofa i nie wykonuję awaryjnego /home.",
                                resumeMining: false,
                                warning: true);
                            break;
                        }

                        AutoReconnectServerProfile? emergencyProfile = GetSelectedAutoReconnectServerProfile();
                        if (emergencyProfile?.MissingPickaxeRecoveryEnabled != true)
                        {
                            StopAutoReconnect(
                                "Nie wykryto diamentowego kilofa, ale w wybranym profilu nie włączono awaryjnego home. Kopacz pozostaje wyłączony.",
                                resumeMining: false,
                                warning: true);
                            break;
                        }

                        ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: true);
                        _emergencyReconnectShutdownAfterHome = true;
                        _autoReconnectReturningHomeAfterMissingPickaxe = true;
                        RecordAutomationLogEvent(
                            MiningLogEventTypes.EmergencyPickaxeMissing,
                            MiningLogStatuses.Completed,
                            $"Po {emergencyPickaxeScanLimit} odczytach EQ nie wykryto diamentowego kilofa. Wybrano awaryjny home: {_autoReconnectActiveHomeCommand}; po teleporcie aplikacja zostanie zamknięta.");
                        _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(350);
                        UpdateAutoReconnectStatus(
                            $"Brak diamentowego kilofa. Wysyłam awaryjne {_autoReconnectActiveHomeCommand}; po teleporcie program zostanie zamknięty.",
                            "Red");
                        break;

                    case AutoReconnectStage.OpenHomeChat:
                        SendChatOpenKeyTap();
                        _autoReconnectStage = AutoReconnectStage.TypeHomeCommand;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(220);
                        UpdateAutoReconnectStatus($"Wysyłam {_autoReconnectActiveHomeCommand}...", "Orange");
                        break;

                    case AutoReconnectStage.TypeHomeCommand:
                        if (!ChatInputGate.IsReady(now))
                            break;
                        if (!SendTextByKeyboard(_autoReconnectActiveHomeCommand))
                        {
                            StopAutoReconnect("Nie udało się wpisać komendy domu.", resumeMining: false, warning: true);
                            break;
                        }
                        _autoReconnectStage = AutoReconnectStage.SubmitHomeCommand;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(120);
                        break;

                    case AutoReconnectStage.SubmitHomeCommand:
                        SendKeyTap(VK_RETURN);
                        if (_emergencyReconnectActive)
                        {
                            RecordAutomationLogEvent(
                                MiningLogEventTypes.EmergencyHomeCommandSent,
                                MiningLogStatuses.Completed,
                                $"Wysłano {_autoReconnectActiveHomeCommand}. Tryb: {(_emergencyReconnectShutdownAfterHome ? "awaryjny home bez kilofa" : "podstawowy home i wznowienie kopania")}. Po zatwierdzeniu home program odczeka {CalculateAutoReconnectTeleportWaitSeconds(_autoReconnectActiveTeleportDelaySeconds)} s przed następną akcją.");
                        }
                        if (_autoReconnectActiveHomeHasGui)
                        {
                            _autoReconnectStage = AutoReconnectStage.WaitForHomeMenu;
                            _nextAutoReconnectActionAtUtc = now.AddSeconds(_autoReconnectActiveHomeGuiDelaySeconds);
                            // Menu bywa wolniejsze niż czas z profilu — sprawdzamy je do tej chwili.
                            _autoReconnectHomeMenuDeadlineUtc = now.AddSeconds(Math.Max(_autoReconnectActiveHomeGuiDelaySeconds + 4, AutoReconnectHomeMenuMaxWaitSeconds));
                            UpdateAutoReconnectStatus($"Czekam na menu wyboru home (do {(int)(_autoReconnectHomeMenuDeadlineUtc - now).TotalSeconds} s)...", "Orange");
                        }
                        else
                        {
                            int teleportWaitSeconds = CalculateAutoReconnectTeleportWaitSeconds(
                                _autoReconnectActiveTeleportDelaySeconds);
                            _autoReconnectStage = AutoReconnectStage.WaitForTeleport;
                            _nextAutoReconnectActionAtUtc = now.AddSeconds(teleportWaitSeconds);
                            UpdateAutoReconnectStatus(
                                $"Czekam {teleportWaitSeconds} s na teleport ({_autoReconnectActiveTeleportDelaySeconds} s z profilu + {AutoReconnectTeleportSafetyBufferSeconds} s bezpieczeństwa)...",
                                "Orange");
                        }
                        break;

                    case AutoReconnectStage.WaitForHomeMenu:
                        _autoReconnectStage = AutoReconnectStage.ClickHomeSlot;
                        _nextAutoReconnectActionAtUtc = now;
                        break;

                    case AutoReconnectStage.ClickHomeSlot:
                        if (!IsGameGuiOpen(_autoReconnectActiveHomeGuiColumns))
                        {
                            // Jeszcze nie ma menu: sprawdzaj co 0,5 s do terminu, potem raz ponów komendę.
                            if (now < _autoReconnectHomeMenuDeadlineUtc)
                            {
                                _nextAutoReconnectActionAtUtc = now.AddMilliseconds(500);
                                break;
                            }
                            if (_autoReconnectHomeCommandRetries < AutoReconnectHomeCommandMaxRetries)
                            {
                                _autoReconnectHomeCommandRetries++;
                                _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
                                _nextAutoReconnectActionAtUtc = now.AddMilliseconds(500);
                                UpdateAutoReconnectStatus($"Menu home się nie otworzyło — ponawiam {_autoReconnectActiveHomeCommand} ({_autoReconnectHomeCommandRetries}/{AutoReconnectHomeCommandMaxRetries})...", "Orange");
                                break;
                            }
                            StopAutoReconnect("Nie wykryto otwartego menu wyboru home mimo ponowienia komendy. Zatrzymano bez klikania.", resumeMining: false, warning: true);
                            break;
                        }
                        if (!TryClickHomeMenuSlot(
                                _autoReconnectActiveHomeSlot,
                                _autoReconnectActiveHomeGuiRows,
                                _autoReconnectActiveHomeGuiColumns))
                        {
                            StopAutoReconnect("Nie udało się wyznaczyć pozycji slotu home.", resumeMining: false, warning: true);
                            break;
                        }
                        int teleportWaitSecondsAfterClick = CalculateAutoReconnectTeleportWaitSeconds(
                            _autoReconnectActiveTeleportDelaySeconds);
                        _autoReconnectStage = AutoReconnectStage.WaitForTeleport;
                        _nextAutoReconnectActionAtUtc = now.AddSeconds(teleportWaitSecondsAfterClick);
                        UpdateAutoReconnectStatus(
                            $"Kliknięto home w slocie {_autoReconnectActiveHomeSlot} układu {_autoReconnectActiveHomeGuiRows}×{_autoReconnectActiveHomeGuiColumns}. Czekam {teleportWaitSecondsAfterClick} s na teleport ({_autoReconnectActiveTeleportDelaySeconds} s + {AutoReconnectTeleportSafetyBufferSeconds} s bezpieczeństwa)...",
                            "Orange");
                        break;

                    case AutoReconnectStage.WaitForTeleport:
                        if (_emergencyReconnectActive)
                        {
                            if (_emergencyReconnectShutdownAfterHome)
                                CompleteEmergencyReconnectAndShutdown();
                            else
                                CompleteEmergencyReconnectAndResumeMining();
                            break;
                        }
                        _autoReconnectStage = AutoReconnectStage.OpenVerificationInventory;
                        _nextAutoReconnectActionAtUtc = now.AddSeconds(1);
                        UpdateAutoReconnectStatus("Teleport zakończony. Czekam dodatkową 1 s przed otwarciem EQ...", "Orange");
                        break;

                    case AutoReconnectStage.OpenVerificationInventory:
                        SendKeyTap(VK_E);
                        _autoReconnectStage = AutoReconnectStage.VerifyAfterTeleport;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(700);
                        UpdateAutoReconnectStatus("Weryfikuję EQ po teleportacji...", "Orange");
                        break;

                    case AutoReconnectStage.VerifyAfterTeleport:
                        var verificationScan = await InspectOpenMinecraftInventoryAsync();
                        if (!IsAutoReconnectResultCurrent(generation, AutoReconnectStage.VerifyAfterTeleport, targetWindow))
                            return;
                        if (verificationScan.Found)
                        {
                            int verificationScale = verificationScan.Scale;
                            SendKeyTap(VK_E);
                            if (_autoReconnectReturningHomeAfterMissingPickaxe)
                            {
                                StopAutoReconnect(
                                    $"Powrót do home zakończony: EQ potwierdzone (GUI x{verificationScale}). Kopanie pozostaje wyłączone.",
                                    resumeMining: false,
                                    warning: true);
                            }
                            else
                            {
                                // Test „Pełny cykl”: po udanym powrocie Kopacz ma ruszyć od zera.
                                InventoryCleanupOwner cycleOwner = TakeFullCycleTestOwner();
                                StopAutoReconnect($"Reconnect zakończony: EQ potwierdzone (GUI x{verificationScale}), kopanie wznowione.", resumeMining: true, warning: false);
                                if (cycleOwner != InventoryCleanupOwner.None)
                                    StartMiningAfterFullCycleTest(cycleOwner);
                            }
                            break;
                        }

                        SendKeyTap(VK_ESCAPE);
                        if (_autoReconnectAttempt >= _settings.AutoReconnectMaxAttempts)
                        {
                            StopAutoReconnect($"Reconnect zatrzymany po {_autoReconnectAttempt} próbach: brak potwierdzenia EQ.", resumeMining: false, warning: true);
                            break;
                        }
                        _autoReconnectStage = AutoReconnectStage.RetryDelay;
                        _nextAutoReconnectActionAtUtc = now.AddSeconds(3);
                        UpdateAutoReconnectStatus("Nie potwierdzono powrotu do gry. Ponawiam analizę za 3 s...", "Orange");
                        break;

                    case AutoReconnectStage.RetryDelay:
                        _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                        _nextAutoReconnectActionAtUtc = now;
                        break;
                }
            }
            catch (Exception ex)
            {
                if (generation == _autoReconnectGeneration)
                    StopAutoReconnect("Błąd auto reconnectu: " + ex.Message, resumeMining: false, warning: true);
            }
            finally
            {
                _autoReconnectTickInProgress = false;
            }
        }

        private bool IsAutoReconnectResultCurrent(int generation, AutoReconnectStage expectedStage, IntPtr targetWindow)
        {
            return generation == _autoReconnectGeneration
                && _autoReconnectStage == expectedStage
                && targetWindow != IntPtr.Zero
                && targetWindow == _targetGameWindowHandle
                && IsGameInputAvailableFor(targetWindow);
        }

        private async Task<(bool Found, int Scale, bool PickaxePresent, bool HasGuiMarkers)> InspectOpenMinecraftInventoryAsync()
        {
            if (!TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out _))
                return (false, 0, false, false);

            using (bitmap)
            {
                return await Task.Run(() =>
                {
                    if (!InventoryMarkerDetector.TryDetect(bitmap!, null, null, out InventoryMarkerDetection detection))
                        return (false, 0, false, false);
                    bool pickaxePresent = InventoryMarkerDetector.ContainsMarkedItem(
                        bitmap!, detection.Layout, "diamond_pickaxe", includeHotbar: true);
                    return (true, detection.Layout.Scale, pickaxePresent, detection.HasGuiMarkers);
                });
            }
        }

        private void BeginMissingPickaxeHomeRecovery(InventoryCleanupOwner owner, DateTime now)
        {
            if (_autoReconnectStage != AutoReconnectStage.None)
                return;

            ReadAutoReconnectSettingsFromUi();
            ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: true);
            _autoReconnectLogMiningRunId = GetMiningRunId(owner);
            _autoReconnectLogOwner = GetInventoryCleanupOwnerLabel(owner);
            RecordAutomationLogEvent(
                MiningLogEventTypes.MissingPickaxeRecovery,
                MiningLogStatuses.Completed,
                $"Brak znacznika diamentowego kilofa. Wysyłam {_autoReconnectActiveHomeCommand}; po powrocie kopanie pozostanie zakończone.");
            StopMinerRuntimeAfterMissingPickaxe(owner);
            _autoReconnectReturningHomeAfterMissingPickaxe = true;
            _autoReconnectManualRun = false;
            _autoReconnectInventoryOnly = false;
            _autoReconnectAttempt = 0;
            _autoReconnectMainMenuClicks = 0;
            _autoReconnectDisconnectScreenSinceUtc = DateTime.MinValue;
            _autoReconnectRefreshServerList = false;
            _autoReconnectHomeCommandRetries = 0;
            _autoReconnectJoinCheckByScreen = false;
            _autoReconnectInventoryFailures = 0;
            _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
            _nextAutoReconnectActionAtUtc = now.AddMilliseconds(350);
            UpdateAutoReconnectStatus(
                $"Auto EQ nie wykrył diamentowego kilofa. Wykonuję {_autoReconnectActiveHomeCommand} i kończę {GetInventoryCleanupOwnerLabel(owner)}...",
                "Red");
        }

        private void StopMinerRuntimeAfterMissingPickaxe(InventoryCleanupOwner owner)
        {
            if (owner == InventoryCleanupOwner.Kopacz533)
            {
                _kopacz533RuntimeEnabled = false;
                SetKopacz533MiningHold(false);
                ResetKopacz533RuntimeState();
                EndMiningLogRun(owner, "Kopanie zakończone: nie wykryto diamentowego kilofa.", MiningLogStatuses.Aborted, announcedSeparately: true);
            }
            else if (owner == InventoryCleanupOwner.Kopacz633)
            {
                _kopacz633RuntimeEnabled = false;
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetKopacz633RuntimeState();
                EndMiningLogRun(owner, "Kopanie zakończone: nie wykryto diamentowego kilofa.", MiningLogStatuses.Aborted, announcedSeparately: true);
            }

            _autoReconnectResumeKopacz533 = false;
            _autoReconnectResumeKopacz633 = false;
            RefreshOverlayHud(DateTime.UtcNow);
        }

        private bool TryInspectDirectConnectAddressField(out bool hasAddress, out string recognizedAddress)
        {
            hasAddress = false;
            recognizedAddress = string.Empty;
            if (!TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out _))
                return false;

            using (bitmap)
            {
                int guiScale = GetMinecraftGuiScale(bitmap!.Width, bitmap.Height);
                int fieldLeft = bitmap!.Width / 2 - 100 * guiScale;
                int fieldTop = 116 * guiScale;
                var textArea = new Drawing.Rectangle(
                    fieldLeft + 4 * guiScale,
                    fieldTop + 3 * guiScale,
                    192 * guiScale,
                    14 * guiScale);

                if (textArea.Left < 0 || textArea.Top < 0 ||
                    textArea.Right > bitmap.Width || textArea.Bottom > bitmap.Height)
                {
                    return false;
                }

                using Drawing.Bitmap addressCrop = bitmap.Clone(textArea, DrawingImaging.PixelFormat.Format32bppArgb);
                if (EnsureF3TesseractEngine())
                {
                    string rawText = RunOcrOnBitmap(addressCrop, TesseractPageSegMode.SingleLine);
                    recognizedAddress = Regex.Replace(rawText ?? string.Empty, @"\s+", string.Empty).Trim();
                    int recognizedCharacters = recognizedAddress.Count(char.IsLetterOrDigit);
                    if (recognizedCharacters >= 2)
                    {
                        hasAddress = true;
                        return true;
                    }
                }

                int brightPixels = 0;
                int firstBrightColumn = addressCrop.Width;
                int lastBrightColumn = -1;
                for (int x = 0; x < addressCrop.Width; x++)
                {
                    int columnPixels = 0;
                    for (int y = 0; y < addressCrop.Height; y++)
                    {
                        Drawing.Color pixel = addressCrop.GetPixel(x, y);
                        int brightest = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                        int darkest = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                        if (brightest >= 120 && brightest - darkest <= 55)
                            columnPixels++;
                    }

                    if (columnPixels >= 2)
                    {
                        brightPixels += columnPixels;
                        firstBrightColumn = Math.Min(firstBrightColumn, x);
                        lastBrightColumn = x;
                    }
                }

                int occupiedWidth = lastBrightColumn >= firstBrightColumn
                    ? lastBrightColumn - firstBrightColumn + 1
                    : 0;
                // Pojedynczy migający kursor zajmuje najwyżej szerokość jednego znaku.
                hasAddress = brightPixels >= 120 && occupiedWidth >= 24;
                return true;
            }
        }

        private bool TryClickDirectConnectAddressField()
        {
            if (!TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT rect))
                return false;

            int clientWidth = rect.Right - rect.Left;
            int clientHeight = rect.Bottom - rect.Top;
            int guiScale = GetMinecraftGuiScale(clientWidth, clientHeight);
            int x = rect.Left + clientWidth / 2;
            int y = rect.Top + (116 + 10) * guiScale;
            if (clientWidth <= 0 || clientHeight <= 0 || y < rect.Top || y >= rect.Bottom)
                return false;

            SetGameCursorPosition(x, y);
            SendMouseClick(leftButton: true, holdPulseMode: false);
            return true;
        }

        private async Task AnalyzeAndHandleAutoReconnectScreenAsync()
        {
            if (_autoReconnectOcrInProgress)
                return;
            if (!EnsureF3TesseractEngine())
            {
                StopAutoReconnect("Brak OCR (eng.traineddata). Nie można rozpoznać ekranu reconnectu.", resumeMining: false, warning: true);
                return;
            }
            if (!TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out Drawing.Rectangle clientArea))
            {
                StopAutoReconnect("Nie udało się przechwycić obrazu okna Minecrafta.", resumeMining: false, warning: true);
                return;
            }

            _autoReconnectOcrInProgress = true;
            _autoReconnectStage = AutoReconnectStage.WaitForScreenAnalysis;
            int generation = _autoReconnectGeneration;
            IntPtr targetWindow = _targetGameWindowHandle;
            try
            {
                using (bitmap)
                {
                    bool inventoryFound = await Task.Run(() => InventoryMarkerDetector.TryDetect(bitmap!, null, null, out _));
                    if (!IsAutoReconnectResultCurrent(generation, AutoReconnectStage.WaitForScreenAnalysis, targetWindow))
                        return;
                    if (inventoryFound)
                    {
                        HandleAutoReconnectScreen(AutoReconnectScreenKind.Inventory, clientArea);
                        return;
                    }

                    string text = await Task.Run(() =>
                    {
                        using Drawing.Bitmap prepared = PrepareBitmapForOcr(bitmap!);
                        string raw = RunOcrOnBitmap(bitmap!, TesseractPageSegMode.SparseText);
                        string enhanced = RunOcrOnBitmap(prepared, TesseractPageSegMode.SparseText);
                        return raw + Environment.NewLine + enhanced;
                    });
                    // A mining STOP can be handled while OCR is running. Never
                    // let a late result click a menu or restart the cancelled flow.
                    if (!IsAutoReconnectResultCurrent(generation, AutoReconnectStage.WaitForScreenAnalysis, targetWindow))
                        return;
                    _autoReconnectLastOcrText = text;
                    HandleAutoReconnectScreen(ClassifyAutoReconnectScreen(text), clientArea);
                }
            }
            finally
            {
                if (generation == _autoReconnectGeneration)
                {
                    _autoReconnectOcrInProgress = false;
                    // If focus/target changed during the await, retry only when
                    // the game is active again instead of staying in WAIT forever.
                    if (_autoReconnectStage == AutoReconnectStage.WaitForScreenAnalysis)
                    {
                        _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                        _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
                    }
                }
            }
        }

        private static AutoReconnectScreenKind ClassifyAutoReconnectScreen(string? text)
        {
            // OCR często zostawia nawiasy/tagi serwera (np. "[MH] DIRECT CONNECT")
            // albo rozdziela wyrazy znakami. Do klasyfikacji liczą się same tokeny.
            string normalized = Regex.Replace((text ?? string.Empty).ToUpperInvariant(), @"\s+", " ").Trim();
            string tokens = Regex.Replace(normalized, @"[^A-Z0-9]+", " ").Trim();
            if (normalized.Contains("BANNED") || normalized.Contains(" BAN ") || normalized.Contains("ZABLOKOW") || normalized.Contains("BAN ENDS"))
                return AutoReconnectScreenKind.Banned;
            if (normalized.Contains("ALREADY CONNECTED") || normalized.Contains("POLACZENIE Z PROXY") || normalized.Contains("POŁĄCZENIE Z PROXY"))
                return AutoReconnectScreenKind.AlreadyConnected;
            if (normalized.Contains("PLAYER DEAD") || normalized.Contains("GAME OVER") || normalized.Contains("RESPAWN"))
                return AutoReconnectScreenKind.PlayerDead;
            if (normalized.Contains("RECONNECT"))
                return AutoReconnectScreenKind.ReconnectChoice;
            if (normalized.Contains("CONNECTION FAILED") || normalized.Contains("CONNECTION LOST") || normalized.Contains("KICKED") || normalized.Contains("LOGIN FAILED") || normalized.Contains("FAILED TO CONNECT") || normalized.Contains("DISCONNECTED"))
                return AutoReconnectScreenKind.Disconnected;
            if (normalized.Contains("SERVER ADDRESS"))
                return AutoReconnectScreenKind.DirectConnect;
            // Menu główne też ma napis „Multiplayer” — musi być rozpoznane PRZED listą serwerów,
            // inaczej kliknięcie „Direct Connect” przy dole trafiłoby w okolice „Quit Game”.
            bool serverListOnly = tokens.Contains("JOIN SERVER") || (tokens.Contains("DIRECT") && tokens.Contains("CONNECT")) || normalized.Contains("SERVER LIST") || tokens.Contains("ADD SERVER") || tokens.Contains("REFRESH");
            bool mainMenu = tokens.Contains("SINGLEPLAYER") || tokens.Contains("SINGLE PLAYER") || tokens.Contains("QUIT GAME")
                || tokens.Contains("JEDNOOSOBOWA") || tokens.Contains("WIELOOSOBOWA") || (tokens.Contains("COPYRIGHT") && tokens.Contains("MOJANG"));
            if (mainMenu && !serverListOnly)
                return AutoReconnectScreenKind.MainMenu;
            bool hasMultiplayerScreen = tokens.Contains("MULTIPLAYER")
                || (tokens.Contains("PLAY") && tokens.Contains("MULTI"));
            bool hasDirectConnectButton = tokens.Contains("DIRECT") && tokens.Contains("CONNECT");
            if (hasMultiplayerScreen
                || hasDirectConnectButton
                || normalized.Contains("SERVER LIST")
                || normalized.Contains("JOIN SERVER"))
                return AutoReconnectScreenKind.ServerList;
            return AutoReconnectScreenKind.Unknown;
        }

        private void HandleAutoReconnectScreen(AutoReconnectScreenKind kind, Drawing.Rectangle clientArea)
        {
            DateTime now = DateTime.UtcNow;
            // Licznik 10 s ekranu rozłączenia biegnie tylko, dopóki widać ten sam ekran.
            if (kind is not (AutoReconnectScreenKind.Disconnected or AutoReconnectScreenKind.AlreadyConnected or AutoReconnectScreenKind.ReconnectChoice))
                _autoReconnectDisconnectScreenSinceUtc = DateTime.MinValue;
            bool joinCheckByScreen = _autoReconnectJoinCheckByScreen;
            _autoReconnectJoinCheckByScreen = false;
            if (joinCheckByScreen)
            {
                if (kind == AutoReconnectScreenKind.Unknown)
                {
                    ContinueAfterServerJoin(now);
                    return;
                }
                if (kind != AutoReconnectScreenKind.Inventory && _autoReconnectAttempt >= _settings.AutoReconnectMaxAttempts)
                {
                    StopAutoReconnect($"Nie potwierdzono wejścia do gry po {_autoReconnectAttempt} próbach.", resumeMining: false, warning: true);
                    return;
                }
            }

            switch (kind)
            {
                case AutoReconnectScreenKind.Inventory:
                    if (_emergencyReconnectActive)
                    {
                        _autoReconnectStage = AutoReconnectStage.EmergencyVerifyInventory;
                        _nextAutoReconnectActionAtUtc = now;
                        UpdateAutoReconnectStatus("Wykryto otwarte EQ. Sprawdzam diamentowy kilof...", "Orange");
                    }
                    else
                    {
                        SendKeyTap(VK_E);
                        _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
                        _nextAutoReconnectActionAtUtc = now.AddMilliseconds(350);
                        UpdateAutoReconnectStatus("Wykryto otwarte EQ. Zamykam je i przechodzę do /home...", "Orange");
                    }
                    return;

                case AutoReconnectScreenKind.PlayerDead:
                    // Respawn w 1.8.8: środek przycisku 82 punkty GUI pod jedną czwartą wysokości.
                    int respawnScale = GetMinecraftGuiScale(clientArea.Width, clientArea.Height);
                    double respawnY = Math.Clamp((clientArea.Height / 4.0 + 82 * respawnScale) / Math.Max(1, clientArea.Height), 0.35, 0.58);
                    ClickClientPoint(clientArea, 0.5, respawnY);
                    _autoReconnectStage = AutoReconnectStage.WaitForServerJoin;
                    _nextAutoReconnectActionAtUtc = now.AddSeconds(Math.Max(3, _settings.AutoReconnectJoinDelaySeconds));
                    UpdateAutoReconnectStatus("Wykryto śmierć gracza. Kliknięto Respawn.", "Orange");
                    return;

                case AutoReconnectScreenKind.ReconnectChoice:
                    ScheduleBlockedDisconnectButtonClick(kind, now);
                    return;

                case AutoReconnectScreenKind.Disconnected:
                case AutoReconnectScreenKind.AlreadyConnected:
                    if (kind == AutoReconnectScreenKind.AlreadyConnected && _autoReconnectAttempt >= _settings.AutoReconnectMaxAttempts)
                    {
                        StopAutoReconnect("Serwer nadal zgłasza aktywne połączenie z proxy. Limit prób osiągnięty.", resumeMining: false, warning: true);
                        return;
                    }
                    ScheduleBlockedDisconnectButtonClick(kind, now);
                    return;

                case AutoReconnectScreenKind.MainMenu:
                {
                    if (++_autoReconnectMainMenuClicks > 3)
                    {
                        StopAutoReconnect("Nie udało się przejść z menu głównego do Multiplayer po 3 próbach.", resumeMining: false, warning: true);
                        return;
                    }
                    // GuiMainMenu 1.8.8: przyciski od height/4 + 48 co 24 punkty GUI; Multiplayer
                    // jest drugi, jego środek to height/4 + 82 (punkty GUI przy faktycznej skali okna).
                    int menuScale = GetMinecraftGuiScale(clientArea.Width, clientArea.Height);
                    int scaledHeight = (int)Math.Ceiling(clientArea.Height / (double)menuScale);
                    double multiplayerY = (scaledHeight / 4 + 82) / (double)scaledHeight;
                    ClickClientPoint(clientArea, 0.5, multiplayerY);
                    _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
                    _nextAutoReconnectActionAtUtc = now.AddMilliseconds(1200);
                    UpdateAutoReconnectStatus("Wykryto menu główne. Klikam Multiplayer...", "Orange");
                    return;
                }

                case AutoReconnectScreenKind.ServerList:
                    _autoReconnectMainMenuClicks = 0;
                    if (_autoReconnectRefreshServerList)
                    {
                        // Po wyrzuceniu z serwera: odśwież listę (F5) i odczekaj, zanim spróbujesz wejść ponownie.
                        _autoReconnectRefreshServerList = false;
                        SendKeyTap(VK_F5);
                        _autoReconnectStage = AutoReconnectStage.OpenDirectConnect;
                        _nextAutoReconnectActionAtUtc = now.AddSeconds(AutoReconnectServerListRefreshSeconds);
                        UpdateAutoReconnectStatus($"Lista serwerów: odświeżam (F5) i czekam {AutoReconnectServerListRefreshSeconds} s przed dołączeniem...", "Orange");
                        return;
                    }
                    _autoReconnectStage = AutoReconnectStage.OpenDirectConnect;
                    _nextAutoReconnectActionAtUtc = now.AddMilliseconds(250);
                    UpdateAutoReconnectStatus("Wykryto listę serwerów. Otwieram Direct Connect...", "Orange");
                    return;

                case AutoReconnectScreenKind.DirectConnect:
                    _autoReconnectStage = AutoReconnectStage.EnterServerAddress;
                    _nextAutoReconnectActionAtUtc = now;
                    return;

                case AutoReconnectScreenKind.Banned:
                    StopAutoReconnect("Wykryto komunikat o banie/blokadzie. Automat nie będzie ponawiał połączenia.", resumeMining: false, warning: true);
                    return;

                default:
                    string preview = Regex.Replace(_autoReconnectLastOcrText ?? string.Empty, @"\s+", " ").Trim();
                    if (preview.Length > 150)
                        preview = preview.Substring(0, 150) + "...";
                    StopAutoReconnect("Nieznany ekran — zatrzymano bez klikania." + (string.IsNullOrWhiteSpace(preview) ? string.Empty : " OCR: " + preview), resumeMining: false, warning: true);
                    return;
            }
        }

        // Ekran rozłączenia klikamy dopiero, gdy jest widoczny nieprzerwanie 10 s: po tym czasie
        // ekran jest sprawdzany ponownie i dopiero ten sam ekran dostaje kliknięcie (przycisk bywa
        // na początku zablokowany, a klient potrafi sam przejść dalej — wtedy nie klikamy na ślepo).
        private void ScheduleBlockedDisconnectButtonClick(AutoReconnectScreenKind kind, DateTime now)
        {
            if (_autoReconnectDisconnectScreenSinceUtc == DateTime.MinValue || _autoReconnectPendingScreenKind != kind)
                _autoReconnectDisconnectScreenSinceUtc = now;
            _autoReconnectPendingScreenKind = kind;
            string action = kind == AutoReconnectScreenKind.ReconnectChoice
                ? "Reconnect"
                : "Back to Server List";

            DateTime clickAt = _autoReconnectDisconnectScreenSinceUtc.AddSeconds(AutoReconnectBlockedButtonWaitSeconds);
            if (now >= clickAt)
            {
                _autoReconnectStage = AutoReconnectStage.WaitForDisconnectButtonUnlock;
                _nextAutoReconnectActionAtUtc = now;
                UpdateAutoReconnectStatus($"Ekran rozłączenia widoczny od {AutoReconnectBlockedButtonWaitSeconds} s — klikam {action}...", "Orange");
                return;
            }

            int remaining = (int)Math.Ceiling((clickAt - now).TotalSeconds);
            _autoReconnectLastCountdownSecond = remaining;
            _autoReconnectStage = AutoReconnectStage.AnalyzeScreen;
            _nextAutoReconnectActionAtUtc = clickAt;
            UpdateAutoReconnectStatus(
                $"Wykryto ekran rozłączenia. Za {remaining} s sprawdzę, czy nadal jest widoczny — wtedy kliknę {action}...",
                "Orange");
        }

        private bool TryClickClientPoint(double xRatio, double? yRatio, int? bottomOffset)
        {
            if (!TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT rect))
                return false;
            var clientArea = new Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            double y = yRatio ?? Math.Clamp((clientArea.Height - (bottomOffset ?? 0)) / (double)Math.Max(1, clientArea.Height), 0.0, 1.0);
            ClickClientPoint(clientArea, xRatio, y);
            return true;
        }

        private void ClickClientPoint(Drawing.Rectangle clientArea, double xRatio, double yRatio)
        {
            int x = clientArea.Left + (int)Math.Round(clientArea.Width * Math.Clamp(xRatio, 0.0, 1.0));
            int y = clientArea.Top + (int)Math.Round(clientArea.Height * Math.Clamp(yRatio, 0.0, 1.0));
            SetGameCursorPosition(x, y);
            SendMouseClick(leftButton: true, holdPulseMode: false);
        }

        private bool TryClickHomeMenuSlot(int oneBasedSlot, int rows, int columns)
        {
            if (!TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT rect))
                return false;

            const int guiWidth = 176;
            rows = Math.Clamp(rows, 1, 6);
            columns = Math.Clamp(columns, 1, 9);
            int guiHeight = 114 + rows * 18;
            oneBasedSlot = Math.Clamp(oneBasedSlot, 1, rows * columns);
            int slot = oneBasedSlot - 1;
            int column = slot % columns;
            int row = slot / columns;
            int clientWidth = rect.Right - rect.Left;
            int clientHeight = rect.Bottom - rect.Top;
            // Jak GuiContainer w 1.8.8: środek liczony w punktach GUI przy faktycznej skali okna.
            int guiScale = GetMinecraftGuiScale(clientWidth, clientHeight);
            int scaledWidth = (int)Math.Ceiling(clientWidth / (double)guiScale);
            int scaledHeight = (int)Math.Ceiling(clientHeight / (double)guiScale);
            int guiLeft = (scaledWidth - guiWidth) / 2;
            int guiTop = (scaledHeight - guiHeight) / 2;
            if (guiLeft < 0 || guiTop < 0)
                return false;

            int gridLeft = columns == 9 ? 8 : Math.Max(8, (guiWidth - columns * 18) / 2);
            int x = rect.Left + (guiLeft + gridLeft + column * 18 + 8) * guiScale;
            int y = rect.Top + (guiTop + 18 + row * 18 + 8) * guiScale;
            SetGameCursorPosition(x, y);
            SendMouseClick(leftButton: true, holdPulseMode: false);
            return true;
        }

        private async void BtnAutoReconnectRecognize_Click(object sender, RoutedEventArgs e)
        {
            if (!CanRunAutoReconnectTest(out string error))
            {
                UpdateAutoReconnectStatus(error, "Red");
                return;
            }
            // Rozpoznanie tylko patrzy na obraz — bierze go wprost z okna gry (jak podgląd Live),
            // więc nie trzeba przełączać się do Minecrafta ani czekać na odliczanie.
            if (IsIconic(_targetGameWindowHandle))
            {
                UpdateAutoReconnectStatus("Okno Minecrafta jest zminimalizowane — przywróć je (może być za innymi oknami) i spróbuj ponownie.", "Red");
                return;
            }
            if (!EnsureF3TesseractEngine())
            {
                UpdateAutoReconnectStatus("Nie udało się uruchomić OCR (brak eng.traineddata).", "Red");
                return;
            }
            if (!BackgroundGameWindow.TryCaptureClient(_targetGameWindowHandle, out Drawing.Bitmap? bitmap) || bitmap == null)
            {
                UpdateAutoReconnectStatus("Nie udało się pobrać obrazu z okna Minecrafta. Sprawdź wybrane okno w zakładce Okno gry.", "Red");
                return;
            }
            UpdateAutoReconnectStatus("Rozpoznaję ekran…", "Orange");

            using (bitmap)
            {
                if (InventoryMarkerDetector.TryDetect(bitmap!, null, null, out InventoryMarkerDetection inventory))
                {
                    UpdateAutoReconnectStatus($"Rozpoznano: otwarte EQ (GUI x{inventory.Layout.Scale}).", "Green");
                    return;
                }
                string text = await Task.Run(() => RunOcrOnBitmap(bitmap!, TesseractPageSegMode.SparseText));
                AutoReconnectScreenKind kind = ClassifyAutoReconnectScreen(text);
                string preview = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
                if (preview.Length > 180)
                    preview = preview.Substring(0, 180) + "...";
                UpdateAutoReconnectStatus($"Rozpoznano: {GetAutoReconnectScreenLabel(kind)}. OCR: {preview}", kind == AutoReconnectScreenKind.Unknown ? "Orange" : "Green");
            }
        }

        private static string GetAutoReconnectScreenLabel(AutoReconnectScreenKind kind)
        {
            return kind switch
            {
                AutoReconnectScreenKind.Inventory => "ekwipunek",
                AutoReconnectScreenKind.PlayerDead => "śmierć / Respawn",
                AutoReconnectScreenKind.Disconnected => "rozłączenie",
                AutoReconnectScreenKind.ReconnectChoice => "przycisk Reconnect",
                AutoReconnectScreenKind.ServerList => "lista serwerów",
                AutoReconnectScreenKind.DirectConnect => "Direct Connect",
                AutoReconnectScreenKind.Banned => "ban / blokada",
                AutoReconnectScreenKind.AlreadyConnected => "stare połączenie proxy",
                AutoReconnectScreenKind.MainMenu => "menu główne",
                _ => "nieznany ekran"
            };
        }

        private void BtnAutoReconnectCheckInventory_Click(object sender, RoutedEventArgs e)
        {
            if (!CanRunAutoReconnectTest(out string error))
            {
                UpdateAutoReconnectStatus(error, "Red");
                return;
            }
            BeginAutoReconnectHealthCheck(manual: true, inventoryOnly: true);
        }

        private void BtnAutoReconnectTestHome_Click(object sender, RoutedEventArgs e)
        {
            if (!CanRunAutoReconnectTest(out string error))
            {
                UpdateAutoReconnectStatus(error, "Red");
                return;
            }
            if (_autoReconnectStage != AutoReconnectStage.None)
                return;
            ReadAutoReconnectSettingsFromUi();
            ConfigureActiveAutoReconnectHome(missingPickaxeRecovery: false);
            _autoReconnectManualRun = true;
            _autoReconnectHomeCommandRetries = 0;
            CaptureMiningModeForAutoReconnect();
            PauseMiningForAutoReconnect();
            RecordAutomationLogEvent(
                MiningLogEventTypes.AutoReconnectStarted,
                MiningLogStatuses.Completed,
                $"Uruchomiono ręczny test powrotu do home. Komenda: {_autoReconnectActiveHomeCommand}; GUI: {(_autoReconnectActiveHomeHasGui ? "tak" : "nie")}.");
            _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
            _nextAutoReconnectActionAtUtc = DateTime.UtcNow;
            UpdateAutoReconnectStatus("Test /home uruchomiony.", "Orange");
        }

        private void BtnAutoReconnectStart_Click(object sender, RoutedEventArgs e)
        {
            if (!CanRunAutoReconnectTest(out string error))
            {
                UpdateAutoReconnectStatus(error, "Red");
                return;
            }
            BeginFullAutoReconnect(manual: true);
        }

        private void BtnAutoReconnectStop_Click(object sender, RoutedEventArgs e)
        {
            StopAutoReconnect("Auto reconnect zatrzymany ręcznie.", resumeMining: true, warning: false);
        }

        private void ChkAutoReconnectEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;
            if (ChkAutoReconnectEnabled.IsChecked != true
                && !_emergencyReconnectActive
                && _autoReconnectStage != AutoReconnectStage.None)
                StopAutoReconnect("Auto reconnect wyłączony.", resumeMining: true, warning: false);
            _nextAutoReconnectHealthCheckAtUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(ParseNonNegativeInt(TxtAutoReconnectWatchdogSeconds?.Text ?? string.Empty), 15, 3600));
            UpdateEnabledStates();
            MarkDirty();
        }

        private bool IsEmergencyReconnectEnabled()
        {
            return ChkEmergencyDamageSoundEnabled?.IsChecked == true
                && RbProtectionReturn?.IsChecked == true;
        }

        private bool IsPeriodicAutoReconnectEnabled()
        {
            return IsEmergencyReconnectEnabled()
                && ChkAutoReconnectEnabled?.IsChecked == true;
        }

        private void UpdateEmergencyProtectionOptionAvailability()
        {
            // These are saved preferences, not runtime arming indicators. Keep
            // them editable so an inactive, checked option can always be cleared.
            if (ChkEmergencyDamageSoundTestMode != null)
                ChkEmergencyDamageSoundTestMode.IsEnabled = true;
            if (ChkAutoReconnectEnabled != null)
                ChkAutoReconnectEnabled.IsEnabled = true;
            if (TxtAutoReconnectAvailability == null)
                return;

            bool active = IsPeriodicAutoReconnectEnabled();
            TxtAutoReconnectAvailability.Text = ChkAutoReconnectEnabled?.IsChecked != true
                ? "Dodatkowa kontrola jest wyłączona. Profile i testy ręczne pozostają dostępne."
                : ChkEmergencyDamageSoundEnabled?.IsChecked != true
                    ? "Wybór zapisany. Kontrola pozostaje nieaktywna, dopóki nie włączysz ochrony Kopacza."
                    : !IsEmergencyReconnectEnabled()
                        ? "Wybór zapisany, ale kontrola jest nieaktywna w trybie tylko Disconnect. Możesz odznaczyć ją niezależnie."
                        : "Kontrola jest włączona i będzie sprawdzać połączenie podczas cykli Auto EQ aktywnego Kopacza.";
            TxtAutoReconnectAvailability.Foreground = active
                ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                : new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193));
        }

        private void AutoReconnectSetting_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;
            MarkDirty();
        }

        private bool HasTestAutoFishingAreaConfigured()
        {
            return _settings.TestAutoFishingCaptureWidth >= MinimumCaptureSelectionSize
                && _settings.TestAutoFishingCaptureHeight >= MinimumCaptureSelectionSize;
        }

        private void UpdateTestAutoFishingAreaInfo()
        {
            if (TxtTestAutoFishingAreaInfo == null)
                return;

            TxtTestAutoFishingAreaInfo.Text = HasTestAutoFishingAreaConfigured()
                ? $"Obszar spławika: x={_settings.TestAutoFishingCaptureX}, y={_settings.TestAutoFishingCaptureY}, {_settings.TestAutoFishingCaptureWidth}x{_settings.TestAutoFishingCaptureHeight} (względem okna gry)."
                : "Brak obszaru monitoringu spławika. Kliknij \"Zaznacz obszar\".";
            RefreshTestAutoFishingPreview(DateTime.UtcNow, force: true);
        }

        private void RefreshTestAutoFishingPreview(DateTime now, bool force = false)
        {
            if (ImgTestAutoFishingPreview == null || TxtTestAutoFishingPreviewInfo == null)
                return;
            // Do not capture and convert preview frames while the app is hidden
            // or Minecraft owns the foreground. This UI-thread work is only
            // useful while the user is looking at the Narzędzia section.
            if (!IsVisible || !IsActive || _isMinimizedToTray || !IsGlassSectionActive(TabTools))
                return;
            if (!HasTestAutoFishingAreaConfigured())
            {
                ImgTestAutoFishingPreview.Source = null;
                TxtTestAutoFishingPreviewInfo.Text = "Brak zaznaczonego obszaru.";
                return;
            }
            if (_targetGameWindowHandle == IntPtr.Zero || !TryGetTestAutoFishingCaptureArea(_targetGameWindowHandle, out Drawing.Rectangle captureArea))
            {
                TxtTestAutoFishingPreviewInfo.Text = "Podgląd niedostępny. Uruchom i wybierz okno gry.";
                return;
            }
            if (!force && now < _nextTestAutoFishingPreviewAtUtc)
                return;

            _nextTestAutoFishingPreviewAtUtc = now.AddMilliseconds(TestAutoFishingPreviewIntervalMs);
            TxtTestAutoFishingPreviewInfo.Text = $"x={_settings.TestAutoFishingCaptureX}, y={_settings.TestAutoFishingCaptureY}, {_settings.TestAutoFishingCaptureWidth}x{_settings.TestAutoFishingCaptureHeight}";
            if (TryCaptureScreenRectBitmapSource(captureArea, out BitmapSource? bitmapSource))
                ImgTestAutoFishingPreview.Source = bitmapSource;
        }

        private static bool TryCaptureScreenRectBitmapSource(Drawing.Rectangle screenRect, out BitmapSource? bitmapSource)
        {
            bitmapSource = null;
            if (screenRect.Width < 2 || screenRect.Height < 2)
                return false;

            try
            {
                using Drawing.Bitmap bitmap = new Drawing.Bitmap(screenRect.Width, screenRect.Height, DrawingImaging.PixelFormat.Format32bppArgb);
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, screenRect.Size, Drawing.CopyPixelOperation.SourceCopy);

                IntPtr hbitmap = bitmap.GetHbitmap();
                try
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    bitmapSource = source;
                    return true;
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Obraz", "Nie udało się pobrać podglądu obszaru: " + ex.GetBaseException().Message);
                return false;
            }
        }

        private void UpdateTestAutoFishingStatusLabel()
        {
            if (TxtTestAutoFishingState == null)
                return;
            if (!_testAutoFishingRuntimeEnabled)
            {
                TxtTestAutoFishingState.Text = "-";
                return;
            }
            if (_testAutoFishingRepairStage != TestAutoFishingRepairStage.None)
            {
                TxtTestAutoFishingState.Text = "komenda: " + GetTestAutoFishingRepairStageLabel(_testAutoFishingRepairStage);
                return;
            }
            if (_testAutoFishingRecastAfterRepairPending)
            {
                TxtTestAutoFishingState.Text = "komenda: ponowne zarzucenie...";
                return;
            }
            if (_testAutoFishingAwaitSecondClick)
            {
                TxtTestAutoFishingState.Text = "PPM #2 (ponowne zarzucenie)";
                return;
            }
            if (_testAutoFishingWaitingForCastRegistration)
            {
                int remainingMs = Math.Max(0, (int)Math.Ceiling((_nextTestAutoFishingActionAtUtc - DateTime.UtcNow).TotalMilliseconds));
                TxtTestAutoFishingState.Text = $"po rzucie: rejestracja spławika za {remainingMs}ms";
                return;
            }
            if (_testAutoFishingBaselineReady && DateTime.UtcNow < _testAutoFishingBaselineArmedAtUtc)
            {
                int remainingMs = Math.Max(0, (int)Math.Ceiling((_testAutoFishingBaselineArmedAtUtc - DateTime.UtcNow).TotalMilliseconds));
                TxtTestAutoFishingState.Text = $"stabilizacja po rzucie... {remainingMs}ms";
                return;
            }
            if (_testAutoFishingBaselineReady && !double.IsNaN(_testAutoFishingLastDetectedBobberY))
            {
                TxtTestAutoFishingState.Text = $"monitoring (Y={(int)Math.Round(_testAutoFishingLastDetectedBobberY)}, baza={(int)Math.Round(_testAutoFishingBaselineBobberY)})";
                return;
            }
            if (_testAutoFishingMissedDetections > 0)
            {
                if (_testAutoFishingNoBobberSinceAtUtc != DateTime.MinValue)
                {
                    double remainingMs = TestAutoFishingNoBobberRecastMs - (DateTime.UtcNow - _testAutoFishingNoBobberSinceAtUtc).TotalMilliseconds;
                    TxtTestAutoFishingState.Text = $"szukanie spławika... ({_testAutoFishingMissedDetections}) | PPM za {Math.Max(0, (int)Math.Ceiling(remainingMs / 1000.0))}s";
                }
                else
                {
                    TxtTestAutoFishingState.Text = $"szukanie spławika... ({_testAutoFishingMissedDetections})";
                }
                return;
            }
            if (_nextTestAutoFishingRepairAtUtc != DateTime.MaxValue)
            {
                int remainingSeconds = Math.Max(0, (int)Math.Ceiling((_nextTestAutoFishingRepairAtUtc - DateTime.UtcNow).TotalSeconds));
                if (remainingSeconds > 0)
                {
                    TxtTestAutoFishingState.Text = $"szukanie spławika... | /repair za {remainingSeconds}s";
                    return;
                }
            }
            TxtTestAutoFishingState.Text = "szukanie spławika...";
        }

        private string GetConfiguredTestAutoFishingRepairCommand()
        {
            string command = TxtTestAutoFishingRepairCommand?.Text?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(command) ? command : (_settings.TestAutoFishingRepairCommand ?? string.Empty).Trim();
        }

        private int GetConfiguredTestAutoFishingRepairIntervalSeconds()
        {
            if (TxtTestAutoFishingRepairEverySeconds != null)
                return Math.Clamp(ParseNonNegativeInt(TxtTestAutoFishingRepairEverySeconds.Text), 0, TestAutoFishingRepairIntervalMaxSeconds);
            return Math.Clamp(_settings.TestAutoFishingRepairEverySeconds, 0, TestAutoFishingRepairIntervalMaxSeconds);
        }

        private bool HasConfiguredTestAutoFishingRepair()
        {
            return GetConfiguredTestAutoFishingRepairIntervalSeconds() > 0
                && !string.IsNullOrWhiteSpace(GetConfiguredTestAutoFishingRepairCommand());
        }

        private void ResetTestAutoFishingCatchStats()
        {
            _testAutoFishingCaughtCount = 0;
            _testAutoFishingLastCatchAtUtc = DateTime.MinValue;
        }

        private void RegisterTestAutoFishingCatch(DateTime now)
        {
            _testAutoFishingCaughtCount++;
            _testAutoFishingLastCatchAtUtc = now;
        }

        private static string GetTestAutoFishingRepairStageLabel(TestAutoFishingRepairStage stage)
        {
            return stage switch
            {
                TestAutoFishingRepairStage.OpenChat => "otwieranie chatu",
                TestAutoFishingRepairStage.TypeCommand => "wpisywanie komendy",
                TestAutoFishingRepairStage.SubmitCommand => "wysyłanie komendy",
                _ => "oczekiwanie"
            };
        }

        private static string GetBindTargetLabel(BindTarget target)
        {
            return target switch
            {
                BindTarget.Kopacz533 => "Kopacz 5/3/3",
                BindTarget.Kopacz633 => "Kopacz 6/3/3",
                BindTarget.TestAutoFishing => "Auto łowienie wędką",
                BindTarget.TestAutoFishingCaptureArea => "Auto łowienie (zaznaczanie obszaru)",
                BindTarget.ChatOpen => "otwieranie chatu",
                BindTarget.DropItem => "wyrzucanie przedmiotu",
                _ => "bind"
            };
        }

        private static string GetSaveButtonBaseContent(BindTarget target)
        {
            return "Zapisz";
        }

        private static bool IsMinecraftControlKeyTarget(BindTarget target)
        {
            return target is BindTarget.ChatOpen or BindTarget.DropItem;
        }

        private Button? GetBindSaveButton(BindTarget target)
        {
            return target switch
            {
                BindTarget.Kopacz533 => BtnKopacz533Capture,
                BindTarget.Kopacz633 => BtnKopacz633Capture,
                BindTarget.TestAutoFishing => BtnTestAutoFishingBind,
                BindTarget.TestAutoFishingCaptureArea => BtnTestAutoFishingCaptureBind,
                BindTarget.ChatOpen => BtnChatOpenKeySave,
                BindTarget.DropItem => BtnDropItemKeySave,
                _ => null
            };
        }

        private TextBox? GetBindTextBox(BindTarget target)
        {
            return target switch
            {
                BindTarget.Kopacz533 => TxtKopacz533Key,
                BindTarget.Kopacz633 => TxtKopacz633Key,
                BindTarget.TestAutoFishing => TxtTestAutoFishingBind,
                BindTarget.TestAutoFishingCaptureArea => TxtTestAutoFishingCaptureBind,
                BindTarget.ChatOpen => TxtChatOpenKey,
                BindTarget.DropItem => TxtDropItemKey,
                _ => null
            };
        }

        private static IEnumerable<BindTarget> GetAllBindTargets()
        {
            yield return BindTarget.Kopacz533;
            yield return BindTarget.Kopacz633;
            yield return BindTarget.TestAutoFishing;
            yield return BindTarget.TestAutoFishingCaptureArea;
            yield return BindTarget.ChatOpen;
            yield return BindTarget.DropItem;
        }

        private static string GetBindOwnerId(BindTarget target)
        {
            return target switch
            {
                BindTarget.Kopacz533 => "core:kopacz-533",
                BindTarget.Kopacz633 => "core:kopacz-633",
                BindTarget.TestAutoFishing => "core:test-auto-fishing",
                BindTarget.TestAutoFishingCaptureArea => "core:test-auto-fishing-capture",
                BindTarget.ChatOpen => "minecraft-control:chat-open",
                BindTarget.DropItem => "minecraft-control:drop-item",
                _ => "core:unknown"
            };
        }

        private static bool IsSameBindKey(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private bool TryFindBindConflict(string keyText, string ownerId, out string conflictOwnerLabel)
        {
            conflictOwnerLabel = string.Empty;
            if (string.IsNullOrWhiteSpace(keyText))
                return false;

            BindTarget[] fixedTargets =
            {
                BindTarget.Kopacz533,
                BindTarget.Kopacz633,
                BindTarget.TestAutoFishing,
                BindTarget.TestAutoFishingCaptureArea
            };

            for (int i = 0; i < fixedTargets.Length; i++)
            {
                BindTarget target = fixedTargets[i];
                string candidateOwnerId = GetBindOwnerId(target);
                if (string.Equals(candidateOwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
                    continue;

                TextBox? candidateTextBox = GetBindTextBox(target);
                string candidateKey = candidateTextBox?.Text ?? string.Empty;
                if (!IsSameBindKey(keyText, candidateKey))
                    continue;

                conflictOwnerLabel = GetBindTargetLabel(target);
                return true;
            }

            return false;
        }

        private void ShowBindConflict(string keyText, string conflictOwnerLabel, string requestedOwnerLabel)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(keyText) ? "?" : keyText.Trim();
            string conflictOwner = string.IsNullOrWhiteSpace(conflictOwnerLabel) ? "innej funkcji" : conflictOwnerLabel.Trim();
            string requestedOwner = string.IsNullOrWhiteSpace(requestedOwnerLabel) ? "tej funkcji" : requestedOwnerLabel.Trim();

            string shortMessage = $"Klawisz {normalizedKey} jest już przypisany do: {conflictOwner}.";
            UpdateStatusBar(shortMessage, "Orange");
            var dialog = new BindConflictDialogWindow(requestedOwner, normalizedKey, conflictOwner)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private void UpdateBindCaptureVisuals()
        {
            foreach (BindTarget target in GetAllBindTargets())
            {
                TextBox? bindBox = GetBindTextBox(target);
                if (bindBox == null)
                    continue;

                bool isCaptureActive = _bindCaptureTarget == target;
                bindBox.BorderBrush = isCaptureActive ? BindCaptureBorderBrush : BindIdleBorderBrush;
                bindBox.BorderThickness = isCaptureActive ? new Thickness(2) : new Thickness(1);
            }
        }

        private void RefreshBindSaveButton(BindTarget target)
        {
            Button? button = GetBindSaveButton(target);
            if (button == null)
                return;

            string baseContent = GetSaveButtonBaseContent(target);
            if (_pendingBindValues.TryGetValue(target, out string? pendingKey))
                button.Content = $"{baseContent} ({pendingKey})";
            else
                button.Content = baseContent;
        }

        private void RefreshBindSaveButtons()
        {
            RefreshBindSaveButton(BindTarget.Kopacz533);
            RefreshBindSaveButton(BindTarget.Kopacz633);
            RefreshBindSaveButton(BindTarget.TestAutoFishing);
            RefreshBindSaveButton(BindTarget.TestAutoFishingCaptureArea);
            RefreshBindSaveButton(BindTarget.ChatOpen);
            RefreshBindSaveButton(BindTarget.DropItem);
            UpdateBindCaptureVisuals();
        }

        private static string GetConfiguredBindLabel(string keyText)
        {
            return string.IsNullOrWhiteSpace(keyText) ? "Brak" : keyText.Trim();
        }

        private void RefreshOverlayHud(DateTime now)
        {
            bool hudEnabled = ChkOverlayHudEnabled?.IsChecked ?? _settings.OverlayHudEnabled;
            if (!hudEnabled)
            {
                if (_overlayHud?.IsVisible == true)
                    _overlayHud.UpdateEntries(Array.Empty<OverlayHudEntry>());
                return;
            }

            List<OverlayHudEntry> entries = BuildOverlayHudEntries(now);
            for (int i = 0; i < entries.Count; i++)
                entries[i] = entries[i] with { Title = $"{_accountDisplayName} · {entries[i].Title}" };
            if (entries.Count == 0)
            {
                if (_overlayHud?.IsVisible == true)
                    _overlayHud.UpdateEntries(entries);
                return;
            }

            bool overlayCreated = false;
            if (_overlayHud == null)
            {
                var overlay = new OverlayHudWindow();
                overlay.Closed += (_, __) =>
                {
                    if (ReferenceEquals(_overlayHud, overlay))
                        _overlayHud = null;
                };
                _overlayHud = overlay;
                overlayCreated = true;
            }

            // Never join Kopacz's UI input queue to Minecraft through HUD ownership.
            _overlayHud.SetOwnerWindowHandle(IntPtr.Zero);
            if (overlayCreated || now >= _nextOverlayLayoutRefreshAtUtc)
            {
                UpdateOverlayLayout();
                _nextOverlayLayoutRefreshAtUtc = now.Add(OverlayLayoutRefreshInterval);
            }
            _overlayHud.UpdateEntries(entries);
            if (_overlayHud.IsLayoutSuppressed && !_overlayLayoutWarningReported)
                UpdateStatusBar("Brak miejsca na HUD tego konta. Wybierz inny monitor dla overlayu w ustawieniach.", "Orange");
            _overlayLayoutWarningReported = _overlayHud.IsLayoutSuppressed;
        }

        private List<OverlayHudEntry> BuildOverlayHudEntries(DateTime now)
        {
            var entries = new List<OverlayHudEntry>();

            bool kop533ModeSelected = ChkKopacz533Enabled.IsChecked == true;
            bool kop633ModeSelected = ChkKopacz633Enabled.IsChecked == true;
            bool inventoryCleanupSelected = ChkInventoryCleanupEnabled.IsChecked == true;
            bool autoFishingModeSelected = ChkTestAutoFishingEnabled.IsChecked == true;
            bool emergencyProtectionSelected = ChkEmergencyDamageSoundEnabled?.IsChecked == true;

            bool emergencyProtectionArmedForActiveMiner = emergencyProtectionSelected
                && (_kopacz533RuntimeEnabled || _kopacz633RuntimeEnabled);
            if (emergencyProtectionArmedForActiveMiner
                || _emergencyReconnectActive
                || _emergencyDamageSoundManualTestActive
                || _autoReconnectStage != AutoReconnectStage.None)
            {
                entries.Add(BuildEmergencyProtectionOverlayEntry(now));
            }

            bool kop533Visible = kop533ModeSelected && (_kopacz533RuntimeEnabled || _kopacz533CommandStage != Kopacz533CommandStage.None || _kopacz533ResumeMiningPending);
            if (kop533Visible)
                entries.Add(BuildKopacz533OverlayEntry(now));

            bool kop633Visible = kop633ModeSelected && (_kopacz633RuntimeEnabled || _kopacz633CommandStage != Kopacz633CommandStage.None || _kopacz633ResumeMiningPending);
            if (kop633Visible)
                entries.Add(BuildKopacz633OverlayEntry(now));

            if (inventoryCleanupSelected
                && (kop533Visible || kop633Visible || _inventoryCleanupStage != InventoryCleanupStage.None))
            {
                entries.Add(BuildInventoryCleanupOverlayEntry(now));
            }

            if (autoFishingModeSelected && _testAutoFishingRuntimeEnabled)
                entries.Add(BuildTestAutoFishingOverlayEntry(now));

            return entries;
        }

        private OverlayHudEntry BuildEmergencyProtectionOverlayEntry(DateTime now)
        {
            AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
            string profileName = profile?.Name?.Trim() ?? "brak profilu";
            string owner = _emergencyReconnectResumeKopacz533 || _kopacz533RuntimeEnabled
                ? "Kopacz 5/3/3"
                : _emergencyReconnectResumeKopacz633 || _kopacz633RuntimeEnabled
                    ? "Kopacz 6/3/3"
                    : "oczekiwanie na Kopacza";

            string state;
            string nextAction;
            string detail;
            OverlayHudTone tone;
            if (_emergencyDamageSoundHandlingAlarm)
            {
                state = "ALARM — WYJŚCIE Z SERWERA";
                nextAction = IsEmergencyReconnectEnabled()
                    ? "Odliczanie do reconnectu"
                    : "Pozostanie poza serwerem — bez reconnectu";
                detail = "Zatrzymywanie Kopacza i wykonywanie ESC → Disconnect";
                tone = OverlayHudTone.Warning;
            }
            else if (_emergencyDamageSoundManualTestActive)
            {
                int seconds = Math.Max(0, (int)Math.Ceiling((_emergencyDamageSoundManualTestUntilUtc - now).TotalSeconds));
                state = "TEST NASŁUCHU";
                nextAction = "Po alarmie tylko wpis w logach";
                detail = $"Pozostało: {seconds} s • bez wychodzenia z serwera";
                tone = OverlayHudTone.Warning;
            }
            else if (_autoReconnectStage != AutoReconnectStage.None)
            {
                state = GetAutoReconnectOverlayStageLabel(_autoReconnectStage);
                nextAction = GetAutoReconnectOverlayNextAction(
                    _autoReconnectStage,
                    _emergencyReconnectShutdownAfterHome);
                if (_autoReconnectStage == AutoReconnectStage.EmergencyWaitBeforeReconnect)
                {
                    int seconds = Math.Max(0, (int)Math.Ceiling((_nextAutoReconnectActionAtUtc - now).TotalSeconds));
                    detail = $"Reconnect za: {seconds} s • nasłuch nadal aktywny";
                }
                else if (_autoReconnectStage == AutoReconnectStage.WaitForDisconnectButtonUnlock)
                {
                    int seconds = Math.Max(0, (int)Math.Ceiling((_nextAutoReconnectActionAtUtc - now).TotalSeconds));
                    detail = $"Kliknięcie możliwe za: {seconds} s";
                }
                else if (_autoReconnectStage == AutoReconnectStage.WaitForTeleport)
                {
                    int seconds = Math.Max(0, (int)Math.Ceiling((_nextAutoReconnectActionAtUtc - now).TotalSeconds));
                    detail = _emergencyReconnectShutdownAfterHome
                        ? $"Teleport i bezpieczne zamknięcie za: {seconds} s"
                        : $"Teleport i wznowienie Kopacza za: {seconds} s";
                }
                else if (_emergencyReconnectShutdownAfterHome)
                {
                    detail = "Brak kilofa → awaryjny home → zamknięcie programu";
                }
                else
                {
                    detail = _emergencyReconnectActive
                        ? "Kilof → wybrany home → slot 1 → wznowienie Kopacza"
                        : "Trwa operacja Auto Reconnect";
                }
                tone = OverlayHudTone.Warning;
            }
            else if (_damageSoundDetector.IsRunning)
            {
                bool testMode = _settings.EmergencyDamageSoundTestMode;
                state = testMode ? "NASŁUCH TESTOWY" : "OCHRONA UZBROJONA";
                nextAction = testMode
                    ? "Po alarmie tylko komunikat i log"
                    : IsEmergencyReconnectEnabled()
                        ? "Disconnect → reconnect → kontrola kilofa i home"
                        : "Tylko Disconnect — bez powrotu na serwer";
                detail = testMode
                    ? "Alarm zostanie zapisany bez wyjścia z serwera"
                    : "Oczekiwanie na dźwięk obrażeń";
                tone = testMode ? OverlayHudTone.Warning : OverlayHudTone.Active;
            }
            else
            {
                state = "GOTOWA";
                nextAction = owner == "oczekiwanie na Kopacza"
                    ? "Uruchom Kopacza, aby uzbroić nasłuch"
                    : "Uruchomienie nasłuchu obrażeń";
                detail = owner == "oczekiwanie na Kopacza"
                    ? "Nasłuch uruchomi się razem z Kopaczem"
                    : "Uruchamianie nasłuchu obrażeń";
                tone = OverlayHudTone.Active;
            }

            string body =
                $"Teraz: {state}\n" +
                $"Następnie: {nextAction}\n" +
                $"Tryb: {owner} • Profil: {profileName}\n" +
                detail;
            string title = _emergencyReconnectActive || ChkEmergencyDamageSoundEnabled?.IsChecked == true
                ? "AWARYJNA OCHRONA KOPACZA"
                : "AUTO RECONNECT";
            return new OverlayHudEntry(title, body, tone, Emphasize: _emergencyReconnectActive);
        }

        private static string GetAutoReconnectOverlayStageLabel(AutoReconnectStage stage)
        {
            return stage switch
            {
                AutoReconnectStage.EmergencyWaitBeforeReconnect => "OCZEKIWANIE NA RECONNECT",
                AutoReconnectStage.AnalyzeScreen or AutoReconnectStage.WaitForScreenAnalysis => "ROZPOZNAWANIE EKRANU",
                AutoReconnectStage.WaitForDisconnectButtonUnlock => "OCZEKIWANIE NA PRZYCISK",
                AutoReconnectStage.WaitAfterScreenClick => "POWRÓT DO LISTY SERWERÓW",
                AutoReconnectStage.OpenDirectConnect or AutoReconnectStage.WaitForDirectConnect => "OTWIERANIE DIRECT CONNECT",
                AutoReconnectStage.EnterServerAddress => "WPISYWANIE ADRESU SERWERA",
                AutoReconnectStage.WaitForServerJoin => "DOŁĄCZANIE DO SERWERA",
                AutoReconnectStage.EmergencyOpenInventory => "OTWIERANIE EQ",
                AutoReconnectStage.EmergencyVerifyInventory => "KONTROLA DIAMENTOWEGO KILOFA",
                AutoReconnectStage.OpenHomeChat or AutoReconnectStage.TypeHomeCommand or AutoReconnectStage.SubmitHomeCommand => "WYSYŁANIE KOMENDY HOME",
                AutoReconnectStage.WaitForHomeMenu => "OCZEKIWANIE NA MENU HOME",
                AutoReconnectStage.ClickHomeSlot => "WYBÓR HOME",
                AutoReconnectStage.WaitForTeleport => "TELEPORT NA HOME",
                AutoReconnectStage.HealthOpenInventory or AutoReconnectStage.HealthVerifyInventory => "KONTROLA POŁĄCZENIA I EQ",
                AutoReconnectStage.OpenVerificationInventory or AutoReconnectStage.VerifyAfterTeleport => "WERYFIKACJA EQ PO TELEPORCIE",
                AutoReconnectStage.RetryDelay => "PONOWIENIE RECONNECTU",
                _ => "AUTO RECONNECT"
            };
        }

        private static string GetAutoReconnectOverlayNextAction(
            AutoReconnectStage stage,
            bool shutdownAfterHome)
        {
            return stage switch
            {
                AutoReconnectStage.EmergencyWaitBeforeReconnect => "Rozpoznanie ekranu i wejście przez Direct Connect",
                AutoReconnectStage.AnalyzeScreen or AutoReconnectStage.WaitForScreenAnalysis => "Kliknięcie właściwego przycisku reconnectu",
                AutoReconnectStage.WaitForDisconnectButtonUnlock => "Kliknięcie Reconnect lub Back to Server List",
                AutoReconnectStage.WaitAfterScreenClick => "Otwarcie Direct Connect",
                AutoReconnectStage.OpenDirectConnect or AutoReconnectStage.WaitForDirectConnect => "Wpisanie adresu serwera",
                AutoReconnectStage.EnterServerAddress => "Dołączenie do serwera",
                AutoReconnectStage.WaitForServerJoin => "Otwarcie EQ i kontrola diamentowego kilofa",
                AutoReconnectStage.EmergencyOpenInventory => "Odczyt znaczników EQ",
                AutoReconnectStage.EmergencyVerifyInventory => "Wybór właściwego home na podstawie kilofa",
                AutoReconnectStage.OpenHomeChat or AutoReconnectStage.TypeHomeCommand or AutoReconnectStage.SubmitHomeCommand => "Otwarcie menu home lub rozpoczęcie oczekiwania",
                AutoReconnectStage.WaitForHomeMenu => "Kliknięcie zapisanego slotu home",
                AutoReconnectStage.ClickHomeSlot => "Odliczanie czasu teleportacji",
                AutoReconnectStage.WaitForTeleport => shutdownAfterHome
                    ? "Bezpieczne zamknięcie Minecraft Helper"
                    : "Slot 1 i wznowienie Kopacza",
                AutoReconnectStage.HealthOpenInventory or AutoReconnectStage.HealthVerifyInventory => "Decyzja: kontynuacja albo odzyskanie połączenia",
                AutoReconnectStage.OpenVerificationInventory or AutoReconnectStage.VerifyAfterTeleport => "Zakończenie reconnectu albo ponowienie próby",
                AutoReconnectStage.RetryDelay => "Ponowne rozpoznanie ekranu",
                _ => "Oczekiwanie na kolejny krok"
            };
        }

        private OverlayHudEntry BuildKopacz533OverlayEntry(DateTime now)
        {
            string bindLabel = GetConfiguredBindLabel(TxtKopacz533Key.Text);
            string runtimeState = GetRuntimeStateLabel(_kopacz533RuntimeEnabled);
            string stageLabel = GetKopacz533StageLabel();
            int elapsedSeconds = GetKopacz533ElapsedSeconds(now);
            string commandLine = BuildKopacz533CommandOverlayLine(now);
            string startedLine = _kopacz533RuntimeStartedAtUtc == DateTime.MinValue
                ? "Start: -"
                : $"Start: {_kopacz533RuntimeStartedAtUtc.ToLocalTime():HH:mm:ss} | Czas: {FormatRuntimeDuration(elapsedSeconds)}";

            string body =
                $"Bind: {bindLabel}\n" +
                $"Stan: {runtimeState} | Etap: {stageLabel}\n" +
                $"{startedLine}\n" +
                $"Historia: EQ {_latestMiningLogSummary.InventorySessions:N0} | CX {_latestMiningLogSummary.CobbleXCreated:N0} | {_latestMiningLogSummary.DiscardedItems:N0} wyrzuconych\n" +
                commandLine;

            return new OverlayHudEntry("KOPACZ 5/3/3", body, OverlayHudTone.Active);
        }

        private OverlayHudEntry BuildKopacz633OverlayEntry(DateTime now)
        {
            string bindLabel = GetConfiguredBindLabel(TxtKopacz633Key.Text);
            string runtimeState = GetRuntimeStateLabel(_kopacz633RuntimeEnabled);
            string stageLabel = GetKopacz633StageLabel();
            string directionLabel = GetKopacz633DirectionLabel();
            string movementLabel = GetKopacz633MovementOverlayLabel(now);
            string commandLine = BuildKopacz633CommandOverlayLine(now);
            int elapsedSeconds = Math.Max(0, (int)Math.Floor((now - _kopacz633RuntimeStartedAtUtc).TotalSeconds));
            string startedLine = _kopacz633RuntimeStartedAtUtc == DateTime.MinValue
                ? "Start: -"
                : $"Start: {_kopacz633RuntimeStartedAtUtc.ToLocalTime():HH:mm:ss} | Czas: {FormatRuntimeDuration(elapsedSeconds)}";

            string sizeLabel;
            if (CbKopacz633Direction.SelectedIndex == 1)
            {
                int width = GetConfiguredKopacz633ForwardWidth();
                sizeLabel = $"Szer: {width}";
            }
            else if (IsKopacz633RectangleMode())
            {
                int width = GetConfiguredKopacz633UpwardWidth();
                int length = GetConfiguredKopacz633UpwardLength();
                sizeLabel = $"Szer: {width} | Dł: {length}";
            }
            else
            {
                sizeLabel = "Szer: - | Dł: -";
            }

            string body =
                $"Bind: {bindLabel}\n" +
                $"Stan: {runtimeState} | Etap: {stageLabel}\n" +
                $"Tryb: {directionLabel} | {sizeLabel}\n" +
                $"{startedLine}\n" +
                $"{movementLabel}\n" +
                $"Historia: EQ {_latestMiningLogSummary.InventorySessions:N0} | CX {_latestMiningLogSummary.CobbleXCreated:N0} | {_latestMiningLogSummary.DiscardedItems:N0} wyrzuconych\n" +
                commandLine;

            return new OverlayHudEntry("KOPACZ 6/3/3", body, OverlayHudTone.Active);
        }

        private OverlayHudEntry BuildInventoryCleanupOverlayEntry(DateTime now)
        {
            if (_goldDepositManualTest != null)
            {
                string progress = _goldDepositManualTest.Started
                    ? _goldDepositFlow?.Message ?? "Test wpłaty"
                    : $"Start za {_goldDepositManualTest.RemainingSeconds(now)} s — przejdź do Minecrafta";
                return new OverlayHudEntry("TEST WPŁATY ZŁOTA", progress + "\nSTOP: przycisk w GUI lub bind kopacza\nKopanie pozostaje wyłączone.", OverlayHudTone.Active);
            }
            int intervalSeconds = GetConfiguredInventoryCleanupIntervalSeconds();
            int selectedSlots = GetSelectedInventoryCleanupSlots().Count;
            int selectedItemTypes = GetSelectedInventoryCleanupItemTypes().Count;
            bool discardEverythingExceptCobblestone = ChkInventoryCleanupAllItemTypes.IsChecked == true;
            string state;
            string progressLine;

            switch (_inventoryCleanupStage)
            {
                case InventoryCleanupStage.ReturnToMiningStart:
                    state = "Powrót na start przed Auto EQ";
                    progressLine = CbKopacz633Direction.SelectedIndex == 2
                        ? "Trzymanie pełnego A + S"
                        : "Trzymanie pełnego A";
                    break;

                case InventoryCleanupStage.OpenInventory:
                    state = "Otwieranie EQ";
                    progressLine = _inventoryCleanupOwner == InventoryCleanupOwner.Kopacz633
                        ? "Pozycja startowa przywrócona"
                        : "Przygotowanie skanowania";
                    break;

                case InventoryCleanupStage.WaitForInventory:
                    state = "Otwieranie i skanowanie EQ";
                    progressLine = $"Próba wykrywania: {Math.Max(1, _inventoryCleanupDetectionAttempts + 1)}/{InventoryCleanupMaximumDetectionAttempts}";
                    break;

                case InventoryCleanupStage.MoveToSlot:
                case InventoryCleanupStage.PressDropModifier:
                case InventoryCleanupStage.PressDropKey:
                case InventoryCleanupStage.ReleaseDropKeys:
                    state = "Wyrzucanie pełnych stosów";
                    int currentTarget = _inventoryCleanupTargets.Count == 0
                        ? 0
                        : Math.Min(_inventoryCleanupTargetIndex + 1, _inventoryCleanupTargets.Count);
                    progressLine = $"Postęp: {currentTarget}/{_inventoryCleanupTargets.Count}";
                    break;

                case InventoryCleanupStage.CloseInventory:
                    state = "Zamykanie EQ";
                    progressLine = $"Wykryte stosy: {_inventoryCleanupTargets.Count}";
                    break;

                case InventoryCleanupStage.OpenCobbleXChat:
                case InventoryCleanupStage.TypeCobbleXCommand:
                case InventoryCleanupStage.SubmitCobbleXCommand:
                    state = "Tworzenie CobbleX";
                    progressLine = $"Wysłanie {Math.Min(_inventoryCleanupCobbleXCommandSendCount + 1, _inventoryCleanupCobbleXCommandTargetCount)}/{_inventoryCleanupCobbleXCommandTargetCount} | Cobble 64: {_inventoryCleanupFullCobblestoneStacks}/{GetConfiguredCobbleXRequiredStacks()} | komenda: {_inventoryCleanupPendingCobbleXCommand}";
                    break;

                case InventoryCleanupStage.SelectFoodSlot:
                case InventoryCleanupStage.StartEating:
                case InventoryCleanupStage.StopEatingAndRestoreTool:
                    state = "Jedzenie po Auto EQ";
                    progressLine = "Slot 2 | PPM 4 s | powrót na slot 1";
                    break;

                case InventoryCleanupStage.DepositGold:
                    state = "Wpłata złota";
                    progressLine = _goldDepositFlow?.Message ?? "Przygotowanie komendy i menu wpłaty";
                    break;

                case InventoryCleanupStage.ResumeMining:
                    state = "Wznawianie kopania";
                    progressLine = $"Wyrzucone stosy: {_inventoryCleanupTargets.Count}";
                    break;

                default:
                    state = "Czekanie";
                    if (_nextInventoryCleanupAtUtc == DateTime.MaxValue)
                    {
                        progressLine = "Następny skan: po uruchomieniu kopacza";
                    }
                    else
                    {
                        int remainingSeconds = Math.Max(0, (int)Math.Ceiling((_nextInventoryCleanupAtUtc - now).TotalSeconds));
                        progressLine = $"Następny skan: za {remainingSeconds}s";
                    }
                    break;
            }

            string cobbleXLine = ChkCobbleXEnabled.IsChecked == true
                ? $"CobbleX: ON | Komenda ×{GetConfiguredCobbleXCommandCount()} | Cobble 64: {_inventoryCleanupLastFullCobblestoneStacks}/{GetConfiguredCobbleXRequiredStacks()} | Komenda: {GetConfiguredCobbleXCommand()}"
                : "CobbleX: OFF";
            string eatingLine = ChkInventoryCleanupEatAfterCleanup.IsChecked == true
                ? "Jedzenie: ON | slot 2 | PPM 4 s | powrót na slot 1"
                : "Jedzenie: OFF";
            string timingLine;
            if (_inventoryCleanupStage != InventoryCleanupStage.None && _inventoryCleanupOpenedAtUtc != DateTime.MinValue)
            {
                int cycleSeconds = Math.Max(0, (int)Math.Floor((now - _inventoryCleanupOpenedAtUtc).TotalSeconds));
                timingLine = $"Bieżące EQ: {_inventoryCleanupOpenedAtUtc.ToLocalTime():HH:mm:ss} | Czas: {FormatRuntimeDuration(cycleSeconds)}";
            }
            else if (_latestMiningLogSummary.LastActivity.HasValue)
            {
                int agoSeconds = Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - _latestMiningLogSummary.LastActivity.Value.ToUniversalTime()).TotalSeconds));
                timingLine = $"Ostatnie EQ: {_latestMiningLogSummary.LastActivity.Value.LocalDateTime:HH:mm:ss} | {FormatRuntimeDuration(agoSeconds)} temu";
            }
            else
            {
                timingLine = "Ostatnie EQ: brak";
            }
            string body =
                $"Stan: {state}\n" +
                $"{progressLine}\n" +
                $"{timingLine}\n" +
                $"Interwał: {intervalSeconds}s | Sloty: {selectedSlots}/27 | " +
                (discardEverythingExceptCobblestone
                    ? "Tryb: wszystko poza cobblestone\n"
                    : $"Typy: {selectedItemTypes}/{InventoryCleanupItemTypes.Length}\n") +
                $"{cobbleXLine}\n" +
                $"{eatingLine}\n" +
                $"Historia EQ: {_latestMiningLogSummary.InventorySessions:N0} skanów | {_latestMiningLogSummary.DiscardedItems:N0} szt. / {_latestMiningLogSummary.DiscardedStacks:N0} stos.\n" +
                $"Ostatni wynik: {_inventoryCleanupLastResult}";

            bool warning = _inventoryCleanupStage == InventoryCleanupStage.None && _inventoryCleanupLastResultWarning;
            return new OverlayHudEntry(
                _inventoryCleanupStage == InventoryCleanupStage.DepositGold ? "WPŁATA ZŁOTA"
                    : ChkCobbleXEnabled.IsChecked == true ? "AUTO EQ / COBBLEX" : "AUTO WYRZUCANIE",
                body,
                warning ? OverlayHudTone.Warning : OverlayHudTone.Active);
        }

        private OverlayHudEntry BuildTestAutoFishingOverlayEntry(DateTime now)
        {
            string bindLabel = GetConfiguredBindLabel(TxtTestAutoFishingBind.Text);
            string captureBindLabel = GetConfiguredBindLabel(TxtTestAutoFishingCaptureBind.Text);
            string runtimeStateLabel = GetRuntimeStateLabel(_testAutoFishingRuntimeEnabled);
            string detectionStateLabel = TxtTestAutoFishingState?.Text?.Trim() ?? "-";
            string areaLine = HasTestAutoFishingAreaConfigured()
                ? $"Obszar: x={_settings.TestAutoFishingCaptureX}, y={_settings.TestAutoFishingCaptureY}, {_settings.TestAutoFishingCaptureWidth}x{_settings.TestAutoFishingCaptureHeight}"
                : "Obszar: brak";
            string bobberLine = !double.IsNaN(_testAutoFishingLastDetectedBobberX) && !double.IsNaN(_testAutoFishingLastDetectedBobberY)
                ? $"Spławik: x={_settings.TestAutoFishingCaptureX + (int)Math.Round(_testAutoFishingLastDetectedBobberX)}, y={_settings.TestAutoFishingCaptureY + (int)Math.Round(_testAutoFishingLastDetectedBobberY)} (okno gry) | czerwone px={_testAutoFishingLastDetectedRedPixels}"
                : "Spławik: brak";
            if (_testAutoFishingBaselineReady)
                bobberLine += $" | baza y={_settings.TestAutoFishingCaptureY + (int)Math.Round(_testAutoFishingBaselineBobberY)}";
            string catchLine = _testAutoFishingCaughtCount > 0 && _testAutoFishingLastCatchAtUtc != DateTime.MinValue
                ? $"Złowione: {_testAutoFishingCaughtCount} | ostatnie {Math.Max(0, (int)Math.Floor((now - _testAutoFishingLastCatchAtUtc).TotalSeconds))}s temu"
                : "Złowione: brak";
            int fishingElapsedSeconds = _testAutoFishingRuntimeStartedAtUtc == DateTime.MinValue
                ? 0
                : Math.Max(0, (int)Math.Floor((now - _testAutoFishingRuntimeStartedAtUtc).TotalSeconds));
            string fishingTimeLine = _testAutoFishingRuntimeStartedAtUtc == DateTime.MinValue
                ? "Start: - | Czas: 00:00"
                : $"Start: {_testAutoFishingRuntimeStartedAtUtc.ToLocalTime():HH:mm:ss} | Czas: {FormatRuntimeDuration(fishingElapsedSeconds)}";

            string command = GetConfiguredTestAutoFishingRepairCommand();
            string commandLine;
            if (string.IsNullOrWhiteSpace(command) || GetConfiguredTestAutoFishingRepairIntervalSeconds() <= 0)
                commandLine = "Komenda: OFF";
            else if (_testAutoFishingRepairStage != TestAutoFishingRepairStage.None)
                commandLine = $"Komenda: {GetStatusCommandPreview(command)} ({GetTestAutoFishingRepairStageLabel(_testAutoFishingRepairStage)})";
            else if (_testAutoFishingRecastAfterRepairPending)
                commandLine = $"Komenda: ponowne zarzucenie za {Math.Max(0, (int)Math.Ceiling((_testAutoFishingRecastAfterRepairAtUtc - now).TotalMilliseconds))}ms";
            else
                commandLine = $"Komenda: {GetStatusCommandPreview(command)} za {(_nextTestAutoFishingRepairAtUtc == DateTime.MaxValue ? 0 : Math.Max(0, (int)Math.Ceiling((_nextTestAutoFishingRepairAtUtc - now).TotalSeconds)))}s";

            string body = $"Bind: {bindLabel}\nZaznaczanie: {captureBindLabel}\nStan: {runtimeStateLabel}\n{fishingTimeLine}\nDetekcja: {detectionStateLabel}\n{areaLine}\n{bobberLine}\n{catchLine}\n{commandLine}";
            return new OverlayHudEntry("AUTO ŁOWIENIE", body, OverlayHudTone.Active);
        }

        private static string FormatRuntimeDuration(int totalSeconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        private string BuildKopacz533CommandOverlayLine(DateTime now)
        {
            if (_kopacz533CommandStage != Kopacz533CommandStage.None && !string.IsNullOrWhiteSpace(_kopacz533PendingCommand))
            {
                string pendingIndex = _kopacz533PendingCommandIndex >= 0 ? (_kopacz533PendingCommandIndex + 1).ToString(CultureInfo.InvariantCulture) : "?";
                return $"Komenda #{pendingIndex}: {GetStatusCommandPreview(_kopacz533PendingCommand)} (teraz)";
            }

            if (TryPeekNextKopacz533Command(out int commandIndex, out string command, out _))
            {
                int remainingSeconds = GetKopaczCommandRemainingSeconds(InventoryCleanupOwner.Kopacz533, now);
                string pauseLabel = IsKopaczCommandCountdownPaused(InventoryCleanupOwner.Kopacz533) ? " (pauza Auto EQ)" : string.Empty;
                return $"Następna #{commandIndex + 1}: {GetStatusCommandPreview(command)} za {remainingSeconds}s{pauseLabel}";
            }

            return "Komenda: brak";
        }

        private string BuildKopacz633CommandOverlayLine(DateTime now)
        {
            if (_kopacz633CommandStage != Kopacz633CommandStage.None && !string.IsNullOrWhiteSpace(_kopacz633PendingCommand))
            {
                string pendingIndex = _kopacz633PendingCommandIndex >= 0 ? (_kopacz633PendingCommandIndex + 1).ToString(CultureInfo.InvariantCulture) : "?";
                return $"Komenda #{pendingIndex}: {GetStatusCommandPreview(_kopacz633PendingCommand)} (teraz)";
            }

            if (TryPeekNextKopacz633Command(out int commandIndex, out string command, out _))
            {
                int remainingSeconds = GetKopaczCommandRemainingSeconds(InventoryCleanupOwner.Kopacz633, now);
                string pauseLabel = IsKopaczCommandCountdownPaused(InventoryCleanupOwner.Kopacz633) ? " (pauza Auto EQ)" : string.Empty;
                return $"Następna #{commandIndex + 1}: {GetStatusCommandPreview(command)} za {remainingSeconds}s{pauseLabel}";
            }

            return "Komenda: brak";
        }

        private string GetKopacz533StageLabel()
        {
            if (_kopacz533ResumeMiningPending)
                return "Wznawianie kopania";

            return _kopacz533CommandStage switch
            {
                Kopacz533CommandStage.OpenChat => "Otwieranie chatu",
                Kopacz533CommandStage.TypeCommand => "Wpisywanie komendy",
                Kopacz533CommandStage.SubmitCommand => "Wysyłanie komendy",
                _ => _kopacz533RuntimeEnabled ? "Kopanie" : "Oczekiwanie"
            };
        }

        private string GetKopacz633StageLabel()
        {
            if (_kopacz633ResumeMiningPending)
                return "Wznawianie kopania";

            return _kopacz633CommandStage switch
            {
                Kopacz633CommandStage.OpenChat => "Otwieranie chatu",
                Kopacz633CommandStage.TypeCommand => "Wpisywanie komendy",
                Kopacz633CommandStage.SubmitCommand => "Wysyłanie komendy",
                _ => _kopacz633RuntimeEnabled ? "Kopanie" : "Oczekiwanie"
            };
        }

        private string GetKopacz633DirectionLabel()
        {
            return CbKopacz633Direction.SelectedIndex switch
            {
                1 => "Na wprost",
                2 => "Do góry",
                _ => "Brak"
            };
        }

        // "Do góry" chodzi prostokątem: pola długości i szerokości.
        private bool IsKopacz633RectangleMode()
        {
            return CbKopacz633Direction.SelectedIndex == 2;
        }

        private static int GetKopacz633MsPerBlock() => Kopacz633MsPerBlock;

        private string GetKopacz633MovementOverlayLabel(DateTime now)
        {
            string movementLabel = _kopacz633StrafeDirection switch
            {
                Kopacz633StrafeDirection.Forward => "W ^",
                Kopacz633StrafeDirection.Right => "D ->",
                Kopacz633StrafeDirection.Backward => "S v",
                Kopacz633StrafeDirection.Left => "A <-",
                _ => "STOP"
            };

            if (_kopacz633StrafeDirection == Kopacz633StrafeDirection.None)
                return $"Ruch: {movementLabel}";

            int remainingMs = Math.Max(0, (int)Math.Ceiling((_kopacz633MovementLegEndAtUtc - now).TotalMilliseconds));
            return $"Ruch: {movementLabel} za {remainingMs}ms";
        }

        private static string GetStatusCommandPreview(string command)
        {
            string value = (command ?? string.Empty).Trim();
            if (value.Length <= 22)
                return value;

            return value.Substring(0, 19) + "...";
        }

        private int GetKopacz533ElapsedSeconds(DateTime now)
        {
            double elapsed = (now - _kopacz533RuntimeStartedAtUtc).TotalSeconds;
            return Math.Max(0, (int)Math.Floor(elapsed));
        }

        // HUD pokazuje czas i etapy pracy, więc podczas kopania odświeżamy go co 200 ms.
        private void RefreshLiveOverlayHud(DateTime now)
        {
            bool runtimeDataLive =
                _kopacz533RuntimeEnabled ||
                _kopacz533CommandStage != Kopacz533CommandStage.None ||
                _kopacz633RuntimeEnabled ||
                _kopacz633CommandStage != Kopacz633CommandStage.None ||
                _inventoryCleanupStage != InventoryCleanupStage.None ||
                _testAutoFishingRuntimeEnabled;
            if (!runtimeDataLive)
                return;

            if (now < _nextRuntimeTileRefreshAtUtc)
                return;

            _nextRuntimeTileRefreshAtUtc = now.AddMilliseconds(200);
            RefreshOverlayHud(now);
        }

        private void SetExpandableSectionState(
            FrameworkElement section,
            bool expanded)
        {
            if (_expandableSectionStates.TryGetValue(section, out bool previousState)
                && previousState == expanded)
                return;

            _expandableSectionStates[section] = expanded;
            GlassMotion.SetExpanded(section, expanded, animate: !_isLoadingUi && IsLoaded);
        }

        private void UpdateEnabledStates(object? sender = null, RoutedEventArgs? e = null)
        {
            bool kop533On = ChkKopacz533Enabled.IsChecked ?? false;
            SetExpandableSectionState(PanelKopacz533Content, kop533On);
            TxtKopacz533Key.IsEnabled = kop533On;
            BtnKopacz533Capture.IsEnabled = kop533On;
            BtnKopacz533Clear.IsEnabled = kop533On;
            PanelKopacz533Commands.IsEnabled = kop533On;
            BtnKopacz533AddCommand.IsEnabled = kop533On;
            if (!kop533On)
            {
                _kopacz533RuntimeEnabled = false;
                ResetKopacz533RuntimeState();
                SetKopacz533MiningHold(false);
                EndMiningLogRun(InventoryCleanupOwner.Kopacz533, "Kanał Kopacz 5/3/3 został wyłączony.");
            }

            bool kop633On = ChkKopacz633Enabled.IsChecked ?? false;
            SetExpandableSectionState(PanelKopacz633Content, kop633On);
            TxtKopacz633Key.IsEnabled = kop633On;
            BtnKopacz633Capture.IsEnabled = kop633On;
            BtnKopacz633Clear.IsEnabled = kop633On;
            CbKopacz633Direction.IsHitTestVisible = kop633On; // blokuje myszkę gdy OFF
            CbKopacz633Direction.Focusable = kop633On;        // nie łapie focusa gdy OFF
            CbKopacz633Direction.Opacity = kop633On ? 1.0 : 0.85; // opcjonalnie lekko przygaś
            PanelKopaczNaWprost.IsEnabled = kop633On;
            PanelKopaczDoGory.IsEnabled = kop633On;
            PanelKopacz633Commands.IsEnabled = kop633On;
            BtnKopacz633AddCommand.IsEnabled = kop633On;
            if (!kop633On)
            {
                _kopacz633RuntimeEnabled = false;
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetKopacz633RuntimeState();
                EndMiningLogRun(InventoryCleanupOwner.Kopacz633, "Kanał Kopacz 6/3/3 został wyłączony.");
            }
            UpdateKopaczUpwardInfoVisibility();

            bool inventoryCleanupOn = ChkInventoryCleanupEnabled.IsChecked == true;
            SetExpandableSectionState(PanelInventoryCleanupExpandableContent, inventoryCleanupOn);
            PanelInventoryCleanupContent.IsEnabled = inventoryCleanupOn;
            RefreshInventoryCleanupItemTypeMode();
            bool cobbleXSettingsOn = inventoryCleanupOn && ChkCobbleXEnabled.IsChecked == true;
            SetExpandableSectionState(PanelCobbleXSettings, cobbleXSettingsOn);
            PanelCobbleXSettings.IsEnabled = cobbleXSettingsOn;
            UpdateGoldDepositSettingsUi();
            if (!inventoryCleanupOn)
            {
                if (IsGoldDepositActive)
                    StopGoldDeposit("Wyłączono Auto EQ podczas wpłaty złota. Sprawdź przedmiot na kursorze.");
                else
                    ResetInventoryCleanupState(scheduleNext: false);
            }

            bool testAutoFishingOn = ChkTestAutoFishingEnabled?.IsChecked == true;
            bool emergencyDamageSoundOn = ChkEmergencyDamageSoundEnabled?.IsChecked == true;
            bool emergencyReconnectOn = IsEmergencyReconnectEnabled();
            bool autoReconnectOn = IsPeriodicAutoReconnectEnabled();
            if (PanelTestAutoFishingExpandableContent != null)
                SetExpandableSectionState(PanelTestAutoFishingExpandableContent, testAutoFishingOn);
            if (PanelTestAutoFishingContent != null)
                PanelTestAutoFishingContent.IsEnabled = testAutoFishingOn;
            if (TxtTestAutoFishingBind != null)
                TxtTestAutoFishingBind.IsEnabled = testAutoFishingOn;
            if (BtnTestAutoFishingBind != null)
                BtnTestAutoFishingBind.IsEnabled = testAutoFishingOn;
            if (BtnTestAutoFishingBindClear != null)
                BtnTestAutoFishingBindClear.IsEnabled = testAutoFishingOn;
            if (TxtTestAutoFishingCaptureBind != null)
                TxtTestAutoFishingCaptureBind.IsEnabled = testAutoFishingOn;
            if (BtnTestAutoFishingCaptureBind != null)
                BtnTestAutoFishingCaptureBind.IsEnabled = testAutoFishingOn;
            if (BtnTestAutoFishingCaptureBindClear != null)
                BtnTestAutoFishingCaptureBindClear.IsEnabled = testAutoFishingOn;
            if (BtnTestAutoFishingSelectArea != null)
                BtnTestAutoFishingSelectArea.IsEnabled = testAutoFishingOn;
            if (BtnTestAutoFishingResetArea != null)
                BtnTestAutoFishingResetArea.IsEnabled = testAutoFishingOn;
            if (TxtTestAutoFishingRepairCommand != null)
                TxtTestAutoFishingRepairCommand.IsEnabled = testAutoFishingOn;
            if (TxtTestAutoFishingRepairEverySeconds != null)
                TxtTestAutoFishingRepairEverySeconds.IsEnabled = testAutoFishingOn;
            if (!testAutoFishingOn)
            {
                _testAutoFishingRuntimeEnabled = false;
                ResetTestAutoFishingRuntimeState();
            }
            UpdateTestAutoFishingStatusLabel();
            if (PanelEmergencyDamageSoundContent != null)
                SetExpandableSectionState(PanelEmergencyDamageSoundContent, emergencyDamageSoundOn);
            if (CbEmergencyDamageSoundDevice != null)
                CbEmergencyDamageSoundDevice.IsEnabled = emergencyDamageSoundOn;
            if (BtnEmergencyDamageSoundRefreshDevices != null)
                BtnEmergencyDamageSoundRefreshDevices.IsEnabled = emergencyDamageSoundOn;
            if (SlEmergencyDamageSoundSimilarity != null)
                SlEmergencyDamageSoundSimilarity.IsEnabled = emergencyDamageSoundOn;
            if (BtnEmergencyDamageSoundReloadReferences != null)
                BtnEmergencyDamageSoundReloadReferences.IsEnabled = emergencyDamageSoundOn && !_damageSoundDetector.IsRunning;
            if (BtnEmergencyDamageSoundTest != null)
                BtnEmergencyDamageSoundTest.IsEnabled = emergencyDamageSoundOn
                    && _settings.EmergencyDamageSoundTemplates.Count > 0;
            if (BtnEmergencyDamageSoundStop != null)
                BtnEmergencyDamageSoundStop.IsEnabled = emergencyDamageSoundOn && _damageSoundDetector.IsRunning;
            if (!emergencyDamageSoundOn)
                StopEmergencyDamageSoundListening(updateStatus: true);
            if (PanelEmergencyReconnectContent != null)
                SetExpandableSectionState(PanelEmergencyReconnectContent, emergencyReconnectOn);
            if (TxtEmergencyReconnectDelaySeconds != null)
                TxtEmergencyReconnectDelaySeconds.IsEnabled = emergencyReconnectOn && !_emergencyReconnectActive;
            if (BtnEmergencyReconnectStop != null)
                BtnEmergencyReconnectStop.IsEnabled = _emergencyReconnectActive;
            if (PanelAutoReconnectContent != null)
                SetExpandableSectionState(PanelAutoReconnectContent, emergencyDamageSoundOn);
            UpdateEmergencyProtectionOptionAvailability();
            if (!autoReconnectOn
                && !_emergencyReconnectActive
                && _autoReconnectStage != AutoReconnectStage.None)
                StopAutoReconnect("Auto reconnect wyłączony.", resumeMining: true, warning: false);

            bool overlayHudOn = ChkOverlayHudEnabled.IsChecked == true;
            SetExpandableSectionState(PanelOverlayHudContent, overlayHudOn);
            PanelOverlayHudContent.IsEnabled = overlayHudOn;

            RefreshOverlayHud(DateTime.UtcNow);
        }

        private void RefreshKopaczCommandsUI(int option)
        {
            StackPanel panel = option == 533 ? PanelKopacz533Commands : PanelKopacz633Commands;
            List<MinerCommand> commands = option == 533 ? _settings.Kopacz533Commands : _settings.Kopacz633Commands;
            panel.Children.Clear();

            foreach (var cmd in commands)
                AddCommandRow(panel, cmd, option);
        }

        private void AddCommandRow(StackPanel panel, MinerCommand cmd, int option)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = new Border
            {
                Style = (Style)FindResource("GlassNumBadge"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (panel.Children.Count + 1).ToString(CultureInfo.InvariantCulture),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            row.Children.Add(number);

            var commandBox = new TextBox
            {
                Text = cmd.Command,
                Style = (Style)FindResource("GlassMonoBox"),
                Margin = new Thickness(10, 0, 10, 0),
                ToolTip = "Komenda wpisywana na czacie"
            };
            Grid.SetColumn(commandBox, 1);
            row.Children.Add(commandBox);

            var everyLabel = new TextBlock
            {
                Text = "co",
                Foreground = GlassPalette.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(everyLabel, 2);
            row.Children.Add(everyLabel);

            // Pełne minuty pokazujemy w minutach; zapis zawsze w sekundach (MinerCommand.Seconds).
            bool inMinutes = cmd.Seconds >= 60 && cmd.Seconds % 60 == 0;
            var secondsBox = new TextBox
            {
                Text = (inMinutes ? cmd.Seconds / 60 : cmd.Seconds).ToString(CultureInfo.InvariantCulture),
                Style = (Style)FindResource("GlassMonoBox"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = "Co ile (liczone od poprzedniej komendy)"
            };
            Grid.SetColumn(secondsBox, 3);
            row.Children.Add(secondsBox);

            var unit = new ComboBox
            {
                ItemsSource = new[] { "s", "min" },
                SelectedIndex = inMinutes ? 1 : 0,
                MinWidth = 66,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0),
                ToolTip = "Jednostka: sekundy albo minuty"
            };
            Grid.SetColumn(unit, 4);
            row.Children.Add(unit);

            var deleteIcon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IcoTrash"),
                Style = (Style)FindResource("GlassIconPath")
            };
            var deleteBox = new Grid { Width = 24, Height = 24 };
            deleteBox.Children.Add(deleteIcon);
            Button deleteBtn = new Button
            {
                Style = (Style)FindResource("GlassIconButton"),
                ToolTip = "Usuń komendę",
                Content = new Viewbox { Width = 15, Height = 15, Child = deleteBox }
            };
            Grid.SetColumn(deleteBtn, 5);
            row.Children.Add(deleteBtn);

            deleteBtn.Click += (s, e) =>
            {
                List<MinerCommand> list = option == 533 ? _settings.Kopacz533Commands : _settings.Kopacz633Commands;
                list.Remove(cmd);
                RefreshKopaczCommandsUI(option);
                MarkDirty();
            };

            panel.Children.Add(new Border
            {
                Style = (Style)FindResource("GlassInset"),
                Padding = new Thickness(10, 8, 6, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = row
            });

            void ApplyInterval()
            {
                MarkDirty();
                if (int.TryParse(secondsBox.Text, out int value))
                    cmd.Seconds = unit.SelectedIndex == 1 ? Math.Min(value, int.MaxValue / 60) * 60 : value;
            }

            secondsBox.TextChanged += (s, e) => ApplyInterval();
            unit.SelectionChanged += (s, e) => ApplyInterval();

            commandBox.TextChanged += (s, e) =>
            {
                cmd.Command = commandBox.Text;
                MarkDirty();
            };
        }

        private void CbKopacz633Direction_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PanelKopaczNaWprost == null || PanelKopaczDoGory == null) return;

            bool animate = !_isLoadingUi && IsLoaded;
            GlassMotion.SetExpanded(PanelKopaczNaWprost, CbKopacz633Direction.SelectedIndex == 1, animate);
            GlassMotion.SetExpanded(PanelKopaczDoGory, IsKopacz633RectangleMode(), animate);
            UpdateKopacz633AreaPreview();
            UpdateKopaczUpwardInfoVisibility();

            if (_kopacz633RuntimeEnabled && !IsKopacz633DirectionSelected())
            {
                _kopacz633RuntimeEnabled = false;
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetKopacz633RuntimeState();
                EndMiningLogRun(InventoryCleanupOwner.Kopacz633, "Kopanie zatrzymane: usunięto wybrany kierunek.", MiningLogStatuses.Aborted);
                UpdateStatusBar("Kopacz 6/3/3 zatrzymany: wybierz kierunek 'Na wprost' albo 'Do góry'", "Orange");
            }

            if (_isLoadingUi)
                return;

            MarkDirty();
            RefreshOverlayHud(DateTime.UtcNow);
        }

        private void UpdateKopaczUpwardInfoVisibility()
        {
            if (PanelKopaczUpwardAfkInfo == null)
                return;

            bool kop633Enabled = ChkKopacz633Enabled?.IsChecked == true;
            bool showAfkInfo = kop633Enabled && CbKopacz633Direction?.SelectedIndex == 2;
            GlassMotion.SetExpanded(PanelKopaczUpwardAfkInfo, showAfkInfo, animate: !_isLoadingUi && IsLoaded);
        }

        private void StartBindCapture(BindTarget target)
        {
            _bindCaptureTarget = target;
            UpdateBindCaptureVisuals();
            UpdateStatusBar($"BINDOWANIE: {GetBindTargetLabel(target)} - naciśnij klawisz", "Orange");
            Focus();
        }

        private void BindKeyBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLoadingUi)
                return;
            if (sender is not TextBox textBox || !textBox.IsEnabled)
                return;
            if (!Enum.TryParse(textBox.Tag?.ToString(), out BindTarget target) || target == BindTarget.None)
                return;

            StartBindCapture(target);
            e.Handled = true;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isLoadingUi)
                return;

            if (_bindCaptureTarget == BindTarget.None)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                _bindCaptureTarget = BindTarget.None;
                UpdateBindCaptureVisuals();
                UpdateStatusBar("Bindowanie anulowane", "Orange");
                e.Handled = true;
                return;
            }
            if (key == Key.None)
                return;

            string keyText = key == Key.Return ? "Enter" : key.ToString();
            BindTarget target = _bindCaptureTarget;
            _pendingBindValues[target] = keyText;
            RefreshBindSaveButton(target);

            // Prevent accidental macro toggle while the capture key is still held.
            _suppressBindToggleUntilRelease = true;

            _bindCaptureTarget = BindTarget.None;
            UpdateBindCaptureVisuals();
            UpdateStatusBar($"Wybrano klawisz: {keyText} ({GetBindTargetLabel(target)}) - kliknij \"Zapisz\"", "Orange");
            e.Handled = true;
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLoadingUi)
                return;

            if (_bindCaptureTarget == BindTarget.None)
                return;

            string? keyText = e.ChangedButton switch
            {
                MouseButton.XButton1 => "MouseX1",
                MouseButton.XButton2 => "MouseX2",
                MouseButton.Middle => "MouseMiddle",
                _ => null
            };

            if (keyText == null)
                return;

            if (IsMinecraftControlKeyTarget(_bindCaptureTarget))
            {
                UpdateStatusBar("Sterowanie Minecrafta: wybierz klawisz klawiatury, nie przycisk myszy.", "Orange");
                e.Handled = true;
                return;
            }

            BindTarget target = _bindCaptureTarget;
            _pendingBindValues[target] = keyText;
            RefreshBindSaveButton(target);

            _suppressBindToggleUntilRelease = true;

            _bindCaptureTarget = BindTarget.None;
            UpdateBindCaptureVisuals();
            UpdateStatusBar($"Wybrano klawisz: {keyText} ({GetBindTargetLabel(target)}) - kliknij \"Zapisz\"", "Orange");
            e.Handled = true;
        }

        private void ConfirmPendingBind(BindTarget target)
        {
            if (!_pendingBindValues.TryGetValue(target, out string? keyText))
            {
                UpdateStatusBar($"Kliknij pole \"Klawisz\" dla {GetBindTargetLabel(target)}, potem naciśnij klawisz i kliknij \"Zapisz\".", "Orange");
                return;
            }

            TextBox? textBox = GetBindTextBox(target);
            if (textBox == null)
                return;

            if (IsMinecraftControlKeyTarget(target) && !IsSupportedMinecraftControlKey(keyText))
            {
                UpdateStatusBar($"{GetBindTargetLabel(target)}: wybierz prawidłowy klawisz klawiatury.", "Orange");
                return;
            }

            string ownerId = GetBindOwnerId(target);
            if (!IsMinecraftControlKeyTarget(target)
                && TryFindBindConflict(keyText, ownerId, out string conflictOwnerLabel))
            {
                ShowBindConflict(keyText, conflictOwnerLabel, GetBindTargetLabel(target));
                return;
            }

            textBox.Text = keyText;
            _pendingBindValues.Remove(target);
            RefreshBindSaveButton(target);

            MarkDirty();
            UpdateStatusBar($"Zapisano klawisz {keyText} dla {GetBindTargetLabel(target)}", "Green");
        }

        private void BtnClearBind_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            if (sender is not Button button)
                return;
            if (!Enum.TryParse(button.Tag?.ToString(), out BindTarget target) || target == BindTarget.None)
                return;

            ClearFixedBind(target);
        }

        private void ClearFixedBind(BindTarget target)
        {
            TextBox? textBox = GetBindTextBox(target);
            if (textBox == null)
                return;

            bool hadBind = !string.IsNullOrWhiteSpace(textBox.Text) || _pendingBindValues.ContainsKey(target) || _bindCaptureTarget == target;
            textBox.Text = string.Empty;
            _pendingBindValues.Remove(target);

            if (_bindCaptureTarget == target)
            {
                _bindCaptureTarget = BindTarget.None;
                UpdateBindCaptureVisuals();
            }

            switch (target)
            {
                case BindTarget.Kopacz533:
                    _kopacz533BindWasDown = false;
                    _kopacz533RuntimeEnabled = false;
                    SetKopacz533MiningHold(false);
                    ResetKopacz533RuntimeState();
                    EndMiningLogRun(InventoryCleanupOwner.Kopacz533, "Kopanie zatrzymane: usunięto bind Kopacza.");
                    break;

                case BindTarget.Kopacz633:
                    _kopacz633BindWasDown = false;
                    _kopacz633RuntimeEnabled = false;
                    SetKopacz633AttackHold(false);
                    SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                    ResetKopacz633RuntimeState();
                    EndMiningLogRun(InventoryCleanupOwner.Kopacz633, "Kopanie zatrzymane: usunięto bind Kopacza.");
                    break;

                case BindTarget.TestAutoFishing:
                    _testAutoFishingBindWasDown = false;
                    _testAutoFishingRuntimeEnabled = false;
                    ResetTestAutoFishingRuntimeState();
                    UpdateTestAutoFishingStatusLabel();
                    break;
                case BindTarget.TestAutoFishingCaptureArea:
                    _testAutoFishingCaptureBindWasDown = false;
                    break;
            }

            RefreshBindSaveButton(target);
            RefreshOverlayHud(DateTime.UtcNow);

            if (hadBind)
            {
                MarkDirty();
                UpdateStatusBar($"Usunięto bind dla {GetBindTargetLabel(target)}", "Orange");
            }
            else
            {
                UpdateStatusBar($"{GetBindTargetLabel(target)}: bind jest już pusty", "Orange");
            }
        }

        private void BtnKopacz533Capture_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPendingBind(BindTarget.Kopacz533);
        }

        private void BtnKopacz633Capture_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPendingBind(BindTarget.Kopacz633);
        }

        private void BtnTestAutoFishingBind_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPendingBind(BindTarget.TestAutoFishing);
        }

        private void BtnTestAutoFishingCaptureBind_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPendingBind(BindTarget.TestAutoFishingCaptureArea);
        }

        private void BtnChatOpenKeySave_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPendingBind(BindTarget.ChatOpen);
        }

        private void BtnDropItemKeySave_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPendingBind(BindTarget.DropItem);
        }

        private void BtnResetMinecraftControlKey_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi || sender is not Button button)
                return;
            if (!Enum.TryParse(button.Tag?.ToString(), out BindTarget target)
                || !IsMinecraftControlKeyTarget(target))
            {
                return;
            }

            string defaultKey = target == BindTarget.ChatOpen ? "T" : "Q";
            TextBox? textBox = GetBindTextBox(target);
            if (textBox == null)
                return;

            textBox.Text = defaultKey;
            _pendingBindValues.Remove(target);
            if (_bindCaptureTarget == target)
                _bindCaptureTarget = BindTarget.None;
            RefreshBindSaveButton(target);
            UpdateBindCaptureVisuals();
            MarkDirty();
            UpdateStatusBar($"Przywrócono {defaultKey} dla: {GetBindTargetLabel(target)}.", "Green");
        }

        private async void BtnTestAutoFishingSelectArea_Click(object sender, RoutedEventArgs e)
        {
            await BeginTestAutoFishingAreaSelectionAsync(triggeredByBind: false);
        }

        private void BtnTestAutoFishingResetArea_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;
            if (_isCaptureAreaSelectionInProgress)
            {
                UpdateStatusBar("Najpierw zakończ zaznaczanie obszaru", "Orange");
                return;
            }

            _settings.TestAutoFishingCaptureX = 0;
            _settings.TestAutoFishingCaptureY = 0;
            _settings.TestAutoFishingCaptureWidth = 0;
            _settings.TestAutoFishingCaptureHeight = 0;
            _testAutoFishingRuntimeEnabled = false;
            ResetTestAutoFishingRuntimeState();
            UpdateTestAutoFishingAreaInfo();
            UpdateTestAutoFishingStatusLabel();
            MarkDirty();
            RefreshOverlayHud(DateTime.UtcNow);
            UpdateStatusBar("Zresetowano obszar auto-łowienia.", "Green");
        }

        private void BeginTestAutoFishingAreaSelectionFromBind()
        {
            if (_isCaptureAreaSelectionInProgress)
                return;

            _ = BeginTestAutoFishingAreaSelectionAsync(triggeredByBind: true);
        }

        private async Task BeginTestAutoFishingAreaSelectionAsync(bool triggeredByBind)
        {
            if (_isCaptureAreaSelectionInProgress || _isLoadingUi)
                return;
            if (triggeredByBind && !_isMinecraftFocused)
            {
                UpdateStatusBar("Najpierw ustaw fokus na okno Minecrafta", "Orange");
                return;
            }
            if (!TryResolveTargetWindow(allowPendingSelection: !triggeredByBind, out IntPtr targetWindowHandle))
            {
                UpdateStatusBar("Wybierz okno gry Minecraft (nie launcher) w zakładce Okno gry i zapisz je", "Orange");
                return;
            }
            if (!TryGetWindowClientRectOnScreen(targetWindowHandle, out RECT clientRect))
            {
                UpdateStatusBar("Nie mogę odczytać rozmiaru okna gry", "Orange");
                return;
            }

            int clientWidth = Math.Max(0, clientRect.Right - clientRect.Left);
            int clientHeight = Math.Max(0, clientRect.Bottom - clientRect.Top);
            if (clientWidth < MinimumCaptureSelectionSize || clientHeight < MinimumCaptureSelectionSize)
            {
                UpdateStatusBar($"Okno gry musi mieć co najmniej {MinimumCaptureSelectionSize}x{MinimumCaptureSelectionSize} px do zaznaczania", "Orange");
                return;
            }

            _isCaptureAreaSelectionInProgress = true;
            bool restoreWindowAfterSelection = !triggeredByBind && IsVisible;
            try
            {
                if (restoreWindowAfterSelection)
                {
                    Hide();
                    await Task.Delay(120);
                }

                UpdateStatusBar("Zaznacz obszar spławika: przytrzymaj LPM i przeciągnij (Esc anuluje)", "Orange");
                Drawing.Rectangle clientBounds = new Drawing.Rectangle(clientRect.Left, clientRect.Top, clientWidth, clientHeight);
                Drawing.Rectangle? screenSelection = await Task.Run(() => CaptureScreenSelectionWithinBounds(clientBounds));
                if (!screenSelection.HasValue)
                {
                    UpdateStatusBar(triggeredByBind
                        ? "Bind auto-łowienia: anulowano zaznaczanie obszaru"
                        : "Anulowano zaznaczanie obszaru auto-łowienia", "Orange");
                    return;
                }

                Drawing.Rectangle selectedRect = screenSelection.Value;
                int relativeX = Math.Clamp(selectedRect.Left - clientRect.Left, 0, clientWidth - MinimumCaptureSelectionSize);
                int relativeY = Math.Clamp(selectedRect.Top - clientRect.Top, 0, clientHeight - MinimumCaptureSelectionSize);
                int relativeWidth = Math.Clamp(selectedRect.Width, MinimumCaptureSelectionSize, clientWidth - relativeX);
                int relativeHeight = Math.Clamp(selectedRect.Height, MinimumCaptureSelectionSize, clientHeight - relativeY);
                _settings.TestAutoFishingCaptureX = relativeX;
                _settings.TestAutoFishingCaptureY = relativeY;
                _settings.TestAutoFishingCaptureWidth = relativeWidth;
                _settings.TestAutoFishingCaptureHeight = relativeHeight;
                ResetTestAutoFishingRuntimeState();
                UpdateTestAutoFishingAreaInfo();
                UpdateTestAutoFishingStatusLabel();
                MarkDirty();
                UpdateStatusBar($"Zapisano obszar spławika: x={relativeX}, y={relativeY}, {relativeWidth}x{relativeHeight}", "Green");
            }
            finally
            {
                if (restoreWindowAfterSelection)
                {
                    Show();
                    Activate();
                }

                _isCaptureAreaSelectionInProgress = false;
                RefreshTestAutoFishingPreview(DateTime.UtcNow, force: true);
            }
        }

        private static Drawing.Rectangle? CaptureScreenSelectionWithinBounds(Drawing.Rectangle bounds)
        {
            if (bounds.Width < MinimumCaptureSelectionSize || bounds.Height < MinimumCaptureSelectionSize)
                return null;

            Drawing.Rectangle previousFrame = Drawing.Rectangle.Empty;
            bool frameDrawn = false;
            bool dragStarted = false;
            Drawing.Point dragStart = Drawing.Point.Empty;

            while (true)
            {
                if (IsVirtualKeyDown(VK_ESCAPE))
                    return null;

                if (!GetCursorPos(out POINT cursorPointRaw))
                {
                    Thread.Sleep(10);
                    continue;
                }

                Drawing.Point cursorPoint = ClampPointToBounds(new Drawing.Point(cursorPointRaw.X, cursorPointRaw.Y), bounds);
                bool leftDown = IsVirtualKeyDown(VK_LBUTTON);

                if (!dragStarted)
                {
                    if (leftDown)
                    {
                        dragStarted = true;
                        dragStart = cursorPoint;
                    }

                    Thread.Sleep(10);
                    continue;
                }

                Drawing.Rectangle currentFrame = CreateNormalizedSelectionRect(dragStart, cursorPoint, bounds);
                if (frameDrawn)
                    DrawReversibleSelectionFrame(previousFrame);

                if (leftDown)
                {
                    if (currentFrame.Width >= 2 && currentFrame.Height >= 2)
                    {
                        DrawReversibleSelectionFrame(currentFrame);
                        previousFrame = currentFrame;
                        frameDrawn = true;
                    }
                    else
                    {
                        frameDrawn = false;
                    }

                    Thread.Sleep(10);
                    continue;
                }

                if (frameDrawn)
                    DrawReversibleSelectionFrame(previousFrame);

                if (currentFrame.Width < 24 || currentFrame.Height < 24)
                    return null;

                return currentFrame;
            }
        }

        private static void DrawReversibleSelectionFrame(Drawing.Rectangle frameRect)
        {
            if (frameRect.Width <= 0 || frameRect.Height <= 0)
                return;

            Forms.ControlPaint.DrawReversibleFrame(frameRect, Drawing.Color.White, Forms.FrameStyle.Dashed);
        }

        private static Drawing.Point ClampPointToBounds(Drawing.Point point, Drawing.Rectangle bounds)
        {
            int clampedX = Math.Clamp(point.X, bounds.Left, bounds.Right - 1);
            int clampedY = Math.Clamp(point.Y, bounds.Top, bounds.Bottom - 1);
            return new Drawing.Point(clampedX, clampedY);
        }

        private static Drawing.Rectangle CreateNormalizedSelectionRect(Drawing.Point start, Drawing.Point end, Drawing.Rectangle bounds)
        {
            int left = Math.Clamp(Math.Min(start.X, end.X), bounds.Left, bounds.Right - 1);
            int right = Math.Clamp(Math.Max(start.X, end.X), bounds.Left + 1, bounds.Right);
            int top = Math.Clamp(Math.Min(start.Y, end.Y), bounds.Top, bounds.Bottom - 1);
            int bottom = Math.Clamp(Math.Max(start.Y, end.Y), bounds.Top + 1, bounds.Bottom);
            return Drawing.Rectangle.FromLTRB(left, top, right, bottom);
        }

        // DODAWANIE KOMEND
        private void BtnKopacz533AddCommand_Click(object sender, RoutedEventArgs e)
        {
            _settings.Kopacz533Commands.Add(new MinerCommand { Seconds = 3, Command = "/repair" });
            RefreshKopaczCommandsUI(533);
            MarkDirty();
        }

        private void BtnKopacz633AddCommand_Click(object sender, RoutedEventArgs e)
        {
            _settings.Kopacz633Commands.Add(new MinerCommand { Seconds = 3, Command = "/repair" });
            RefreshKopaczCommandsUI(633);
            MarkDirty();
        }

        // AUTO-SAVE
        private void AnyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            MarkDirty();
            RefreshOverlayHud(DateTime.UtcNow);
        }

        // Podsumowanie historii dla statusu zdalnego i HUD; szczegóły pokazuje zakładka Logi.
        private void RefreshMiningLogsSummary()
        {
            _latestMiningLogSummary = _miningLogService.GetSummary();
        }

        private void TxtTestAutoFishingRepairConfig_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            MarkDirty();
            if (_testAutoFishingRuntimeEnabled)
                ResetTestAutoFishingRuntimeState(DateTime.UtcNow);
            UpdateTestAutoFishingStatusLabel();
            RefreshOverlayHud(DateTime.UtcNow);
        }

        private void CbTargetProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            ProcessTargetOption? selected = GetSelectedTargetProcessOption();
            if (selected != null)
            {
                TxtTargetWindowTitle.Text = selected.WindowTitle;
                UpdateStatusBar("Niezapisany wybór procesu - kliknij \"Zapisz program\"", "Orange");
            }
        }

        private void BtnRefreshTargetProcessList_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            RefreshTargetProcessChoices();
            ProcessTargetOption? selected = GetSelectedTargetProcessOption();
            if (selected == null)
                UpdateStatusBar("Odświeżono listę procesów. Wybierz proces gry.", "Orange");
            else
                UpdateStatusBar("Odświeżono listę procesów.", "Green");
        }

        private void MarkDirty()
        {
            _pendingChanges = true;

            TxtSettingsSaved.Text = "✗ Nie";
            TxtSettingsSaved.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));
            EllSettingsSaved.Fill = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));

            _dirtyTimer.Stop();
            _dirtyTimer.Start();
        }

        private void AutoSaveSettings()
        {
            try
            {
                ReadFromUi(includeWindowTitle: false);
                _settingsService.Save(_settings);

                _pendingChanges = false;

                TxtSettingsSaved.Text = "✓ Tak";
                TxtSettingsSaved.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));
                EllSettingsSaved.Fill = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));

                UpdateStatusBar("Ustawienia zapisane", "Green");
            }
            catch (Exception ex)
            {
                _pendingChanges = true;

                TxtSettingsSaved.Text = "Błąd";
                TxtSettingsSaved.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));
                EllSettingsSaved.Fill = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));

                UpdateStatusBar("Błąd zapisu: " + ex.Message, "Red");
            }
        }

        private void ReadFromUi(bool includeWindowTitle = true)
        {
            // KOPACZ
            _settings.Kopacz533Enabled = ChkKopacz533Enabled.IsChecked ?? false;
            _settings.Kopacz633Enabled = ChkKopacz633Enabled.IsChecked ?? false;
            _settings.Kopacz533Key = TxtKopacz533Key.Text.Trim();
            _settings.Kopacz633Key = TxtKopacz633Key.Text.Trim();

            if (CbKopacz633Direction.SelectedIndex == 1)
            {
                _settings.Kopacz633Direction = "Na wprost";
                _settings.Kopacz633Width = ParseNonNegativeInt(TxtKopacz633Width.Text);
            }
            else if (IsKopacz633RectangleMode())
            {
                _settings.Kopacz633Direction = "Do góry";
                _settings.Kopacz633Width = ParseNonNegativeInt(TxtKopacz633WidthUp.Text);
                _settings.Kopacz633Length = ParseNonNegativeInt(TxtKopacz633LengthUp.Text);
            }
            else
            {
                _settings.Kopacz633Direction = "";
            }

            _settings.InventoryCleanupEnabled = ChkInventoryCleanupEnabled.IsChecked == true;
            _settings.InventoryCleanupIntervalSeconds = GetConfiguredInventoryCleanupIntervalSeconds();
            _settings.InventoryCleanupDiscardAllItemTypes = ChkInventoryCleanupAllItemTypes.IsChecked == true;
            _settings.InventoryCleanupEatAfterCleanup = ChkInventoryCleanupEatAfterCleanup.IsChecked == true;
            _settings.InventoryCleanupSlots = GetSelectedInventoryCleanupSlots().OrderBy(slot => slot).ToList();
            _settings.InventoryCleanupItemTypes = GetSelectedInventoryCleanupItemTypes()
                .OrderBy(itemId => itemId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _settings.CobbleXEnabled = ChkCobbleXEnabled.IsChecked == true;
            _settings.CobbleXRepeatThreeTimes = ChkCobbleXRepeatThreeTimes.IsChecked == true;
            _settings.BackgroundMiningEnabled = ChkBackgroundMiningEnabled.IsChecked == true;
            _settings.BackgroundGameShieldEnabled = ChkGameShieldEnabled.IsChecked == true;
            _settings.CobbleXCommand = GetConfiguredCobbleXCommand();
            _settings.CobbleXRequiredFullStacks = GetConfiguredCobbleXRequiredStacks();
            SaveGoldDepositSettings();

            _settings.TestAutoFishingEnabled = ChkTestAutoFishingEnabled.IsChecked ?? false;
            _settings.TestAutoFishingBind = TxtTestAutoFishingBind.Text.Trim();
            _settings.TestAutoFishingCaptureBind = TxtTestAutoFishingCaptureBind.Text.Trim();
            _settings.TestAutoFishingRepairCommand = TxtTestAutoFishingRepairCommand.Text.Trim();
            _settings.TestAutoFishingRepairEverySeconds = Math.Clamp(ParseNonNegativeInt(TxtTestAutoFishingRepairEverySeconds.Text), 0, TestAutoFishingRepairIntervalMaxSeconds);
            _settings.EmergencyDamageSoundEnabled = ChkEmergencyDamageSoundEnabled.IsChecked == true;
            _settings.EmergencyDamageSoundTestMode = ChkEmergencyDamageSoundTestMode.IsChecked != false;
            _settings.EmergencyDamageSoundDeviceId = GetSelectedEmergencyDamageSoundDeviceId();
            _settings.EmergencyDamageSoundSimilarityPercent = Math.Clamp(
                (int)Math.Round(SlEmergencyDamageSoundSimilarity.Value),
                70,
                99);
            _settings.EmergencyReconnectEnabled = RbProtectionReturn.IsChecked == true;
            _settings.EmergencyReconnectDelaySeconds = Math.Clamp(
                ParseNonNegativeInt(TxtEmergencyReconnectDelaySeconds.Text),
                1,
                600);
            _settings.AutoReconnectEnabled = ChkAutoReconnectEnabled.IsChecked == true;
            AutoReconnectServerProfile? selectedAutoReconnectProfile = GetSelectedAutoReconnectServerProfile();
            if (selectedAutoReconnectProfile != null)
                ApplyAutoReconnectServerProfile(selectedAutoReconnectProfile, updateUi: false);
            _settings.OverlayHudEnabled = ChkOverlayHudEnabled.IsChecked == true;
            _settings.OverlayAnimationsEnabled = ChkOverlayAnimationsEnabled.IsChecked == true;
            _settings.AnimatedBackgroundEnabled = ChkAnimatedBackgroundEnabled.IsChecked != false;
            _settings.ChatOpenKey = NormalizeMinecraftControlKey(TxtChatOpenKey.Text, "T");
            _settings.DropItemKey = NormalizeMinecraftControlKey(TxtDropItemKey.Text, "Q");
            _settings.OverlayMonitorIndex = Math.Max(0, CbOverlayMonitor?.SelectedIndex ?? 0);
            _settings.OverlayCorner = ToOverlayCornerSetting(GetSelectedOverlayCorner());

            if (includeWindowTitle)
            {
                ProcessTargetOption? selectedProcess = GetSelectedTargetProcessOption();
                if (selectedProcess != null)
                {
                    _settings.TargetProcessId = selectedProcess.ProcessId;
                    _settings.TargetProcessName = selectedProcess.ProcessName;
                    _settings.TargetWindowTitle = selectedProcess.WindowTitle;
                    TxtTargetWindowTitle.Text = selectedProcess.WindowTitle;
                }
                else
                {
                    _settings.TargetProcessId = 0;
                    _settings.TargetProcessName = string.Empty;
                    _settings.TargetWindowTitle = TxtTargetWindowTitle.Text.Trim();
                }

                TxtCurrentWindowTitle.Text = BuildTargetProcessDisplayText();
            }
        }

        private static int ParseNonNegativeInt(string value)
        {
            if (int.TryParse(value, out int parsed) && parsed >= 0)
                return parsed;
            return 0;
        }

        private bool TryGetTestAutoFishingCaptureArea(IntPtr windowHandle, out Drawing.Rectangle captureArea)
        {
            captureArea = Drawing.Rectangle.Empty;
            if (!HasTestAutoFishingAreaConfigured())
                return false;
            if (!TryGetWindowClientRectOnScreen(windowHandle, out RECT clientRect))
                return false;

            int clientWidth = Math.Max(0, clientRect.Right - clientRect.Left);
            int clientHeight = Math.Max(0, clientRect.Bottom - clientRect.Top);
            if (clientWidth < MinimumCaptureSelectionSize || clientHeight < MinimumCaptureSelectionSize)
                return false;

            int offsetX = Math.Clamp(_settings.TestAutoFishingCaptureX, 0, Math.Max(0, clientWidth - MinimumCaptureSelectionSize));
            int offsetY = Math.Clamp(_settings.TestAutoFishingCaptureY, 0, Math.Max(0, clientHeight - MinimumCaptureSelectionSize));
            int width = Math.Clamp(_settings.TestAutoFishingCaptureWidth, MinimumCaptureSelectionSize, Math.Max(MinimumCaptureSelectionSize, clientWidth - offsetX));
            int height = Math.Clamp(_settings.TestAutoFishingCaptureHeight, MinimumCaptureSelectionSize, Math.Max(MinimumCaptureSelectionSize, clientHeight - offsetY));
            if (offsetX + width > clientWidth)
                width = clientWidth - offsetX;
            if (offsetY + height > clientHeight)
                height = clientHeight - offsetY;
            if (width < MinimumCaptureSelectionSize || height < MinimumCaptureSelectionSize)
                return false;

            captureArea = new Drawing.Rectangle(clientRect.Left + offsetX, clientRect.Top + offsetY, width, height);
            return true;
        }

        private static bool TryGetWindowClientRectOnScreen(IntPtr windowHandle, out RECT clientRectOnScreen)
        {
            clientRectOnScreen = default;
            if (windowHandle == IntPtr.Zero)
                return false;
            if (!GetClientRect(windowHandle, out RECT clientRect))
                return false;

            POINT topLeft = new POINT { X = clientRect.Left, Y = clientRect.Top };
            POINT bottomRight = new POINT { X = clientRect.Right, Y = clientRect.Bottom };
            if (!ClientToScreen(windowHandle, ref topLeft))
                return false;
            if (!ClientToScreen(windowHandle, ref bottomRight))
                return false;

            clientRectOnScreen = new RECT
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = bottomRight.X,
                Bottom = bottomRight.Y
            };

            return true;
        }

        private bool EnsureF3TesseractEngine()
        {
            if (_f3TesseractEngine != null)
                return true;

            string? tessDataPath = ResolveTessDataPath();
            if (string.IsNullOrWhiteSpace(tessDataPath))
                return false;

            try
            {
                _f3TesseractEngine = new TesseractEngine(tessDataPath, "eng", TesseractEngineMode.Default);
                _f3TesseractEngine.DefaultPageSegMode = TesseractPageSegMode.SparseText;
                _f3TesseractEngine.SetVariable("tessedit_char_whitelist", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,:/;|\\-()[] ");
                _f3TesseractEngine.SetVariable("preserve_interword_spaces", "1");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("OCR", "Nie udało się uruchomić Tesseract (rozpoznawanie tekstu) — reconnect i wpłata złota nie odczytają ekranu", ex);
                _f3TesseractEngine?.Dispose();
                _f3TesseractEngine = null;
                return false;
            }
        }

        private static string? ResolveTessDataPath()
        {
            string baseDirectory = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "tessdata"),
                Path.Combine(baseDirectory, "x64", "tessdata"),
                Path.Combine(baseDirectory, "runtimes", "win-x64", "native", "tessdata")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "eng.traineddata")))
                    return candidate;
            }

            return null;
        }

        private string RunOcrOnBitmap(Drawing.Bitmap bitmap, TesseractPageSegMode pageSegMode)
        {
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, DrawingImaging.ImageFormat.Png);
            byte[] imageBytes = memoryStream.ToArray();

            lock (_f3TesseractLock)
            {
                if (_f3TesseractEngine == null)
                    return string.Empty;

                TesseractPageSegMode previousMode = _f3TesseractEngine.DefaultPageSegMode;
                using TesseractPix pix = TesseractPix.LoadFromMemory(imageBytes);
                try
                {
                    _f3TesseractEngine.DefaultPageSegMode = pageSegMode;
                    using TesseractPage page = _f3TesseractEngine.Process(pix);
                    return page.GetText() ?? string.Empty;
                }
                finally
                {
                    _f3TesseractEngine.DefaultPageSegMode = previousMode;
                }
            }
        }

        private static Drawing.Bitmap PrepareBitmapForOcr(Drawing.Bitmap source)
        {
            const int scale = 2;
            var scaled = new Drawing.Bitmap(source.Width * scale, source.Height * scale, DrawingImaging.PixelFormat.Format32bppArgb);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = Drawing2D.SmoothingMode.None;
                graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half;
                graphics.CompositingQuality = Drawing2D.CompositingQuality.HighSpeed;
                graphics.DrawImage(
                    source,
                    new Drawing.Rectangle(0, 0, scaled.Width, scaled.Height),
                    new Drawing.Rectangle(0, 0, source.Width, source.Height),
                    Drawing.GraphicsUnit.Pixel);
            }

            Drawing.Rectangle rect = new Drawing.Rectangle(0, 0, scaled.Width, scaled.Height);
            DrawingImaging.BitmapData bitmapData = scaled.LockBits(rect, DrawingImaging.ImageLockMode.ReadWrite, DrawingImaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = bitmapData.Stride;
                int absStride = Math.Abs(stride);
                int bytes = absStride * bitmapData.Height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(bitmapData.Scan0, buffer, 0, bytes);

                for (int y = 0; y < bitmapData.Height; y++)
                {
                    int rowOffset = stride >= 0 ? y * stride : (bitmapData.Height - 1 - y) * absStride;
                    for (int x = 0; x < bitmapData.Width; x++)
                    {
                        int pixelOffset = rowOffset + x * 4;
                        byte b = buffer[pixelOffset];
                        byte g = buffer[pixelOffset + 1];
                        byte r = buffer[pixelOffset + 2];

                        int max = Math.Max(r, Math.Max(g, b));
                        int min = Math.Min(r, Math.Min(g, b));
                        int luminance = (r * 299 + g * 587 + b * 114) / 1000;
                        int saturationRange = max - min;

                        bool likelyWhiteText = saturationRange <= 38 && luminance >= 165;
                        bool likelyLightGrayText = saturationRange <= 24 && luminance >= 142;
                        bool likelyText = likelyWhiteText || likelyLightGrayText;
                        if (!likelyText && saturationRange <= 14 && luminance >= 180)
                            likelyText = true;
                        byte value = likelyText ? (byte)0 : (byte)255;

                        buffer[pixelOffset] = value;
                        buffer[pixelOffset + 1] = value;
                        buffer[pixelOffset + 2] = value;
                        buffer[pixelOffset + 3] = 255;
                    }
                }

                Marshal.Copy(buffer, 0, bitmapData.Scan0, bytes);
            }
            finally
            {
                scaled.UnlockBits(bitmapData);
            }

            return scaled;
        }

        private string GetRuntimeStateLabel(bool enabled)
        {
            if (!enabled)
                return "OFF";
            return "ON";
        }

        private static bool TryGetVisibleCursorInfo(out CURSORINFO cursorInfo)
        {
            cursorInfo = new CURSORINFO
            {
                cbSize = Marshal.SizeOf<CURSORINFO>()
            };

            if (!GetCursorInfo(out cursorInfo))
                return false;

            return (cursorInfo.flags & CURSOR_SHOWING) == CURSOR_SHOWING;
        }

        private bool IsInventoryCursorVisible()
        {
            if (!TryGetVisibleCursorInfo(out CURSORINFO cursorInfo))
                return false;

            // LWJGL 2 keeps the Windows cursor technically "visible" while gameplay
            // is active, but replaces it with a fully transparent cursor. This is
            // especially noticeable in fullscreen and must not be treated as GUI.
            if (IsCursorVisuallyBlank(cursorInfo.hCursor))
                return false;

            return true;
        }

        private static bool IsCursorVisuallyBlank(IntPtr cursorHandle)
        {
            if (cursorHandle == IntPtr.Zero)
                return false;

            lock (CursorAppearanceCacheLock)
            {
                if (CursorBlankAppearanceCache.TryGetValue(cursorHandle, out bool cached))
                    return cached;
            }

            bool isBlank = DetectBlankCursor(cursorHandle);
            lock (CursorAppearanceCacheLock)
            {
                if (CursorBlankAppearanceCache.Count >= 32)
                    CursorBlankAppearanceCache.Clear();
                CursorBlankAppearanceCache[cursorHandle] = isBlank;
            }

            return isBlank;
        }

        private static bool DetectBlankCursor(IntPtr cursorHandle)
        {
            int width = Math.Clamp(GetSystemMetrics(SM_CXCURSOR), 1, 128);
            int height = Math.Clamp(GetSystemMetrics(SM_CYCURSOR), 1, 128);

            return !CursorChangesBackground(cursorHandle, width, height, Drawing.Color.Black)
                && !CursorChangesBackground(cursorHandle, width, height, Drawing.Color.White);
        }

        private static bool CursorChangesBackground(
            IntPtr cursorHandle,
            int width,
            int height,
            Drawing.Color background)
        {
            using var bitmap = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format32bppArgb);
            using Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(background);

            IntPtr hdc = graphics.GetHdc();
            bool drawn;
            try
            {
                drawn = DrawIconEx(
                    hdc,
                    0,
                    0,
                    cursorHandle,
                    width,
                    height,
                    0,
                    IntPtr.Zero,
                    DI_NORMAL);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            // If Windows refuses to render the cursor, keep the safety pause enabled.
            if (!drawn)
                return true;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Drawing.Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R != background.R || pixel.G != background.G || pixel.B != background.B)
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetVirtualKey(string keyText, out int virtualKey)
        {
            virtualKey = 0;

            if (string.IsNullOrWhiteSpace(keyText))
                return false;
            string normalized = keyText.Trim();
            switch (normalized.ToUpperInvariant())
            {
                case "ENTER":
                case "RETURN":
                    virtualKey = VK_RETURN;
                    return true;
                case "MOUSEMIDDLE":
                    virtualKey = VK_MBUTTON;
                    return true;
                case "MOUSEX1":
                    virtualKey = VK_XBUTTON1;
                    return true;
                case "MOUSEX2":
                    virtualKey = VK_XBUTTON2;
                    return true;
            }

            if (!Enum.TryParse(normalized, true, out Key key))
                return false;

            virtualKey = KeyInterop.VirtualKeyFromKey(key);
            return virtualKey != 0;
        }

        private static bool IsSupportedMinecraftControlKey(string keyText)
        {
            if (!TryGetVirtualKey(keyText, out int virtualKey))
                return false;

            return virtualKey is not VK_LBUTTON
                and not VK_RBUTTON
                and not VK_MBUTTON
                and not VK_XBUTTON1
                and not VK_XBUTTON2;
        }

        private static string NormalizeMinecraftControlKey(string? keyText, string fallback)
        {
            string normalized = string.IsNullOrWhiteSpace(keyText) ? fallback : keyText.Trim();
            if (string.Equals(normalized, "Return", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Enter", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Enter";
            }
            return IsSupportedMinecraftControlKey(normalized) ? normalized : fallback;
        }

        private int GetConfiguredChatOpenVirtualKey()
        {
            string keyText = NormalizeMinecraftControlKey(TxtChatOpenKey?.Text, "T");
            return TryGetVirtualKey(keyText, out int virtualKey) ? virtualKey : VK_T;
        }

        private int GetConfiguredDropItemVirtualKey()
        {
            string keyText = NormalizeMinecraftControlKey(TxtDropItemKey?.Text, "Q");
            return TryGetVirtualKey(keyText, out int virtualKey) ? virtualKey : VK_Q;
        }

        private void SendChatOpenKeyTap()
        {
            if (BackgroundGameWindow.IsActive)
                BackgroundGameWindow.PrepareCursorTransition();
            SendKeyTap(GetConfiguredChatOpenVirtualKey());
            // Etapy wpisywania czekają na ChatInputGate.IsReady, inaczej „/” może przepaść.
            bool background = BackgroundGameWindow.IsActive;
            ChatInputGate.Arm(background ? BackgroundGameWindow.Window : _targetGameWindowHandle, postedInput: background);
        }

        private static bool IsVirtualKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static bool IsBindPressed(string keyText, ref bool wasDown)
        {
            if (!TryGetVirtualKey(keyText, out int virtualKey))
            {
                wasDown = false;
                return false;
            }

            bool isDown = IsVirtualKeyDown(virtualKey);
            bool justPressed = isDown && !wasDown;
            wasDown = isDown;
            return justPressed;
        }

        private static bool IsConfiguredBindKeyDown(string keyText)
        {
            return TryGetVirtualKey(keyText, out int virtualKey) && IsVirtualKeyDown(virtualKey);
        }

        private void RunMacroTick(object? sender, EventArgs e)
        {
            try
            {
                RunMacroTickCore();
            }
            finally
            {
                // Also runs after early returns (focus loss, bind capture, etc.).
                UpdateEmergencyDamageSoundMonitoring();
                UpdateMacroTimerCadence();
            }
        }

        private void UpdateMacroTimerCadence()
        {
            TimeSpan targetInterval = IsFastMacroCadenceRequired()
                ? MacroTimerActiveInterval
                : MacroTimerIdleInterval;
            if (_macroTimer.Interval != targetInterval)
                _macroTimer.Interval = targetInterval;
        }

        private bool IsFastMacroCadenceRequired()
        {
            return _kopacz533RuntimeEnabled
                || _kopacz633RuntimeEnabled
                || _testAutoFishingRuntimeEnabled
                || _inventoryCleanupStage != InventoryCleanupStage.None
                || _inventoryCleanupScanInProgress
                || _autoReconnectStage != AutoReconnectStage.None
                || _kopacz533CommandStage != Kopacz533CommandStage.None
                || _kopacz633CommandStage != Kopacz633CommandStage.None
                || _testAutoFishingRepairStage != TestAutoFishingRepairStage.None
                || _inventoryCleanupEatingRightButtonDown
                || _kopacz533Holding
                || _kopacz633HoldingAttack
                || _kopacz633StrafeDirection != Kopacz633StrafeDirection.None
                || _emergencyDamageSoundHandlingAlarm;
        }

        // Bindy zapamiętują stan klawisza, żeby po powrocie fokusu albo po wpisanej
        // komendzie nie zadziałały od razu same z siebie.
        private void SyncMinerBindStates()
        {
            _kopacz533BindWasDown = IsConfiguredBindKeyDown(TxtKopacz533Key.Text);
            _kopacz633BindWasDown = IsConfiguredBindKeyDown(TxtKopacz633Key.Text);
            _testAutoFishingBindWasDown = IsConfiguredBindKeyDown(TxtTestAutoFishingBind.Text);
            _testAutoFishingCaptureBindWasDown = IsConfiguredBindKeyDown(TxtTestAutoFishingCaptureBind.Text);
        }

        private void RunMacroTickCore()
        {
            if (_isLoadingUi)
            {
                SetInventoryCleanupEatingHold(false);
                return;
            }

            if (TryRunGoldDepositManualTest(DateTime.UtcNow))
                return;

            if (_bindCaptureTarget != BindTarget.None)
            {
                SetInventoryCleanupEatingHold(false);
                return;
            }

            if (_suppressBindToggleUntilRelease)
            {
                SyncMinerBindStates();
                if (_kopacz533BindWasDown || _kopacz633BindWasDown || _testAutoFishingBindWasDown || _testAutoFishingCaptureBindWasDown)
                {
                    SetInventoryCleanupEatingHold(false);
                    return;
                }

                _suppressBindToggleUntilRelease = false;
            }

            // Do an immediate handle check as well as the slower focus timer. This
            // prevents even a short burst of input from leaking after Alt+Tab.
            bool targetWindowFocusedNow = _targetGameWindowHandle != IntPtr.Zero
                && GetForegroundWindow() == _targetGameWindowHandle;
            // The immediate handle check decides: the 250 ms focus timer would keep the
            // background session alive briefly after the user clicks back into the game.
            bool backgroundInput = UpdateBackgroundMiningSession(targetWindowFocusedNow);

            // Bind toggles can be changed only while Minecraft window has focus.
            // Background mining keeps the runtime going, but binds stay blocked below.
            if (!backgroundInput && (!_isMinecraftFocused || !targetWindowFocusedNow))
            {
                if (IsGoldDepositActive)
                {
                    StopGoldDeposit("Minecraft stracił fokus podczas wpłaty złota. Sprawdź przedmiot na kursorze.");
                }
                else if (_inventoryCleanupStage == InventoryCleanupStage.ReturnToMiningStart)
                {
                    DateTime focusLostAtUtc = DateTime.UtcNow;
                    ResetInventoryCleanupState(scheduleNext: false, focusLostAtUtc);
                    _nextInventoryCleanupAtUtc = focusLostAtUtc;
                    UpdateInventoryCleanupStatus("Powrót przed Auto EQ przerwany po utracie fokusu. Próba zostanie ponowiona po powrocie do gry.", "Orange");
                }

                // Keep key state in sync to avoid accidental toggle right after refocus.
                SyncMinerBindStates();
                SetKopacz533MiningHold(false);
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetTestAutoFishingRuntimeState();
                SetInventoryCleanupEatingHold(false);
                return;
            }

            // STOP has priority over every mining stage, including open inventory,
            // command entry and reconnect. Starting a macro still uses the guards
            // below; this path can only stop work that is already active.
            // In background mode the keyboard belongs to another app, so binds are ignored.
            if (!backgroundInput && TryStopMiningFromBinds())
                return;

            if (_autoReconnectStage != AutoReconnectStage.None)
            {
                SetKopacz533MiningHold(false);
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                SetInventoryCleanupEatingHold(false);
                return;
            }

            bool changed = false;

            bool kop533ModeSelected = ChkKopacz533Enabled.IsChecked == true;
            bool kop633ModeSelected = ChkKopacz633Enabled.IsChecked == true;
            bool autoFishingModeSelected = ChkTestAutoFishingEnabled.IsChecked == true;
            // In background mode physical keys are typed into another app; treat them
            // exactly like internally typed chat commands, so no bind can toggle.
            bool internalCommandTyping =
                backgroundInput ||
                _kopacz533CommandStage != Kopacz533CommandStage.None ||
                _kopacz633CommandStage != Kopacz633CommandStage.None ||
                _testAutoFishingRepairStage != TestAutoFishingRepairStage.None ||
                _inventoryCleanupStage != InventoryCleanupStage.None;

            if (internalCommandTyping)
            {
                // Prevent self-trigger: internal typed keys (chat commands) cannot toggle bind states.
                SyncMinerBindStates();
            }

            if (!internalCommandTyping && autoFishingModeSelected && IsBindPressed(TxtTestAutoFishingCaptureBind.Text, ref _testAutoFishingCaptureBindWasDown))
                BeginTestAutoFishingAreaSelectionFromBind();

            if (!internalCommandTyping && IsBindPressed(TxtTestAutoFishingBind.Text, ref _testAutoFishingBindWasDown) && autoFishingModeSelected)
            {
                if (!HasTestAutoFishingAreaConfigured())
                {
                    _testAutoFishingRuntimeEnabled = false;
                    ResetTestAutoFishingRuntimeState(DateTime.UtcNow);
                    UpdateStatusBar("Auto łowienie: najpierw zaznacz obszar spławika", "Orange");
                }
                else
                {
                    bool enabling = !_testAutoFishingRuntimeEnabled;
                    _testAutoFishingRuntimeEnabled = enabling;
                    DateTime toggledAtUtc = DateTime.UtcNow;
                    if (enabling)
                    {
                        _testAutoFishingRuntimeStartedAtUtc = toggledAtUtc;
                        ResetTestAutoFishingCatchStats();
                    }
                    else
                    {
                        _testAutoFishingRuntimeStartedAtUtc = DateTime.MinValue;
                    }
                    ResetTestAutoFishingRuntimeState(toggledAtUtc);
                    if (enabling)
                        StartTestAutoFishingCastRegistrationDelay(toggledAtUtc);
                    UpdateStatusBar(enabling ? "Auto łowienie aktywowane" : "Auto łowienie wyłączone", "Orange");
                }
                UpdateTestAutoFishingStatusLabel();
                changed = true;
            }

            if (!internalCommandTyping && IsBindPressed(TxtKopacz533Key.Text, ref _kopacz533BindWasDown) && kop533ModeSelected)
            {
                ToggleKopacz533Runtime();
                changed = true;
            }

            if (!internalCommandTyping && IsBindPressed(TxtKopacz633Key.Text, ref _kopacz633BindWasDown) && kop633ModeSelected)
            {
                ToggleKopacz633Runtime();
                changed = true;
            }

            if (!kop533ModeSelected && _kopacz533RuntimeEnabled)
            {
                _kopacz533RuntimeEnabled = false;
                SetKopacz533MiningHold(false);
                ResetKopacz533RuntimeState();
                EndMiningLogRun(InventoryCleanupOwner.Kopacz533, "Kopanie zatrzymane: kanał nie jest już aktywny.");
                changed = true;
            }
            if (!kop633ModeSelected && _kopacz633RuntimeEnabled)
            {
                _kopacz633RuntimeEnabled = false;
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetKopacz633RuntimeState();
                EndMiningLogRun(InventoryCleanupOwner.Kopacz633, "Kopanie zatrzymane: kanał nie jest już aktywny.");
                changed = true;
            }
            if (!autoFishingModeSelected && _testAutoFishingRuntimeEnabled)
            {
                _testAutoFishingRuntimeEnabled = false;
                ResetTestAutoFishingRuntimeState();
                UpdateTestAutoFishingStatusLabel();
                changed = true;
            }

            if (changed)
                RefreshOverlayHud(DateTime.UtcNow);

            DateTime now = DateTime.UtcNow;
            if (kop533ModeSelected && _kopacz533RuntimeEnabled)
                RunKopacz533Tick(now);
            else
            {
                SetKopacz533MiningHold(false);
                ResetKopacz533RuntimeState(now);
            }

            if (kop633ModeSelected && _kopacz633RuntimeEnabled)
                RunKopacz633Tick(now);
            else
            {
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetKopacz633RuntimeState(now);
            }

            if (_inventoryCleanupStage == InventoryCleanupStage.None)
            {
                if (autoFishingModeSelected && _testAutoFishingRuntimeEnabled)
                    RunTestAutoFishingTick(now);
                else
                    ResetTestAutoFishingRuntimeState(now);
            }

            RefreshLiveOverlayHud(now);
        }

        private void StopOtherExclusivePointerMacros(
            bool keepKopacz533 = false,
            bool keepKopacz633 = false)
        {
            if (!keepKopacz533 && _kopacz533RuntimeEnabled)
            {
                _kopacz533RuntimeEnabled = false;
                SetKopacz533MiningHold(false);
                ResetKopacz533RuntimeState();
                EndMiningLogRun(InventoryCleanupOwner.Kopacz533, "Kopanie zatrzymane przez uruchomienie innego makra.");
            }

            if (!keepKopacz633 && _kopacz633RuntimeEnabled)
            {
                _kopacz633RuntimeEnabled = false;
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                ResetKopacz633RuntimeState();
                EndMiningLogRun(InventoryCleanupOwner.Kopacz633, "Kopanie zatrzymane przez uruchomienie innego makra.");
            }
        }

        private void RunTestAutoFishingTick(DateTime now)
        {
            if (_kopacz533CommandStage != Kopacz533CommandStage.None
                || _kopacz633CommandStage != Kopacz633CommandStage.None)
                return;

            if (_testAutoFishingRepairStage != TestAutoFishingRepairStage.None)
            {
                if (!TryProcessTestAutoFishingRepairCommand(now))
                    ResetTestAutoFishingRuntimeState(now);
                UpdateTestAutoFishingStatusLabel();
                return;
            }

            if (HasConfiguredTestAutoFishingRepair() && now >= _nextTestAutoFishingRepairAtUtc)
            {
                StartTestAutoFishingRepairCommand(now);
                UpdateTestAutoFishingStatusLabel();
                return;
            }

            if (_testAutoFishingRecastAfterRepairPending)
            {
                if (now < _testAutoFishingRecastAfterRepairAtUtc)
                    return;

                SendMouseClick(leftButton: false, holdPulseMode: false);
                _testAutoFishingRecastAfterRepairPending = false;
                _testAutoFishingAwaitSecondClick = true;
                _testAutoFishingWaitingForCastRegistration = false;
                _nextTestAutoFishingActionAtUtc = now.AddMilliseconds(TestAutoFishingSecondClickDelayMs);
                ResetTestAutoFishingDetection(now);
                UpdateTestAutoFishingStatusLabel();
                return;
            }

            if (_testAutoFishingAwaitSecondClick)
            {
                if (now < _nextTestAutoFishingActionAtUtc)
                    return;

                SendMouseClick(leftButton: false, holdPulseMode: false);
                _testAutoFishingAwaitSecondClick = false;
                StartTestAutoFishingCastRegistrationDelay(now);
                UpdateTestAutoFishingStatusLabel();
                return;
            }

            if (_testAutoFishingWaitingForCastRegistration)
            {
                if (now < _nextTestAutoFishingActionAtUtc)
                {
                    UpdateTestAutoFishingStatusLabel();
                    return;
                }

                _testAutoFishingWaitingForCastRegistration = false;
                _nextTestAutoFishingActionAtUtc = now;
                _nextTestAutoFishingScanAtUtc = now;
            }

            if (now < _nextTestAutoFishingActionAtUtc || now < _nextTestAutoFishingScanAtUtc)
                return;
            if (!TryGetTestAutoFishingCaptureArea(_targetGameWindowHandle, out Drawing.Rectangle captureArea))
            {
                _testAutoFishingRuntimeEnabled = false;
                ResetTestAutoFishingRuntimeState(now);
                UpdateTestAutoFishingStatusLabel();
                UpdateStatusBar("Auto łowienie: obszar spławika jest nieprawidłowy", "Orange");
                RefreshOverlayHud(DateTime.UtcNow);
                return;
            }

            _nextTestAutoFishingScanAtUtc = now.AddMilliseconds(TestAutoFishingScanIntervalMs);
            try
            {
                using Drawing.Bitmap bitmap = new Drawing.Bitmap(captureArea.Width, captureArea.Height, DrawingImaging.PixelFormat.Format32bppArgb);
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(captureArea.Left, captureArea.Top, 0, 0, captureArea.Size, Drawing.CopyPixelOperation.SourceCopy);

                if (!TryGetFishingBobberPosition(bitmap, out double bobberX, out double bobberY, out int redPixels))
                {
                    HandleMissingFishingBobber(now);
                    return;
                }

                _testAutoFishingMissedDetections = 0;
                _testAutoFishingNoBobberSinceAtUtc = DateTime.MinValue;
                _testAutoFishingLastDetectedBobberX = bobberX;
                _testAutoFishingLastDetectedBobberY = bobberY;
                _testAutoFishingLastDetectedRedPixels = redPixels;
                if (!_testAutoFishingBaselineReady)
                {
                    _testAutoFishingBaselineBobberY = bobberY;
                    _testAutoFishingBaselineReady = true;
                    _testAutoFishingBiteConfirmationFrames = 0;
                    _testAutoFishingBaselineArmedAtUtc = now.AddMilliseconds(TestAutoFishingBiteArmingDelayMs);
                    UpdateTestAutoFishingStatusLabel();
                    return;
                }

                bool armed = now >= _testAutoFishingBaselineArmedAtUtc;
                double drop = bobberY - _testAutoFishingBaselineBobberY;
                if (armed && drop >= TestAutoFishingDropThresholdPx)
                {
                    _testAutoFishingBiteConfirmationFrames++;
                    if (_testAutoFishingBiteConfirmationFrames >= TestAutoFishingRequiredBiteFrames)
                    {
                        TriggerTestAutoFishingCatch(now, "Auto łowienie: branie! PPM -> ponowne zarzucenie");
                        return;
                    }
                }
                else
                {
                    _testAutoFishingBiteConfirmationFrames = 0;
                }

                if (_testAutoFishingBiteConfirmationFrames == 0)
                {
                    double updateWeight = armed ? (drop > 0 ? 0.01 : 0.04) : 0.55;
                    _testAutoFishingBaselineBobberY = _testAutoFishingBaselineBobberY * (1.0 - updateWeight) + bobberY * updateWeight;
                }
                UpdateTestAutoFishingStatusLabel();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Łowienie", "Błąd analizy spławika: " + ex.GetBaseException().Message);
                HandleMissingFishingBobber(now);
            }
        }

        private void HandleMissingFishingBobber(DateTime now)
        {
            _testAutoFishingLastDetectedBobberX = double.NaN;
            _testAutoFishingLastDetectedBobberY = double.NaN;
            _testAutoFishingLastDetectedRedPixels = 0;
            _testAutoFishingBiteConfirmationFrames = 0;
            _testAutoFishingMissedDetections++;
            if (_testAutoFishingNoBobberSinceAtUtc == DateTime.MinValue)
                _testAutoFishingNoBobberSinceAtUtc = now;

            if (_testAutoFishingBaselineReady
                && now >= _testAutoFishingBaselineArmedAtUtc
                && _testAutoFishingMissedDetections >= TestAutoFishingLossTriggerMisses)
            {
                TriggerTestAutoFishingCatch(now, "Auto łowienie: branie (zanik spławika)");
                return;
            }

            if ((now - _testAutoFishingNoBobberSinceAtUtc).TotalMilliseconds >= TestAutoFishingNoBobberRecastMs)
            {
                SendMouseClick(leftButton: false, holdPulseMode: false);
                _testAutoFishingAwaitSecondClick = false;
                StartTestAutoFishingCastRegistrationDelay(now);
                UpdateTestAutoFishingStatusLabel();
                UpdateStatusBar("Auto łowienie: brak spławika przez 5s -> ponowny rzut (PPM)", "Orange");
                return;
            }

            if (_testAutoFishingMissedDetections >= TestAutoFishingMissTolerance)
                _testAutoFishingBaselineReady = false;
            UpdateTestAutoFishingStatusLabel();
        }

        private void TriggerTestAutoFishingCatch(DateTime now, string status)
        {
            RegisterTestAutoFishingCatch(now);
            SendMouseClick(leftButton: false, holdPulseMode: false);
            _testAutoFishingAwaitSecondClick = true;
            _testAutoFishingWaitingForCastRegistration = false;
            _nextTestAutoFishingActionAtUtc = now.AddMilliseconds(TestAutoFishingSecondClickDelayMs);
            ResetTestAutoFishingDetection(now);
            UpdateTestAutoFishingStatusLabel();
            UpdateStatusBar(status, "Orange");
        }

        private void StartTestAutoFishingCastRegistrationDelay(DateTime now)
        {
            ResetTestAutoFishingDetection(now);
            _testAutoFishingNoBobberSinceAtUtc = now;
            _testAutoFishingWaitingForCastRegistration = true;
            _nextTestAutoFishingActionAtUtc = now.AddMilliseconds(TestAutoFishingCastRegistrationDelayMs);
            _nextTestAutoFishingScanAtUtc = _nextTestAutoFishingActionAtUtc;
        }

        private void ResetTestAutoFishingDetection(DateTime now)
        {
            _testAutoFishingBaselineReady = false;
            _testAutoFishingBaselineBobberY = 0;
            _testAutoFishingBaselineArmedAtUtc = now;
            _testAutoFishingNoBobberSinceAtUtc = DateTime.MinValue;
            _testAutoFishingLastDetectedBobberX = double.NaN;
            _testAutoFishingLastDetectedBobberY = double.NaN;
            _testAutoFishingLastDetectedRedPixels = 0;
            _testAutoFishingMissedDetections = 0;
            _testAutoFishingBiteConfirmationFrames = 0;
        }

        private void StartTestAutoFishingRepairCommand(DateTime now)
        {
            if (!HasConfiguredTestAutoFishingRepair())
            {
                _nextTestAutoFishingRepairAtUtc = DateTime.MaxValue;
                return;
            }

            _testAutoFishingRepairStage = TestAutoFishingRepairStage.OpenChat;
            _nextTestAutoFishingRepairStageAtUtc = now;
            _testAutoFishingAwaitSecondClick = false;
            _testAutoFishingRecastAfterRepairPending = false;
            _testAutoFishingWaitingForCastRegistration = false;
            ResetTestAutoFishingDetection(now);
            _nextTestAutoFishingActionAtUtc = now;
            _nextTestAutoFishingScanAtUtc = now;
            UpdateStatusBar("Auto łowienie: wykonywanie komendy naprawy", "Orange");
        }

        private bool TryProcessTestAutoFishingRepairCommand(DateTime now)
        {
            if (_testAutoFishingRepairStage == TestAutoFishingRepairStage.None)
                return false;
            if (now < _nextTestAutoFishingRepairStageAtUtc)
                return true;

            switch (_testAutoFishingRepairStage)
            {
                case TestAutoFishingRepairStage.OpenChat:
                    SendChatOpenKeyTap();
                    _testAutoFishingRepairStage = TestAutoFishingRepairStage.TypeCommand;
                    _nextTestAutoFishingRepairStageAtUtc = now.AddMilliseconds(TestAutoFishingRepairDelayAfterOpenChatMs);
                    return true;
                case TestAutoFishingRepairStage.TypeCommand:
                    if (!ChatInputGate.IsReady(now))
                        return true;
                    string command = GetConfiguredTestAutoFishingRepairCommand();
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        _testAutoFishingRepairStage = TestAutoFishingRepairStage.None;
                        _nextTestAutoFishingRepairAtUtc = DateTime.MaxValue;
                        return true;
                    }
                    if (!SendTextByKeyboard(command))
                    {
                        UpdateStatusBar("Auto łowienie: błąd wpisywania komendy naprawy", "Red");
                        _testAutoFishingRepairStage = TestAutoFishingRepairStage.None;
                        _nextTestAutoFishingRepairAtUtc = now.AddSeconds(3);
                        return true;
                    }
                    _testAutoFishingRepairStage = TestAutoFishingRepairStage.SubmitCommand;
                    _nextTestAutoFishingRepairStageAtUtc = now.AddMilliseconds(TestAutoFishingRepairDelayAfterTypeCommandMs);
                    return true;
                case TestAutoFishingRepairStage.SubmitCommand:
                    SendKeyTap(VK_RETURN);
                    _testAutoFishingRepairStage = TestAutoFishingRepairStage.None;
                    int interval = GetConfiguredTestAutoFishingRepairIntervalSeconds();
                    _nextTestAutoFishingRepairAtUtc = interval > 0 ? now.AddSeconds(interval) : DateTime.MaxValue;
                    _testAutoFishingRecastAfterRepairPending = true;
                    _testAutoFishingRecastAfterRepairAtUtc = now.AddMilliseconds(TestAutoFishingRepairRecastDelayMs);
                    _nextTestAutoFishingActionAtUtc = _testAutoFishingRecastAfterRepairAtUtc;
                    _nextTestAutoFishingScanAtUtc = _testAutoFishingRecastAfterRepairAtUtc;
                    ResetTestAutoFishingDetection(now);
                    UpdateStatusBar("Auto łowienie: komenda naprawy wykonana", "Green");
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetFishingBobberPosition(Drawing.Bitmap screenshot, out double bobberX, out double bobberY, out int matchedPixelCount)
        {
            bobberX = 0;
            bobberY = 0;
            matchedPixelCount = 0;
            if (screenshot.Width < 2 || screenshot.Height < 2)
                return false;

            Drawing.Rectangle rect = new Drawing.Rectangle(0, 0, screenshot.Width, screenshot.Height);
            DrawingImaging.BitmapData bitmapData = screenshot.LockBits(rect, DrawingImaging.ImageLockMode.ReadOnly, DrawingImaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = bitmapData.Stride;
                int absoluteStride = Math.Abs(stride);
                byte[] pixels = new byte[absoluteStride * bitmapData.Height];
                Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
                int width = bitmapData.Width;
                int height = bitmapData.Height;
                bool[] redMask = new bool[width * height];
                for (int y = 0; y < bitmapData.Height; y++)
                {
                    int row = stride >= 0 ? y * stride : (bitmapData.Height - 1 - y) * absoluteStride;
                    for (int x = 0; x < bitmapData.Width; x++)
                    {
                        int index = row + x * 4;
                        byte blue = pixels[index];
                        byte green = pixels[index + 1];
                        byte red = pixels[index + 2];
                        if (red >= 130 && green <= 145 && blue <= 145 && red - green >= 15 && red - blue >= 10)
                            redMask[y * width + x] = true;
                    }
                }

                // The red tip of a bobber is one compact mark. Ignore unrelated scattered red pixels.
                bool[] visited = new bool[redMask.Length];
                var pending = new Queue<int>();
                double bestXTotal = 0;
                double bestYTotal = 0;
                for (int start = 0; start < redMask.Length; start++)
                {
                    if (!redMask[start] || visited[start])
                        continue;

                    int clusterPixels = 0;
                    double clusterXTotal = 0;
                    double clusterYTotal = 0;
                    visited[start] = true;
                    pending.Enqueue(start);

                    while (pending.Count > 0)
                    {
                        int point = pending.Dequeue();
                        int pointX = point % width;
                        int pointY = point / width;
                        clusterPixels++;
                        clusterXTotal += pointX;
                        clusterYTotal += pointY;

                        for (int offsetY = -1; offsetY <= 1; offsetY++)
                        {
                            int neighbourY = pointY + offsetY;
                            if (neighbourY < 0 || neighbourY >= height)
                                continue;

                            for (int offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                if (offsetX == 0 && offsetY == 0)
                                    continue;

                                int neighbourX = pointX + offsetX;
                                if (neighbourX < 0 || neighbourX >= width)
                                    continue;

                                int neighbour = neighbourY * width + neighbourX;
                                if (!redMask[neighbour] || visited[neighbour])
                                    continue;

                                visited[neighbour] = true;
                                pending.Enqueue(neighbour);
                            }
                        }
                    }

                    if (clusterPixels > matchedPixelCount)
                    {
                        matchedPixelCount = clusterPixels;
                        bestXTotal = clusterXTotal;
                        bestYTotal = clusterYTotal;
                    }
                }

                if (matchedPixelCount < TestAutoFishingMinRedPixels)
                    return false;
                bobberX = bestXTotal / matchedPixelCount;
                bobberY = bestYTotal / matchedPixelCount;
                return true;
            }
            finally
            {
                screenshot.UnlockBits(bitmapData);
            }
        }

        private void StartKopacz533Runtime(DateTime now)
        {
            ResetKopacz533RuntimeState(now);
            _kopacz533RuntimeStartedAtUtc = now;
            ScheduleNextInventoryCleanup(now);

            if (TryPeekNextKopacz533Command(out _, out _, out int firstDelaySeconds))
            {
                _kopacz533CommandSequenceCompleted = false;
                _nextKopacz533CommandAtUtc = now.AddSeconds(firstDelaySeconds);
            }
            else
            {
                _kopacz533CommandSequenceCompleted = true;
                _nextKopacz533CommandAtUtc = DateTime.MaxValue;
            }

            _nextRuntimeTileRefreshAtUtc = now;
            StartMiningLogRun(InventoryCleanupOwner.Kopacz533);
        }

        private bool HasMiningWork(InventoryCleanupOwner owner)
        {
            bool runtimeActive = owner == InventoryCleanupOwner.Kopacz533
                ? _kopacz533RuntimeEnabled || _kopacz533ResumeMiningPending
                : _kopacz633RuntimeEnabled || _kopacz633ResumeMiningPending;
            return runtimeActive
                || (_inventoryCleanupStage != InventoryCleanupStage.None && _inventoryCleanupOwner == owner)
                || IsReconnectForMiner(owner);
        }

        private bool IsReconnectForMiner(InventoryCleanupOwner owner)
        {
            return _autoReconnectStage != AutoReconnectStage.None
                && ((owner == InventoryCleanupOwner.Kopacz533 && _autoReconnectResumeKopacz533)
                    || (owner == InventoryCleanupOwner.Kopacz633 && _autoReconnectResumeKopacz633)
                    || _autoReconnectLogOwner == GetInventoryCleanupOwnerLabel(owner));
        }

        private bool TryStopMiningFromBinds()
        {
            bool stop533 = HasMiningWork(InventoryCleanupOwner.Kopacz533)
                && IsMiningStopBindPressed(TxtKopacz533Key.Text, ref _kopacz533BindWasDown);
            bool stop633 = HasMiningWork(InventoryCleanupOwner.Kopacz633)
                && IsMiningStopBindPressed(TxtKopacz633Key.Text, ref _kopacz633BindWasDown);
            if (stop533)
                StopMiningFromBind(InventoryCleanupOwner.Kopacz533);
            if (stop633)
                StopMiningFromBind(InventoryCleanupOwner.Kopacz633);
            return stop533 || stop633;
        }

        private bool IsMiningStopBindPressed(string keyText, ref bool wasDown)
        {
            if (!TryGetVirtualKey(keyText, out int key))
                return false;

            // Auto EQ holds Ctrl/drop and movement keys across timer ticks. Their
            // injected DOWN must not be mistaken for another press of a bind.
            bool injectedDown = (_inventoryCleanupDropKeyDown && key == _inventoryCleanupDropVirtualKey)
                || (_inventoryCleanupControlDown && key is VK_CONTROL or VK_LCONTROL)
                || (_inventoryCleanupReturnLeftDown && key == VK_A)
                || (_inventoryCleanupReturnBackwardDown && key == VK_S)
                || (_kopacz533Holding && key is VK_SHIFT or 0xA0) // Shift / LeftShift
                || (_kopacz633StrafeDirection == Kopacz633StrafeDirection.Left && key == VK_A)
                || (_kopacz633StrafeDirection == Kopacz633StrafeDirection.Right && key == VK_D)
                || (_kopacz633StrafeDirection == Kopacz633StrafeDirection.Forward && key == VK_W)
                || (_kopacz633StrafeDirection == Kopacz633StrafeDirection.Backward && key == VK_S)
                || ((_kopacz533Holding || _kopacz633HoldingAttack) && key == VK_LBUTTON)
                || (_inventoryCleanupEatingRightButtonDown && key == VK_RBUTTON);
            return ConsumeMiningStopPress(IsVirtualKeyDown(key), injectedDown, ref wasDown);
        }

        private static bool ConsumeMiningStopPress(bool isDown, bool injectedDown, ref bool wasDown)
        {
            bool pressed = isDown && !wasDown && !injectedDown;
            wasDown = isDown;
            return pressed;
        }

        private void StopMiningFromBind(InventoryCleanupOwner owner, bool remote = false)
        {
            string label = GetInventoryCleanupOwnerLabel(owner);
            string source = remote ? "z panelu kont" : "bindem";
            // Release held inputs before any disk logging or UI refresh.
            ReleaseMiningInputs(owner);
            if (IsReconnectForMiner(owner))
                StopAutoReconnect($"{label}: przerwano bieżącą akcję {source}.", resumeMining: false, warning: true);

            if (_inventoryCleanupOwner == owner && _inventoryCleanupStage != InventoryCleanupStage.None)
            {
                RecordAbortedInventoryCleanup(remote ? "Auto EQ przerwane z panelu kont." : "Auto EQ przerwane bindem kopacza.");
                _inventoryCleanupLastResult = remote ? "Przerwano z panelu kont" : "Przerwano bindem kopacza";
                _inventoryCleanupLastResultWarning = true;
                UpdateInventoryCleanupStatus($"Auto EQ zatrzymane {source}. Pozostałe akcje anulowano.", "Orange");
            }

            StopMiningRuntime(owner);
            EndMiningLogRun(owner, remote ? "Kopanie i bieżąca akcja zatrzymane z panelu kont." : "Kopanie i bieżąca akcja zatrzymane bindem użytkownika.");
            UpdateStatusBar(remote ? $"{label} zatrzymany z panelu kont — bieżąca akcja anulowana." : $"{label} wyłączony — bieżąca akcja anulowana.", "Orange");
            RefreshOverlayHud(DateTime.UtcNow);
        }

        private void StopMiningRuntime(InventoryCleanupOwner owner)
        {
            ReleaseMiningInputs(owner);
            if (owner == InventoryCleanupOwner.Kopacz533)
            {
                _kopacz533RuntimeEnabled = false;
                ResetKopacz533RuntimeState();
            }
            else
            {
                _kopacz633RuntimeEnabled = false;
                ResetKopacz633RuntimeState();
            }
        }

        private void ReleaseMiningInputs(InventoryCleanupOwner owner)
        {
            if (owner == InventoryCleanupOwner.Kopacz533)
                SetKopacz533MiningHold(false);
            else
            {
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
            }
            if (_inventoryCleanupOwner == owner)
            {
                ReleaseInventoryCleanupDropKeys();
                ReleaseInventoryCleanupReturnKeys();
                SetInventoryCleanupEatingHold(false);
            }
        }

        private void ToggleKopacz533Runtime()
        {
            if (ChkKopacz533Enabled?.IsChecked != true)
            {
                UpdateStatusBar("Najpierw zaznacz kanał Kopacz 5/3/3.", "Red");
                return;
            }

            _kopacz533RuntimeEnabled = !_kopacz533RuntimeEnabled;
            if (_kopacz533RuntimeEnabled)
            {
                StopOtherExclusivePointerMacros(keepKopacz533: true);
                StartKopacz533Runtime(DateTime.UtcNow);
            }
            else
            {
                StopMiningFromBind(InventoryCleanupOwner.Kopacz533);
                return;
            }

            UpdateStatusBar(_kopacz533RuntimeEnabled ? "Kopacz 5/3/3 aktywowany" : "Kopacz 5/3/3 wyłączony", "Orange");
            RefreshOverlayHud(DateTime.UtcNow);
        }

        private bool IsKopacz633DirectionSelected()
        {
            int selectedIndex = CbKopacz633Direction.SelectedIndex;
            return selectedIndex is 1 or 2;
        }

        private int GetConfiguredKopacz633ForwardWidth()
        {
            return Math.Max(1, ParseNonNegativeInt(TxtKopacz633Width.Text));
        }

        private int GetConfiguredKopacz633UpwardWidth()
        {
            return Math.Max(1, ParseNonNegativeInt(TxtKopacz633WidthUp.Text));
        }

        private int GetConfiguredKopacz633UpwardLength()
        {
            return Math.Max(1, ParseNonNegativeInt(TxtKopacz633LengthUp.Text));
        }

        private void StartKopacz633Runtime(DateTime now)
        {
            ResetKopacz633RuntimeState(now);
            _kopacz633RuntimeStartedAtUtc = now;
            ScheduleNextInventoryCleanup(now);

            if (TryPeekNextKopacz633Command(out _, out _, out int firstDelaySeconds))
            {
                _kopacz633CommandSequenceCompleted = false;
                _nextKopacz633CommandAtUtc = now.AddSeconds(firstDelaySeconds);
            }
            else
            {
                _kopacz633CommandSequenceCompleted = true;
                _nextKopacz633CommandAtUtc = DateTime.MaxValue;
            }

            _kopacz633UpwardLegIndex = 0;
            StartKopacz633NextMovementLeg(now);
            SetKopacz633AttackHold(true);
            _nextRuntimeTileRefreshAtUtc = now;
            StartMiningLogRun(InventoryCleanupOwner.Kopacz633);
        }

        private void ToggleKopacz633Runtime()
        {
            if (ChkKopacz633Enabled?.IsChecked != true)
            {
                UpdateStatusBar("Najpierw zaznacz kanał Kopacz 6/3/3.", "Red");
                return;
            }

            if (!_kopacz633RuntimeEnabled && !IsKopacz633DirectionSelected())
            {
                UpdateStatusBar("Kopacz 6/3/3: wybierz kierunek 'Na wprost' albo 'Do góry'", "Orange");
                return;
            }

            _kopacz633RuntimeEnabled = !_kopacz633RuntimeEnabled;
            if (_kopacz633RuntimeEnabled)
            {
                StopOtherExclusivePointerMacros(keepKopacz633: true);
                StartKopacz633Runtime(DateTime.UtcNow);
            }
            else
            {
                StopMiningFromBind(InventoryCleanupOwner.Kopacz633);
                return;
            }

            UpdateStatusBar(_kopacz633RuntimeEnabled ? "Kopacz 6/3/3 aktywowany" : "Kopacz 6/3/3 wyłączony", "Orange");
            RefreshOverlayHud(DateTime.UtcNow);
        }

        private void ScheduleNextInventoryCleanup(DateTime now)
        {
            _nextInventoryCleanupAtUtc = ChkInventoryCleanupEnabled.IsChecked == true
                ? now.AddSeconds(GetConfiguredInventoryCleanupIntervalSeconds())
                : DateTime.MaxValue;
        }

        private bool TryRunInventoryCleanup(InventoryCleanupOwner owner, DateTime now)
        {
            if (_inventoryCleanupStage != InventoryCleanupStage.None)
            {
                if (_inventoryCleanupOwner != owner)
                    return false;

                ProcessInventoryCleanupStage(now);
                return true;
            }

            if (ChkInventoryCleanupEnabled.IsChecked != true || now < _nextInventoryCleanupAtUtc)
                return false;
            if (_kopacz533CommandStage != Kopacz533CommandStage.None
                || _kopacz633CommandStage != Kopacz633CommandStage.None
                || _testAutoFishingRepairStage != TestAutoFishingRepairStage.None)
                return false;

            bool cobbleXEnabled = ChkCobbleXEnabled.IsChecked == true;
            bool goldEnabled = ChkGoldDepositEnabled.IsChecked == true;
            if (goldEnabled && GoldOptions().Validate() is string goldError && goldError.Length > 0)
            {
                StopMiningRuntime(owner);
                EndMiningLogRun(owner, "Niepoprawna konfiguracja wpłaty złota: " + goldError);
                SetGoldStatus(goldError, true);
                UpdateInventoryCleanupStatus("Wpłata złota: " + goldError + " Kopacz zatrzymany.", "Red");
                return true;
            }
            bool discardEverythingExceptCobblestone = ChkInventoryCleanupAllItemTypes.IsChecked == true;
            HashSet<int> enabledSlots = GetSelectedInventoryCleanupSlots();
            if (!cobbleXEnabled && !goldEnabled && enabledSlots.Count == 0)
            {
                ScheduleNextInventoryCleanup(now);
                _inventoryCleanupLastResult = "Pominięto: nie wybrano slotów";
                _inventoryCleanupLastResultWarning = true;
                UpdateInventoryCleanupStatus("Pominięto: nie wybrano żadnego slotu.", "Orange");
                return false;
            }
            if (!cobbleXEnabled
                && !goldEnabled
                && !discardEverythingExceptCobblestone
                && GetSelectedInventoryCleanupItemTypes().Count == 0)
            {
                ScheduleNextInventoryCleanup(now);
                _inventoryCleanupLastResult = "Pominięto: nie wybrano typów przedmiotów";
                _inventoryCleanupLastResultWarning = true;
                UpdateInventoryCleanupStatus("Pominięto: nie wybrano żadnego typu przedmiotu do wyrzucenia.", "Orange");
                return false;
            }

            SetKopacz533MiningHold(false);
            SetKopacz633AttackHold(false);
            SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
            ResetTestAutoFishingRuntimeState(now);

            _inventoryCleanupOwner = owner;
            PauseKopaczCommandCountdown(owner, now);
            _inventoryCleanupStage = owner == InventoryCleanupOwner.Kopacz633
                ? InventoryCleanupStage.ReturnToMiningStart
                : InventoryCleanupStage.OpenInventory;
            _inventoryCleanupDetectionAttempts = 0;
            _inventoryCleanupDropPass = 0;
            _inventoryCleanupInitialMarkedStacks = 0;
            _inventoryCleanupRemovedStacks = 0;
            _inventoryCleanupRemovedItems = 0;
            _inventoryCleanupRemainingMarkedStacks = 0;
            _inventoryCleanupCursorParkedForDetection = false;
            _inventoryCleanupTargetIndex = 0;
            _inventoryCleanupTargets.Clear();
            _inventoryCleanupInitialItemTypeCounts.Clear();
            _inventoryCleanupInitialItemTypeStackCounts.Clear();
            _inventoryCleanupItemTypeCounts.Clear();
            _inventoryCleanupItemTypeStackCounts.Clear();
            _inventoryCleanupClientArea = Drawing.Rectangle.Empty;
            _inventoryCleanupFullCobblestoneStacks = 0;
            _inventoryCleanupCobbleXCommandPending = false;
            _inventoryCleanupCobbleXCommandSent = false;
            _inventoryCleanupCobbleXCommandFailed = false;
            _inventoryCleanupCobbleXCommandTargetCount = GetConfiguredCobbleXCommandCount();
            _inventoryCleanupCobbleXCommandSendCount = 0;
            _inventoryCleanupPendingCobbleXCommand = string.Empty;
            ResetGoldDepositState();
            _goldDepositSummary = string.Empty;
            _inventoryCleanupEatAfterCleanupPending = ChkInventoryCleanupEatAfterCleanup.IsChecked == true;
            _inventoryCleanupEatingCompleted = false;
            _inventoryCleanupOpenedAtUtc = now;
            _inventoryCleanupLogStartError = string.Empty;
            if (owner == InventoryCleanupOwner.Kopacz633)
            {
                StartInventoryCleanupReturnToMiningStart(now);
            }
            else
            {
                _nextInventoryCleanupStageAtUtc = now;
                UpdateInventoryCleanupStatus("Otwieranie ekwipunku...", "Orange");
            }
            return true;
        }

        private void StartInventoryCleanupLogSession()
        {
            if (!string.IsNullOrWhiteSpace(_inventoryCleanupLogSessionId))
                return;

            bool discardEverythingExceptCobblestone = ChkInventoryCleanupAllItemTypes.IsChecked == true;
            IReadOnlyList<string> selectedItemTypes = discardEverythingExceptCobblestone
                ? Array.Empty<string>()
                : GetSelectedInventoryCleanupItemTypes()
                    .Select(GetInventoryCleanupItemLabel)
                    .OrderBy(label => label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            if (!_miningLogService.StartInventorySession(
                    GetInventoryCleanupOwnerLabel(_inventoryCleanupOwner),
                    GetMiningRunId(_inventoryCleanupOwner),
                    discardEverythingExceptCobblestone ? "all-except-cobblestone" : "selected-items",
                    ChkCobbleXEnabled.IsChecked == true,
                    _inventoryCleanupEatAfterCleanupPending,
                    GetSelectedInventoryCleanupSlots().OrderBy(slot => slot).ToList(),
                    selectedItemTypes,
                    out _inventoryCleanupLogSessionId,
                    out string logStartError))
            {
                _inventoryCleanupLogStartError = logStartError;
            }

            RefreshMiningLogsSummary();
        }

        private void StartInventoryCleanupReturnToMiningStart(DateTime now)
        {
            ReleaseInventoryCleanupReturnKeys();

            bool upwardMode = IsKopacz633RectangleMode();
            int msPerBlock = GetKopacz633MsPerBlock();
            int leftBlocks = upwardMode
                ? GetConfiguredKopacz633UpwardWidth()
                : GetConfiguredKopacz633ForwardWidth();
            int backwardBlocks = upwardMode
                ? GetConfiguredKopacz633UpwardLength()
                : 0;

            _inventoryCleanupReturnLeftUntilUtc = now.AddMilliseconds(leftBlocks * msPerBlock);
            _inventoryCleanupReturnBackwardUntilUtc = backwardBlocks > 0
                ? now.AddMilliseconds(backwardBlocks * msPerBlock)
                : DateTime.MinValue;

            SetInventoryCleanupReturnLeft(down: true);
            if (backwardBlocks > 0)
                SetInventoryCleanupReturnBackward(down: true);

            _nextInventoryCleanupStageAtUtc = backwardBlocks > 0
                ? (_inventoryCleanupReturnLeftUntilUtc <= _inventoryCleanupReturnBackwardUntilUtc
                    ? _inventoryCleanupReturnLeftUntilUtc
                    : _inventoryCleanupReturnBackwardUntilUtc)
                : _inventoryCleanupReturnLeftUntilUtc;

            UpdateInventoryCleanupStatus(
                upwardMode
                    ? $"Auto EQ gotowe do startu. Wracam na początek: A ({leftBlocks} bl.) + S ({backwardBlocks} bl.)..."
                    : $"Auto EQ gotowe do startu. Wracam na początek: A ({leftBlocks} bl.)...",
                "Orange");
        }

        private void ProcessInventoryCleanupReturnToMiningStart(DateTime now)
        {
            if (_inventoryCleanupReturnLeftDown && now >= _inventoryCleanupReturnLeftUntilUtc)
                SetInventoryCleanupReturnLeft(down: false);
            if (_inventoryCleanupReturnBackwardDown && now >= _inventoryCleanupReturnBackwardUntilUtc)
                SetInventoryCleanupReturnBackward(down: false);

            if (_inventoryCleanupReturnLeftDown || _inventoryCleanupReturnBackwardDown)
            {
                DateTime nextReleaseAtUtc = DateTime.MaxValue;
                if (_inventoryCleanupReturnLeftDown)
                    nextReleaseAtUtc = _inventoryCleanupReturnLeftUntilUtc;
                if (_inventoryCleanupReturnBackwardDown && _inventoryCleanupReturnBackwardUntilUtc < nextReleaseAtUtc)
                    nextReleaseAtUtc = _inventoryCleanupReturnBackwardUntilUtc;

                _nextInventoryCleanupStageAtUtc = nextReleaseAtUtc;
                return;
            }

            // The upward route always restarts from its first (W) leg after A+S
            // has pushed the player back into the starting corner.
            _kopacz633UpwardLegIndex = 0;
            _kopacz633MovementLegEndAtUtc = now;
            _inventoryCleanupStage = InventoryCleanupStage.OpenInventory;
            _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupReturnSettleMs);
            UpdateInventoryCleanupStatus("Pozycja startowa przywrócona. Za chwilę otwieram EQ...", "Orange");
        }

        private void ProcessInventoryCleanupStage(DateTime now)
        {
            if (now < _nextInventoryCleanupStageAtUtc)
                return;

            if (_inventoryCleanupStage == InventoryCleanupStage.DepositGold)
            {
                ProcessGoldDeposit(now);
                return;
            }

            switch (_inventoryCleanupStage)
            {
                case InventoryCleanupStage.ReturnToMiningStart:
                    ProcessInventoryCleanupReturnToMiningStart(now);
                    return;

                case InventoryCleanupStage.OpenInventory:
                    ReleaseInventoryCleanupReturnKeys();
                    StartInventoryCleanupLogSession();
                    SendKeyTap(VK_E);
                    _inventoryCleanupStage = InventoryCleanupStage.WaitForInventory;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupOpenDelayMs);
                    UpdateInventoryCleanupStatus(
                        _inventoryCleanupOwner == InventoryCleanupOwner.Kopacz633
                            ? "Pozycja startowa przywrócona. Otwieranie ekwipunku..."
                            : "Otwieranie ekwipunku...",
                        "Orange");
                    return;

                case InventoryCleanupStage.WaitForInventory:
                    DetectInventoryCleanupTargets(now);
                    return;

                case InventoryCleanupStage.MoveToSlot:
                    if (_inventoryCleanupTargetIndex >= _inventoryCleanupTargets.Count)
                    {
                        _inventoryCleanupStage = InventoryCleanupStage.WaitForInventory;
                        _inventoryCleanupCursorParkedForDetection = false;
                        _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupBetweenDropsMs);
                        return;
                    }

                    Drawing.Point target = _inventoryCleanupTargets[_inventoryCleanupTargetIndex];
                    SetGameCursorPosition(target.X, target.Y);
                    _inventoryCleanupStage = InventoryCleanupStage.PressDropModifier;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupCursorSettleMs);
                    return;

                case InventoryCleanupStage.PressDropModifier:
                    if (!SetInventoryCleanupControl(down: true))
                    {
                        AbortInventoryCleanup(now, "Nie udało się nacisnąć lewego Ctrl.");
                        return;
                    }
                    _inventoryCleanupStage = InventoryCleanupStage.PressDropKey;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupModifierSettleMs);
                    return;

                case InventoryCleanupStage.PressDropKey:
                    if (!SetInventoryCleanupDropKey(down: true))
                    {
                        string dropKey = NormalizeMinecraftControlKey(TxtDropItemKey.Text, "Q");
                        AbortInventoryCleanup(now, $"Nie udało się nacisnąć klawisza wyrzucania ({dropKey}).");
                        return;
                    }
                    _inventoryCleanupStage = InventoryCleanupStage.ReleaseDropKeys;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupDropKeyHoldMs);
                    return;

                case InventoryCleanupStage.ReleaseDropKeys:
                    ReleaseInventoryCleanupDropKeys();
                    _inventoryCleanupTargetIndex++;
                    bool hasMoreTargets = _inventoryCleanupTargetIndex < _inventoryCleanupTargets.Count;
                    _inventoryCleanupStage = hasMoreTargets
                        ? InventoryCleanupStage.MoveToSlot
                        : InventoryCleanupStage.WaitForInventory;
                    if (!hasMoreTargets)
                    {
                        _inventoryCleanupCursorParkedForDetection = false;
                        UpdateInventoryCleanupStatus(
                            $"Przebieg {_inventoryCleanupDropPass}/{InventoryCleanupMaximumDropPasses} zakończony. Odsuwam kursor i skanuję EQ ponownie...",
                            "Orange");
                    }
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupBetweenDropsMs);
                    return;

                case InventoryCleanupStage.CloseInventory:
                    ReleaseInventoryCleanupDropKeys();
                    if (!_inventoryCleanupClientArea.IsEmpty)
                    {
                        SetGameCursorPosition(
                            _inventoryCleanupClientArea.Left + _inventoryCleanupClientArea.Width / 2,
                            _inventoryCleanupClientArea.Top + _inventoryCleanupClientArea.Height / 2);
                    }
                    SendKeyTap(VK_E);
                    _inventoryCleanupStage = _inventoryCleanupCobbleXCommandPending
                        ? InventoryCleanupStage.OpenCobbleXChat
                        : GetInventoryCleanupPostCommandStage();
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(
                        _inventoryCleanupCobbleXCommandPending
                            ? CobbleXDelayAfterCloseInventoryMs
                            : InventoryCleanupResumeDelayMs);
                    return;

                case InventoryCleanupStage.OpenCobbleXChat:
                    SendChatOpenKeyTap();
                    _inventoryCleanupStage = InventoryCleanupStage.TypeCobbleXCommand;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(CobbleXDelayAfterOpenChatMs);
                    return;

                case InventoryCleanupStage.TypeCobbleXCommand:
                    if (!ChatInputGate.IsReady(now))
                        return;
                    if (!SendTextByKeyboard(_inventoryCleanupPendingCobbleXCommand))
                    {
                        _inventoryCleanupCobbleXCommandFailed = true;
                        _inventoryCleanupCobbleXCommandPending = false;
                        InventoryCleanupOwner failedOwner = _inventoryCleanupOwner;
                        string failure = $"Nie udało się wpisać komendy CobbleX (wysłanie {_inventoryCleanupCobbleXCommandSendCount + 1}/{_inventoryCleanupCobbleXCommandTargetCount}). Pozostałe akcje anulowano.";
                        RecordAbortedInventoryCleanup(failure);
                        StopMiningRuntime(failedOwner);
                        EndMiningLogRun(failedOwner, failure, MiningLogStatuses.Interrupted);
                        UpdateInventoryCleanupStatus(failure, "Red");
                        UpdateStatusBar("Kopacz zatrzymany — nie udało się wpisać komendy CobbleX.", "Red");
                        return;
                    }

                    _inventoryCleanupStage = InventoryCleanupStage.SubmitCobbleXCommand;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(CobbleXDelayAfterTypeCommandMs);
                    return;

                case InventoryCleanupStage.SubmitCobbleXCommand:
                    if (!_inventoryCleanupCobbleXCommandPending)
                    {
                        _inventoryCleanupStage = GetInventoryCleanupPostCommandStage();
                        return;
                    }
                    SendKeyTap(VK_RETURN);
                    AdvanceCobbleXCommandSequence(now);
                    UpdateInventoryCleanupStatus(
                        $"CobbleX: wysłano komendę {_inventoryCleanupCobbleXCommandSendCount}/{_inventoryCleanupCobbleXCommandTargetCount} razy. "
                        + (_inventoryCleanupCobbleXCommandPending ? "Krótka przerwa przed kolejnym wysłaniem..." : "Seria zakończona — przechodzę do kolejnej akcji."), "Orange");
                    return;

                case InventoryCleanupStage.SelectFoodSlot:
                    SendKeyTap(VK_2);
                    _inventoryCleanupStage = InventoryCleanupStage.StartEating;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupFoodSlotSettleMs);
                    UpdateInventoryCleanupStatus("Auto EQ zakończone. Wybrano slot 2 — rozpoczynam jedzenie...", "Orange");
                    return;

                case InventoryCleanupStage.StartEating:
                    SetInventoryCleanupEatingHold(true);
                    _inventoryCleanupStage = InventoryCleanupStage.StopEatingAndRestoreTool;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupEatingHoldMs);
                    UpdateInventoryCleanupStatus("Jedzenie ze slotu 2: trzymam PPM przez 4 sekundy...", "Orange");
                    return;

                case InventoryCleanupStage.StopEatingAndRestoreTool:
                    SetInventoryCleanupEatingHold(false);
                    SendKeyTap(VK_1);
                    _inventoryCleanupEatingCompleted = true;
                    _inventoryCleanupStage = InventoryCleanupStage.ResumeMining;
                    _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupToolSlotSettleMs);
                    UpdateInventoryCleanupStatus("Jedzenie zakończone. Wrócono na slot 1 — wznawiam kopanie...", "Green");
                    return;

                case InventoryCleanupStage.ResumeMining:
                    int removedStacks = _inventoryCleanupRemovedStacks;
                    int removedItems = _inventoryCleanupRemovedItems;
                    int cobbleStacks = _inventoryCleanupFullCobblestoneStacks;
                    int requiredCobbleStacks = GetConfiguredCobbleXRequiredStacks();
                    bool cobbleXSent = _inventoryCleanupCobbleXCommandSent;
                    bool cobbleXFailed = _inventoryCleanupCobbleXCommandFailed;
                    string cobbleXCommand = _inventoryCleanupPendingCobbleXCommand;
                    InventoryCleanupOwner cleanupOwner = _inventoryCleanupOwner;
                    string cleanupResult = removedStacks == 0
                        ? "Brak przedmiotów do wyrzucenia"
                        : $"Wyrzucono {removedItems} szt. z {removedStacks} stosów";
                    if (_inventoryCleanupRemainingMarkedStacks > 0)
                        cleanupResult += $"; pozostało {_inventoryCleanupRemainingMarkedStacks} po {InventoryCleanupMaximumDropPasses} przebiegach";
                    string cobbleResult = cobbleXSent
                        ? $"wysłano komendę CobbleX {_inventoryCleanupCobbleXCommandSendCount}/{_inventoryCleanupCobbleXCommandTargetCount} razy ({_inventoryCleanupPendingCobbleXCommand})"
                            + (cobbleXFailed ? "; nie dokończono serii komend" : string.Empty)
                        : cobbleXFailed
                            ? "CobbleX niewysłany: uzupełnij poprawną komendę"
                            : $"Cobble 64: {cobbleStacks}/{requiredCobbleStacks}";
                    string miningLogError = RecordCompletedInventoryCleanup(
                        cleanupOwner,
                        removedItems,
                        removedStacks,
                        cobbleXSent,
                        cobbleXCommand,
                        cobbleStacks,
                        requiredCobbleStacks);
                    _inventoryCleanupLastResult = $"{cleanupResult}; {cobbleResult}";
                    if (_goldDepositSummary.Length > 0) _inventoryCleanupLastResult += "; " + _goldDepositSummary;
                    if (_inventoryCleanupEatingCompleted)
                        _inventoryCleanupLastResult += "; jedzenie zakończone, slot 1 przywrócony";
                    bool cleanupWarning = _inventoryCleanupRemainingMarkedStacks > 0;
                    _inventoryCleanupLastResultWarning = cobbleXFailed || cleanupWarning || !string.IsNullOrWhiteSpace(miningLogError);
                    ResetInventoryCleanupState(scheduleNext: true, now);
                    UpdateInventoryCleanupStatus(
                        string.IsNullOrWhiteSpace(miningLogError)
                            ? $"Gotowe: {cleanupResult}; {cobbleResult}. Następny skan za {GetConfiguredInventoryCleanupIntervalSeconds()} s."
                            : $"Gotowe: {cleanupResult}; {cobbleResult}. Nie zapisano części logów: {miningLogError}",
                        cobbleXFailed || cleanupWarning || !string.IsNullOrWhiteSpace(miningLogError) ? "Orange" : "Green");
                    return;
            }
        }

        private bool IsInventoryCleanupScanCurrent(int generation, InventoryCleanupOwner owner)
        {
            return generation == _inventoryCleanupGeneration
                && _inventoryCleanupStage == InventoryCleanupStage.WaitForInventory
                && _inventoryCleanupOwner == owner
                && (owner == InventoryCleanupOwner.Kopacz533
                    ? _kopacz533RuntimeEnabled
                    : owner == InventoryCleanupOwner.Kopacz633 && _kopacz633RuntimeEnabled);
        }

        // async void z pętli kopania: każdy wyjątek musi zostać złapany tutaj, inaczej zamknąłby program.
        private async void DetectInventoryCleanupTargets(DateTime now)
        {
            try
            {
                await DetectInventoryCleanupTargetsCoreAsync(now);
            }
            catch (Exception ex)
            {
                _inventoryCleanupScanInProgress = false;
                AppLog.Error("Auto EQ", "Nieoczekiwany błąd skanu EQ", ex);
                RetryOrAbortInventoryCleanup(DateTime.UtcNow, "Błąd skanowania EQ: " + ex.GetBaseException().Message);
            }
        }

        private async Task DetectInventoryCleanupTargetsCoreAsync(DateTime now)
        {
            if (_inventoryCleanupScanInProgress)
                return;

            if (!_inventoryCleanupCursorParkedForDetection)
            {
                if (TryGetWindowClientRectOnScreen(_targetGameWindowHandle, out RECT clientRect))
                {
                    // Keep the cursor outside the inventory GUI so an item tooltip
                    // cannot cover markers in neighbouring slots during capture.
                    SetGameCursorPosition(
                        Math.Max(clientRect.Left + 8, clientRect.Right - 16),
                        Math.Max(clientRect.Top + 8, clientRect.Bottom - 16));
                }

                _inventoryCleanupCursorParkedForDetection = true;
                _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupTooltipClearDelayMs);
                UpdateInventoryCleanupStatus("Odsuwanie kursora poza EQ i oczekiwanie na zniknięcie podpowiedzi...", "Orange");
                return;
            }

            _inventoryCleanupCursorParkedForDetection = false;
            _inventoryCleanupDetectionAttempts++;
            // In the background the cursor check would read the user's desktop;
            // the texture-pack marker scan below confirms the open inventory instead.
            if (!BackgroundGameWindow.IsActive && !IsInventoryCursorVisible())
            {
                RetryOrAbortInventoryCleanup(now, "Gra nie pokazała ekwipunku.", startReconnectIfEnabled: true);
                return;
            }

            if (!TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out Drawing.Rectangle clientArea))
            {
                RetryOrAbortInventoryCleanup(now, "Nie udało się przechwycić okna gry.");
                return;
            }

            using (Drawing.Bitmap capturedBitmap = bitmap!)
            {
                int generation = _inventoryCleanupGeneration;
                InventoryCleanupOwner owner = _inventoryCleanupOwner;
                IntPtr targetWindow = _targetGameWindowHandle;
                _inventoryCleanupScanInProgress = true;
                try
                {
                    HashSet<int> enabledSlots = GetSelectedInventoryCleanupSlots();
                    HashSet<string> enabledItemTypes = GetSelectedInventoryCleanupItemTypes();
                    bool discardEverythingExceptCobblestone = ChkInventoryCleanupAllItemTypes.IsChecked == true;
                    bool goldEnabled = ChkGoldDepositEnabled.IsChecked == true;
                    GoldDepositOptions goldOptions = GoldOptions();
                    var scan = await Task.Run(() =>
                    {
                        bool found = InventoryMarkerDetector.TryDetect(capturedBitmap, enabledSlots, enabledItemTypes, out InventoryMarkerDetection result);
                        if (found)
                            MinerLivePreviewService.RememberInventory(capturedBitmap, result.Layout, MinerRemoteCommandRouter.ScreenshotWidth);
                        var goldItems = found && goldEnabled
                            ? InventoryMarkerDetector.ReadMarkedItems(capturedBitmap, result.Layout, goldOptions.IncludeHotbar)
                                .Where(item => goldOptions.IsSelected(item.ItemId)).ToArray()
                            : Array.Empty<DetectedInventoryItem>();
                        return (Found: found, Detection: result, GoldItems: goldItems);
                    });
                    if (!IsInventoryCleanupScanCurrent(generation, owner)
                        || targetWindow != _targetGameWindowHandle
                        || !IsGameInputAvailableFor(targetWindow))
                        return;

                    now = DateTime.UtcNow;
                    InventoryMarkerDetection detection = scan.Detection;
                    if (!scan.Found)
                    {
                        RetryOrAbortInventoryCleanup(now, "Nie znaleziono znaczników texturepacka.", startReconnectIfEnabled: true);
                        return;
                    }
                    if (discardEverythingExceptCobblestone && !detection.SupportsFullInventoryScan)
                    {
                        AbortInventoryCleanup(now, "Tryb 'Wyrzucaj wszystko' nie potwierdził widocznej siatki EQ. Włącz aktualną paczkę Minecraft Helper.");
                        return;
                    }
                    if (!discardEverythingExceptCobblestone && detection.UnknownMarkerSlots.Count > 0)
                    {
                        AbortInventoryCleanup(now, "Wykryto starą wersję texturepacka bez rozpoznawania typów. Włącz nową paczkę Minecraft Helper.");
                        return;
                    }
                    if (TryHandleMissingPickaxeDuringInventoryCleanup(capturedBitmap, detection, now))
                        return;

                    if (goldEnabled && !detection.HasGuiMarkers)
                    {
                        StopGoldDeposit("Wpłata wymaga potwierdzonych znaczników GUI aktualnej paczki Minecraft Helper. Niczego nie wyrzucono.");
                        return;
                    }

                    _inventoryCleanupDetectionAttempts = 0;
                    _inventoryCleanupClientArea = clientArea;
                    _inventoryCleanupFullCobblestoneStacks = detection.FullCobblestoneSlots.Count;
                    _inventoryCleanupLastFullCobblestoneStacks = _inventoryCleanupFullCobblestoneStacks;
                    IReadOnlyList<DetectedInventoryItem> detectedItems = discardEverythingExceptCobblestone
                        ? detection.AllNonCobblestoneItems
                        : detection.Items;
                    IReadOnlyList<int> detectedSlots = discardEverythingExceptCobblestone
                        ? detection.AllNonCobblestoneSlots
                        : detection.MarkedSlots;
                    // Protect both gold types even if only one can be deposited.
                    // Deposit removal must never be counted as discarded items.
                    if (goldEnabled)
                    {
                        detectedItems = detectedItems.Where(item => !GoldDepositOptions.IsGold(item.ItemId) && item.ItemId != "other").ToArray();
                        detectedSlots = detectedItems.Select(item => item.Slot).ToArray();
                        _goldDepositPending = ChkGoldDepositEnabled.IsChecked == true && scan.GoldItems.Length > 0;
                    }
                    UpdateConfirmedInventoryCleanupCounts(detectedItems);
                    int requiredCobbleStacks = GetConfiguredCobbleXRequiredStacks();
                    _inventoryCleanupPendingCobbleXCommand = GetConfiguredCobbleXCommand();
                    _inventoryCleanupCobbleXCommandPending = ChkCobbleXEnabled.IsChecked == true
                        && _inventoryCleanupFullCobblestoneStacks >= requiredCobbleStacks
                        && !string.IsNullOrWhiteSpace(_inventoryCleanupPendingCobbleXCommand);
                    _inventoryCleanupCobbleXCommandFailed = ChkCobbleXEnabled.IsChecked == true
                        && _inventoryCleanupFullCobblestoneStacks >= requiredCobbleStacks
                        && string.IsNullOrWhiteSpace(_inventoryCleanupPendingCobbleXCommand);
                    _inventoryCleanupTargets.Clear();
                    foreach (int slot in detectedSlots)
                    {
                        Drawing.Point relativeCenter = detection.Layout.GetSlotCenter(slot);
                        _inventoryCleanupTargets.Add(new Drawing.Point(
                            clientArea.Left + relativeCenter.X,
                            clientArea.Top + relativeCenter.Y));
                    }

                    _inventoryCleanupTargetIndex = 0;
                    if (_inventoryCleanupTargets.Count == 0)
                    {
                        string scanSummary = _inventoryCleanupDropPass == 0
                            ? "brak przedmiotów do wyrzucenia"
                            : $"EQ czyste po {_inventoryCleanupDropPass} przebiegach; wyrzucono {_inventoryCleanupRemovedItems} szt. z {_inventoryCleanupRemovedStacks} stosów";
                        UpdateInventoryCleanupStatus($"Skan OK (GUI x{detection.Layout.Scale}): {scanSummary}; Cobble 64: {_inventoryCleanupFullCobblestoneStacks}/{requiredCobbleStacks}.", "Green");
                        _inventoryCleanupStage = InventoryCleanupStage.CloseInventory;
                        _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupCloseDelayMs);
                    }
                    else if (_inventoryCleanupDropPass >= InventoryCleanupMaximumDropPasses)
                    {
                        _inventoryCleanupRemainingMarkedStacks = _inventoryCleanupTargets.Count;
                        _inventoryCleanupTargets.Clear();
                        UpdateInventoryCleanupStatus(
                            $"Zatrzymano po {InventoryCleanupMaximumDropPasses} przebiegach: nadal wykryto {_inventoryCleanupRemainingMarkedStacks} stosów. Zamykam EQ, aby uniknąć pętli.",
                            "Orange");
                        _inventoryCleanupStage = InventoryCleanupStage.CloseInventory;
                        _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupCloseDelayMs);
                    }
                    else
                    {
                        _inventoryCleanupDropPass++;
                        UpdateInventoryCleanupStatus(
                            $"Skan {_inventoryCleanupDropPass}/{InventoryCleanupMaximumDropPasses} (GUI x{detection.Layout.Scale}): {_inventoryCleanupTargets.Count} stosów do wyrzucenia; Cobble 64: {_inventoryCleanupFullCobblestoneStacks}/{requiredCobbleStacks}.",
                            "Green");
                        _inventoryCleanupStage = InventoryCleanupStage.MoveToSlot;
                        _nextInventoryCleanupStageAtUtc = now;
                    }
                }
                catch (Exception ex)
                {
                    if (IsInventoryCleanupScanCurrent(generation, owner))
                        RetryOrAbortInventoryCleanup(DateTime.UtcNow, "Błąd skanowania EQ: " + ex.Message);
                }
                finally
                {
                    _inventoryCleanupScanInProgress = false;
                }
            }
        }

        private bool TryHandleMissingPickaxeDuringInventoryCleanup(
            Drawing.Bitmap capturedBitmap,
            InventoryMarkerDetection detection,
            DateTime now)
        {
            if (!IsPeriodicAutoReconnectEnabled()
                || !_settings.AutoReconnectMissingPickaxeRecoveryEnabled)
            {
                return false;
            }

            if (!detection.HasGuiMarkers)
            {
                AbortInventoryCleanup(
                    now,
                    "Nie potwierdzono aktualnej paczki Minecraft Helper. Pomijam kontrolę kilofa, aby nie wykonać fałszywego /home.");
                return true;
            }

            if (InventoryMarkerDetector.ContainsMarkedItem(
                    capturedBitmap,
                    detection.Layout,
                    "diamond_pickaxe",
                    includeHotbar: true))
            {
                return false;
            }

            InventoryCleanupOwner owner = _inventoryCleanupOwner;
            string ownerLabel = GetInventoryCleanupOwnerLabel(owner);
            const string reason = "Auto EQ nie wykrył znacznika diamentowego kilofa w EQ ani na hotbarze.";
            SendKeyTap(VK_E);
            RecordAbortedInventoryCleanup(reason);
            _inventoryCleanupLastResult = "Przerwano: brak diamentowego kilofa";
            _inventoryCleanupLastResultWarning = true;
            ResetInventoryCleanupState(scheduleNext: false, now);
            UpdateInventoryCleanupStatus($"{reason} Uruchamiam powrót do home dla {ownerLabel}.", "Red");
            BeginMissingPickaxeHomeRecovery(owner, now);
            return true;
        }

        private string RecordCompletedInventoryCleanup(
            InventoryCleanupOwner owner,
            int removedItems,
            int removedStacks,
            bool cobbleXSent,
            string cobbleXCommand,
            int detectedCobblestoneStacks,
            int requiredCobblestoneStacks)
        {
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(_inventoryCleanupLogStartError))
                errors.Add(_inventoryCleanupLogStartError);

            string ownerLabel = GetInventoryCleanupOwnerLabel(owner);
            List<MiningLogItemDetail> itemDetails = BuildInventoryCleanupItemDetails();

            string details = removedStacks == 0
                ? "Skan zakończony — nie znaleziono przedmiotów do wyrzucenia."
                : $"Skan zakończony — wyrzucono {removedItems} szt. z {removedStacks} stosów.";
            if (_inventoryCleanupRemainingMarkedStacks > 0)
                details += $" Pozostało {_inventoryCleanupRemainingMarkedStacks} stosów po {InventoryCleanupMaximumDropPasses} przebiegach.";
            if (_inventoryCleanupCobbleXCommandSendCount > 0)
                details += $" CobbleX: wysłano komendę {_inventoryCleanupCobbleXCommandSendCount}/{_inventoryCleanupCobbleXCommandTargetCount} razy."
                    + (_inventoryCleanupCobbleXCommandFailed ? " Nie dokończono serii komend." : string.Empty);

            bool saved = _miningLogService.CompleteInventorySession(
                _inventoryCleanupLogSessionId,
                ownerLabel,
                removedItems,
                removedStacks,
                itemDetails,
                _inventoryCleanupDropPass,
                _inventoryCleanupRemainingMarkedStacks,
                cobbleXSent,
                cobbleXCommand,
                detectedCobblestoneStacks,
                requiredCobblestoneStacks,
                _inventoryCleanupEatingCompleted,
                details,
                out string completionError);
            if (!saved)
                errors.Add(completionError);

            _inventoryCleanupLogSessionId = string.Empty;
            _inventoryCleanupLogStartError = string.Empty;

            RefreshMiningLogsSummary();
            return string.Join(" | ", errors.Where(error => !string.IsNullOrWhiteSpace(error)).Distinct());
        }

        private List<MiningLogItemDetail> BuildInventoryCleanupItemDetails()
        {
            var itemDetails = new List<MiningLogItemDetail>();
            foreach ((string itemId, int itemCount) in _inventoryCleanupItemTypeCounts
                .OrderBy(pair => string.Equals(pair.Key, "other", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(pair => GetInventoryCleanupItemLabel(pair.Key), StringComparer.CurrentCultureIgnoreCase))
            {
                _inventoryCleanupItemTypeStackCounts.TryGetValue(itemId, out int stackCount);
                if (itemCount <= 0 && stackCount <= 0)
                    continue;

                itemDetails.Add(new MiningLogItemDetail
                {
                    ItemId = itemId,
                    Label = GetInventoryCleanupItemLabel(itemId),
                    ItemCount = Math.Max(0, itemCount),
                    StackCount = Math.Max(0, stackCount)
                });
            }
            return itemDetails;
        }

        private static string GetInventoryCleanupOwnerLabel(InventoryCleanupOwner owner)
        {
            return owner switch
            {
                InventoryCleanupOwner.Kopacz533 => "Kopacz 5/3/3",
                InventoryCleanupOwner.Kopacz633 => "Kopacz 6/3/3",
                _ => "Kopacz"
            };
        }

        private string GetMiningRunId(InventoryCleanupOwner owner)
        {
            return owner switch
            {
                InventoryCleanupOwner.Kopacz533 => _kopacz533MiningRunId,
                InventoryCleanupOwner.Kopacz633 => _kopacz633MiningRunId,
                _ => string.Empty
            };
        }

        private void SetMiningRunId(InventoryCleanupOwner owner, string runId)
        {
            if (owner == InventoryCleanupOwner.Kopacz533)
                _kopacz533MiningRunId = runId;
            else if (owner == InventoryCleanupOwner.Kopacz633)
                _kopacz633MiningRunId = runId;
        }

        private void StartMiningLogRun(InventoryCleanupOwner owner)
        {
            string previousRunId = GetMiningRunId(owner);
            if (!string.IsNullOrWhiteSpace(previousRunId))
            {
                _ = _miningLogService.CompleteMiningRun(
                    previousRunId,
                    "Poprzednia sesja została zamknięta podczas uruchamiania nowego kopania.",
                    MiningLogStatuses.Interrupted,
                    out _);
            }

            string cleanupMode = ChkInventoryCleanupEnabled.IsChecked != true
                ? "Auto EQ wyłączone"
                : ChkInventoryCleanupAllItemTypes.IsChecked == true
                    ? "Auto EQ: wszystko poza cobblestone"
                    : "Auto EQ: wybrane przedmioty";
            string direction = owner == InventoryCleanupOwner.Kopacz633
                ? IsKopacz633RectangleMode()
                    ? $"{GetKopacz633DirectionLabel().ToLowerInvariant()}, {GetConfiguredKopacz633UpwardWidth()}×{GetConfiguredKopacz633UpwardLength()}"
                    : $"na wprost, szerokość {GetConfiguredKopacz633ForwardWidth()}"
                : "kopanie w miejscu, LPM + Shift";
            string details = $"Uruchomiono {GetInventoryCleanupOwnerLabel(owner)} ({direction}). {cleanupMode}; "
                + $"CobbleX: {(ChkCobbleXEnabled.IsChecked == true ? "ON" : "OFF")}; "
                + $"jedzenie po EQ: {(ChkInventoryCleanupEatAfterCleanup.IsChecked == true ? "ON" : "OFF")}.";

            NotifyDiscordMiningStarted(
                GetInventoryCleanupOwnerLabel(owner),
                direction,
                cleanupMode.Replace("Auto EQ: ", string.Empty),
                ChkCobbleXEnabled.IsChecked == true,
                ChkInventoryCleanupEatAfterCleanup.IsChecked == true);

            if (_miningLogService.StartMiningRun(
                    GetInventoryCleanupOwnerLabel(owner),
                    details,
                    out string runId,
                    out _))
            {
                SetMiningRunId(owner, runId);
            }
            else
            {
                SetMiningRunId(owner, string.Empty);
            }

            RefreshMiningLogsSummary();
        }

        private void EndMiningLogRun(
            InventoryCleanupOwner owner,
            string reason,
            string status = MiningLogStatuses.Completed,
            bool announcedSeparately = false)
        {
            string runId = GetMiningRunId(owner);
            if (string.IsNullOrWhiteSpace(runId))
                return;

            _ = _miningLogService.CompleteMiningRun(runId, reason, status, out _);
            SetMiningRunId(owner, string.Empty);
            // Przerwanie (np. przez błąd) to ważniejsze zdarzenie niż zwykłe zatrzymanie.
            // Przerwanie przez alarm obrażeń albo brak kilofa ma już własne powiadomienie z oznaczeniem.
            bool regularEnd = status == MiningLogStatuses.Completed || _isExitRequested || _emergencyDamageSoundHandlingAlarm || announcedSeparately;
            NotifyDiscordMiningEnded(GetInventoryCleanupOwnerLabel(owner), reason, regularEnd, runId);
            RefreshMiningLogsSummary();
        }

        private void RecordAutomationLogEvent(string eventType, string status, string details)
        {
            NotifyDiscordAutomationEvent(eventType, status, details);
            string owner = string.IsNullOrWhiteSpace(_autoReconnectLogOwner)
                ? "Auto reconnect"
                : _autoReconnectLogOwner;
            _ = _miningLogService.RecordAutomationEvent(
                _autoReconnectLogMiningRunId,
                owner,
                eventType,
                status,
                details,
                out _);
            RefreshMiningLogsSummary();
        }

        private void UpdateConfirmedInventoryCleanupCounts(IReadOnlyList<DetectedInventoryItem> currentItems)
        {
            if (_inventoryCleanupDropPass == 0)
            {
                _inventoryCleanupInitialMarkedStacks = currentItems.Count;
                _inventoryCleanupInitialItemTypeCounts.Clear();
                _inventoryCleanupInitialItemTypeStackCounts.Clear();
                foreach (DetectedInventoryItem item in currentItems)
                {
                    _inventoryCleanupInitialItemTypeCounts.TryGetValue(item.ItemId, out int count);
                    _inventoryCleanupInitialItemTypeCounts[item.ItemId] = count + Math.Max(1, item.Quantity);
                    _inventoryCleanupInitialItemTypeStackCounts.TryGetValue(item.ItemId, out int stacks);
                    _inventoryCleanupInitialItemTypeStackCounts[item.ItemId] = stacks + 1;
                }
            }

            var currentItemTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var currentItemTypeStackCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DetectedInventoryItem item in currentItems)
            {
                currentItemTypeCounts.TryGetValue(item.ItemId, out int count);
                currentItemTypeCounts[item.ItemId] = count + Math.Max(1, item.Quantity);
                currentItemTypeStackCounts.TryGetValue(item.ItemId, out int stacks);
                currentItemTypeStackCounts[item.ItemId] = stacks + 1;
            }

            _inventoryCleanupRemovedStacks = Math.Max(0, _inventoryCleanupInitialMarkedStacks - currentItems.Count);
            _inventoryCleanupRemovedItems = 0;
            _inventoryCleanupItemTypeCounts.Clear();
            _inventoryCleanupItemTypeStackCounts.Clear();
            foreach ((string itemId, int initialCount) in _inventoryCleanupInitialItemTypeCounts)
            {
                currentItemTypeCounts.TryGetValue(itemId, out int currentCount);
                int removedCount = Math.Max(0, initialCount - currentCount);
                if (removedCount == 0)
                    continue;

                _inventoryCleanupItemTypeCounts[itemId] = removedCount;
                _inventoryCleanupRemovedItems += removedCount;

                _inventoryCleanupInitialItemTypeStackCounts.TryGetValue(itemId, out int initialStacks);
                currentItemTypeStackCounts.TryGetValue(itemId, out int currentStacks);
                int removedStacks = Math.Max(0, initialStacks - currentStacks);
                if (removedStacks > 0)
                    _inventoryCleanupItemTypeStackCounts[itemId] = removedStacks;
            }
        }

        private void RetryOrAbortInventoryCleanup(
            DateTime now,
            string reason,
            bool startReconnectIfEnabled = false)
        {
            if (_inventoryCleanupDetectionAttempts < InventoryCleanupMaximumDetectionAttempts)
            {
                _nextInventoryCleanupStageAtUtc = now.AddMilliseconds(InventoryCleanupDetectionRetryMs);
                return;
            }

            InventoryCleanupOwner owner = _inventoryCleanupOwner;
            bool ownerStillRunning = owner == InventoryCleanupOwner.Kopacz533
                ? _kopacz533RuntimeEnabled
                : owner == InventoryCleanupOwner.Kopacz633 && _kopacz633RuntimeEnabled;
            if (startReconnectIfEnabled
                && IsPeriodicAutoReconnectEnabled()
                && ownerStillRunning
                && _autoReconnectStage == AutoReconnectStage.None)
            {
                string reconnectReason = reason + " Po trzech próbach uruchomiono flow reconnectu.";
                RecordAbortedInventoryCleanup(reconnectReason);
                _inventoryCleanupLastResult = "Reconnect: " + reason;
                _inventoryCleanupLastResultWarning = true;
                ResetInventoryCleanupState(scheduleNext: false, now);
                UpdateInventoryCleanupStatus(reconnectReason, "Orange");
                BeginFullAutoReconnect(manual: false);
                return;
            }

            AbortInventoryCleanup(now, reason + " Sprawdź aktywny texturepack.");
        }

        private void AbortInventoryCleanup(DateTime now, string reason)
        {
            ReleaseInventoryCleanupDropKeys();
            if (IsGameGuiOpen())
                SendKeyTap(VK_ESCAPE);

            RecordAbortedInventoryCleanup(reason);
            _inventoryCleanupLastResult = "Przerwano: " + reason;
            _inventoryCleanupLastResultWarning = true;
            ResetInventoryCleanupState(scheduleNext: true, now);
            UpdateInventoryCleanupStatus($"Czyszczenie przerwane: {reason}", "Red");
        }

        private void RecordAbortedInventoryCleanup(string reason)
        {
            if (_inventoryCleanupCobbleXCommandSendCount > 0)
                reason += $" CobbleX: wysłano {_inventoryCleanupCobbleXCommandSendCount}/{_inventoryCleanupCobbleXCommandTargetCount} komend."
                    + (_inventoryCleanupCobbleXCommandSendCount < _inventoryCleanupCobbleXCommandTargetCount ? " Pozostałe powtórzenia anulowano." : " Seria komend była zakończona.");
            if (_goldDepositFlow != null)
                reason += " Wpłata złota: " + _goldDepositFlow.Message
                    + $"; potwierdzone przeniesienia {_goldDepositFlow.ConfirmedStacks} stosów. Sprawdź przedmiot na kursorze.";
            if (string.IsNullOrWhiteSpace(_inventoryCleanupLogSessionId))
                return;

            _ = _miningLogService.AbortInventorySession(
                _inventoryCleanupLogSessionId,
                GetInventoryCleanupOwnerLabel(_inventoryCleanupOwner),
                reason,
                _inventoryCleanupRemovedItems,
                _inventoryCleanupRemovedStacks,
                BuildInventoryCleanupItemDetails(),
                _inventoryCleanupDropPass,
                _inventoryCleanupRemainingMarkedStacks,
                _inventoryCleanupFullCobblestoneStacks,
                GetConfiguredCobbleXRequiredStacks(),
                out _);
            _inventoryCleanupLogSessionId = string.Empty;
            _inventoryCleanupLogStartError = string.Empty;
            RefreshMiningLogsSummary();
        }

        private bool SetInventoryCleanupControl(bool down)
        {
            if (_inventoryCleanupControlDown == down)
                return true;

            if (!SendGameKey(VK_LCONTROL, down))
                return false;

            _inventoryCleanupControlDown = down;
            return true;
        }

        private bool SetInventoryCleanupDropKey(bool down)
        {
            if (_inventoryCleanupDropKeyDown == down)
                return true;

            if (down)
            {
                int virtualKey = GetConfiguredDropItemVirtualKey();
                if (!SendGameKey(virtualKey, down: true))
                    return false;

                _inventoryCleanupDropVirtualKey = virtualKey;
                _inventoryCleanupDropKeyDown = true;
                return true;
            }

            int pressedVirtualKey = _inventoryCleanupDropVirtualKey != 0
                ? _inventoryCleanupDropVirtualKey
                : GetConfiguredDropItemVirtualKey();
            if (!SendGameKey(pressedVirtualKey, down: false))
                return false;

            _inventoryCleanupDropKeyDown = false;
            _inventoryCleanupDropVirtualKey = 0;
            return true;
        }

        private void ReleaseInventoryCleanupDropKeys()
        {
            if (_inventoryCleanupDropKeyDown)
            {
                int virtualKey = _inventoryCleanupDropVirtualKey != 0
                    ? _inventoryCleanupDropVirtualKey
                    : GetConfiguredDropItemVirtualKey();
                SendGameKey(virtualKey, down: false);
                _inventoryCleanupDropKeyDown = false;
                _inventoryCleanupDropVirtualKey = 0;
            }

            if (_inventoryCleanupControlDown)
            {
                SendGameKey(VK_LCONTROL, down: false);
                _inventoryCleanupControlDown = false;
            }
        }

        private void SetInventoryCleanupReturnLeft(bool down)
        {
            if (_inventoryCleanupReturnLeftDown == down)
                return;

            if (down)
                SendKeyDown(VK_A);
            else
                SendKeyUp(VK_A);
            _inventoryCleanupReturnLeftDown = down;
        }

        private void SetInventoryCleanupReturnBackward(bool down)
        {
            if (_inventoryCleanupReturnBackwardDown == down)
                return;

            if (down)
                SendKeyDown(VK_S);
            else
                SendKeyUp(VK_S);
            _inventoryCleanupReturnBackwardDown = down;
        }

        private void ReleaseInventoryCleanupReturnKeys()
        {
            SetInventoryCleanupReturnLeft(down: false);
            SetInventoryCleanupReturnBackward(down: false);
            _inventoryCleanupReturnLeftUntilUtc = DateTime.MinValue;
            _inventoryCleanupReturnBackwardUntilUtc = DateTime.MinValue;
        }

        private InventoryCleanupStage GetInventoryCleanupPostCommandStage()
        {
            if (_goldDepositPending && ChkGoldDepositEnabled.IsChecked == true) return InventoryCleanupStage.DepositGold;
            return _inventoryCleanupEatAfterCleanupPending
                ? InventoryCleanupStage.SelectFoodSlot
                : InventoryCleanupStage.ResumeMining;
        }

        private void SetInventoryCleanupEatingHold(bool enabled)
        {
            if (_inventoryCleanupEatingRightButtonDown == enabled)
                return;

            SendMouseButton(leftButton: false, down: enabled);
            _inventoryCleanupEatingRightButtonDown = enabled;
        }

        private bool IsKopaczCommandCountdownPaused(InventoryCleanupOwner owner)
        {
            return owner != InventoryCleanupOwner.None
                && _inventoryCleanupCommandPauseOwner == owner
                && _inventoryCleanupCommandPauseStartedAtUtc != DateTime.MinValue;
        }

        private void PauseKopaczCommandCountdown(InventoryCleanupOwner owner, DateTime now)
        {
            if (owner == InventoryCleanupOwner.None || IsKopaczCommandCountdownPaused(owner))
                return;

            _inventoryCleanupCommandPauseOwner = owner;
            _inventoryCleanupCommandPauseStartedAtUtc = now;
        }

        private int GetKopaczCommandRemainingSeconds(InventoryCleanupOwner owner, DateTime now)
        {
            DateTime deadline = owner == InventoryCleanupOwner.Kopacz533
                ? _nextKopacz533CommandAtUtc
                : _nextKopacz633CommandAtUtc;
            if (deadline == DateTime.MaxValue)
                return 0;

            DateTime countdownNow = IsKopaczCommandCountdownPaused(owner)
                ? _inventoryCleanupCommandPauseStartedAtUtc
                : now;
            return Math.Max(0, (int)Math.Ceiling((deadline - countdownNow).TotalSeconds));
        }

        private void ResumeKopaczCommandCountdown(DateTime now)
        {
            InventoryCleanupOwner owner = _inventoryCleanupCommandPauseOwner;
            DateTime pauseStartedAtUtc = _inventoryCleanupCommandPauseStartedAtUtc;
            _inventoryCleanupCommandPauseOwner = InventoryCleanupOwner.None;
            _inventoryCleanupCommandPauseStartedAtUtc = DateTime.MinValue;
            if (owner == InventoryCleanupOwner.None || pauseStartedAtUtc == DateTime.MinValue)
                return;

            TimeSpan pausedFor = now > pauseStartedAtUtc ? now - pauseStartedAtUtc : TimeSpan.Zero;
            if (owner == InventoryCleanupOwner.Kopacz533
                && _kopacz533RuntimeEnabled
                && _nextKopacz533CommandAtUtc != DateTime.MaxValue)
            {
                _nextKopacz533CommandAtUtc = AddWithoutOverflow(_nextKopacz533CommandAtUtc, pausedFor);
            }
            else if (owner == InventoryCleanupOwner.Kopacz633
                && _kopacz633RuntimeEnabled
                && _nextKopacz633CommandAtUtc != DateTime.MaxValue)
            {
                _nextKopacz633CommandAtUtc = AddWithoutOverflow(_nextKopacz633CommandAtUtc, pausedFor);
            }
        }

        private static DateTime AddWithoutOverflow(DateTime value, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
                return value;
            long remainingTicks = DateTime.MaxValue.Ticks - value.Ticks;
            return duration.Ticks >= remainingTicks ? DateTime.MaxValue : value.Add(duration);
        }

        private void ResetInventoryCleanupState(bool scheduleNext, DateTime? now = null)
        {
            // Invalidate results still being calculated on the worker thread.
            _inventoryCleanupGeneration++;
            DateTime resetAtUtc = now ?? DateTime.UtcNow;
            ResumeKopaczCommandCountdown(resetAtUtc);
            if (_inventoryCleanupStage != InventoryCleanupStage.None
                && !string.IsNullOrWhiteSpace(_inventoryCleanupLogSessionId))
            {
                RecordAbortedInventoryCleanup("Sesja EQ została zatrzymana przed zakończeniem skanowania.");
            }
            ResetGoldDepositState();

            ReleaseInventoryCleanupDropKeys();
            ReleaseInventoryCleanupReturnKeys();
            SetInventoryCleanupEatingHold(false);
            _inventoryCleanupStage = InventoryCleanupStage.None;
            _inventoryCleanupOwner = InventoryCleanupOwner.None;
            _inventoryCleanupTargets.Clear();
            _inventoryCleanupInitialItemTypeCounts.Clear();
            _inventoryCleanupInitialItemTypeStackCounts.Clear();
            _inventoryCleanupItemTypeCounts.Clear();
            _inventoryCleanupItemTypeStackCounts.Clear();
            _inventoryCleanupTargetIndex = 0;
            _inventoryCleanupDetectionAttempts = 0;
            _inventoryCleanupDropPass = 0;
            _inventoryCleanupInitialMarkedStacks = 0;
            _inventoryCleanupRemovedStacks = 0;
            _inventoryCleanupRemovedItems = 0;
            _inventoryCleanupRemainingMarkedStacks = 0;
            _inventoryCleanupCursorParkedForDetection = false;
            _inventoryCleanupClientArea = Drawing.Rectangle.Empty;
            _inventoryCleanupFullCobblestoneStacks = 0;
            _inventoryCleanupCobbleXCommandPending = false;
            _inventoryCleanupCobbleXCommandSent = false;
            _inventoryCleanupCobbleXCommandFailed = false;
            _inventoryCleanupCobbleXCommandTargetCount = 1;
            _inventoryCleanupCobbleXCommandSendCount = 0;
            _inventoryCleanupPendingCobbleXCommand = string.Empty;
            _inventoryCleanupEatAfterCleanupPending = false;
            _inventoryCleanupEatingCompleted = false;
            _inventoryCleanupLogSessionId = string.Empty;
            _inventoryCleanupLogStartError = string.Empty;
            _inventoryCleanupOpenedAtUtc = DateTime.MinValue;
            _nextInventoryCleanupStageAtUtc = resetAtUtc;

            if (scheduleNext && ChkInventoryCleanupEnabled.IsChecked == true)
                ScheduleNextInventoryCleanup(resetAtUtc);
            else
                _nextInventoryCleanupAtUtc = DateTime.MaxValue;
        }

        private void ResetInventoryCleanupForOwner(InventoryCleanupOwner owner)
        {
            if (_inventoryCleanupOwner == owner)
                ResetInventoryCleanupState(scheduleNext: false);
        }

        private void RunKopacz633Tick(DateTime now)
        {
            if (!IsKopacz633DirectionSelected())
            {
                SetKopacz633AttackHold(false);
                SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                return;
            }

            if (TryRunInventoryCleanup(InventoryCleanupOwner.Kopacz633, now))
                return;

            if (TryProcessKopacz633Command(now))
                return;

            if (_kopacz633ResumeMiningPending)
            {
                if (now < _nextKopacz633ResumeAtUtc)
                    return;

                _kopacz633ResumeMiningPending = false;
            }

            SetKopacz633AttackHold(true);

            if (_kopacz633StrafeDirection == Kopacz633StrafeDirection.None || now >= _kopacz633MovementLegEndAtUtc)
                StartKopacz633NextMovementLeg(now);

            if (_kopacz633CommandSequenceCompleted)
                return;

            if (now < _nextKopacz633CommandAtUtc)
                return;

            if (!TryPeekNextKopacz633Command(out int commandIndex, out string command, out int delaySeconds))
            {
                _kopacz633CommandSequenceCompleted = true;
                _nextKopacz633CommandAtUtc = DateTime.MaxValue;
                return;
            }

            _kopacz633PendingCommandIndex = commandIndex;
            _kopacz633PendingCommand = command;
            _kopacz633CommandStage = Kopacz633CommandStage.OpenChat;
            _nextKopacz633StageAtUtc = now;
            _nextKopacz633CommandAtUtc = now.AddSeconds(Math.Max(1, delaySeconds));
            SetKopacz633AttackHold(false);
            SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
        }

        private void StartKopacz633NextMovementLeg(DateTime now)
        {
            if (CbKopacz633Direction.SelectedIndex == 1)
            {
                int widthBlocks = GetConfiguredKopacz633ForwardWidth();
                Kopacz633StrafeDirection nextDirection = _kopacz633StrafeDirection == Kopacz633StrafeDirection.Right
                    ? Kopacz633StrafeDirection.Left
                    : Kopacz633StrafeDirection.Right;

                StartKopacz633MovementLeg(nextDirection, widthBlocks, now);
                return;
            }

            if (CbKopacz633Direction.SelectedIndex == 2)
            {
                int widthBlocks = GetConfiguredKopacz633UpwardWidth();
                int lengthBlocks = GetConfiguredKopacz633UpwardLength();

                Kopacz633StrafeDirection legDirection = _kopacz633UpwardLegIndex switch
                {
                    0 => Kopacz633StrafeDirection.Forward,
                    1 => Kopacz633StrafeDirection.Right,
                    2 => Kopacz633StrafeDirection.Backward,
                    _ => Kopacz633StrafeDirection.Left
                };

                int legBlocks = _kopacz633UpwardLegIndex % 2 == 0 ? lengthBlocks : widthBlocks;
                _kopacz633UpwardLegIndex = (_kopacz633UpwardLegIndex + 1) % 4;
                StartKopacz633MovementLeg(legDirection, legBlocks, now);
                return;
            }

            SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
            _kopacz633MovementLegEndAtUtc = now;
        }

        private void StartKopacz633MovementLeg(Kopacz633StrafeDirection direction, int widthBlocks, DateTime now)
        {
            widthBlocks = Math.Max(1, widthBlocks);
            SetKopacz633StrafeDirection(direction);
            _kopacz633MovementLegEndAtUtc = now.AddMilliseconds(widthBlocks * GetKopacz633MsPerBlock());
        }

        private void RunKopacz533Tick(DateTime now)
        {
            if (TryRunInventoryCleanup(InventoryCleanupOwner.Kopacz533, now))
                return;

            if (TryProcessKopacz533Command(now))
                return;

            if (_kopacz533ResumeMiningPending)
            {
                if (now < _nextKopacz533ResumeAtUtc)
                    return;

                _kopacz533ResumeMiningPending = false;
            }

            SetKopacz533MiningHold(true);

            if (_kopacz533CommandSequenceCompleted)
                return;

            if (now < _nextKopacz533CommandAtUtc)
                return;

            if (!TryPeekNextKopacz533Command(out int commandIndex, out string command, out int delaySeconds))
            {
                _kopacz533CommandSequenceCompleted = true;
                _nextKopacz533CommandAtUtc = DateTime.MaxValue;
                return;
            }

            _kopacz533PendingCommandIndex = commandIndex;
            _kopacz533PendingCommand = command;
            _kopacz533CommandStage = Kopacz533CommandStage.OpenChat;
            _nextKopacz533StageAtUtc = now;
            _nextKopacz533CommandAtUtc = now.AddSeconds(Math.Max(1, delaySeconds));
            SetKopacz533MiningHold(false);
        }

        private bool TryProcessKopacz533Command(DateTime now)
        {
            if (_kopacz533CommandStage == Kopacz533CommandStage.None)
                return false;

            if (now < _nextKopacz533StageAtUtc)
                return true;

            switch (_kopacz533CommandStage)
            {
                case Kopacz533CommandStage.OpenChat:
                    SendChatOpenKeyTap();
                    _kopacz533CommandStage = Kopacz533CommandStage.TypeCommand;
                    _nextKopacz533StageAtUtc = now.AddMilliseconds(Kopacz533DelayAfterOpenChatMs);
                    return true;

                case Kopacz533CommandStage.TypeCommand:
                    if (!ChatInputGate.IsReady(now))
                        return true;
                    if (!SendTextByKeyboard(_kopacz533PendingCommand))
                    {
                        UpdateStatusBar("Błąd: nie udało się wpisać komendy kopacza 5/3/3", "Red");
                        _nextKopacz533CommandAtUtc = now.AddSeconds(1);
                        _kopacz533CommandStage = Kopacz533CommandStage.None;
                        _kopacz533PendingCommandIndex = -1;
                        _kopacz533PendingCommand = string.Empty;
                        SetKopacz533MiningHold(true);
                        return true;
                    }

                    _kopacz533CommandStage = Kopacz533CommandStage.SubmitCommand;
                    _nextKopacz533StageAtUtc = now.AddMilliseconds(Kopacz533DelayAfterTypeCommandMs);
                    return true;

                case Kopacz533CommandStage.SubmitCommand:
                    SendKeyTap(VK_RETURN);
                    if (_kopacz533PendingCommandIndex >= 0)
                        _kopacz533CommandIndex = _kopacz533PendingCommandIndex + 1;

                    if (TryPeekNextKopacz533Command(out _, out _, out int nextDelaySeconds))
                    {
                        _kopacz533CommandSequenceCompleted = false;
                        _nextKopacz533CommandAtUtc = now.AddSeconds(nextDelaySeconds);
                    }
                    else
                    {
                        _kopacz533CommandSequenceCompleted = true;
                        _nextKopacz533CommandAtUtc = DateTime.MaxValue;
                    }

                    _kopacz533CommandStage = Kopacz533CommandStage.None;
                    _kopacz533PendingCommandIndex = -1;
                    _kopacz533PendingCommand = string.Empty;
                    // Force re-arm of mining after chat closes to avoid sticky "no dig" state.
                    SetKopacz533MiningHold(false);
                    _kopacz533ResumeMiningPending = true;
                    _nextKopacz533ResumeAtUtc = now.AddMilliseconds(Kopacz533DelayAfterSubmitResumeMs);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryProcessKopacz633Command(DateTime now)
        {
            if (_kopacz633CommandStage == Kopacz633CommandStage.None)
                return false;

            if (now < _nextKopacz633StageAtUtc)
                return true;

            switch (_kopacz633CommandStage)
            {
                case Kopacz633CommandStage.OpenChat:
                    SendChatOpenKeyTap();
                    _kopacz633CommandStage = Kopacz633CommandStage.TypeCommand;
                    _nextKopacz633StageAtUtc = now.AddMilliseconds(Kopacz633DelayAfterOpenChatMs);
                    return true;

                case Kopacz633CommandStage.TypeCommand:
                    if (!ChatInputGate.IsReady(now))
                        return true;
                    if (!SendTextByKeyboard(_kopacz633PendingCommand))
                    {
                        UpdateStatusBar("Błąd: nie udało się wpisać komendy kopacza 6/3/3", "Red");
                        _nextKopacz633CommandAtUtc = now.AddSeconds(1);
                        _kopacz633CommandStage = Kopacz633CommandStage.None;
                        _kopacz633PendingCommandIndex = -1;
                        _kopacz633PendingCommand = string.Empty;
                        SetKopacz633AttackHold(true);
                        return true;
                    }

                    _kopacz633CommandStage = Kopacz633CommandStage.SubmitCommand;
                    _nextKopacz633StageAtUtc = now.AddMilliseconds(Kopacz633DelayAfterTypeCommandMs);
                    return true;

                case Kopacz633CommandStage.SubmitCommand:
                    SendKeyTap(VK_RETURN);
                    if (_kopacz633PendingCommandIndex >= 0)
                        _kopacz633CommandIndex = _kopacz633PendingCommandIndex + 1;

                    if (TryPeekNextKopacz633Command(out _, out _, out int nextDelaySeconds))
                    {
                        _kopacz633CommandSequenceCompleted = false;
                        _nextKopacz633CommandAtUtc = now.AddSeconds(nextDelaySeconds);
                    }
                    else
                    {
                        _kopacz633CommandSequenceCompleted = true;
                        _nextKopacz633CommandAtUtc = DateTime.MaxValue;
                    }

                    _kopacz633CommandStage = Kopacz633CommandStage.None;
                    _kopacz633PendingCommandIndex = -1;
                    _kopacz633PendingCommand = string.Empty;
                    SetKopacz633AttackHold(false);
                    SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
                    _kopacz633ResumeMiningPending = true;
                    _nextKopacz633ResumeAtUtc = now.AddMilliseconds(Kopacz633DelayAfterSubmitResumeMs);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryPeekNextKopacz633Command(out int commandIndex, out string command, out int delaySeconds)
        {
            return TryGetKopacz633CommandAtOrAfter(_kopacz633CommandIndex, out commandIndex, out command, out delaySeconds);
        }

        private bool TryGetKopacz633CommandAtOrAfter(int startIndex, out int commandIndex, out string command, out int delaySeconds)
        {
            commandIndex = -1;
            command = string.Empty;
            delaySeconds = 3;

            if (_settings.Kopacz633Commands == null || _settings.Kopacz633Commands.Count == 0)
                return false;

            int count = _settings.Kopacz633Commands.Count;
            if (startIndex < 0)
                startIndex = 0;
            else if (startIndex >= count)
                startIndex = 0;

            for (int attempt = 0; attempt < count; attempt++)
            {
                int i = (startIndex + attempt) % count;
                MinerCommand cmd = _settings.Kopacz633Commands[i];

                string cmdText = (cmd.Command ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(cmdText))
                    continue;

                commandIndex = i;
                command = cmdText;
                delaySeconds = Math.Max(1, cmd.Seconds);
                return true;
            }

            return false;
        }

        private bool TryPeekNextKopacz533Command(out int commandIndex, out string command, out int delaySeconds)
        {
            return TryGetKopacz533CommandAtOrAfter(_kopacz533CommandIndex, out commandIndex, out command, out delaySeconds);
        }

        private bool TryGetKopacz533CommandAtOrAfter(int startIndex, out int commandIndex, out string command, out int delaySeconds)
        {
            commandIndex = -1;
            command = string.Empty;
            delaySeconds = 3;

            if (_settings.Kopacz533Commands == null || _settings.Kopacz533Commands.Count == 0)
                return false;

            int count = _settings.Kopacz533Commands.Count;
            if (startIndex < 0)
                startIndex = 0;
            else if (startIndex >= count)
                startIndex = 0;

            // Wrap around the list so after the last command we return to the first one.
            for (int attempt = 0; attempt < count; attempt++)
            {
                int i = (startIndex + attempt) % count;
                MinerCommand cmd = _settings.Kopacz533Commands[i];

                string cmdText = (cmd.Command ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(cmdText))
                    continue;

                commandIndex = i;
                command = cmdText;
                delaySeconds = Math.Max(1, cmd.Seconds);
                return true;
            }

            return false;
        }

        private void SetKopacz533MiningHold(bool enabled)
        {
            if (_kopacz533Holding == enabled)
                return;

            if (enabled)
            {
                SendKeyDown(VK_SHIFT);
                SendMouseButton(leftButton: true, down: true);
            }
            else
            {
                SendMouseButton(leftButton: true, down: false);
                SendKeyUp(VK_SHIFT);
            }

            _kopacz533Holding = enabled;
        }

        private void SetKopacz633AttackHold(bool enabled)
        {
            if (_kopacz633HoldingAttack == enabled)
                return;

            if (enabled)
                SendMouseButton(leftButton: true, down: true);
            else
                SendMouseButton(leftButton: true, down: false);

            _kopacz633HoldingAttack = enabled;
        }

        private void SetKopacz633StrafeDirection(Kopacz633StrafeDirection direction)
        {
            if (_kopacz633StrafeDirection == direction)
                return;

            switch (_kopacz633StrafeDirection)
            {
                case Kopacz633StrafeDirection.Forward:
                    SendKeyUp(VK_W);
                    break;
                case Kopacz633StrafeDirection.Right:
                    SendKeyUp(VK_D);
                    break;
                case Kopacz633StrafeDirection.Backward:
                    SendKeyUp(VK_S);
                    break;
                case Kopacz633StrafeDirection.Left:
                    SendKeyUp(VK_A);
                    break;
            }

            switch (direction)
            {
                case Kopacz633StrafeDirection.Forward:
                    SendKeyDown(VK_W);
                    break;
                case Kopacz633StrafeDirection.Right:
                    SendKeyDown(VK_D);
                    break;
                case Kopacz633StrafeDirection.Backward:
                    SendKeyDown(VK_S);
                    break;
                case Kopacz633StrafeDirection.Left:
                    SendKeyDown(VK_A);
                    break;
                default:
                    SendKeyUp(VK_W);
                    SendKeyUp(VK_D);
                    SendKeyUp(VK_S);
                    SendKeyUp(VK_A);
                    break;
            }

            _kopacz633StrafeDirection = direction;
        }

        private static bool SendTextByKeyboard(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char ch in text)
            {
                // Background Unicode characters carry their own key-down/up
                // events, without touching the user's keyboard state or clipboard.
                bool sent = BackgroundGameWindow.IsActive
                    ? BackgroundGameWindow.SendChar(ch)
                    : TrySendCharByKeyboard(ch);
                if (!sent)
                    return false;
            }

            return true;
        }

        private static bool TrySendCharByKeyboard(char ch)
        {
            short vkInfo = VkKeyScan(ch);
            if (vkInfo == -1)
                return false;

            int virtualKey = vkInfo & 0xFF;
            int shiftState = (vkInfo >> 8) & 0xFF;

            bool needsShift = (shiftState & 1) != 0;
            bool needsCtrl = (shiftState & 2) != 0;
            bool needsAlt = (shiftState & 4) != 0;

            if (needsShift)
                SendKeyDown(VK_SHIFT);
            if (needsCtrl)
                SendKeyDown(VK_CONTROL);
            if (needsAlt)
                SendKeyDown(VK_MENU);

            SendKeyTap(virtualKey);

            if (needsAlt)
                SendKeyUp(VK_MENU);
            if (needsCtrl)
                SendKeyUp(VK_CONTROL);
            if (needsShift)
                SendKeyUp(VK_SHIFT);

            return true;
        }

        private void ResetTestAutoFishingRuntimeState()
        {
            ResetTestAutoFishingRuntimeState(DateTime.UtcNow);
        }

        private void ResetTestAutoFishingRuntimeState(DateTime now)
        {
            _nextTestAutoFishingScanAtUtc = now;
            _nextTestAutoFishingActionAtUtc = now;
            _nextTestAutoFishingRepairAtUtc = DateTime.MaxValue;
            _nextTestAutoFishingRepairStageAtUtc = now;
            _testAutoFishingAwaitSecondClick = false;
            _testAutoFishingRecastAfterRepairPending = false;
            _testAutoFishingRecastAfterRepairAtUtc = now;
            _testAutoFishingWaitingForCastRegistration = false;
            _testAutoFishingRepairStage = TestAutoFishingRepairStage.None;
            ResetTestAutoFishingDetection(now);
            if (_testAutoFishingRuntimeEnabled && HasConfiguredTestAutoFishingRepair())
                _nextTestAutoFishingRepairAtUtc = now.AddSeconds(GetConfiguredTestAutoFishingRepairIntervalSeconds());
        }

        private void ResetKopacz533RuntimeState()
        {
            ResetKopacz533RuntimeState(DateTime.UtcNow);
        }

        private void ResetKopacz533RuntimeState(DateTime now)
        {
            _nextKopacz533CommandAtUtc = now;
            _nextKopacz533StageAtUtc = now;
            _nextKopacz533ResumeAtUtc = now;
            _nextRuntimeTileRefreshAtUtc = now;
            _kopacz533ResumeMiningPending = false;
            _kopacz533CommandStage = Kopacz533CommandStage.None;
            _kopacz533CommandSequenceCompleted = false;
            _kopacz533CommandIndex = 0;
            _kopacz533PendingCommandIndex = -1;
            _kopacz533PendingCommand = string.Empty;
            _kopacz533RuntimeStartedAtUtc = now;
            ResetInventoryCleanupForOwner(InventoryCleanupOwner.Kopacz533);
        }

        private void ResetKopacz633RuntimeState()
        {
            ResetKopacz633RuntimeState(DateTime.UtcNow);
        }

        private void ResetKopacz633RuntimeState(DateTime now)
        {
            _nextKopacz633CommandAtUtc = now;
            _nextKopacz633StageAtUtc = now;
            _nextKopacz633ResumeAtUtc = now;
            _nextRuntimeTileRefreshAtUtc = now;
            _kopacz633ResumeMiningPending = false;
            _kopacz633CommandStage = Kopacz633CommandStage.None;
            _kopacz633CommandSequenceCompleted = false;
            _kopacz633CommandIndex = 0;
            _kopacz633PendingCommandIndex = -1;
            _kopacz633PendingCommand = string.Empty;
            _kopacz633RuntimeStartedAtUtc = now;
            _kopacz633MovementLegEndAtUtc = now;
            _kopacz633UpwardLegIndex = 0;
            _kopacz633StrafeDirection = Kopacz633StrafeDirection.None;
            ResetInventoryCleanupForOwner(InventoryCleanupOwner.Kopacz633);
        }

        private static void SendMouseMoveRelative(int deltaX, int deltaY)
        {
            NativeInput.SendMouseMoveRelative(deltaX, deltaY);
        }

        // Every miner input goes through these wrappers. During background mining
        // they post messages to the game window instead of using global SendInput.
        private bool SendMouseClick(bool leftButton, bool holdPulseMode)
        {
            return BackgroundGameWindow.IsActive
                ? BackgroundGameWindow.SendMouseClick(leftButton, holdPulseMode)
                : NativeInput.SendMouseClick(leftButton, holdPulseMode);
        }

        private void SendMouseButton(bool leftButton, bool down)
        {
            if (BackgroundGameWindow.IsActive)
                BackgroundGameWindow.SendMouseButton(leftButton, down);
            else
                NativeInput.SendMouseButton(leftButton, down);
        }

        private static void SendKeyTap(int virtualKey)
        {
            SendKeyDown(virtualKey);
            SendKeyUp(virtualKey);
        }

        private static void SendKeyDown(int virtualKey)
        {
            SendGameKey(virtualKey, down: true);
        }

        private static void SendKeyUp(int virtualKey)
        {
            SendGameKey(virtualKey, down: false);
        }

        private static bool SendGameKey(int virtualKey, bool down)
        {
            return BackgroundGameWindow.IsActive
                ? BackgroundGameWindow.SendKey(virtualKey, down)
                : NativeInput.SendKey(virtualKey, down);
        }

        // Screen coordinates, as computed from the game's client rect. In background
        // mode only the game's own cursor position changes, never the user's cursor.
        private static bool SetGameCursorPosition(int x, int y)
        {
            return BackgroundGameWindow.IsActive
                ? BackgroundGameWindow.MoveCursorToScreenPoint(x, y)
                : NativeInput.SetCursorPosition(x, y);
        }

        private void UpdateStatusBar(string message, string colorName)
        {
            LogStatus("Kopacz", message, colorName);
            if (TxtStatusBar == null) return;

            _transientStatusTimer?.Stop();

            TxtStatusBar.Text = message;
            TxtStatusBar.Foreground =
                colorName == "Red" ? new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107)) :
                colorName == "Green" ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180)) :
                colorName == "Orange" ? new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36)) :
                new SolidColorBrush(GlassPalette.MapLegacyColor(207, 219, 235));
        }

        private void UpdateTemporaryStatusBar(string message, string colorName, int seconds = 4)
        {
            UpdateStatusBar(message, colorName);
            _transientStatusTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
            _transientStatusTimer.Start();
        }

        private void ChkOverlaySettings_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            UpdateEnabledStates();
            UpdateOverlayLayout();
            RefreshOverlayHud(DateTime.UtcNow);
            MarkDirty();
        }

        private void ChkAnimatedBackgroundEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (CyberBackground != null)
                CyberBackground.IsAnimationEnabled = ChkAnimatedBackgroundEnabled.IsChecked != false;

            if (_isLoadingUi)
                return;

            MarkDirty();
        }

        private void CbOverlayMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            UpdateOverlayLayout();
            MarkDirty();
        }

        private void CbOverlayCorner_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            UpdateOverlayLayout();
            MarkDirty();
        }

        private void RefreshEmergencyDamageSoundDevices()
        {
            if (CbEmergencyDamageSoundDevice == null)
                return;

            string preferredId = GetSelectedEmergencyDamageSoundDeviceId();
            if (string.IsNullOrWhiteSpace(preferredId))
                preferredId = _settings.EmergencyDamageSoundDeviceId;

            bool previousLoading = _isLoadingUi;
            _isLoadingUi = true;
            try
            {
                CbEmergencyDamageSoundDevice.Items.Clear();
                IReadOnlyList<AudioOutputDeviceInfo> devices = DamageSoundDetector.GetOutputDevices();
                foreach (AudioOutputDeviceInfo device in devices)
                {
                    CbEmergencyDamageSoundDevice.Items.Add(new ComboBoxItem
                    {
                        Content = device.IsDefault ? $"{device.Name} (domyślne)" : device.Name,
                        Tag = device.Id
                    });
                }

                int selectedIndex = -1;
                for (int i = 0; i < CbEmergencyDamageSoundDevice.Items.Count; i++)
                {
                    if (CbEmergencyDamageSoundDevice.Items[i] is ComboBoxItem item
                        && string.Equals(item.Tag as string, preferredId, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                CbEmergencyDamageSoundDevice.SelectedIndex = selectedIndex >= 0
                    ? selectedIndex
                    : (CbEmergencyDamageSoundDevice.Items.Count > 0 ? 0 : -1);
            }
            catch (Exception ex)
            {
                CbEmergencyDamageSoundDevice.Items.Add(new ComboBoxItem
                {
                    Content = "Nie udało się odczytać urządzeń audio",
                    IsEnabled = false
                });
                CbEmergencyDamageSoundDevice.SelectedIndex = 0;
                UpdateEmergencyDamageSoundStatus("Błąd urządzeń audio: " + ex.Message, "Red");
            }
            finally
            {
                _isLoadingUi = previousLoading;
            }

            UpdateEmergencyDamageSoundSourcePreview();
        }

        private string GetSelectedEmergencyDamageSoundDeviceId()
        {
            return CbEmergencyDamageSoundDevice?.SelectedItem is ComboBoxItem item
                ? item.Tag as string ?? string.Empty
                : string.Empty;
        }

        private string ResolveEmergencyDamageSoundDeviceId(out string sourceDescription, out bool matchedMinecraft)
        {
            matchedMinecraft = false;
            if (TryGetCurrentMinecraftProcessId(out int minecraftProcessId)
                && DamageSoundDetector.TryGetOutputDeviceForProcess(minecraftProcessId, out AudioOutputDeviceInfo? minecraftDevice)
                && minecraftDevice != null)
            {
                matchedMinecraft = true;
                sourceDescription = $"Minecraft [{minecraftProcessId}] → {minecraftDevice.Name}";
                return minecraftDevice.Id;
            }

            string fallbackDeviceId = GetSelectedEmergencyDamageSoundDeviceId();
            string fallbackName = CbEmergencyDamageSoundDevice?.SelectedItem is ComboBoxItem fallbackItem
                ? fallbackItem.Content?.ToString() ?? "domyślne urządzenie Windows"
                : "domyślne urządzenie Windows";
            sourceDescription = TryGetCurrentMinecraftProcessId(out int unresolvedProcessId)
                ? $"Nie znaleziono sesji Minecraft [{unresolvedProcessId}] — awaryjnie: {fallbackName}"
                : $"Brak zapisanego procesu Minecraft — awaryjnie: {fallbackName}";
            return fallbackDeviceId;
        }

        private bool TryGetCurrentMinecraftProcessId(out int processId)
        {
            processId = 0;
            IntPtr targetWindow = _targetGameWindowHandle;
            if (targetWindow == IntPtr.Zero && TryResolveTargetWindow(allowPendingSelection: false, out IntPtr resolvedWindow))
            {
                targetWindow = resolvedWindow;
                _targetGameWindowHandle = resolvedWindow;
            }

            if (targetWindow != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(targetWindow, out uint processIdRaw);
                if (processIdRaw > 0 && processIdRaw <= int.MaxValue)
                {
                    processId = (int)processIdRaw;
                    return true;
                }
            }

            int configuredProcessId = _settings.TargetProcessId;
            if (configuredProcessId <= 0)
                return false;
            try
            {
                using Process configuredProcess = Process.GetProcessById(configuredProcessId);
                string configuredName = (_settings.TargetProcessName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(configuredName)
                    && !string.Equals(configuredProcess.ProcessName, configuredName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                processId = configuredProcessId;
                return true;
            }
            catch
            {
                // Zapisany PID już nie istnieje albo nie mamy do niego dostępu.
                return false;
            }
        }

        private void UpdateEmergencyDamageSoundSourcePreview()
        {
            if (TxtEmergencyDamageSoundSource == null)
                return;

            try
            {
                _ = ResolveEmergencyDamageSoundDeviceId(out string sourceDescription, out bool matchedMinecraft);
                TxtEmergencyDamageSoundSource.Text = matchedMinecraft
                    ? "Automatycznie: " + sourceDescription
                    : sourceDescription;
                TxtEmergencyDamageSoundSource.Foreground = matchedMinecraft
                    ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                    : new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36));
            }
            catch (Exception ex)
            {
                TxtEmergencyDamageSoundSource.Text = "Nie udało się sprawdzić sesji Minecrafta: " + ex.Message;
                TxtEmergencyDamageSoundSource.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));
            }
        }

        private void UpdateEmergencyDamageSoundReferenceInfo()
        {
            if (TxtEmergencyDamageSoundReferenceInfo == null)
                return;
            int count = _settings.EmergencyDamageSoundTemplates?.Count ?? 0;
            TxtEmergencyDamageSoundReferenceInfo.Text = count > 0
                ? $"Stały wzorzec gotowy: hit1–hit4 • {count} profili widma • minimalny poziom {_settings.EmergencyDamageSoundMinimumDb:0} dB."
                : "Nie udało się wczytać stałych wzorców hit1–hit4 z plików aplikacji.";
            TxtEmergencyDamageSoundReferenceInfo.Foreground = count > 0
                ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                : new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193));
        }

        private void UpdateEmergencyDamageSoundStatus(string message, string colorName = "Default")
        {
            LogStatus("Ochrona", message, colorName);
            if (TxtEmergencyDamageSoundStatus == null)
                return;
            TxtEmergencyDamageSoundStatus.Text = message;
            TxtEmergencyDamageSoundStatus.Foreground = colorName == "Red"
                ? new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107))
                : colorName == "Green"
                    ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                    : colorName == "Orange"
                        ? new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36))
                        : new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193));
        }

        private void UpdateEmergencyReconnectStatus(string message, string colorName = "Default")
        {
            LogStatus("Ochrona", message, colorName);
            if (TxtEmergencyReconnectStatus == null)
                return;
            TxtEmergencyReconnectStatus.Text = message;
            TxtEmergencyReconnectStatus.Foreground = colorName == "Red"
                ? new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107))
                : colorName == "Green"
                    ? new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180))
                    : colorName == "Orange"
                        ? new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36))
                        : new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193));
        }

        private void RefreshEmergencyReconnectProfileSummary()
        {
            if (TxtEmergencyReconnectProfileSummary == null)
                return;

            if (!IsEmergencyReconnectEnabled())
            {
                TxtEmergencyReconnectProfileSummary.Text = "Reconnect wyłączony. Po alarmie tylko Disconnect, bez home i wznowienia kopania. Profil nie jest wymagany.";
                TxtEmergencyReconnectProfileSummary.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193));
                return;
            }

            AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
            if (profile == null)
            {
                TxtEmergencyReconnectProfileSummary.Text = "Brak profilu Auto reconnect. Dodaj i wybierz profil serwera.";
                TxtEmergencyReconnectProfileSummary.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(255, 107, 107));
                return;
            }

            NormalizeAutoReconnectHomeSettings(profile);
            string normalHome = profile.HomeHasGui
                ? $"{profile.HomeCommand} → GUI {profile.HomeGuiRows}×{profile.HomeGuiColumns}, slot {profile.HomeGuiSlot}"
                : $"{profile.HomeCommand} → bez GUI";
            string missingHome = profile.MissingPickaxeHomeHasGui == true
                ? $"{profile.MissingPickaxeHomeCommand} → GUI {profile.MissingPickaxeHomeGuiRows}×{profile.MissingPickaxeHomeGuiColumns}, slot {profile.MissingPickaxeHomeGuiSlot}"
                : $"{profile.MissingPickaxeHomeCommand} → bez GUI";
            TxtEmergencyReconnectProfileSummary.Text =
                $"Profil: {profile.Name} • serwer: {profile.ServerAddress}\n" +
                $"Kilof wykryty: {normalHome} → wznowienie Kopacza\n" +
                $"Brak kilofa: {missingHome} → teleport i zamknięcie programu";
            TxtEmergencyReconnectProfileSummary.Foreground = profile.MissingPickaxeRecoveryEnabled
                ? new SolidColorBrush(GlassPalette.MapLegacyColor(146, 166, 193))
                : new SolidColorBrush(GlassPalette.MapLegacyColor(251, 191, 36));
            if (!profile.MissingPickaxeRecoveryEnabled)
                TxtEmergencyReconnectProfileSummary.Text += "\nUWAGA: w profilu jest wyłączona obsługa braku kilofa.";
        }

        private void EmergencyReconnectSetting_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;
            _settings.EmergencyReconnectDelaySeconds = Math.Clamp(
                ParseNonNegativeInt(TxtEmergencyReconnectDelaySeconds.Text),
                1,
                600);
            MarkDirty();
        }

        private void BtnEmergencyReconnectStop_Click(object sender, RoutedEventArgs e)
        {
            if (_autoReconnectStage != AutoReconnectStage.None || _emergencyReconnectActive)
                StopAutoReconnect("Awaryjna procedura zatrzymana ręcznie.", resumeMining: false, warning: true);
            else
                ClearEmergencyReconnectRuntimeState(stopSoundMonitoring: true);
            UpdateEmergencyReconnectStatus("Awaryjna procedura zatrzymana ręcznie.", "Orange");
            UpdateEnabledStates();
        }

        private void UpdateEmergencyReconnectReadyStatus()
        {
            if (ChkEmergencyDamageSoundEnabled?.IsChecked != true)
                UpdateEmergencyReconnectStatus("Ochrona awaryjna jest wyłączona.");
            else if (ChkEmergencyDamageSoundTestMode?.IsChecked != false)
                UpdateEmergencyReconnectStatus("Tryb testowy: tylko alarm i log, bez Disconnect ani reconnectu.", "Orange");
            else if (_settings.EmergencyDamageSoundTemplates.Count == 0)
                UpdateEmergencyReconnectStatus("Brakuje wzorców hit1–hit4 — ochrona nie jest jeszcze uzbrojona.", "Orange");
            else
                UpdateEmergencyReconnectStatus(IsEmergencyReconnectEnabled()
                    ? "Gotowe. Po alarmie Disconnect, odczekanie, reconnect i kontrola kilofa."
                    : "Tryb tylko Disconnect. Po alarmie Kopacz zostanie zatrzymany; program nie wróci na serwer.", "Green");
        }

        // Wybór „Tylko Disconnect” / „Powrót i kopanie” w zakładce Ochrona.
        private void OnEmergencyReconnectModeChanged()
        {
            if (_isLoadingUi || _settings == null)
                return;
            _settings.EmergencyReconnectEnabled = RbProtectionReturn.IsChecked == true;
            if (!_settings.EmergencyReconnectEnabled
                && (_emergencyReconnectActive
                    || (!_autoReconnectManualRun && _autoReconnectStage != AutoReconnectStage.None)))
            {
                StopAutoReconnect("Reconnect wyłączony. Pozostałe kroki powrotu anulowano; kopanie nie będzie wznowione.", resumeMining: false, warning: false);
            }
            RefreshEmergencyReconnectProfileSummary();
            UpdateEmergencyReconnectReadyStatus();
            UpdateEnabledStates();
            MarkDirty();
        }

        private void ChkEmergencyDamageSoundEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            _settings.EmergencyDamageSoundEnabled = ChkEmergencyDamageSoundEnabled.IsChecked == true;
            if (!_settings.EmergencyDamageSoundEnabled)
            {
                if (_emergencyReconnectActive)
                    StopAutoReconnect("Awaryjna ochrona wyłączona przez użytkownika.", resumeMining: false, warning: true);
                StopEmergencyDamageSoundListening(updateStatus: true);
                UpdateEmergencyReconnectStatus("Ochrona awaryjna jest wyłączona.");
            }
            else if (_settings.EmergencyDamageSoundTemplates.Count == 0)
            {
                UpdateEmergencyDamageSoundStatus("Włączono, ale brakuje stałych wzorców hit1–hit4.", "Orange");
                UpdateEmergencyReconnectStatus("Brakuje wzorców hit1–hit4 — procedura nie jest jeszcze uzbrojona.", "Orange");
            }
            else
            {
                UpdateEmergencyDamageSoundStatus("Gotowy. Nasłuch uruchomi się razem z Kopaczem.", "Green");
                UpdateEmergencyReconnectReadyStatus();
            }
            RefreshEmergencyReconnectProfileSummary();
            UpdateEnabledStates();
            MarkDirty();
        }

        private void ChkEmergencyDamageSoundSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            _settings.EmergencyDamageSoundTestMode = ChkEmergencyDamageSoundTestMode.IsChecked != false;
            UpdateEmergencyDamageSoundStatus(
                _settings.EmergencyDamageSoundTestMode
                    ? "Tryb testowy: alarm nie zatrzyma kopania ani nie wyjdzie z serwera."
                    : "TRYB REALNY UZBROJONY: wykrycie uruchomi ESC → Disconnect.",
                _settings.EmergencyDamageSoundTestMode ? "Green" : "Orange");
            UpdateEmergencyReconnectReadyStatus();
            UpdateEmergencyProtectionOptionAvailability();
            MarkDirty();
        }

        private void SlEmergencyDamageSoundSimilarity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int value = Math.Clamp((int)Math.Round(e.NewValue), 70, 99);
            if (TxtEmergencyDamageSoundSimilarityValue != null)
                TxtEmergencyDamageSoundSimilarityValue.Text = $"{value}%";
            if (_isLoadingUi)
                return;

            _settings.EmergencyDamageSoundSimilarityPercent = value;
            if (_damageSoundDetector.IsRunning)
            {
                _damageSoundDetector.Stop();
                _emergencyDamageSoundMonitoringForMiner = false;
                _emergencyDamageSoundManualTestActive = false;
            }
            MarkDirty();
        }

        private void CbEmergencyDamageSoundDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            _settings.EmergencyDamageSoundDeviceId = GetSelectedEmergencyDamageSoundDeviceId();
            StopEmergencyDamageSoundListening(updateStatus: false);
            UpdateEmergencyDamageSoundSourcePreview();
            UpdateEmergencyDamageSoundStatus("Zmieniono urządzenie awaryjne. Minecraft nadal jest wybierany automatycznie.", "Green");
            MarkDirty();
        }

        private void BtnEmergencyDamageSoundRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            StopEmergencyDamageSoundListening(updateStatus: false);
            RefreshEmergencyDamageSoundDevices();
            _settings.EmergencyDamageSoundDeviceId = GetSelectedEmergencyDamageSoundDeviceId();
            UpdateEmergencyDamageSoundSourcePreview();
            UpdateEmergencyDamageSoundStatus("Sprawdzono sesję audio Minecrafta i odświeżono urządzenia.", "Green");
            MarkDirty();
        }

        private void BtnEmergencyDamageSoundReloadReferences_Click(object sender, RoutedEventArgs e)
        {
            StopEmergencyDamageSoundListening(updateStatus: false);
            LoadBundledDamageSoundReferences(showStatus: true);
            UpdateEnabledStates();
        }

        private bool LoadBundledDamageSoundReferences(bool showStatus)
        {
            string soundDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "Damage");
            string[] soundFiles = Enumerable.Range(1, 4)
                .Select(index => Path.Combine(soundDirectory, $"hit{index}.ogg"))
                .ToArray();
            try
            {
                DamageSoundReferenceSet referenceSet = DamageSoundDetector.LoadReferenceFiles(soundFiles);
                _settings.EmergencyDamageSoundTemplates = referenceSet.Templates
                    .Select(template => template.ToList())
                    .ToList();
                _settings.EmergencyDamageSoundMinimumDb = referenceSet.SuggestedMinimumDb;
                UpdateEmergencyDamageSoundReferenceInfo();
                if (showStatus)
                {
                    UpdateEmergencyDamageSoundStatus(
                        $"Wczytano {referenceSet.SourceFileCount} stałe dźwięki obrażeń i {_settings.EmergencyDamageSoundTemplates.Count} profili widma.",
                        "Green");
                    MarkDirty();
                }
                return true;
            }
            catch (Exception ex)
            {
                _settings.EmergencyDamageSoundTemplates.Clear();
                UpdateEmergencyDamageSoundReferenceInfo();
                if (showStatus || TxtEmergencyDamageSoundStatus != null)
                    UpdateEmergencyDamageSoundStatus("Błąd stałych wzorców dźwięku: " + ex.Message, "Red");
                return false;
            }
        }

        private void BtnEmergencyDamageSoundTest_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.EmergencyDamageSoundTemplates.Count == 0)
            {
                UpdateEmergencyDamageSoundStatus("Nie wczytano stałych wzorców hit1–hit4.", "Red");
                return;
            }

            _emergencyDamageSoundManualTestActive = true;
            _emergencyDamageSoundManualTestUntilUtc = DateTime.UtcNow.AddSeconds(15);
            StartEmergencyDamageSoundMonitoring(manualTest: true);
        }

        private void BtnEmergencyDamageSoundStop_Click(object sender, RoutedEventArgs e)
        {
            StopEmergencyDamageSoundListening(updateStatus: false);
            UpdateEmergencyDamageSoundStatus("Test/nasłuch zatrzymany. Automatycznie wróci przy następnym uruchomieniu Kopacza.", "Orange");
            UpdateEnabledStates();
        }

        private void UpdateEmergencyDamageSoundMonitoring()
        {
            if (_isLoadingUi || _emergencyDamageSoundHandlingAlarm)
                return;

            DateTime now = DateTime.UtcNow;
            if (_emergencyDamageSoundManualTestActive && now >= _emergencyDamageSoundManualTestUntilUtc)
            {
                bool wasLiveTest = _emergencyDamageSoundLiveTestActive;
                StopEmergencyDamageSoundListening(updateStatus: false);
                UpdateEmergencyDamageSoundStatus(wasLiveTest ? "Nasłuch z reakcją zakończony — w ciągu 30 sekund nie wykryto uderzenia, nic nie zrobiono." : "Test zakończony. Nie wykryto wzorca w czasie 15 sekund.", "Green");
                UpdateEnabledStates();
            }

            bool minerActive = _kopacz533RuntimeEnabled || _kopacz633RuntimeEnabled;
            bool emergencyGuardActive = _emergencyReconnectSoundGuardActive;
            bool enabled = ChkEmergencyDamageSoundEnabled?.IsChecked == true;
            bool hasTemplates = _settings.EmergencyDamageSoundTemplates.Count > 0;
            bool shouldMonitorForMiner = ShouldMonitorEmergencyDamageSound(
                enabled,
                hasTemplates,
                minerActive || emergencyGuardActive,
                manualTest: false);
            bool shouldMonitor = ShouldMonitorEmergencyDamageSound(
                enabled,
                hasTemplates,
                minerActive || emergencyGuardActive,
                _emergencyDamageSoundManualTestActive);

            if (!shouldMonitor)
            {
                if (_damageSoundDetector.IsRunning)
                    StopEmergencyDamageSoundListening(updateStatus: false);
                _emergencyDamageSoundMonitoringForMiner = false;
                return;
            }

            if (_damageSoundDetector.IsRunning)
            {
                _emergencyDamageSoundMonitoringForMiner = shouldMonitorForMiner && !_emergencyDamageSoundManualTestActive;
                return;
            }

            StartEmergencyDamageSoundMonitoring(_emergencyDamageSoundManualTestActive);
        }

        private static bool ShouldMonitorEmergencyDamageSound(
            bool enabled,
            bool hasTemplates,
            bool minerActive,
            bool manualTest)
        {
            return manualTest || (enabled && hasTemplates && minerActive);
        }

        private void StartEmergencyDamageSoundMonitoring(bool manualTest)
        {
            try
            {
                string deviceId = ResolveEmergencyDamageSoundDeviceId(
                    out string sourceDescription,
                    out bool matchedMinecraft);
                _damageSoundDetector.StartMonitoring(
                    deviceId,
                    _settings.EmergencyDamageSoundTemplates,
                    _settings.EmergencyDamageSoundSimilarityPercent,
                    _settings.EmergencyDamageSoundMinimumDb);
                _emergencyDamageSoundMonitoringForMiner = !manualTest;
                UpdateEmergencyDamageSoundSourcePreview();
                string sourceNote = matchedMinecraft
                    ? $" Audio: {sourceDescription}."
                    : $" Użyto źródła awaryjnego: {sourceDescription}.";
                UpdateEmergencyDamageSoundStatus(
                    manualTest && _emergencyDamageSoundLiveTestActive
                        ? "Nasłuch Z REAKCJĄ trwa 30 s — po wykryciu uderzenia Kopacz wykona pełną reakcję (wyjście z serwera)." + sourceNote
                    : manualTest
                        ? "Bezpieczny test trwa 15 s — odtwórz dźwięk obrażeń. Minecraft pozostanie na serwerze." + sourceNote
                        : _settings.EmergencyDamageSoundTestMode
                            ? "Nasłuch aktywny z Kopaczem • TRYB TESTOWY." + sourceNote
                            : "Nasłuch aktywny z Kopaczem • TRYB REALNY UZBROJONY." + sourceNote,
                    manualTest || _settings.EmergencyDamageSoundTestMode ? "Green" : "Orange");
                UpdateEnabledStates();
            }
            catch (Exception ex)
            {
                _emergencyDamageSoundManualTestActive = false;
                _emergencyDamageSoundMonitoringForMiner = false;
                UpdateEmergencyDamageSoundStatus("Nie udało się uruchomić nasłuchu: " + ex.Message, "Red");
            }
        }

        private void StopEmergencyDamageSoundListening(bool updateStatus)
        {
            _emergencyDamageSoundManualTestActive = false;
            _emergencyDamageSoundLiveTestActive = false;
            _emergencyDamageSoundManualTestUntilUtc = DateTime.MinValue;
            _emergencyDamageSoundMonitoringForMiner = false;
            _damageSoundDetector.Stop();
            if (updateStatus)
                UpdateEmergencyDamageSoundStatus("Nieaktywny. Nasłuch jest wyłączony.");
        }

        private void DamageSoundDetector_ProgressChanged(object? sender, DamageSoundProgressEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                // Live similarity is a configuration aid. Do not keep changing
                // hidden WPF text/layout while the user is playing Minecraft.
                if (!IsVisible || !IsActive || _isMinimizedToTray || !IsGlassSectionActive(TabProtection))
                    return;
                if (DateTime.UtcNow - _emergencyDamageSoundLastAlarmAtUtc < TimeSpan.FromSeconds(2))
                    return;
                if (_damageSoundDetector.IsRunning)
                {
                    string mode = _emergencyDamageSoundManualTestActive
                        ? "Test"
                        : _settings.EmergencyDamageSoundTestMode ? "Nasłuch testowy" : "Nasłuch uzbrojony";
                    UpdateEmergencyDamageSoundStatus(
                        $"{mode} • podobieństwo {e.Similarity * 100:0}% • poziom {e.LevelDb:0} dB.",
                        _settings.EmergencyDamageSoundTestMode || _emergencyDamageSoundManualTestActive ? "Green" : "Orange");
                }
            });
        }

        private void DamageSoundDetector_CaptureFailed(object? sender, string message)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                _emergencyDamageSoundManualTestActive = false;
                _emergencyDamageSoundMonitoringForMiner = false;
                UpdateEmergencyDamageSoundStatus(message, "Red");
                UpdateEnabledStates();
            });
        }

        private void DamageSoundDetector_DamageDetected(object? sender, DamageSoundDetectedEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(new Action(async () => await HandleEmergencyDamageSoundDetectedAsync(e)));
        }

        private async Task HandleEmergencyDamageSoundDetectedAsync(DamageSoundDetectedEventArgs e)
        {
            if (_emergencyDamageSoundHandlingAlarm)
                return;

            bool minerActive = _kopacz533RuntimeEnabled || _kopacz633RuntimeEnabled;
            bool emergencyGuardActive = _emergencyReconnectSoundGuardActive;
            // Nasłuch z reakcją (przycisk testowy w Ochronie): prawdziwa reakcja także bez kopania
            // i mimo zaznaczonego trybu testowego.
            bool liveTest = _emergencyDamageSoundLiveTestActive;
            bool safeTest = !liveTest && (_emergencyDamageSoundManualTestActive || _settings.EmergencyDamageSoundTestMode);
            string measurement = $"podobieństwo {e.Similarity * 100:0}%, poziom {e.LevelDb:0} dB";
            _emergencyDamageSoundLastAlarmAtUtc = DateTime.UtcNow;
            if (safeTest)
            {
                bool wasManualTest = _emergencyDamageSoundManualTestActive;
                if (minerActive)
                    RecordEmergencyDamageSoundEvent(measurement, testMode: true);
                // Pozwala sprawdzić oznaczenie na Discordzie bez wychodzenia z serwera.
                NotifyDiscordAlarmTest();
                if (wasManualTest)
                {
                    StopEmergencyDamageSoundListening(updateStatus: false);
                    UpdateEnabledStates();
                }
                UpdateEmergencyDamageSoundStatus(
                    $"WYKRYTO DŹWIĘK OBRAŻEŃ ({measurement}). Tryb testowy — bez reakcji.",
                    "Red");
                UpdateStatusBar("Wykryto testowy alarm dźwięku obrażeń — bez wychodzenia z serwera.", "Orange");
                return;
            }

            if (!liveTest
                && ((!minerActive && !emergencyGuardActive) || ChkEmergencyDamageSoundEnabled?.IsChecked != true))
                return;

            if (liveTest)
            {
                _emergencyDamageSoundLiveTestActive = false;
                _emergencyDamageSoundManualTestActive = false;
            }
            await RunEmergencyProtectionAsync(measurement, liveTest ? "nasłuch z reakcją (przycisk testowy)" : null);
        }

        // Reakcja na alarm obrażeń: zatrzymanie kopania, ESC → Disconnect i — w trybie powrotu —
        // awaryjny reconnect z kontrolą kilofa i home. testLabel: wywołanie z przycisku testowego;
        // dziennik zapisuje wtedy „Test:”, żeby statystyki nie liczyły tego jako wyjścia z serwera.
        private async Task RunEmergencyProtectionAsync(string measurement, string? testLabel = null)
        {
            if (_emergencyDamageSoundHandlingAlarm)
                return;
            if (testLabel != null)
                measurement = "TEST — " + measurement;

            _emergencyDamageSoundHandlingAlarm = true;
            // Gdy nic nie kopało (np. nasłuch z reakcją), sesja w tle jeszcze nie działa —
            // włączamy ją od razu, żeby ESC → Disconnect doszło do gry bez fokusu.
            UpdateBackgroundMiningSession(GetForegroundWindow() == _targetGameWindowHandle);
            IntPtr minecraftWindow = _targetGameWindowHandle;
            bool minecraftGuiAlreadyOpen = IsMinecraftGuiLikelyOpenForEmergency();
            bool useEmergencyReconnect = IsEmergencyReconnectEnabled();
            try
            {
                if (useEmergencyReconnect)
                {
                    if (!_emergencyReconnectActive)
                    {
                        _emergencyReconnectResumeKopacz533 = _kopacz533RuntimeEnabled;
                        _emergencyReconnectResumeKopacz633 = _kopacz633RuntimeEnabled;
                        _autoReconnectLogMiningRunId = _kopacz533RuntimeEnabled
                            ? _kopacz533MiningRunId
                            : _kopacz633RuntimeEnabled
                                ? _kopacz633MiningRunId
                                : string.Empty;
                    }

                    if (_autoReconnectStage != AutoReconnectStage.None)
                        ResetAutoReconnectStageForEmergencyRedetection();

                    _emergencyReconnectActive = true;
                    _emergencyReconnectSoundGuardActive = true;
                }
                else if (_autoReconnectStage != AutoReconnectStage.None)
                {
                    StopAutoReconnect("Alarm obrażeń: anulowano poprzednią akcję. Wybrany tryb tylko Disconnect.", resumeMining: false, warning: true);
                }

                RecordEmergencyDamageSoundEvent(measurement, testMode: false, testLabel);
                if (_kopacz533RuntimeEnabled)
                    StopMiningForEmergency(InventoryCleanupOwner.Kopacz533);
                if (_kopacz633RuntimeEnabled)
                    StopMiningForEmergency(InventoryCleanupOwner.Kopacz633);
                if (!useEmergencyReconnect)
                    StopEmergencyDamageSoundListening(updateStatus: false);

                int reconnectGeneration = _autoReconnectGeneration;
                (bool disconnected, string result) = await TryDisconnectMinecraftAfterEmergencyAsync(
                    minecraftWindow,
                    minecraftGuiAlreadyOpen);
                bool reconnectStarted = disconnected && useEmergencyReconnect
                    && BeginEmergencyReconnectAfterDisconnect(DateTime.UtcNow, reconnectGeneration);
                if (!disconnected && useEmergencyReconnect && CanContinueEmergencyReconnect(reconnectGeneration))
                {
                    RecordAutomationLogEvent(
                        MiningLogEventTypes.EmergencyProtectionFinished,
                        MiningLogStatuses.Aborted,
                        $"Nie udało się wyjść z serwera po alarmie: {result}. Reconnect nie został uruchomiony.");
                    ClearEmergencyReconnectRuntimeState(stopSoundMonitoring: true);
                }
                NotifyDiscordServerExit(disconnected, reconnectStarted, measurement, result);
                UpdateEmergencyDamageSoundStatus(
                    disconnected
                        ? reconnectStarted
                            ? $"ALARM ({measurement}) — kliknięto Disconnect; uruchomiono awaryjny reconnect."
                            : $"ALARM ({measurement}) — Kopacz zatrzymany, kliknięto Disconnect. Bez ponownego wejścia na serwer."
                        : $"ALARM ({measurement}) — Kopacz zatrzymany, ale wyjście z serwera nie powiodło się: {result}",
                    "Red");
                UpdateStatusBar(
                    disconnected
                        ? reconnectStarted
                            ? "Awaryjne wyjście wykonane — trwa odliczanie do reconnectu."
                            : "Awaryjne wyjście: Disconnect wykonany. Kopanie wyłączone, bez reconnectu."
                        : "Awaryjne wyjście: zatrzymano Kopacza, lecz nie udało się kliknąć Disconnect.",
                    "Red");
            }
            finally
            {
                _emergencyDamageSoundHandlingAlarm = false;
                RefreshOverlayHud(DateTime.UtcNow);
                UpdateEnabledStates();
            }
        }

        private bool IsMinecraftGuiLikelyOpenForEmergency()
        {
            bool inventoryOpen = _inventoryCleanupScanInProgress
                || (_inventoryCleanupStage == InventoryCleanupStage.DepositGold && IsGameGuiOpen())
                || _inventoryCleanupStage is InventoryCleanupStage.WaitForInventory
                    or InventoryCleanupStage.MoveToSlot
                    or InventoryCleanupStage.PressDropModifier
                    or InventoryCleanupStage.PressDropKey
                    or InventoryCleanupStage.ReleaseDropKeys
                    or InventoryCleanupStage.CloseInventory;
            bool cleanupChatOpen = _inventoryCleanupStage is InventoryCleanupStage.TypeCobbleXCommand
                or InventoryCleanupStage.SubmitCobbleXCommand;
            bool minerChatOpen = _kopacz533CommandStage is Kopacz533CommandStage.TypeCommand
                    or Kopacz533CommandStage.SubmitCommand
                || _kopacz633CommandStage is Kopacz633CommandStage.TypeCommand
                    or Kopacz633CommandStage.SubmitCommand;
            bool reconnectInventoryOpen = _autoReconnectStage is AutoReconnectStage.HealthVerifyInventory
                or AutoReconnectStage.VerifyAfterTeleport
                or AutoReconnectStage.EmergencyVerifyInventory;
            return inventoryOpen || cleanupChatOpen || minerChatOpen || reconnectInventoryOpen;
        }

        private async Task<(bool Success, string Result)> TryDisconnectMinecraftAfterEmergencyAsync(
            IntPtr minecraftWindow,
            bool closeExistingGuiFirst)
        {
            if (minecraftWindow == IntPtr.Zero)
                return (false, "brak uchwytu okna Minecrafta");
            if (!IsGameInputAvailableFor(minecraftWindow))
                return (false, "Minecraft nie był aktywnym oknem ani nie pracował w tle");

            if (closeExistingGuiFirst)
            {
                SendKeyTap(VK_ESCAPE);
                await Task.Delay(120);
                if (!IsGameInputAvailableFor(minecraftWindow))
                    return (false, "Minecraft utracił fokus po zamknięciu poprzedniego GUI");
            }

            SendKeyTap(VK_ESCAPE);
            await Task.Delay(180);
            if (!IsGameInputAvailableFor(minecraftWindow))
                return (false, "Minecraft utracił fokus przed kliknięciem Disconnect");
            if (!TryClickMinecraftDisconnectButton(minecraftWindow))
                return (false, "nie udało się wyznaczyć położenia przycisku Disconnect");

            return (true, "kliknięto Disconnect");
        }

        private bool TryClickMinecraftDisconnectButton(IntPtr minecraftWindow)
        {
            if (!TryGetWindowClientRectOnScreen(minecraftWindow, out RECT rect))
                return false;

            int clientWidth = rect.Right - rect.Left;
            int clientHeight = rect.Bottom - rect.Top;
            if (clientWidth <= 0 || clientHeight <= 0)
                return false;

            int x = rect.Left + clientWidth / 2;
            int y = rect.Top + CalculateMinecraftDisconnectButtonClientY(clientWidth, clientHeight);
            if (y < rect.Top || y >= rect.Bottom)
                return false;

            if (!SetGameCursorPosition(x, y))
                return false;
            SendMouseClick(leftButton: true, holdPulseMode: false);
            return true;
        }

        // Skala GUI, której Minecraft 1.8.8 naprawdę używa przy ustawieniu Large (3): w mniejszym
        // oknie gra sama ją obniża — każdy stopień wymaga co najmniej 320×240 punktów GUI.
        // Okno konta 854×480 (Kalibruj okno) daje więc skalę 2, nie 3.
        internal static int GetMinecraftGuiScale(int clientWidth, int clientHeight)
        {
            const int configuredGuiScale = 3; // wymagane GUI Scale: Large
            int actualScale = 1;
            while (actualScale < configuredGuiScale
                && clientWidth / (actualScale + 1) >= 320
                && clientHeight / (actualScale + 1) >= 240)
            {
                actualScale++;
            }
            return actualScale;
        }

        private static int CalculateMinecraftDisconnectButtonClientY(int clientWidth, int clientHeight)
        {
            if (clientWidth <= 0 || clientHeight <= 0)
                return 0;

            int actualScale = GetMinecraftGuiScale(clientWidth, clientHeight);
            int scaledHeight = (int)Math.Ceiling(clientHeight / (double)actualScale);
            // Minecraft 1.8.8: y = scaledHeight / 4 + 120 - 16,
            // button height = 20, so its centre is scaledHeight / 4 + 114.
            int buttonCenterGuiY = scaledHeight / 4 + 114;
            int clientY = (int)Math.Round(buttonCenterGuiY * clientHeight / (double)scaledHeight);
            return Math.Clamp(clientY, 0, clientHeight - 1);
        }

        private void RecordEmergencyDamageSoundEvent(string measurement, bool testMode, string? testLabel = null)
        {
            InventoryCleanupOwner owner = _kopacz533RuntimeEnabled || _emergencyReconnectResumeKopacz533
                ? InventoryCleanupOwner.Kopacz533
                : InventoryCleanupOwner.Kopacz633;
            string ownerLabel = GetInventoryCleanupOwnerLabel(owner);
            string miningRunId = _emergencyReconnectActive && !string.IsNullOrWhiteSpace(_autoReconnectLogMiningRunId)
                ? _autoReconnectLogMiningRunId
                : GetMiningRunId(owner);
            _ = _miningLogService.RecordAutomationEvent(
                miningRunId,
                ownerLabel,
                MiningLogEventTypes.EmergencyDamageSoundDetected,
                MiningLogStatuses.Completed,
                testMode
                    ? $"Test: rozpoznano dźwięk obrażeń ({measurement}); nie wykonano reakcji."
                    : (testLabel == null ? string.Empty : $"Test: {testLabel}. ") + $"Rozpoznano dźwięk obrażeń ({measurement}); zatrzymano automat i uruchomiono ESC → Disconnect. "
                        + (IsEmergencyReconnectEnabled()
                            ? "Po rozłączeniu zaplanowano reconnect i kontrolę kilofa."
                            : "Tryb tylko Disconnect: bez reconnectu, home ani wznowienia kopania."),
                out _);
            RefreshMiningLogsSummary();
        }

        private void StopMiningForEmergency(InventoryCleanupOwner owner)
        {
            string label = GetInventoryCleanupOwnerLabel(owner);
            ReleaseMiningInputs(owner);
            if (IsReconnectForMiner(owner))
                StopAutoReconnect($"{label}: awaryjne zatrzymanie po dźwięku obrażeń.", resumeMining: false, warning: true);
            if (_inventoryCleanupOwner == owner && _inventoryCleanupStage != InventoryCleanupStage.None)
            {
                RecordAbortedInventoryCleanup("Auto EQ przerwane przez alarm dźwięku obrażeń.");
                _inventoryCleanupLastResult = "Przerwano przez alarm dźwięku obrażeń";
                _inventoryCleanupLastResultWarning = true;
            }
            StopMiningRuntime(owner);
            EndMiningLogRun(owner, "Kopanie przerwane przez awaryjny alarm dźwięku obrażeń.", MiningLogStatuses.Interrupted);
        }

        private void ChkTestAutoFishingEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            if (ChkTestAutoFishingEnabled.IsChecked != true)
            {
                _testAutoFishingRuntimeEnabled = false;
                ResetTestAutoFishingRuntimeState();
            }

            UpdateEnabledStates();
            RefreshOverlayHud(DateTime.UtcNow);
            UpdateTestAutoFishingStatusLabel();
            MarkDirty();
        }

        private void ChkKopacz533Enabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            UpdateEnabledStates();
            MarkDirty();
        }

        private void ChkKopacz633Enabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            UpdateEnabledStates();
            MarkDirty();
        }

        private void ChkInventoryCleanupEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            if (ChkInventoryCleanupEnabled.IsChecked != true)
            {
                if (IsGoldDepositActive)
                    StopGoldDeposit("Wyłączono Auto EQ podczas wpłaty złota. Sprawdź przedmiot na kursorze.");
                else
                {
                    ResetInventoryCleanupState(scheduleNext: false);
                    UpdateInventoryCleanupStatus("Auto EQ jest wyłączone.", "Default");
                }
            }
            else
            {
                _nextInventoryCleanupAtUtc = DateTime.UtcNow.AddSeconds(GetConfiguredInventoryCleanupIntervalSeconds());
                UpdateInventoryCleanupStatus("Gotowe. Czyszczenie uruchomi się podczas pracy kopacza.", "Green");
            }

            UpdateEnabledStates();
            RefreshOverlayHud(DateTime.UtcNow);
            MarkDirty();
        }

        private void ChkCobbleXEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            UpdateEnabledStates();
            RefreshOverlayHud(DateTime.UtcNow);
            MarkDirty();
        }

        private void ChkInventoryCleanupEatAfterCleanup_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            RefreshOverlayHud(DateTime.UtcNow);
            MarkDirty();
        }

        private void BtnSaveTargetWindowTitle_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingUi)
                return;

            try
            {
                ReadFromUi(includeWindowTitle: false);

                ProcessTargetOption? selectedProcess = GetSelectedTargetProcessOption();
                if (selectedProcess != null && _gameWindowClaim.IsClaimedByAnotherProfile(selectedProcess.ProcessId))
                {
                    UpdateStatusBar("To okno Minecrafta jest już przypisane do innego konta Kopacza. Wybierz inne okno.", "Red");
                    return;
                }

                if (selectedProcess != null)
                {
                    _settings.TargetProcessId = selectedProcess.ProcessId;
                    _settings.TargetProcessName = selectedProcess.ProcessName;
                    _settings.TargetWindowTitle = selectedProcess.WindowTitle;
                    TxtTargetWindowTitle.Text = selectedProcess.WindowTitle;
                }
                else
                {
                    string legacyTitle = TxtTargetWindowTitle.Text.Trim();
                    if (string.IsNullOrWhiteSpace(legacyTitle))
                    {
                        UpdateStatusBar("Wybierz proces z listy i kliknij \"Zapisz program\"", "Orange");
                        return;
                    }

                    _settings.TargetProcessId = 0;
                    _settings.TargetProcessName = string.Empty;
                    _settings.TargetWindowTitle = legacyTitle;
                }

                TxtCurrentWindowTitle.Text = BuildTargetProcessDisplayText();
                _targetGameWindowHandle = IntPtr.Zero;
                _ = TryResolveTargetWindow(allowPendingSelection: false, out _targetGameWindowHandle);
                UpdateEmergencyDamageSoundSourcePreview();
                _settingsService.Save(_settings);

                _pendingChanges = false;
                _dirtyTimer.Stop();
                TxtSettingsSaved.Text = "✓ Tak";
                TxtSettingsSaved.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));
                EllSettingsSaved.Fill = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));

                _isMinecraftFocused = CheckGameFocus();
                if (selectedProcess != null && LooksLikeLauncherWindow(selectedProcess))
                {
                    UpdateStatusBar("Zapisano launcher. Bindy działają w wybranym oknie; wybierz okno właściwej gry Minecraft.", "Orange");
                }
                else
                {
                    UpdateStatusBar("Program gry zapisany", "Green");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusBar("Błąd zapisu programu gry: " + ex.Message, "Red");
            }
        }

        // IMPORT / EXPORT
        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = Path.GetFileName(_settingsService.SettingsFilePath),
                InitialDirectory = _settingsService.SettingsDirectoryPath
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ReadFromUi(includeWindowTitle: false);
                    _settingsService.ExportToFile(_settings, dlg.FileName);
                    UpdateStatusBar("Ustawienia wyeksportowane", "Green");
                }
                catch (Exception ex)
                {
                    UpdateStatusBar("Błąd eksportu: " + ex.Message, "Red");
                }
            }
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                InitialDirectory = _settingsService.SettingsDirectoryPath
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _settings = _settingsService.ImportFromFile(dlg.FileName);
                    EnsureSettingsConsistency();
                    _settingsService.Save(_settings);
                    _isLoadingUi = true;
                    try
                    {
                        LoadToUi();
                    }
                    finally
                    {
                        _isLoadingUi = false;
                    }
                    UpdateEnabledStates();
                    RefreshOverlayHud(DateTime.UtcNow);
                    _isMinecraftFocused = CheckGameFocus();
                    UpdateStatusBar("Ustawienia wczytane", "Green");

                    _pendingChanges = false;
                    TxtSettingsSaved.Text = "✓ Tak";
                    TxtSettingsSaved.Foreground = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));
                    EllSettingsSaved.Fill = new SolidColorBrush(GlassPalette.MapLegacyColor(56, 214, 180));
                }
                catch (Exception ex)
                {
                    UpdateStatusBar("Błąd importu: " + ex.Message, "Red");
                }
            }
        }

        // KLIKALNY GITHUB
        private void Github_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _isExitRequested = true;
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.IsOpen = false;
                _trayMenu = null;
            }

            _dirtyTimer?.Stop();
            _focusTimer?.Stop();
            _macroTimer?.Stop();
            _autoReconnectTimer?.Stop();
            _transientStatusTimer?.Stop();
            _glassUiTimer?.Stop();
            _damageSoundDetector?.Dispose();

            // XAML loading can fail before the constructor creates services or
            // timers. Preserve that original error during application shutdown.
            if (_settings == null)
            {
                base.OnClosed(e);
                return;
            }

            if (_goldDepositManualTest != null)
                FinishGoldDepositManualTest("Test przerwany zamknięciem programu.", success: false);

            // Always release injected input when the mining process exits.
            SetKopacz533MiningHold(false);
            SetKopacz633AttackHold(false);
            SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
            ResetInventoryCleanupState(scheduleNext: false);
            EndMiningLogRun(
                InventoryCleanupOwner.Kopacz533,
                "Sesja zakończona wraz z zamknięciem aplikacji.",
                MiningLogStatuses.Interrupted);
            EndMiningLogRun(
                InventoryCleanupOwner.Kopacz633,
                "Sesja zakończona wraz z zamknięciem aplikacji.",
                MiningLogStatuses.Interrupted);
            _testAutoFishingRuntimeEnabled = false;
            ResetTestAutoFishingRuntimeState();

            BackgroundGameWindow.Deactivate();
            BackgroundGameWindow.ReleaseLayoutUnlessTarget(IntPtr.Zero);

            if (_overlayHud != null)
            {
                _overlayHud.Close();
                _overlayHud = null;
            }
            lock (_f3TesseractLock)
            {
                _f3TesseractEngine?.Dispose();
                _f3TesseractEngine = null;
            }
            base.OnClosed(e);
        }
    }
}
