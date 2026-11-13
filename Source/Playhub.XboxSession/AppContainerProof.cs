// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Playhub.GameSession;

internal sealed record AppContainerLaunchPlan(string PackageFullName, string Executable, bool ExecutableVerified = true);

internal static class AppContainerProof
{
    internal const string BridgeFileName = "WsgmUwpBridge.dll";
    private static readonly string[] RequiredExports =
        ["InitializeBridge", "InitializeInputBridge", "InputBridgeRoutes", "BridgeInitialOwnerRequested"];

    internal static AppContainerLaunchPlan Resolve(XboxCommand command, IRegisteredPackageCatalog catalog,
        Func<string, Stream>? openExecutable = null, bool verifyImage = true)
    {
        var parts = command.Aumid.Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid application identity.");
        var matches = new List<AppContainerLaunchPlan>();
        foreach (var package in catalog.ForFamily(parts[0]))
        {
            var manifestPath = Path.Combine(package.Directory, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) continue;
            using var reader = XmlReader.Create(manifestPath, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var manifest = XDocument.Load(reader).Root;
            if (manifest?.Name.LocalName != "Package") continue;
            var identity = manifest?.Elements().SingleOrDefault(element => element.Name.LocalName == "Identity");
            var applications = manifest?.Elements().SingleOrDefault(element => element.Name.LocalName == "Applications");
            var application = applications?.Elements().SingleOrDefault(element => element.Name.LocalName == "Application"
                && (string?)element.Attribute("Id") == parts[1]);
            if (identity is null || application is null) continue;

            var name = (string?)identity.Attribute("Name");
            var packageName = package.FullName.Split('_');
            if (string.IsNullOrWhiteSpace(name) || packageName.Length != 5 || !string.Equals(packageName[0], name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals($"{name}_{packageName[4]}", parts[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The installed package and manifest identities do not match the requested family.");
            if (!string.Equals((string?)identity.Attribute("ProcessorArchitecture"), "x64", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(packageName[2], "x64", StringComparison.OrdinalIgnoreCase) || !DeclaresAppContainer(application)) continue;

            var executable = ContainedExecutable(package.Directory, (string?)application.Attribute("Executable"));
            var expected = ContainedExecutable(package.Directory, command.Executable);
            if (!executable.Equals(expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
                throw new InvalidOperationException("The requested executable does not match the registered AppContainer application.");
            matches.Add(new AppContainerLaunchPlan(package.FullName, executable,
                verifyImage && VerifyExecutable(executable, openExecutable ?? File.OpenRead)));
        }

        return matches.Count == 1 ? matches[0] : throw new InvalidOperationException(matches.Count == 0
            ? "No registered x64 AppContainer application matches the requested identity."
            : "More than one registered AppContainer package matches the requested identity.");
    }

    private static bool VerifyExecutable(string executable, Func<string, Stream> openExecutable)
    {
        try
        {
            using var image = new PEReader(openExecutable(executable));
            if (image.PEHeaders.CoffHeader.Machine != Machine.Amd64 || !image.PEHeaders.IsExe)
                throw new InvalidDataException("The registered application executable is not native x64.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // A protected Store image can be checked instead via a query-only live process handle.
            return false;
        }
    }

    private static bool DeclaresAppContainer(XElement application)
    {
        string? trust = (string?)application.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "TrustLevel");
        string? runtime = (string?)application.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "RuntimeBehavior");
        string? entry = (string?)application.Attribute("EntryPoint");
        return (trust is null || trust == "appContainer") && (runtime is null || runtime == "windowsApp")
            && !string.IsNullOrWhiteSpace(entry) && !entry.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase);
    }

    private static string ContainedExecutable(string directory, string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || Path.IsPathRooted(executable) || executable.Contains(':'))
            throw new InvalidOperationException("The application executable must be inside its registered package.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var image = Path.GetFullPath(Path.Combine(root, executable));
        if (!image.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The application executable must be inside its registered package.");
        return image;
    }

    internal static void VerifyBridge(Stream stream)
    {
        using var image = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var headers = image.PEHeaders;
        var directory = headers.PEHeader?.ExportTableDirectory;
        if (headers.CoffHeader.Machine != Machine.Amd64 || !headers.IsDll || directory is null || directory.Value.Size < 40)
            throw new InvalidDataException("The AppContainer bridge is not an x64 DLL with exports.");
        var table = image.GetSectionData(directory.Value.RelativeVirtualAddress).GetReader(0, 40);
        table.Offset = 20;
        int functionCount = table.ReadInt32();
        int nameCount = table.ReadInt32();
        int functionsRva = table.ReadInt32();
        int namesRva = table.ReadInt32();
        int ordinalsRva = table.ReadInt32();
        if (functionCount is < 1 or > 4096 || nameCount is < 1 or > 4096)
            throw new InvalidDataException("The AppContainer bridge has an invalid export table.");
        var functions = image.GetSectionData(functionsRva).GetReader(0, functionCount * 4);
        var names = image.GetSectionData(namesRva).GetReader(0, nameCount * 4);
        var ordinals = image.GetSectionData(ordinalsRva).GetReader(0, nameCount * 2);
        var missing = new HashSet<string>(RequiredExports, StringComparer.Ordinal);
        for (int index = 0; index < nameCount; index++)
        {
            var nameBytes = image.GetSectionData(names.ReadInt32());
            int prefixLength = Math.Min(nameBytes.Length, 128);
            var prefix = nameBytes.GetReader(0, prefixLength).ReadBytes(prefixLength);
            int terminator = Array.IndexOf(prefix, (byte)0);
            int ordinal = ordinals.ReadUInt16();
            if (terminator < 1 || ordinal >= functionCount) throw new InvalidDataException("The AppContainer bridge has an invalid export entry.");
            var name = Encoding.ASCII.GetString(prefix, 0, terminator);
            if (!missing.Remove(name)) continue;
            functions.Offset = ordinal * 4;
            int rva = functions.ReadInt32();
            if (!headers.SectionHeaders.Any(section => (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0
                && rva >= section.VirtualAddress && (long)rva < (long)section.VirtualAddress + section.VirtualSize))
                throw new InvalidDataException($"The AppContainer bridge export {name} has no executable body.");
        }
        if (missing.Count > 0) throw new InvalidDataException("The AppContainer bridge is missing: " + string.Join(", ", missing));
    }
}
