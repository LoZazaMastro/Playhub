// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Playhub.Services;
using Playhub.StoreLibrary;
using Playhub.Integrations;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private StoreAccountsPage? _storeLibraryPage;
    private UIElement BuildStoreLibraryPage()
    {
        var panel = Page("digital-stores", "StoreLibrary.Stores", "StoreLibrary.Tagline");
        var service = StoreLibraryService.CreateDefault();
        var controller = new StoreLibraryController(service);
        _storeLibraryPage = new StoreAccountsPage(controller, service as IStoreXboxAccountService, T, _settings.Language);
        panel.Children.Add(_storeLibraryPage);
        RenderStoreMigration();
        Closed += (_, _) => _storeLibraryPage.Dispose();
        return panel;
    }
    private void SetStoreLibraryActive(string tag)
    {
        _storeLibraryPage?.Relocalize(_settings.Language);
        _storeLibraryPage?.SetActive(tag == "digital-stores");
    }
    internal Task<bool> ShowStoreLibraryInstallAsync(StoreKind store, string productId)
    {
        if (!Enum.IsDefined(store) || string.IsNullOrWhiteSpace(productId) || productId.Length > 512 || productId.Any(char.IsControl)) return Task.FromResult(false);
        ShowDigitalStores();
        // Old activation links cannot reopen the removed desktop installation UI.
        return Task.FromResult(false);
    }
    internal void ShowDigitalStores()
    {
        var navigationItem = _navigation?.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, "digital-stores"));
        if (navigationItem is not null) _navigation!.SelectedItem = navigationItem;
        ShowPage("digital-stores");
    }
}
