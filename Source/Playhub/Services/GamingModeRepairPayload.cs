using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.Services;

internal sealed class GamingModeRepairDeferredException : IOException { }
internal enum GamingModeStartupRepair { Healthy, Repaired, Unresolved }

// Used only by the explicitly invoked repair operation, never by startup sync.
internal static class GamingModeRepairPayload
{
    internal static bool IsCurrent(string package, string installed)
    {
        var files = PayloadFiles(package);
        foreach (var source in files)
        {
            var target = Path.Combine(installed, Path.GetRelativePath(package, source));
            RejectRedirectedPath(target);
            if (!File.Exists(target) || !SameContent(source, target)) return false;
        }
        return true;
    }

    private static bool SameContent(string source, string target)
    {
        using var left = File.OpenRead(source);
        using var right = File.OpenRead(target);
        return left.Length == right.Length && SHA256.HashData(left).SequenceEqual(SHA256.HashData(right));
    }

    private static List<string> PayloadFiles(string package)
    {
        RejectRedirectedPath(package);
        var exe = Path.Combine(package, "GamingMode.exe");
        if (!File.Exists(exe) || new FileInfo(exe).Length == 0)
            throw new IOException("Pacchetto agente mancante o incompleto. Reinstalla Playhub.");
        var files = new List<string>();
        Visit(package);
        return files;

        void Visit(string directory)
        {
            RejectRedirectedPath(directory);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                RejectRedirectedPath(file);
                var relative = Path.GetRelativePath(package, file);
                // Copy runtime content only, never installers, shortcuts or user configuration.
                if (relative.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relative, "GamingMode.exe", StringComparison.OrdinalIgnoreCase) ||
                    (!relative.Contains(Path.DirectorySeparatorChar) &&
                     (relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                      relative.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
                      relative.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))))
                    files.Add(file);
            }
            foreach (var child in Directory.EnumerateDirectories(directory)) Visit(child);
        }
    }

    internal static void Restore(string package, string installed, Func<bool>? isAgentRunning = null)
    {
        var files = PayloadFiles(package);
        if ((isAgentRunning ?? (() => IsAgentRunning(installed)))())
            throw new GamingModeRepairDeferredException();
        foreach (var source in files)
        {
            var target = Path.Combine(installed, Path.GetRelativePath(package, source));
            RejectRedirectedPath(target);
            if (File.Exists(target) && SameContent(source, target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".repair-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, temporary);
                if (!SameContent(source, temporary)) throw new IOException("Copia del payload non valida.");
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static bool IsAgentRunning(string installed)
    {
        // No name-based kills. Even an unrelated/unknown agent blocks replacement safely.
        var processes = Process.GetProcessesByName("GamingMode");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, Path.Combine(installed, "GamingMode.exe"), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { return true; } // An inaccessible process cannot be safely ruled out.
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static void RejectRedirectedPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Percorso reindirizzato non modificato: " + current);
        }
    }

    internal static int ReadApiPort(string configFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configFile));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Configurazione non valida.");
        if (!root.TryGetProperty("defaultMode", out var mode) ||
            mode.ValueKind != JsonValueKind.String ||
            !(string.Equals(mode.GetString(), "Gaming", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(mode.GetString(), "Desktop", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Modalita' di avvio non valida.");
        if (!root.TryGetProperty("gaming", out var gaming) || gaming.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Preferenze Gaming Mode non valide.");
        if (!root.TryGetProperty("safety", out var safety)) return 47991;
        if (safety.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Configurazione API non valida.");
        if (!safety.TryGetProperty("apiPort", out var port)) return 47991;
        if (!port.TryGetInt32(out var value) || value < 1 || value > 65535)
            throw new InvalidDataException("Porta API non valida.");
        return value;
    }

    internal static bool IsExpectedStartup(string target, string arguments, string workingDirectory, string exe) =>
        string.Equals(target, exe, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(arguments.Trim(), "agent --boot", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(workingDirectory.TrimEnd('\\', '/'), Path.GetDirectoryName(exe)?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    internal static bool IsKnownAgentStartup(string target, string arguments) =>
        Path.IsPathFullyQualified(target) &&
        string.Equals(Path.GetFileName(target), "GamingMode.exe", StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(arguments) ||
         string.Equals(arguments.Trim(), "agent", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(arguments.Trim(), "agent --boot", StringComparison.OrdinalIgnoreCase));

    internal static GamingModeStartupRepair CheckStartup(string exe, bool payloadReady) =>
        RepairStartupShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Gaming Mode Agent.lnk"), exe, payloadReady);

    internal static GamingModeStartupRepair RepairStartupShortcut(string shortcutPath, string exe, bool payloadReady)
    {
        // The bundled installer registers a Startup shortcut, not a scheduled task.
        // A missing/disabled registration can be intentional; never recreate it here.
        if (!File.Exists(shortcutPath))
            return GamingModeStartupRepair.Unresolved;
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(shortcutPath);
            if (!IsExpectedStartup((string)((dynamic)shortcut).TargetPath,
                (string)((dynamic)shortcut).Arguments, (string)((dynamic)shortcut).WorkingDirectory, exe))
            {
                if (!payloadReady || !IsKnownAgentStartup((string)((dynamic)shortcut).TargetPath, (string)((dynamic)shortcut).Arguments))
                    return GamingModeStartupRepair.Unresolved;
                ((dynamic)shortcut).TargetPath = exe;
                ((dynamic)shortcut).Arguments = "agent --boot";
                ((dynamic)shortcut).WorkingDirectory = Path.GetDirectoryName(exe)!;
                ((dynamic)shortcut).Save();
                Marshal.FinalReleaseComObject(shortcut);
                shortcut = ((dynamic)shell!).CreateShortcut(shortcutPath);
                return IsExpectedStartup((string)((dynamic)shortcut).TargetPath,
                    (string)((dynamic)shortcut).Arguments, (string)((dynamic)shortcut).WorkingDirectory, exe)
                    ? GamingModeStartupRepair.Repaired : GamingModeStartupRepair.Unresolved;
            }
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
        return GamingModeStartupRepair.Healthy;
    }
}
