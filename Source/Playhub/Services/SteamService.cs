using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class SteamService
{
    public string? GetSteamFolder()
    {
        const string registryPath = @"SOFTWARE\Valve\Steam";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = localKey.OpenSubKey(registryPath);
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                return path;
            }
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public IReadOnlyList<string> GetUserFolders()
    {
        var steam = GetSteamFolder();
        if (steam is null)
        {
            return Array.Empty<string>();
        }

        var userdata = Path.Combine(steam, "userdata");
        return Directory.Exists(userdata) ? Directory.GetDirectories(userdata) : Array.Empty<string>();
    }

    public async Task RestartSteamAsync()
    {
        var steam = Process.GetProcessesByName("steam").FirstOrDefault();
        if (steam is null)
        {
            var folder = GetSteamFolder();
            var exe = folder is null ? null : Path.Combine(folder, "steam.exe");
            if (exe is not null && File.Exists(exe))
            {
                ProcessService.StartDetached(exe);
            }
            return;
        }

        var steamExe = steam.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(steamExe))
        {
            return;
        }

        ProcessService.StartDetached(steamExe, "-exitsteam", hidden: true);
        for (var i = 0; i < 16; i++)
        {
            await Task.Delay(500);
            if (!Process.GetProcessesByName("steam").Any())
            {
                ProcessService.StartDetached(steamExe);
                return;
            }
        }
    }
}
