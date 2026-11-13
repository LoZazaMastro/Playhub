using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using GamingMode.Services;
using Playhub.GameSession;
using Playhub.Shared;

internal static class RegisteredDesktopAssociationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var owner = Process.GetCurrentProcess();
        uint wrapperPid = (uint)owner.Id;
        long wrapperBirth = owner.StartTime.ToUniversalTime().ToFileTimeUtc();
        var images = new UwpSteamSessionAssociation.InstalledImages(@"F:\Fixture\Playhub\Playhub.GameSession.exe",
            @"F:\Fixture\Playhub\Playhub.XboxSession.exe");
        foreach (var game in new[] { ("Fixture.Doom_abcdef!Game", "DOOM64_x64.exe"), ("Fixture.Outer_abcdef!Game", "OuterWilds.exe") })
        {
            string family = game.Item1.Split('!')[0];
            string image = Path.Combine(@"F:\Fixture\XboxGames\Content", game.Item2);
            uint gamePid = 200;
            long gameBirth = wrapperBirth + TimeSpan.FromSeconds(1).Ticks;
            const uint appId = 0xF0010203;
            ulong steamId = ((ulong)appId << 32) | 0x02000000;
            var identity = new UwpSessionIdentity(wrapperPid, wrapperBirth, gamePid, gameBirth, game.Item1, family, image);
            var tracked = new UwpSteamSessionAssociation.Tracked(wrapperPid, steamId,
                DateTime.FromFileTimeUtc(wrapperBirth).ToLocalTime(), images.Wrapper, game.Item1, game.Item2);
            var runtime = new Runtime(game.Item1, family, game.Item2, () =>
            {
                using var timeout = new CancellationTokenSource(1500);
                using var pipe = new NamedPipeClientStream(".", UwpSessionIdentity.PipeName(wrapperPid, wrapperBirth),
                    PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
                using var reader = new StreamReader(pipe);
                var received = JsonSerializer.Deserialize<UwpSessionIdentity>(reader.ReadLineAsync(timeout.Token).GetAwaiter().GetResult()!)!;
                check(received == identity, "Registered desktop fixture publishes the real game rather than GameLaunchHelper through the production identity transport");
                bool Match(UwpSessionIdentity value, string observedImage, uint observedPid = 200, long? observedBirth = null) =>
                    UwpSteamSessionAssociation.Matches(tracked, value, wrapperPid, observedPid, appId,
                        wrapperBirth, observedBirth ?? gameBirth, images.Wrapper, family, observedImage, images);
                check(Match(received, image), "Overlay and HUD association accept the registered desktop game's exact wrapper, PFN, image and lifetime");
                check(!Match(received, Path.Combine(Path.GetDirectoryName(image)!, "GameLaunchHelper.exe")),
                    "The activation helper cannot replace the game in the overlay association");
                check(!Match(received, image, observedBirth: gameBirth + 1) && !Match(received, image, observedPid: 201),
                    "Recycled or different desktop game lifetimes cannot reuse the association");
            }, () => new UwpIdentityPublication(identity));
            check(UwpLifetime.Run(game.Item1, game.Item2, "--original-user-argument", runtime) == 42
                && runtime.Activations == 1 && runtime.Handoffs == 1 && runtime.Publications == 1 && runtime.Disposed,
                "Registered desktop fixture retains one original Windows activation, follows helper handoff and cleans its identity at exact game exit");
        }
    }

    // No Windows activation is invoked. Only the production lifetime and private
    // identity pipe run; helper/game identities are deliberately inert fixtures.
    private sealed class Runtime(string aumid, string family, string executable, Action verify, Func<IDisposable> publish) : IUwpRuntime
    {
        public long Now { get; private set; }
        public bool ShellReady => true;
        public int Activations, Handoffs, Publications;
        public bool Disposed;
        public void StartShell() => throw new Exception("Unexpected shell mutation");
        public void Delay(int milliseconds) => Now += milliseconds;
        public void Log(string message) { }
        public IUwpProcess Activate(string requested, string arguments)
        {
            if (requested != aumid || arguments != "--original-user-argument") throw new Exception("Original activation changed");
            Activations++;
            return new ProcessFixture("GameLaunchHelper.exe", family, () => throw new Exception("Helper was mistaken for game"));
        }
        public IUwpProcess? FindGame(string requestedFamily, string expected)
        {
            if (requestedFamily != family || expected != executable) throw new Exception("Game identity changed");
            Handoffs++;
            return new ProcessFixture(executable, family, () => { verify(); return 42; });
        }
        public IDisposable? PublishIdentity(string requested, IUwpProcess game)
        {
            if (requested != aumid || game.Executable != executable || game.PackageFamily != family)
                throw new Exception("Wrong desktop identity publication");
            Publications++;
            return new Publication(publish(), () => Disposed = true);
        }
    }
    private sealed class ProcessFixture(string executable, string family, Func<int> wait) : IUwpProcess
    {
        public uint Id => 200;
        public string Executable => executable;
        public string PackageFamily => family;
        public int WaitForExit() => wait();
        public void Dispose() { }
    }
    private sealed class Publication(IDisposable inner, Action disposed) : IDisposable
    {
        public void Dispose() { inner.Dispose(); disposed(); }
    }
}
