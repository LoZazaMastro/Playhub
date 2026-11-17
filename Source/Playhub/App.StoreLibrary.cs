// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using Playhub.StoreLibrary;
using Playhub.Services;

namespace Playhub;

public partial class App
{
    private async Task HandleStoreLibraryActivationAsync(string? arguments)
    {
        try
        {
            var parsed = string.IsNullOrWhiteSpace(arguments)
                ? Environment.GetCommandLineArgs().Skip(1).ToList() : CommandLine.Parse(arguments);
            if (_window is MainWindow stores && parsed.Count == 1 && parsed[0] == "--digital-stores")
            { stores.ShowDigitalStores(); return; }
            if (_window is MainWindow window && StoreLibraryLaunchRequest.TryParse(parsed, "--store-install", out var key))
                await window.ShowStoreLibraryInstallAsync(key!.Store, key.Id);
        }
        catch (Exception error) { Diag.Crash("Store library activation", error); }
    }
}
