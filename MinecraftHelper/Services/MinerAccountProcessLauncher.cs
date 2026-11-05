using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Reflection;
using MinecraftHelper.Models;

namespace MinecraftHelper.Services;

internal static class MinerAccountProcessLauncher
{
    public static Process Launch(MinerAccountProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string profileId = MinerProfileId.Normalize(profile.Id);
        EnsureProfileSettings(profileId);

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Nie udało się ustalić ścieżki programu Kopacza.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string assemblyPath = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Nie udało się ustalić pliku MinecraftHelper.Miner.dll.");
            startInfo.ArgumentList.Add(assemblyPath);
        }

        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add(profileId);
        startInfo.ArgumentList.Add("--workspace-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Nie udało się uruchomić profilu {profile.Name}.");
    }

    private static void EnsureProfileSettings(string profileId)
    {
        var target = new SettingsService(profileId);
        if (target.SettingsFileExists)
            return;

        AppSettings settings = new SettingsService().Load();
        settings.TargetWindowTitle = string.Empty;
        settings.TargetProcessId = 0;
        settings.TargetProcessName = string.Empty;
        target.Save(settings);
    }
}

