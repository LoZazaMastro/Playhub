// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using Playhub.StoreLibrary;

namespace Playhub.GameSession;

internal static class StoreLibraryGameLaunch
{
    internal static int Run(StoreGameKey key)
    {
        var service = (StoreLibraryService)StoreLibraryService.CreateDefault();
        return Run(key, () => service.GetVerifiedLaunchDescriptorAsync(key).GetAwaiter().GetResult(),
            () => service.RequestInstallUiAsync(key).GetAwaiter().GetResult(),
            () => service.PreparePrivateLaunchAsync(key).GetAwaiter().GetResult(), RunPrepared);
    }

    internal static int Run(StoreGameKey key, Func<StoreLaunchDescriptor> describe,
        Func<StoreUiOpenResult> requestUi, Func<StorePrivateLaunchPlan> prepare, Func<StorePrivateLaunchPlan, int> launch)
    {
        var installed = describe();
        if (installed.Key != key) throw new InvalidDataException("The store could not confirm this game.");
        if (!installed.IsInstalled || !installed.IsLaunchable)
        {
            var opened = requestUi();
            if (!opened.Accepted) throw new InvalidOperationException("Open Digital Stores in Steam to install or manage this game.");
            return 0;
        }
        // Account exchange material is fetched only after an explicit launch.
        // The returned plan is private, one-shot and absent from all UI snapshots.
        var plan = prepare();
        var now = DateTimeOffset.UtcNow;
        if (plan.Key != key || plan.LaunchId == Guid.Empty || plan.ExpiresAt <= now || plan.ExpiresAt > now.AddSeconds(35) ||
            !Path.IsPathFullyQualified(plan.GameExecutable) || !File.Exists(plan.GameExecutable) ||
            !Path.GetExtension(plan.GameExecutable).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(plan.WorkingDirectory) || !Directory.Exists(plan.WorkingDirectory) ||
            plan.Arguments is null || plan.Arguments.Count > 256 || plan.Arguments.Any(a => a is null || a.Contains('\0')) ||
            plan.Environment is null || string.Join(" ", plan.Arguments.Select(GameLifetime.Quote)).Length > 30000)
            throw new InvalidDataException("The store could not prepare this game.");
        return launch(plan);
    }

    static int RunPrepared(StorePrivateLaunchPlan plan)
    {
        if (plan.XboxAumid is not null)
            return StoreXboxUwpLaunch.Run(plan, new WindowsUwpRuntime());
        if (plan.Key.Store == StoreKind.Xbox) throw new InvalidDataException("The Xbox registration was not verified.");
        // An official Launcher install has no private CLI installed.json.
        // Reuse the native route in this process, without a child wrapper.
        var official = StoreOfficialEpicLaunch.TryRun(plan, installed =>
        {
            using var runtime = new WindowsEpicProtocolRuntime(installed);
            return EpicProtocolSession.Run(installed, runtime);
        });
        if (official is int exitCode) return exitCode;
        // Steam launches this process; the game remains its direct child just as
        // in the existing Win32 path. Curtain, input and overlay ownership stay intact.
        return GameLifetime.Run(plan.GameExecutable, plan.Arguments, plan.WorkingDirectory,
            steamCompanion: true, environment: plan.Environment);
    }
}
