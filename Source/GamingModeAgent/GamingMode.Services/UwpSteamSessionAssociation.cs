using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using Playhub.Shared;

namespace GamingMode.Services;

// COM-activated UWP games are not wrapper children. Accept only an identity
// published by the exact live installed wrapper Steam currently tracks.
internal static class UwpSteamSessionAssociation
{
    private const string InstallerKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub";
    private const string InstallLocation = "InstallLocation";
    internal sealed record InstalledImages(string Wrapper, string Companion);
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
    private const int MaxCommandLength = 32768;
    private static readonly Regex Command = new(@"^""?""(?<image>[^""]+)"" --uwp ""(?<aumid>[^""]+)"" ""(?<exe>[^""]+)""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
    // Steam also surrounds the entire raw command with one quote, leaving the
    // executable unquoted inside it. Do not confuse that display form with the
    // separately quoted executable above (paths can contain spaces).
    private static readonly Regex WholeCommand = new(@"^""(?<image>[A-Za-z]:\\[^""]+\.exe) --uwp ""(?<aumid>[^""]+)"" ""(?<exe>[^""]+)""(?: .*)?""$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
    internal static InstalledImages? ReadInstalledImages()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InstallerKey);
            bool registered = key is not null && key.GetValueNames().Contains(InstallLocation, StringComparer.OrdinalIgnoreCase);
            string? location = registered && key!.GetValueKind(InstallLocation) == RegistryValueKind.String
                ? key.GetValue(InstallLocation, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string : null;
            string defaultLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playhub");
            return ResolveInstalledImages(registered, location, defaultLocation);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    internal static InstalledImages? ResolveInstalledImages(bool registered, string? location, string defaultLocation)
    {
        string? root = (registered ? location : defaultLocation)?.TrimEnd('\\');
        if (root is null || root.Length < 4 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            return null;
        var invalid = Path.GetInvalidFileNameChars();
        foreach (string part in root[3..].Split('\\'))
        {
            if (part.Length == 0 || part[^1] is '.' or ' ' || part.IndexOfAny(invalid) >= 0)
                return null;
        }
        return new(Path.Combine(root, "Playhub.GameSession.exe"), Path.Combine(root, "Playhub.XboxSession.exe"));
    }

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
            string arguments = added.Groups["args"].Value.TrimEnd('\r');
            if (arguments.Length > MaxCommandLength || arguments.Any(char.IsControl)) continue;
            Match command = Command.Match(arguments);
            if (!command.Success) command = WholeCommand.Match(arguments);
            if (!command.Success || !ulong.TryParse(added.Groups["id"].Value, out ulong id) || line.Length < 21
                || !DateTime.TryParseExact(line.Substring(1,19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime started)) continue;
            active[pid] = new(pid, id, started, command.Groups["image"].Value, command.Groups["aumid"].Value, command.Groups["exe"].Value);
        }
        return active.Values.ToArray();
    }

    internal static bool Matches(Tracked session, UwpSessionIdentity identity, uint serverPid, uint gamePid, uint appId,
        long wrapperBirth, long gameBirth, string wrapperImage, string package, string image, InstalledImages images)
    {
        return identity.WrapperPid == session.Pid && serverPid == session.Pid && identity.WrapperBirth == wrapperBirth
            && wrapperBirth != 0 && identity.GamePid == gamePid && identity.GameBirth == gameBirth && gameBirth != 0
            && (appId == 0 || AppId(session.GameId) == appId)
            && string.Equals(session.Image, images.Wrapper, StringComparison.OrdinalIgnoreCase)
            && string.Equals(wrapperImage, images.Wrapper, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Aumid, session.Aumid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.PackageFamily, package, StringComparison.OrdinalIgnoreCase)
            && string.Equals(package, session.Aumid.Split('!')[0], StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(image), Path.GetFileName(session.Executable), StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Executable, image, StringComparison.OrdinalIgnoreCase)
            && SteamGameIdentityPolicy.IsCurrentProcess(DateTime.FromFileTimeUtc(wrapperBirth).ToLocalTime(), session.Started);
    }

    internal static bool IsTrackedGame(int pid) => pid > 0 && Find((uint)pid, 0) is not null;
    internal static UwpSessionIdentity? Find(uint pid, uint appId) =>
        ReadInstalledImages() is { } images ? Find(pid, appId, images) : null;

    private static UwpSessionIdentity? Find(uint pid, uint appId, InstalledImages images)
    {
        if (!ReadProcess(pid, out long gameBirth, out string gameImage, out string package) || package.Length == 0) return null;
        Tracked[] tracked;
        Verified? cached;
        lock (Sync)
        {
            tracked = ReadTracked().ToArray();
            Cache.TryGetValue(pid, out cached);
        }
        if (cached is not null)
        {
            if (tracked.Contains(cached.Session) && ReadProcess(cached.Session.Pid, out long wrapperBirth, out string wrapperImage, out _)
                && Matches(cached.Session, cached.Identity, cached.Session.Pid, pid, appId, wrapperBirth, gameBirth, wrapperImage, package, gameImage, images))
            {
                lock (Sync)
                    if (Cache.TryGetValue(pid, out var current) && ReferenceEquals(current, cached) && ReadTracked().Contains(cached.Session))
                        return cached.Identity;
            }
            lock (Sync)
                if (Cache.TryGetValue(pid, out var current) && ReferenceEquals(current, cached)) Cache.Remove(pid);
        }
        foreach (var session in tracked)
        {
            if ((appId != 0 && AppId(session.GameId) != appId) || !session.Image.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)
                || !session.Aumid.StartsWith(package + "!", StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(gameImage).Equals(Path.GetFileName(session.Executable), StringComparison.OrdinalIgnoreCase)
                || !ReadProcess(session.Pid, out long wrapperBirth, out string wrapperImage, out _)
                || !wrapperImage.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                // IPC can take its full bounded timeout. It must not hold the
                // shared tracked/cache lock used by independent launch queries.
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
                if (identity is null || !Matches(session, identity, serverPid, pid, appId, wrapperBirth, gameBirth, wrapperImage, package, gameImage, images)) continue;
                // The IPC reply cannot inherit a removed wrapper or recycled
                // game PID while this query was outside the coordination lock.
                if (!ReadProcess(pid, out long latestGameBirth, out string latestGameImage, out string latestPackage)
                    || !ReadProcess(session.Pid, out long latestWrapperBirth, out string latestWrapperImage, out _)
                    || !Matches(session, identity, serverPid, pid, appId, latestWrapperBirth, latestGameBirth,
                        latestWrapperImage, latestPackage, latestGameImage, images)) continue;
                lock (Sync)
                {
                    if (!ReadTracked().Contains(session)) continue;
                    if (Cache.TryGetValue(pid, out var current) && !ReferenceEquals(current, cached) && current.Identity != identity) continue;
                    if (Cache.Count > 64) Cache.Clear();
                    Cache[pid] = new(identity, session);
                    return identity;
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }
    internal static IReadOnlyList<UwpSessionIdentity> FindForApp(uint appId) =>
        appId != 0 && ReadInstalledImages() is { } images ? FindForApp(appId, images) : Array.Empty<UwpSessionIdentity>();

    internal static IReadOnlyList<UwpSessionIdentity> FindForApp(uint appId, InstalledImages images)
    {
        if (appId == 0) return Array.Empty<UwpSessionIdentity>();
        Tracked[] sessions;
        lock (Sync) sessions = ReadTracked().Where(s => AppId(s.GameId) == appId
            && s.Image.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)).Take(4).ToArray();
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
                    if (Find((uint)process.Id, appId, images) is { } identity && !found.Contains(identity)) found.Add(identity);
                }
            }
            finally { foreach (var process in candidates) process.Dispose(); }
        }
        return found;
    }

    internal sealed record PendingLaunch(uint AppId, uint WrapperPid, long WrapperBirth, string Aumid, string Executable,
        string WrapperImage, long StartedAtUnixMs, long ExpiresAtUnixMs, bool GameReadyClaimed = false, bool InputAuthorityClaimed = false);
    internal sealed record PendingWrapper(long Birth, string Image, uint Session);
    internal static IReadOnlyList<PendingLaunch> FindPendingForApp(uint appId)
    {
        if (appId == 0 || ReadInstalledImages() is not { } images) return [];
        using var own = Process.GetCurrentProcess();
        lock (Sync) return FindPending(appId, images, ReadTracked, ReadPendingWrapper, PendingRegistration,
            () => DateTime.UtcNow, (uint)own.SessionId);
    }
    // Pure policy seam: all production provenance reads are supplied above.
    internal static IReadOnlyList<PendingLaunch> FindPending(uint appId, InstalledImages images,
        Func<IReadOnlyList<Tracked>> tracked, Func<uint, PendingWrapper?> read,
        Func<Tracked, bool> registered, Func<DateTime> clock, uint sessionId)
    {
        DateTime utcNow = clock();
        if (appId == 0 || utcNow.Kind != DateTimeKind.Utc) return [];
        var found = new List<PendingLaunch>();
        foreach (var item in tracked().Where(s => AppId(s.GameId) == appId).Take(4).ToArray())
        {
            try
            {
                var before = read(item.Pid);
                if (before is null || before.Birth <= 0 || before.Session != sessionId
                    || !item.Image.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)
                    || !before.Image.Equals(images.Wrapper, StringComparison.OrdinalIgnoreCase)) continue;
                DateTime started = DateTime.FromFileTimeUtc(before.Birth), expires = started.AddSeconds(15);
                if (utcNow < started || utcNow >= expires
                    || Math.Abs((started.ToLocalTime() - item.Started).TotalSeconds) > 2
                    || !registered(item)) continue;
                // Registry/manifest I/O cannot preserve a removed/recycled wrapper.
                if (read(item.Pid) != before || !tracked().Contains(item)) continue;
                DateTime completed = clock();
                if (completed.Kind != DateTimeKind.Utc || completed < utcNow || completed < started || completed >= expires) continue;
                found.Add(new(appId, item.Pid, before.Birth, item.Aumid, item.Executable, before.Image,
                    new DateTimeOffset(started).ToUnixTimeMilliseconds(), new DateTimeOffset(expires).ToUnixTimeMilliseconds()));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException
                or ArgumentException or InvalidOperationException or InvalidDataException or XmlException) { }
        }
        return found;
    }
    private static PendingWrapper? ReadPendingWrapper(uint pid)
    {
        return ReadProcess(pid, out long birth, out string image, out _)
            && ProcessIdToSessionId(pid, out uint session) ? new(birth, image, session) : null;
    }
    private static bool PendingRegistration(Tracked item)
    {
        var parts = item.Aumid.Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || item.Aumid.Length > 512
            || string.IsNullOrWhiteSpace(item.Executable) || Path.IsPathRooted(item.Executable)
            || item.Executable.Split('\\', '/').Any(p => p is ".." or "." or "")
            || !item.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        uint count = 0, characters = 0;
        if (GetPackagesByPackageFamily(parts[0], ref count, 0, ref characters, 0) != 122
            || count is 0 or > 128 || characters is 0 or > 1048576) return false;
        nint pointers = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size)), buffer = Marshal.AllocHGlobal(checked((int)characters * 2));
        try
        {
            if (GetPackagesByPackageFamily(parts[0], ref count, pointers, ref characters, buffer) != 0) return false;
            int matches = 0;
            for (int index = 0; index < count; ++index)
            {
                string? fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, checked(index * IntPtr.Size)));
                if (string.IsNullOrEmpty(fullName)) continue;
                uint size = 0;
                if (GetPackagePathByFullName(fullName, ref size, null) != 122 || size is 0 or > 32768) continue;
                var path = new StringBuilder((int)size);
                if (GetPackagePathByFullName(fullName, ref size, path) != 0) continue;
                string directory = Path.GetFullPath(path.ToString());
                var xml = ReadPendingXml(Path.Combine(directory, "AppxManifest.xml"));
                var applications = xml.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Applications");
                var app = applications?.Elements().SingleOrDefault(e => e.Name.LocalName == "Application"
                    && string.Equals((string?)e.Attribute("Id"), parts[1], StringComparison.OrdinalIgnoreCase));
                string executable = (string?)app?.Attribute("Executable") ?? "";
                bool same = executable.Equals(item.Executable, StringComparison.OrdinalIgnoreCase);
                if (!same && Path.GetFileName(executable).Equals("GameLaunchHelper.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var config = ReadPendingXml(Path.Combine(directory, "MicrosoftGame.Config"));
                    same = config.Root?.Elements().Where(e => e.Name.LocalName == "ExecutableList").SelectMany(e => e.Elements())
                        .Count(e => e.Name.LocalName == "Executable"
                            && string.Equals((string?)e.Attribute("Id"), parts[1], StringComparison.OrdinalIgnoreCase)
                            && string.Equals((string?)e.Attribute("Name"), item.Executable, StringComparison.OrdinalIgnoreCase)) == 1;
                }
                string game = Path.GetFullPath(Path.Combine(directory, item.Executable));
                if (same && game.StartsWith(directory.TrimEnd('\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(game)) ++matches;
            }
            return matches == 1;
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(pointers); }
    }
    private static XDocument ReadPendingXml(string path)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > 1048576) throw new InvalidDataException("Bounded registered application metadata required.");
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 1048576, CloseInput = false });
        return XDocument.Load(reader);
    }
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, nint names, ref uint characters, nint buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);

    // A Windows frame identifies the app, so the frame alone cannot distinguish two live copies.
    internal static bool IsOnlyRunningInstance(UwpSessionIdentity identity)
    {
        var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(identity.Executable));
        try
        {
            return candidates.All(process => process.Id == identity.GamePid
                || !ReadProcess((uint)process.Id, out _, out string image, out string family)
                || !string.Equals(image, identity.Executable, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(family, identity.PackageFamily, StringComparison.OrdinalIgnoreCase));
        }
        finally { foreach (var process in candidates) process.Dispose(); }
    }

    private static IReadOnlyList<Tracked> ReadTracked()
    {
        try
        {
            if (SteamInstallationPath.FindRoot() is not { } root) return Array.Empty<Tracked>();
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
