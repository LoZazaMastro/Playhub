using Playhub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Playhub.Services;

public sealed class ExecutableGameService
{
    private static readonly string[] ExcludedFileTerms =
    {
        "unins", "uninstall", "setup", "installer", "crashreport", "crashhandler",
        "reporter", "redist", "vcredist", "dxsetup", "easyanticheat", "unitycrashhandler",
        "cefsubprocess", "helper", "updater", "benchmark", "dedicatedserver",
        "cleanup", "touchup", "ffmpeg", "replication-server"
    };

    private static readonly string[] ExcludedPathTerms =
    {
        "_commonredist", "__installer", "redist", "redistributable", "prereq", "support",
        "crashreport", "easyanticheat", "engine\\extras", "directx"
    };

    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "binaries", "binary", "bin", "win64", "win32", "wingdk", "x64", "x86",
        "engine", "build", "release", "shipping", "game", "games", "client",
        "content", "assets", "data", "streamingassets", "managed", "resources",
        "plugins", "saved", "source", "src", "__installer"
    };

    private static readonly HashSet<string> LibraryFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "games", "xbox games", "steamapps", "common", "epic games", "gog games",
        "program files", "program files x86", "installed games", "game library"
    };

    private sealed record Candidate(UwpGameEntry Game, string Group, int Score);

    public Task<IReadOnlyList<UwpGameEntry>> ScanAsync(string rootFolder)
    {
        return Task.Run<IReadOnlyList<UwpGameEntry>>(() => Scan(rootFolder));
    }

    public Task<UwpGameEntry?> CreateEntryAsync(string executablePath)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            var rootFolder = Path.GetDirectoryName(executablePath) ?? "";
            var candidate = CreateCandidate(rootFolder, executablePath, forceInclude: true, preferRootFolder: false);
            if (candidate is null)
            {
                return null;
            }

            return candidate.Game;
        });
    }

    private static IReadOnlyList<UwpGameEntry> Scan(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return Array.Empty<UwpGameEntry>();
        }

        var candidates = new List<Candidate>();
        var preferRootFolder = !LooksLikeLibraryRoot(rootFolder);
        foreach (var path in EnumerateExecutables(rootFolder))
        {
            var candidate = CreateCandidate(rootFolder, path, forceInclude: false, preferRootFolder);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        var games = candidates
            .GroupBy(candidate => candidate.Group, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First().Game)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return games;
    }

    private static Candidate? CreateCandidate(string rootFolder, string path, bool forceInclude, bool preferRootFolder)
    {
        try
        {
            var file = new FileInfo(path);
            if (!forceInclude && (file.Length < 128 * 1024 || IsExcluded(path)))
            {
                return null;
            }

            var version = FileVersionInfo.GetVersionInfo(path);
            var group = GetGameGroup(rootFolder, path);
            var product = CleanTitle(version.ProductName);
            var description = CleanTitle(version.FileDescription);
            var fileTitle = CleanTitle(Path.GetFileNameWithoutExtension(path));
            var folderName = CleanTitle(FindBestGameFolderName(rootFolder, path, group, preferRootFolder, forceInclude));
            var title = ChooseBestTitle(product, description, folderName, fileTitle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var score = ScoreCandidate(path, file.Length, product, description, folderName, fileTitle);
            return new Candidate(new UwpGameEntry
            {
                Name = title,
                Aumid = path,
                Executable = Path.GetFileName(path),
                LocalExecutablePath = path,
                IsLocalExecutable = true,
                Publisher = CleanPublisher(version.CompanyName),
                FileSize = file.Length
            }, group, score);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateExecutables(string rootFolder)
    {
        var pending = new Stack<string>();
        pending.Push(rootFolder);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(folder, "*.exe", SearchOption.TopDirectoryOnly);
                directories = Directory.GetDirectories(folder);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static bool IsExcluded(string path)
    {
        var normalized = path.Replace('/', '\\').ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return ExcludedFileTerms.Any(fileName.Contains) || ExcludedPathTerms.Any(normalized.Contains);
    }

    private static string GetGameGroup(string rootFolder, string executablePath)
    {
        var relative = Path.GetRelativePath(rootFolder, executablePath);
        var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1 || GenericFolders.Contains(parts[0]))
        {
            return "__root__";
        }
        return parts[0];
    }

    private static bool LooksLikeLibraryRoot(string rootFolder)
    {
        try
        {
            var rootName = NormalizeTitle(new DirectoryInfo(rootFolder).Name);
            if (LibraryFolders.Contains(rootName))
            {
                return true;
            }

            return Directory.EnumerateDirectories(rootFolder)
                .Select(Path.GetFileName)
                .Where(IsMeaningfulFolder)
                .Take(2)
                .Count() >= 2;
        }
        catch
        {
            return true;
        }
    }

    private static string FindBestGameFolderName(
        string rootFolder,
        string executablePath,
        string group,
        bool preferRootFolder,
        bool forceInclude)
    {
        var rootName = new DirectoryInfo(rootFolder).Name;
        if (preferRootFolder && IsMeaningfulFolder(rootName))
        {
            return rootName;
        }

        try
        {
            var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(rootFolder, executablePath)) ?? "";
            var relativeFolders = relativeDirectory
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Where(IsMeaningfulFolder)
                .ToList();
            if (relativeFolders.Count > 0)
            {
                return relativeFolders[0];
            }
        }
        catch
        {
        }

        if (IsMeaningfulFolder(rootName))
        {
            return rootName;
        }

        if (forceInclude)
        {
            var candidates = new List<string>();
            var current = Directory.GetParent(Path.GetDirectoryName(executablePath) ?? "");
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                if (IsMeaningfulFolder(current.Name))
                {
                    candidates.Add(current.Name);
                }
            }

            var best = candidates
                .OrderByDescending(FolderTitleScore)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(best))
            {
                return best;
            }
        }

        return group == "__root__" ? rootName : group;
    }

    private static bool IsMeaningfulFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var normalized = NormalizeTitle(folder.Replace("(x86)", "x86", StringComparison.OrdinalIgnoreCase));
        return !GenericFolders.Contains(normalized) &&
               !LibraryFolders.Contains(normalized) &&
               !normalized.EndsWith(" data", StringComparison.OrdinalIgnoreCase) &&
               !normalized.StartsWith(".", StringComparison.Ordinal);
    }

    private static int FolderTitleScore(string folder)
    {
        var cleaned = CleanTitle(folder);
        var words = NormalizeTitle(cleaned).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return words * 24 + Math.Min(cleaned.Length, 50);
    }

    private static int ScoreCandidate(string path, long size, string product, string description, string folder, string file)
    {
        var score = (int)Math.Min(90, Math.Log2(size / 1024d / 1024d + 1) * 14);
        if (IsUsefulTitle(product)) score += 110;
        if (IsUsefulTitle(description)) score += 55;
        if (NormalizeTitle(file) == NormalizeTitle(folder)) score += 45;
        if (path.Contains("shipping", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (path.Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 35;
        if (path.Contains("editor", StringComparison.OrdinalIgnoreCase)) score -= 50;
        return score;
    }

    private static string FirstUsefulTitle(params string[] values)
    {
        return values.FirstOrDefault(IsUsefulTitle) ?? "";
    }

    private static string ChooseBestTitle(string product, string description, string folder, string file)
    {
        var metadata = FirstUsefulTitle(product, description);
        if (IsUsefulTitle(folder))
        {
            if (!IsUsefulTitle(metadata) || LooksInternalTitle(metadata) || !TitlesRelated(metadata, folder))
            {
                return folder;
            }
        }

        return FirstUsefulTitle(metadata, folder, file);
    }

    private static bool LooksInternalTitle(string title)
    {
        var normalized = NormalizeTitle(title);
        return normalized.StartsWith("project ", StringComparison.Ordinal) ||
               normalized.Contains(" shipping", StringComparison.Ordinal) ||
               (!normalized.Contains(' ') && normalized.Length <= 5);
    }

    private static bool TitlesRelated(string first, string second)
    {
        var firstNormalized = NormalizeTitle(first);
        var secondNormalized = NormalizeTitle(second);
        var firstCompact = firstNormalized.Replace(" ", "");
        var secondCompact = secondNormalized.Replace(" ", "");
        if (firstCompact.Contains(secondCompact, StringComparison.Ordinal) ||
            secondCompact.Contains(firstCompact, StringComparison.Ordinal))
        {
            return true;
        }

        var firstWords = firstNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 4)
            .ToHashSet(StringComparer.Ordinal);
        return secondNormalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Length >= 4 && firstWords.Contains(word));
    }

    private static bool IsUsefulTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
        {
            return false;
        }

        var normalized = NormalizeTitle(title);
        return normalized is not "game" and not "launcher" and not "application" and not "client" and
               not "unreal engine" and not "unity" and not "shipping" and not "bootstrapper";
    }

    private static string CleanTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var title = value.Trim();
        title = Regex.Replace(title, "(?<=[a-z0-9])(?=[A-Z])", " ");
        title = Regex.Replace(title, @"(?i)[._-]?(win(32|64|gdk)|x(86|64)|shipping|release|launcher|bootstrapper|client|game)([._-].*)?$", "");
        title = Regex.Replace(title, @"(?i)\s+(version|ver|v)\s*\d+(\.\d+)*.*$", "");
        title = title.Replace("™", "").Replace("®", "").Replace("©", "");
        title = Regex.Replace(title, @"[_\.]+", " ");
        title = Regex.Replace(title, @"\s{2,}", " ").Trim(' ', '-', '_');
        return title;
    }

    private static string NormalizeTitle(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

    private static string CleanPublisher(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value, @"\s{2,}", " ").Trim();

}
