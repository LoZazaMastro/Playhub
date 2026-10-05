using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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

    /// <summary>
    /// Chiude Steam e aspetta che sia davvero uscito. Prima con le buone (-exitsteam,
    /// lo stesso comando che usa il menu Esci), poi, se dopo mezzo minuto è ancora lì,
    /// terminando i processi rimasti: sovrascrivere i file di Steam mentre è aperto
    /// lascia l'installazione a metà.
    /// </summary>
    public async Task<bool> StopSteamAsync(CancellationToken token = default)
    {
        static Process[] Running() => Process.GetProcessesByName("steam")
            .Concat(Process.GetProcessesByName("steamwebhelper"))
            .ToArray();

        if (Running().Length == 0) return true;

        var exe = Process.GetProcessesByName("steam").FirstOrDefault()?.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exe))
        {
            var folder = GetSteamFolder();
            exe = folder is null ? null : Path.Combine(folder, "steam.exe");
        }
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            try { ProcessService.StartDetached(exe, "-exitsteam", hidden: true); } catch { }
        }

        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (Running().Length == 0) return true;
            await Task.Delay(500, token);
        }

        foreach (var process in Running())
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Running().Length == 0) return true;
            await Task.Delay(500, token);
        }
        return Running().Length == 0;
    }

    public async Task RestartSteamAsync()
    {
        string? executable = null;
        var processes = Process.GetProcessesByName("steam");
        try
        {
            foreach (var process in processes)
            {
                try { executable = process.MainModule?.FileName; } catch { }
                if (!string.IsNullOrWhiteSpace(executable)) break;
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (string.IsNullOrWhiteSpace(executable))
        {
            var folder = GetSteamFolder();
            executable = folder is null ? null : Path.Combine(folder, "steam.exe");
        }
        static bool Running()
        {
            var current = Process.GetProcessesByName("steam");
            try { return current.Length > 0; }
            finally { foreach (var process in current) process.Dispose(); }
        }
        await RestartSequence.SteamAsync(executable, File.Exists, Running,
            path => ProcessService.StartDetached(path, "-exitsteam", hidden: true),
            path => ProcessService.StartDetached(path), milliseconds => Task.Delay(milliseconds));
    }
}
