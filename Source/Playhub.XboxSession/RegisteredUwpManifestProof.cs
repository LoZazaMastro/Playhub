// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;
using Playhub.GameSession;

internal static class RegisteredUwpManifestProof
{
    // Keep the verified manifest open through the exact process lifetime. A
    // package update becomes a new launch, rather than silently changing the
    // identity which authorized this companion.
    internal static IDisposable Verify(XboxCommand command, AppContainerLaunchPlan plan,
        Func<string, IReadOnlyList<RegisteredPackage>>? registered = null)
    {
        string family = command.Aumid.Split('!')[0];
        registered ??= new RegisteredPackageCatalog(physicalPaths: true).ForFamily;
        var matches = registered(family).Where(value => value.FullName.Equals(plan.PackageFullName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches is not [var package]) throw new InvalidDataException("The exact UWP package registration changed.");
        string root = RegisteredPackageCatalog.PhysicalDirectory(package.Directory);
        string expected = Path.GetFullPath(Path.Combine(root, command.Executable));
        if (!plan.Executable.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || !expected.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The UWP image is outside its exact registered package.");
        string manifestPath = Path.Combine(root, "AppxManifest.xml");
        var manifest = File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (manifest.Length is <= 0 or > 1024 * 1024 || !FinalPath(manifest.SafeFileHandle).Equals(manifestPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The UWP manifest path changed or exceeds its limit.");
            using var reader = XmlReader.Create(manifest, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024, CloseInput = false });
            var xml = XDocument.Load(reader).Root;
            var identity = xml?.Elements().SingleOrDefault(value => value.Name.LocalName == "Identity");
            if (xml?.Name.LocalName != "Package" || identity is null
                || !FullName(identity).Equals(plan.PackageFullName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The UWP manifest publisher, architecture or version differs from its registration.");
            // A package-root junction is resolved above; executable junctions
            // must still resolve to the exact image authorized by the manifest.
            using var image = CreateFile(expected, 0, 7, 0, 3, 0, 0);
            if (image.IsInvalid || !FinalPath(image).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The UWP executable resolves to another image.");
            return manifest;
        }
        catch { manifest.Dispose(); throw; }
    }

    internal static string FullName(XElement identity)
    {
        string? name = (string?)identity.Attribute("Name"), publisher = (string?)identity.Attribute("Publisher");
        string? text = (string?)identity.Attribute("Version"), architecture = (string?)identity.Attribute("ProcessorArchitecture");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher) || name.Length > 255 || publisher.Length > 8192
            || !Version.TryParse(text, out var version) || version.Build < 0 || version.Revision < 0
            || new[] { version.Major, version.Minor, version.Build, version.Revision }.Any(value => value > ushort.MaxValue)
            || architecture != "x64") throw new InvalidDataException("The UWP manifest has no complete native x64 identity.");
        var id = new PackageId
        {
            Architecture = 9, Version = ((ulong)version.Major << 48) | ((ulong)version.Minor << 32) | ((ulong)version.Build << 16) | (uint)version.Revision,
            Name = name, Publisher = publisher, ResourceId = (string?)identity.Attribute("ResourceId") ?? ""
        };
        uint length = 0;
        if (PackageFullNameFromId(ref id, ref length, null) != 122 || length is < 1 or > 4096)
            throw new InvalidDataException("Windows refused the UWP manifest identity.");
        var value = new StringBuilder((int)length);
        if (PackageFullNameFromId(ref id, ref length, value) != 0)
            throw new InvalidDataException("Windows could not derive the UWP package full identity.");
        return value.ToString();
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var path = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) throw new InvalidDataException("The UWP file has no bounded physical path.");
        string value = path.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value[8..];
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        return Path.GetFullPath(value);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PackageId
    {
        internal uint Reserved, Architecture;
        internal ulong Version;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Name, Publisher, ResourceId;
        internal nint PublisherId;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PackageFullNameFromId(ref PackageId id, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint attributes, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}
