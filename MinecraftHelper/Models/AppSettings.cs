using System.Collections.Generic;

namespace MinecraftHelper.Models
{
    public class MinerCommand
    {
        public int Seconds { get; set; }
        public string Command { get; set; } = "";
    }

    public class AutoReconnectServerProfile
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Nowy serwer";
        public string ServerAddress { get; set; } = "";
        public string HomeCommand { get; set; } = "/home";
        public bool HomeHasGui { get; set; } = true;
        public int HomeGuiDelaySeconds { get; set; } = 1;
        public int HomeGuiRows { get; set; } = 3;
        public int HomeGuiColumns { get; set; } = 9;
        public int HomeGuiSlot { get; set; } = 11;
        public int JoinDelaySeconds { get; set; } = 8;
        public int TeleportDelaySeconds { get; set; } = 10;
        public int WatchdogSeconds { get; set; } = 60;
        public int MaxAttempts { get; set; } = 3;
        public bool MissingPickaxeRecoveryEnabled { get; set; } = true;
        public string MissingPickaxeHomeCommand { get; set; } = "";
        public bool? MissingPickaxeHomeHasGui { get; set; }
        public int MissingPickaxeHomeGuiDelaySeconds { get; set; } = -1;
        public int MissingPickaxeHomeGuiRows { get; set; }
        public int MissingPickaxeHomeGuiColumns { get; set; }
        public int MissingPickaxeHomeGuiSlot { get; set; }
    }

    public class AppSettings
    {
        public string LastAcknowledgedVersion { get; set; } = "";

        public bool Kopacz533Enabled { get; set; }
        public string Kopacz533Key { get; set; } = "";
        public List<MinerCommand> Kopacz533Commands { get; set; } = new List<MinerCommand>();

        public bool Kopacz633Enabled { get; set; }
        public string Kopacz633Key { get; set; } = "";
        public string Kopacz633Direction { get; set; } = "";
        public int Kopacz633Width { get; set; }
        public int Kopacz633Length { get; set; }
        public List<MinerCommand> Kopacz633Commands { get; set; } = new List<MinerCommand>();
        public bool InventoryCleanupEnabled { get; set; }
        public int InventoryCleanupIntervalSeconds { get; set; } = 120;
        public bool InventoryCleanupDiscardAllItemTypes { get; set; }
        public bool InventoryCleanupEatAfterCleanup { get; set; }
        public List<int> InventoryCleanupSlots { get; set; } = new List<int>
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16, 17,
            18, 19, 20, 21, 22, 23, 24, 25, 26
        };
        public List<string> InventoryCleanupItemTypes { get; set; } = new List<string>
        {
            "diamond", "gold_ingot", "gold_block", "iron_ingot", "iron_block", "emerald",
            "emerald_block", "obsidian", "apple", "sand", "gunpowder", "coal", "quartz",
            "book", "ender_pearl", "redstone"
        };
        public bool CobbleXEnabled { get; set; }
        public bool CobbleXRepeatThreeTimes { get; set; }
        public bool BackgroundMiningEnabled { get; set; }
        public bool BackgroundGameShieldEnabled { get; set; } = true;
        public bool GoldDepositEnabled { get; set; }
        public string GoldDepositCommand { get; set; } = "";
        public string GoldDepositMenuTitle { get; set; } = "Bazar";
        public int GoldDepositRows { get; set; } = 6;
        public int GoldDepositColumns { get; set; } = 9;
        public int GoldDepositTargetSlot { get; set; } = 11;
        public int GoldDepositOpenDelayMs { get; set; } = 1000;
        public bool GoldDepositIngots { get; set; } = true;
        public bool GoldDepositBlocks { get; set; } = true;
        public bool GoldDepositIncludeHotbar { get; set; } = true;
        public string CobbleXCommand { get; set; } = "/cx";
        public int CobbleXRequiredFullStacks { get; set; } = 9;
        public bool TestAutoFishingEnabled { get; set; }
        public string TestAutoFishingBind { get; set; } = "";
        public string TestAutoFishingCaptureBind { get; set; } = "";
        public int TestAutoFishingCaptureX { get; set; }
        public int TestAutoFishingCaptureY { get; set; }
        public int TestAutoFishingCaptureWidth { get; set; }
        public int TestAutoFishingCaptureHeight { get; set; }
        public string TestAutoFishingRepairCommand { get; set; } = "";
        public int TestAutoFishingRepairEverySeconds { get; set; }
        public bool EmergencyDamageSoundEnabled { get; set; }
        public bool EmergencyDamageSoundTestMode { get; set; } = true;
        public string EmergencyDamageSoundDeviceId { get; set; } = "";
        public int EmergencyDamageSoundSimilarityPercent { get; set; } = 90;
        public double EmergencyDamageSoundMinimumDb { get; set; } = -45;
        public List<List<double>> EmergencyDamageSoundTemplates { get; set; } = new List<List<double>>();
        public bool EmergencyReconnectEnabled { get; set; }
        public int EmergencyReconnectDelaySeconds { get; set; } = 30;
        public bool AutoReconnectEnabled { get; set; }
        public string AutoReconnectProfile { get; set; } = "Arivi";
        public string AutoReconnectServerAddress { get; set; } = "";
        public string AutoReconnectHomeCommand { get; set; } = "/home";
        public int AutoReconnectHomeSlot { get; set; } = 11;
        public int AutoReconnectJoinDelaySeconds { get; set; } = 8;
        public int AutoReconnectTeleportDelaySeconds { get; set; } = 10;
        public int AutoReconnectWatchdogSeconds { get; set; } = 60;
        public int AutoReconnectMaxAttempts { get; set; } = 3;
        public bool AutoReconnectHomeHasGui { get; set; } = true;
        public int AutoReconnectHomeGuiDelaySeconds { get; set; } = 1;
        public int AutoReconnectHomeGuiRows { get; set; } = 3;
        public int AutoReconnectHomeGuiColumns { get; set; } = 9;
        public bool AutoReconnectMissingPickaxeRecoveryEnabled { get; set; } = true;
        public string AutoReconnectSelectedServerProfileId { get; set; } = "";
        public List<AutoReconnectServerProfile> AutoReconnectServerProfiles { get; set; } = new List<AutoReconnectServerProfile>();
        public bool OverlayHudEnabled { get; set; }
        public bool OverlayAnimationsEnabled { get; set; }
        public bool AnimatedBackgroundEnabled { get; set; } = true;
        public string GlassAccent { get; set; } = "iris";
        public string ChatOpenKey { get; set; } = "T";
        public string DropItemKey { get; set; } = "Q";
        public int OverlayMonitorIndex { get; set; }
        public string OverlayCorner { get; set; } = "RightBottom";
        public string TargetWindowTitle { get; set; } = "";
        public int TargetProcessId { get; set; }
        public string TargetProcessName { get; set; } = "";
    }
}
