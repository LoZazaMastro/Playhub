using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Playhub.Shared;
using Microsoft.Win32;

namespace GamingMode.Services;

// COM-activated UWP games are not wrapper children. Accept only an identity
// published by the exact live installed wrapper Steam currently tracks.
internal static class UwpSteamSessionAssociation
{
    internal sealed record Tracked(uint Pid, ulong GameId, DateTime Started, string Image, string Aumid, string Executable);
    private sealed record Verified(UwpSessionIdentity Identity, Tracked Session);
    private static readonly object Sync = new();
    private static readonly Dictionary<uint, Verified> Cache = new();
    private static string _logPath = "";
    private static long _logLength = -1;
    private static DateTime _logModified;
    private static IReadOnlyList<Tracked> _tracked = Array.Empty<Tracked>();
    private static readonly Regex Added = new(@"AppID (?<id>\d+) adding PID (?<pid>\d+) as a tracked process (?<args>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Removed = new(@"no longer tracking PID (?<pid>\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex Command = new("^\\\"?\\\"(?<image>[^\\\"]+)\\\" --uwp \\\"(?<aumid>[^\\\"]+)\\\" \\\"(?<exe>[^\\\"]+)\\\"", RegexOptions.CultureInvariant);
    private static readonly string TrustedHelper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playhub", "Playhub.GameSession.exe");

    internal static uint AppId(ulong gameId) => gameId > uint.MaxValue ? (uint)(gameId >> 32) : (uint)gameId;
    internal static IReadOnlyList<Tracked> Parse(string text)
    {
        var active = new Dictionary<uint, Tracked>();
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("Client version:", StringComparison.Ordinal)) { active.Clear(); continue; }
            Match remove = Removed.Match(line);
            if (remove.Success && uint.TryParse(remove.Groups["pid"].Value, out uint removed)) { active.Remove(removed); continue; }
            Match added = Added.Match(line);
            if (!added.Success || !uint.TryParse(added.Groups["pid"].Value, out uint pid)) continue;
            active.Remove(pid);
            Match command = Command.Match(added.Groups["args"].Value);
            if (!command.Success || !ulong.TryParse(added.Groups["id"].Value, out ulong id) || line.Length < 21
                || !DateTime.TryParseExact(line.Substring(1,19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime started)) continue;
            active[pid] = new(pid, id, started, command.Groups["image"].Value, command.Groups["aumid"].Value, command.Groups["exe"].Value);
        }
        return active.Values.ToArray();
    }

    internal static bool Matches(Tracked session, UwpSessionIdentity identity, uint serverPid, uint gamePid, uint appId,
        long wrapperBirth, long gameBirth, string wrapperImage, string package, string image)
    {
        return identity.WrapperPid == session.Pid && serverPid == session.Pid && identity.WrapperBirth == wrapperBirth
            && wrapperBirth != 0 && identity.GamePid == gamePid && identity.GameBirth == gameBirth && gameBirth != 0
            && (appId == 0 || AppId(session.GameId) == appId)
            && string.Equals(Path.GetFullPath(session.Image), Path.GetFullPath(TrustedHelper), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFullPath(wrapperImage), Path.GetFullPath(TrustedHelper), StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Aumid, session.Aumid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.PackageFamily, package, StringComparison.OrdinalIgnoreCase)
            && string.Equals(package, session.Aumid.Split('!')[0], StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(image), session.Executable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Executable, image, StringComparison.OrdinalIgnoreCase)
            && SteamGameIdentityPolicy.IsCurrentProcess(DateTime.FromFileTimeUtc(wrapperBirth).ToLocalTime(), session.Started);
    }

    internal static bool IsTrackedGame(int pid) => pid > 0 && Find((uint)pid, 0) is not null;
    internal static UwpSessionIdentity? Find(uint pid, uint appId)
    {
        lock (Sync)
        {
            if (!ReadProcess(pid, out long gameBirth, out string gameImage, out string package) || package.Length == 0) return null;
            IReadOnlyList<Tracked> tracked = ReadTracked();
            if (Cache.TryGetValue(pid, out var cached))
            {
                if (tracked.Contains(cached.Session) && ReadProcess(cached.Session.Pid, out long wrapperBirth, out string wrapperImage, out _)
                    && Matches(cached.Session, cached.Identity, cached.Session.Pid, pid, appId, wrapperBirth, gameBirth, wrapperImage, package, gameImage))
                    return cached.Identity;
                Cache.Remove(pid);
            }
            foreach (var session in tracked)
            {
                if ((appId != 0 && AppId(session.GameId) != appId) || !session.Image.Equals(TrustedHelper, StringComparison.OrdinalIgnoreCase)
                    || !session.Aumid.StartsWith(package + "!", StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(gameImage).Equals(session.Executable, StringComparison.OrdinalIgnoreCase)
                    || !ReadProcess(session.Pid, out long wrapperBirth, out string wrapperImage, out _)
                    || !wrapperImage.Equals(TrustedHelper, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var stop = new CancellationTokenSource(200);
                    using var pipe = new NamedPipeClientStream(".", UwpSessionIdentity.PipeName(session.Pid, wrapperBirth), PipeDirection.In,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    pipe.ConnectAsync(stop.Token).GetAwaiter().GetResult();
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint serverPid) || serverPid != session.Pid) continue;
                    byte[] bytes = new byte[4096]; int length = 0;
                    while (length < bytes.Length)
                    {
                        int read = pipe.ReadAsync(bytes.AsMemory(length), stop.Token).AsTask().GetAwaiter().GetResult();
                        if (read == 0) break;
                        length += read;
                        if (Array.IndexOf(bytes, (byte)'\n', 0, length) >= 0) break;
                    }
                    if (length == bytes.Length) continue;
                    var identity = JsonSerializer.Deserialize<UwpSessionIdentity>(Encoding.UTF8.GetString(bytes, 0, length));
                    if (identity is null || !Matches(session, identity, serverPid, pid, appId, wrapperBirth, gameBirth, wrapperImage, package, gameImage)) continue;
                    if (Cache.Count > 64) Cache.Clear();
                    Cache[pid] = new(identity, session);
                    return identity;
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException) { }
            }
            return null;
        }
    }
    internal static IReadOnlyList<UwpSessionIdentity> FindForApp(uint appId)
    {
        if (appId == 0) return Array.Empty<UwpSessionIdentity>();
        Tracked[] sessions;
        lock (Sync) sessions = ReadTracked().Where(s => AppId(s.GameId) == appId
            && s.Image.Equals(TrustedHelper, StringComparison.OrdinalIgnoreCase)).Take(4).ToArray();
        var found = new List<UwpSessionIdentity>();
        var deadline = Stopwatch.StartNew();
        foreach (var session in sessions)
        {
            string name = Path.GetFileNameWithoutExtension(session.Executable);
            if (name.Length == 0 || SteamGameIdentityPolicy.IsXboxLaunchHelper(name)) continue;
            var candidates = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in candidates.Take(8))
                {
                    if (deadline.ElapsedMilliseconds >= 250) break;
                    if (Find((uint)process.Id, appId) is { } identity && !found.Contains(identity)) found.Add(identity);
                }
            }
            finally { foreach (var process in candidates) process.Dispose(); }
        }
        return found;
    }
    private static IReadOnlyList<Tracked> ReadTracked()
    {
        try
        {
            string root = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string ?? @"C:\Program Files (x86)\Steam";
            string path = Path.Combine(root, "logs", "gameprocess_log.txt");
            var info = new FileInfo(path);
            if (path == _logPath && info.Exists && info.Length == _logLength && info.LastWriteTimeUtc == _logModified) return _tracked;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 768 * 1024) stream.Seek(-768 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            _tracked = Parse(reader.ReadToEnd()); _logPath = path; _logLength = info.Length; _logModified = info.LastWriteTimeUtc;
            return _tracked;
        }
        catch (IOException) { _tracked = Array.Empty<Tracked>(); _logLength = -1; return _tracked; }
    }
    private static bool ReadProcess(uint pid, out long birth, out string image, out string package)
    {
        birth = 0; image = package = "";
        nint handle = OpenProcess(0x1000, false, pid);
        if (handle == 0) return false;
        try
        {
            if (!GetExitCodeProcess(handle, out uint code) || code != 259 || !GetProcessTimes(handle, out birth, out _, out _, out _)) return false;
            uint length = 32768; StringBuilder path = new((int)length);
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) return false;
            image = path.ToString(); length = 512; StringBuilder family = new((int)length);
            if (GetPackageFamilyName(handle, ref length, family) == 0) package = family.ToString();
            return true;
        }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint pid);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint process, out uint exit);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long birth, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder image, ref uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder package);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
