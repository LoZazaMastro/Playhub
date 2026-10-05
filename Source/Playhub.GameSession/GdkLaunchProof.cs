using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Playhub.GameSession;

internal sealed record RegisteredPackage(string FullName, string Directory);
internal sealed record GdkLaunchPlan(string PackageFullName, string Directory, string Helper, string Game);
internal interface IRegisteredPackageCatalog
{
    IReadOnlyList<RegisteredPackage> ForFamily(string family);
}

internal static class GdkLaunchProof
{
    internal static void RejectRunningGame(GdkLaunchPlan plan)
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(plan.Game)))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited || process.SessionId != current.SessionId) continue;
                    var image = process.MainModule?.FileName;
                    if (image is null) throw new InvalidOperationException("An existing game process could not be identified; another launch was not started.");
                    var actual = Path.Combine(RegisteredPackageCatalog.PhysicalDirectory(Path.GetDirectoryName(image)!), Path.GetFileName(image));
                    var expected = Path.Combine(RegisteredPackageCatalog.PhysicalDirectory(Path.GetDirectoryName(plan.Game)!), Path.GetFileName(plan.Game));
                    if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("This Xbox game is already running; another launch was not started.");
                }
                catch (Win32Exception error)
                {
                    throw new InvalidOperationException("An existing game process could not be identified; another launch was not started.", error);
                }
            }
        }
    }

    internal static GdkLaunchPlan Resolve(string aumid, string expectedExecutable, IRegisteredPackageCatalog catalog)
    {
        var parts = aumid.Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(expectedExecutable))
            throw new ArgumentException("Invalid Xbox application identity.");
        var matches = new List<GdkLaunchPlan>();
        foreach (var package in catalog.ForFamily(parts[0]))
        {
            var manifestPath = Path.Combine(package.Directory, "AppxManifest.xml");
            var configPath = Path.Combine(package.Directory, "MicrosoftGame.Config");
            if (!File.Exists(manifestPath) || !File.Exists(configPath)) continue;
            var manifest = ReadXml(manifestPath);
            var identity = manifest.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "Identity");
            var applications = manifest.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "Applications");
            var application = applications?.Elements().SingleOrDefault(element => element.Name.LocalName == "Application" && (string?)element.Attribute("Id") == parts[1]);
            if (application is null || !string.Equals((string?)application.Attribute("EntryPoint"), "Windows.FullTrustApplication", StringComparison.OrdinalIgnoreCase)) continue;
            var identityName = (string?)identity?.Attribute("Name") ?? "";
            if (identityName.Length == 0 || !package.FullName.StartsWith(identityName + "_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The registered Xbox package does not match its manifest identity.");
            var helperName = (string?)application.Attribute("Executable") ?? "";
            if (!string.Equals(Path.GetFileName(helperName), "GameLaunchHelper.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var helper = Inside(package.Directory, helperName);
            var config = ReadXml(configPath);
            var configured = config.Root?.Elements().Where(element => element.Name.LocalName == "ExecutableList")
                .SelectMany(element => element.Elements()).Where(element => element.Name.LocalName == "Executable" && (string?)element.Attribute("Id") == parts[1]).ToArray() ?? [];
            var expected = Inside(package.Directory, expectedExecutable);
            var targets = configured.Select(element => (string?)element.Attribute("Name") ?? "")
                .Where(name => string.Equals(Inside(package.Directory, name), expected, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (targets.Length != 1 || !File.Exists(helper) || !File.Exists(expected))
                throw new InvalidOperationException("The Xbox game executable does not match its registered launch configuration.");
            matches.Add(new GdkLaunchPlan(package.FullName, Path.GetFullPath(package.Directory), helper, expected));
        }
        return matches.Count == 1 ? matches[0] : throw new InvalidOperationException(matches.Count == 0
            ? "No registered full-trust Xbox launch helper matches this game."
            : "Multiple registered Xbox packages match this game; launch was not started.");
    }

    private static XDocument ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string Inside(string directory, string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || Path.IsPathRooted(executable))
            throw new InvalidOperationException("Xbox launch configuration requires an executable inside its installation.");
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, executable));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Xbox launch configuration requires an executable inside its installation.");
        return path;
    }
}

internal sealed class RegisteredPackageCatalog(bool physicalPaths = false) : IRegisteredPackageCatalog
{
    public IReadOnlyList<RegisteredPackage> ForFamily(string family)
    {
        uint count = 0, characters = 0;
        var result = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref characters, IntPtr.Zero);
        if (result == 0 && count == 0) return [];
        if (result != 122 || count == 0 || count > 128 || characters > 1048576) throw new Win32Exception(result);
        var pointers = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        var buffer = Marshal.AllocHGlobal(checked((int)characters * 2));
        try
        {
            result = GetPackagesByPackageFamily(family, ref count, pointers, ref characters, buffer);
            if (result != 0) throw new Win32Exception(result);
            var packages = new List<RegisteredPackage>();
            for (var index = 0; index < count; index++)
            {
                var fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, checked(index * IntPtr.Size)))!;
                uint size = 0;
                result = GetPackagePathByFullName(fullName, ref size, null);
                if (result != 122 || size > 32768) throw new Win32Exception(result);
                var path = new StringBuilder((int)size);
                result = GetPackagePathByFullName(fullName, ref size, path);
                if (result != 0) throw new Win32Exception(result);
                packages.Add(new RegisteredPackage(fullName, physicalPaths ? PhysicalDirectory(path.ToString()) : path.ToString()));
            }
            return packages;
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(pointers); }
    }

    internal static string PhysicalDirectory(string directory)
    {
        using var handle = CreateFile(directory, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= path.Capacity) throw new InvalidOperationException("The registered Xbox installation path is too long.");
        var value = path.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value[8..];
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr attributes, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint characters, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr fullNames, ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
}
