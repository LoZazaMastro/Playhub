// Included only by the owned session fixture projects.
using Playhub.GameSession;

internal sealed class SessionFixtureLogs : IDisposable
{
    private readonly string _directory;
    private readonly string _marker;
    private SessionFixtureLogs(string directory, string marker) => (_directory, _marker) = (directory, marker);

    internal static SessionFixtureLogs Start()
    {
        string marker = "playhub-session-fixture-" + Guid.NewGuid().ToString("N");
        string directory = Directory.CreateTempSubdirectory(marker + "-").FullName;
        var fixture = new SessionFixtureLogs(directory, marker);
        UwpSessionLog.FixtureDirectory = directory;
        UwpSessionLog.Write(marker);
#if PLAYHUB_XBOX_FIXTURE
        WSGM.PackagedLaunch.PackagedLaunchLog.FixtureDirectory = directory;
        WSGM.PackagedLaunch.PackagedLaunchLog.Info(marker);
#endif
        fixture.Verify();
        return fixture;
    }

    private void Verify()
    {
        if (!File.ReadAllText(Path.Combine(_directory, "game-session.log")).Contains(_marker, StringComparison.Ordinal))
            throw new InvalidOperationException("The UWP fixture did not use its owned log destination.");
#if PLAYHUB_XBOX_FIXTURE
        if (!File.ReadAllText(Path.Combine(_directory, "xbox-session.log")).Contains(_marker, StringComparison.Ordinal))
            throw new InvalidOperationException("The Xbox fixture did not use its owned log destination.");
#endif
        string original = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "logs");
        foreach (string name in new[] { "game-session.log", "xbox-session.log" })
        {
            string path = Path.Combine(original, name);
            if (!File.Exists(path)) continue;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Position = Math.Max(0, stream.Length - 2 * 1024 * 1024);
            using var reader = new StreamReader(stream);
            if (reader.ReadToEnd().Contains(_marker, StringComparison.Ordinal))
                throw new InvalidOperationException("A session fixture polluted the real Playhub log.");
        }
    }

    public void Dispose()
    {
        try { Verify(); }
        finally
        {
            UwpSessionLog.FixtureDirectory = null;
#if PLAYHUB_XBOX_FIXTURE
            WSGM.PackagedLaunch.PackagedLaunchLog.FixtureDirectory = null;
#endif
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(_directory);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith(_marker + "-", StringComparison.Ordinal))
                throw new InvalidOperationException("The fixture log cleanup path changed.");
            Directory.Delete(target, recursive: true);
        }
    }
}
