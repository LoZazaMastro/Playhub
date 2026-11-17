// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using Playhub.StoreLibrary;

namespace Playhub.GameSession;

internal static class StoreXboxUwpLaunch
{
    internal static int Run(StorePrivateLaunchPlan plan, IUwpRuntime runtime)
    {
        if (plan.Key.Store != StoreKind.Xbox || plan.XboxAumid is null || plan.Arguments.Count != 0 || plan.Environment.Count != 0)
            throw new InvalidDataException("The Xbox activation plan was not verified.");
        string relative = Path.GetRelativePath(plan.WorkingDirectory, plan.GameExecutable);
        UwpCompanionPreparation.ValidateApplication(plan.XboxAumid, relative);
        // This is the same registered activation/lifetime/Steam companion path
        // used by existing imported UWP and verified GDK games. Never invoke the
        // protected game executable as an ordinary Win32 process or use explorer.
        return UwpLifetime.Run(plan.XboxAumid, relative, "", runtime, UwpStoreCompanionBinding.Create(plan, DateTimeOffset.UtcNow));
    }
}
