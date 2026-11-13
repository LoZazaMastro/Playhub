using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class Win32CompanionChecks
{
    internal static void Run(Action<bool, string> check, Action<Action, string> denied)
    {
        var layout = new SteamControllerLayoutTrial.Win32LayoutTransition();
        check(!layout.Observe(false, true) && !layout.NeedsExitRestore,
            "Win32 input layout is untouched before an owned overlay is observed");
        check(!layout.Observe(true, false) && layout.NeedsExitRestore && !layout.Observe(true, false),
            "Win32 overlay keeps its menu layout while the companion owns input");
        check(!layout.Observe(false, false) && layout.NeedsExitRestore,
            "Win32 layout restore waits through a foreign foreground");
        check(layout.Observe(false, true) && !layout.Observe(false, true),
            "Win32 overlay close requests one layout release on exact game foreground");
        layout.Restored(true);
        check(!layout.NeedsExitRestore, "A dispatched Win32 layout release needs no duplicate exit reset");
        layout.Observe(true, false);
        check(layout.NeedsExitRestore && !layout.Observe(false, false),
            "Exit while an owned overlay is closing retains one automatic reset obligation");
        layout.Observe(false, true);
        layout.Restored(false);
        check(layout.NeedsExitRestore && !layout.Observe(false, true),
            "A refused Win32 layout dispatch waits before retry and retains exact exit cleanup");
        check(!layout.Observe(false, true, 499) && !layout.Observe(false, false, 500)
            && layout.Observe(false, true, 500),
            "The pending game layout retries only after 500 ms on the exact foreground game");
        layout.Restored(false, 500);
        check(!layout.Observe(false, true, 999) && layout.Observe(false, true, 1000),
            "A second refused dispatch gets one final scoped retry");
        layout.Restored(false, 1000);
        check(!layout.Observe(false, true, 1500) && !layout.Observe(false, true, 30000)
            && layout.NeedsExitRestore,
            "Repeated command refusal is bounded to three attempts and preserves exit cleanup");
        layout.Observe(true, false, 30010);
        check(layout.Observe(false, true, 30020), "A new observed overlay close starts a fresh layout restoration");
        layout.Restored(true, 30021);
        check(!layout.NeedsExitRestore && !layout.Observe(false, true, 40000),
            "Successful retry releases the override without repeating Steam commands");
        using var parent = Process.GetCurrentProcess();
        long parentBirth = parent.StartTime.ToFileTimeUtc();
        const ulong gameId = ((ulong)0xf0000001 << 32) | 0x02000000;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--fixture-win32-child");
        using var child = Process.Start(start)!;
        try
        {
            long birth = child.StartTime.ToFileTimeUtc();
            var handoff = new Win32CompanionHandoff((uint)parent.Id, parentBirth, (uint)child.Id, birth, gameId,
                Environment.ProcessPath!);
            using (var exact = Win32SteamCompanion.Inspect(handoff, parent, gameId, parent.SessionId))
                check(!exact.IsInvalid && !child.HasExited, "Win32 companion retains its exact original direct child by image, birth, user and session");
            denied(() => Win32SteamCompanion.Inspect(handoff with { GameBirth = birth - 1 }, parent, gameId, parent.SessionId).Dispose(),
                "Win32 companion refuses a recycled or mismatched process birth");
            denied(() => Win32SteamCompanion.Inspect(handoff with { Executable = Path.Combine(AppContext.BaseDirectory, "another.exe") }, parent, gameId, parent.SessionId).Dispose(),
                "Win32 companion refuses a different original image");
            denied(() => Win32SteamCompanion.Inspect(handoff, parent, gameId + (1UL << 32), parent.SessionId).Dispose(),
                "Win32 companion refuses another Steam shortcut identity");
            denied(() => Win32SteamCompanion.Inspect(handoff, parent, gameId, parent.SessionId + 1).Dispose(),
                "Win32 companion refuses another Windows session");
            check(!handoff.Matches((uint)parent.Id, parentBirth, gameId, handoff.GamePid, birth,
                    handoff.Executable, birth + TimeSpan.FromSeconds(16).Ticks), "Win32 companion refuses stale handoffs");
            check(!handoff.Matches((uint)parent.Id + 1, parentBirth, gameId, handoff.GamePid, birth,
                    handoff.Executable, birth), "Win32 companion refuses another wrapper owner");
            string payload = JsonSerializer.Serialize(handoff) + "\n";
            using var serialized = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            check(Win32SteamCompanion.ReadHandoff(serialized).GetAwaiter().GetResult() == handoff,
                "Win32 private handoff round trip preserves exact original process and executable");
            using var empty = new MemoryStream();
            check(Win32SteamCompanion.ReadHandoff(empty).GetAwaiter().GetResult() is null,
                "Closed Win32 handoff cancels without selecting a target");
            foreach (string suffix in new[] { "trailing", payload })
            {
                using var extra = new MemoryStream(Encoding.UTF8.GetBytes(payload + suffix));
                denied(() => Win32SteamCompanion.ReadHandoff(extra).GetAwaiter().GetResult(),
                    "Win32 private handoff refuses extra data and duplicate messages");
            }
            using var incomplete = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(handoff)));
            denied(() => Win32SteamCompanion.ReadHandoff(incomplete).GetAwaiter().GetResult(),
                "Win32 private handoff requires a complete bounded message");
        }
        finally { child.WaitForExit(5000); }
    }
}
