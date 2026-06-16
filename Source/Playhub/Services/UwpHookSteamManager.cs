using Microsoft.Win32;
using System;
using System.IO;
using VDFParser;
using VDFParser.Models;
using VdfSerializer = VDFParser.VDFSerializer;

namespace Playhub.Services;

public static class UwpHookSteamManager
{
    public static string? GetSteamFolder()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = localKey.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string[] GetUsers(string steamInstallPath)
    {
        var userdata = Path.Combine(steamInstallPath, "userdata");
        return Directory.Exists(userdata) ? Directory.GetDirectories(userdata) : Array.Empty<string>();
    }

    public static VDFEntry[] ReadShortcuts(string userPath)
    {
        var shortcutsPath = Path.Combine(userPath, "config", "shortcuts.vdf");
        if (!File.Exists(shortcutsPath))
        {
            return Array.Empty<VDFEntry>();
        }

        try
        {
            return VDFParser.VDFParser.Parse(shortcutsPath);
        }
        catch (VDFTooShortException)
        {
            return Array.Empty<VDFEntry>();
        }
    }

    public static void WriteShortcuts(VDFEntry[] shortcuts, string shortcutsPath)
    {
        var directory = Path.GetDirectoryName(shortcutsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var data = VdfSerializer.Serialize(shortcuts);

        if (File.Exists(shortcutsPath))
        {
            // Clear any read-only flag so the file can be overwritten.
            try { File.SetAttributes(shortcutsPath, FileAttributes.Normal); } catch { }
        }

        try
        {
            File.WriteAllBytes(shortcutsPath, data);
        }
        catch (UnauthorizedAccessException)
        {
            // Stubborn read-only/replaced file: drop it and recreate fresh.
            try { File.SetAttributes(shortcutsPath, FileAttributes.Normal); } catch { }
            File.Delete(shortcutsPath);
            File.WriteAllBytes(shortcutsPath, data);
        }
    }
}
