// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.Xml;
using System.Xml.Linq;
using Playhub.GameSession;

internal enum RegisteredXboxApplicationKind { Unsupported, AppContainer, PackagedDesktop }

internal static class RegisteredXboxApplication
{
    internal const string Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    internal const string LegacyFoundation = "http://schemas.microsoft.com/appx/2010/manifest";
    internal const string Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";

    // Microsoft Application schema: legacy FullTrustApplication is equivalent
    // to packagedClassicApp/mediumIL. Never infer trust from a game's name.
    internal static RegisteredXboxApplicationKind Classify(XElement application)
    {
        string ns = application.Name.NamespaceName;
        if (application.Name.LocalName != "Application" || ns is not (Foundation or LegacyFoundation))
            throw new InvalidDataException("The registered application has an unknown manifest namespace.");
        foreach (string attribute in new[] { "RuntimeBehavior", "TrustLevel" })
        {
            var values = application.Attributes().Where(value => value.Name.LocalName == attribute).ToArray();
            if (values.Length > 1 || values.Any(value => value.Name.NamespaceName != Uap10))
                throw new InvalidDataException("The registered application has ambiguous trust metadata.");
        }
        string? runtime = (string?)application.Attribute(XName.Get("RuntimeBehavior", Uap10));
        string? trust = (string?)application.Attribute(XName.Get("TrustLevel", Uap10));
        string? entry = (string?)application.Attribute("EntryPoint");
        bool legacyDesktop = string.Equals(entry, "Windows.FullTrustApplication", StringComparison.OrdinalIgnoreCase);
        if (legacyDesktop)
        {
            if (runtime is not (null or "packagedClassicApp") || trust is not (null or "mediumIL"))
                throw new InvalidDataException("The registered application declares conflicting Windows trust modes.");
            return RegisteredXboxApplicationKind.PackagedDesktop;
        }
        if (runtime is "packagedClassicApp" or "win32App")
        {
            if (trust != "mediumIL") return RegisteredXboxApplicationKind.Unsupported;
            if (!string.IsNullOrEmpty(entry))
                throw new InvalidDataException("The registered desktop application declares a conflicting entry point.");
            return RegisteredXboxApplicationKind.PackagedDesktop;
        }
        return (runtime is null or "windowsApp") && (trust is null or "appContainer")
            && !string.IsNullOrWhiteSpace(entry) && !entry.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase)
            ? RegisteredXboxApplicationKind.AppContainer : RegisteredXboxApplicationKind.Unsupported;
    }

    internal static RegisteredXboxApplicationKind Resolve(XboxCommand command, IRegisteredPackageCatalog catalog)
    {
        string[] parts = command.Aumid.Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Invalid registered application identity.");
        var found = new List<RegisteredXboxApplicationKind>();
        foreach (var package in catalog.ForFamily(parts[0]))
        {
            string path = Path.Combine(package.Directory, "AppxManifest.xml");
            if (!File.Exists(path)) continue;
            using var reader = XmlReader.Create(path, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "Package" || root.Name.NamespaceName is not (Foundation or LegacyFoundation))
                throw new InvalidDataException("The registered package has an unknown manifest namespace.");
            XNamespace ns = root.Name.Namespace;
            var identity = root.Elements(ns + "Identity").SingleOrDefault();
            var applications = root.Elements(ns + "Applications").SingleOrDefault();
            var application = applications?.Elements(ns + "Application").SingleOrDefault(value => (string?)value.Attribute("Id") == parts[1]);
            if (identity is null || application is null) continue;
            string full = RegisteredUwpManifestProof.FullName(identity);
            string family = ((string?)identity.Attribute("Name") ?? "") + "_" + full.Split('_')[4];
            if (!full.Equals(package.FullName, StringComparison.OrdinalIgnoreCase) || !family.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The registered package identity changed before companion preparation.");
            found.Add(Classify(application));
        }
        return found.Count == 1 ? found[0] : throw new InvalidDataException("A unique registered application is required for companion preparation.");
    }
}
