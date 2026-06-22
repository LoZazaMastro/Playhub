using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Playhub.Models;

namespace Playhub.Services;

/// <summary>
/// Trova i giochi installati di GOG (Galaxy o installer offline) leggendo il
/// registro: HKLM\SOFTWARE\GOG.com\Games\&lt;id&gt; (chiavi: gameName, path, exe...).
/// L'installazione resta a GOG: qui generiamo solo le voci da importare come
/// scorciatoie Steam (l'eseguibile reale del gioco).
/// </summary>
public sealed class GogService
{
    private const string GamesPath = @"SOFTWARE\GOG.com\Games";

    public Task<IReadOnlyList<UwpGameEntry>> ScanAsync() => Task.Run<IReadOnlyList<UwpGameEntry>>(Scan);

    private static IReadOnlyList<UwpGameEntry> Scan()
    {
        var games = new List<UwpGameEntry>();

        // GOG è a 32 bit: su Windows a 64 bit le chiavi sono sotto WOW6432Node
        // (gestito da RegistryView.Registry32). Provo anche la vista a 64 bit e HKCU.
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), games);
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), games);
        ReadFrom(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32), games);

        return games
            .GroupBy(g => g.LocalExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void ReadFrom(RegistryKey baseKey, List<UwpGameEntry> games)
    {
        try
        {
            using var gamesKey = baseKey.OpenSubKey(GamesPath);
            if (gamesKey is null)
            {
                return;
            }

            foreach (var id in gamesKey.GetSubKeyNames())
            {
                var entry = FromGameKey(gamesKey, id);
                if (entry is not null)
                {
                    games.Add(entry);
                }
            }
        }
        catch
        {
            // Vista non disponibile o accesso negato: ignora.
        }
        finally
        {
            baseKey.Dispose();
        }
    }

    private static UwpGameEntry? FromGameKey(RegistryKey gamesKey, string id)
    {
        try
        {
            using var key = gamesKey.OpenSubKey(id);
            if (key is null)
            {
                return null;
            }

            var name = key.GetValue("gameName") as string ?? "";
            var path = key.GetValue("path") as string ?? "";
            var exe = key.GetValue("exe") as string ?? "";
            var exeFile = key.GetValue("exeFile") as string ?? "";

            var exePath = ResolveExe(path, exe, exeFile);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(exePath);
            }

            long size = 0;
            try { size = new FileInfo(exePath).Length; } catch { }

            return new UwpGameEntry
            {
                Name = name,
                // Identità unica del gioco (come per l'import EXE): senza questa
                // i giochi locali condividerebbero la stessa cache → cover/icone scambiate.
                Aumid = exePath,
                IsLocalExecutable = true,
                LocalExecutablePath = exePath,
                Executable = exePath,
                FileSize = size
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveExe(string path, string exe, string exeFile)
    {
        if (!string.IsNullOrWhiteSpace(exe) && Path.IsPathRooted(exe))
        {
            return exe;
        }

        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(exeFile))
        {
            return Path.Combine(path, exeFile);
        }

        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(exe))
        {
            return Path.Combine(path, exe);
        }

        return "";
    }
}
