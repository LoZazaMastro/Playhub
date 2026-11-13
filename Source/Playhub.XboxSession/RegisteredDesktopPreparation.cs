// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;
using Playhub.GameSession;

internal sealed record RegisteredDesktopPlan(string PackageFullName, string Directory, string Helper, string Executable);

internal sealed class RegisteredDesktopPreparation : IDisposable
{
    private readonly FileStream _manifest, _configuration;
    internal RegisteredDesktopPlan Plan { get; }
    internal string PackageFullName => Plan.PackageFullName;
    internal string Executable => Plan.Executable;
    private RegisteredDesktopPreparation(RegisteredDesktopPlan plan, FileStream manifest, FileStream configuration) =>
        (Plan, _manifest, _configuration) = (plan, manifest, configuration);

    internal static RegisteredDesktopPreparation Prepare(XboxCommand command, IRegisteredPackageCatalog catalog)
    {
        string[] parts = command.Aumid.Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Invalid registered desktop application.");
        RegisteredDesktopPreparation? result = null;
        try
        {
            foreach (var package in catalog.ForFamily(parts[0]))
            {
                string root = RegisteredPackageCatalog.PhysicalDirectory(package.Directory);
                string manifestPath = Path.Combine(root, "AppxManifest.xml"), configPath = Path.Combine(root, "MicrosoftGame.Config");
                if (!File.Exists(manifestPath) || !File.Exists(configPath)) continue;
                FileStream? manifest = null, config = null;
                try
                {
                    manifest = Pin(manifestPath);
                    config = Pin(configPath);
                    var xml = Read(manifest);
                    if (xml.Name.LocalName != "Package" || xml.Name.NamespaceName is not
                        (RegisteredXboxApplication.Foundation or RegisteredXboxApplication.LegacyFoundation))
                        throw new InvalidDataException("Unknown registered desktop manifest namespace.");
                    XNamespace ns = xml.Name.Namespace;
                    var identity = xml.Elements(ns + "Identity").SingleOrDefault()
                        ?? throw new InvalidDataException("Missing registered desktop identity.");
                    var application = xml.Elements(ns + "Applications").SingleOrDefault()?.Elements(ns + "Application")
                        .SingleOrDefault(value => (string?)value.Attribute("Id") == parts[1]);
                    if (application is null) continue;
                    if (RegisteredXboxApplication.Classify(application) != RegisteredXboxApplicationKind.PackagedDesktop)
                        throw new InvalidDataException("The registered application is not a desktop game.");
                    string fullName = RegisteredUwpManifestProof.FullName(identity);
                    if (!fullName.Equals(package.FullName, StringComparison.OrdinalIgnoreCase)
                        || !(((string?)identity.Attribute("Name")) + "_" + fullName.Split('_')[4]).Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The registered desktop package identity differs from the requested family.");
                    string helper = Inside(root, (string?)application.Attribute("Executable"));
                    if (!Path.GetFileName(helper).Equals("GameLaunchHelper.exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The registered desktop game does not use its declared Xbox launch helper.");
                    string executable = Inside(root, command.Executable);
                    if (executable.Equals(helper, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The Xbox launch helper cannot be treated as the game process.");
                    ValidateConfiguration(Read(config), identity, parts[1], executable, root);
                    VerifyImage(helper); VerifyImage(executable);
                    if (result is not null) throw new InvalidDataException("Multiple registered desktop games match the activation.");
                    result = new(new(fullName, root, helper, executable), manifest, config);
                    manifest = null; config = null;
                }
                finally { manifest?.Dispose(); config?.Dispose(); }
            }
            return result ?? throw new InvalidDataException("No registered desktop game matches the activation.");
        }
        catch { result?.Dispose(); throw; }
    }

    internal static void ValidateConfiguration(XElement config, XElement identity, string appId, string executable, string root)
    {
        if (config.Name != "Game") throw new InvalidDataException("Unknown Xbox launch configuration namespace.");
        var configIdentity = config.Elements("Identity").SingleOrDefault()
            ?? throw new InvalidDataException("Missing Xbox launch configuration identity.");
        foreach (string field in new[] { "Name", "Publisher", "Version" })
            if (!string.Equals((string?)configIdentity.Attribute(field), (string?)identity.Attribute(field), StringComparison.Ordinal))
                throw new InvalidDataException("The Xbox launch configuration does not match the registered package identity.");
        var entries = config.Elements("ExecutableList").SingleOrDefault()?.Elements("Executable")
            .Where(value => (string?)value.Attribute("Id") == appId).ToArray() ?? [];
        if (entries is not [var entry] || !Inside(root, (string?)entry.Attribute("Name")).Equals(executable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Xbox launch configuration does not identify one exact game executable.");
    }
    internal static string Inside(string root, string? image)
    {
        if (string.IsNullOrWhiteSpace(image) || Path.IsPathRooted(image) || image.Contains(':') || image.Contains('\0')
            || !image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || image.Replace('/', '\\').Split('\\').Any(value => value is "" or "." or ".."))
            throw new InvalidDataException("A contained registered desktop executable is required.");
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, image));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The desktop image is outside its package.");
        return path;
    }
    private static XElement Read(Stream stream)
    {
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024, CloseInput = false });
        return XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty registered application metadata.");
    }
    private static FileStream Pin(string path)
    {
        var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (file.Length is <= 0 or > 1024 * 1024 || !FinalPath(file.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Registered desktop metadata changed its physical path or size.");
            return file;
        }
        catch { file.Dispose(); throw; }
    }
    private static void VerifyImage(string path)
    {
        using var file = CreateFile(path, 0, 7, 0, 3, 0, 0);
        if (file.IsInvalid || !FinalPath(file).Equals(path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The registered desktop image resolves to a different file.");
    }
    private static string FinalPath(SafeFileHandle file)
    {
        var value = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(file, value, (uint)value.Capacity, 0);
        if (length == 0 || length >= value.Capacity) throw new InvalidDataException("Registered desktop physical path unavailable.");
        string path = value.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return Path.GetFullPath(path);
    }
    public void Dispose() { _configuration.Dispose(); _manifest.Dispose(); }
    [DllImport("kernel32.dll", EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,nint security,uint creation,uint flags,nint template);
    [DllImport("kernel32.dll",EntryPoint="GetFinalPathNameByHandleW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder value,uint length,uint flags);
}
