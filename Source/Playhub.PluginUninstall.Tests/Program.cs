using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Playhub;
using Playhub.Models;
using Playhub.Services;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

await MainWindow.RunUninstallTestsAsync();

namespace Playhub
{
    public sealed partial class MainWindow
    {
        private readonly ObservableCollection<DeckyPluginInfo> _plugins = new();
        private readonly FakePluginService _pluginService = new();
        private readonly Settings _settings = new();
        private bool _pluginBulkUpdateRunning;
        private bool _pluginCardsDirty;
        private bool _pluginManagementDirty;
        private string _currentPageTag = "plugins";
        private string _pluginStoreMode = "manage";
        private string? _pluginPagePluginKey;
        private int _viewInvalidations, _featuredInvalidations, _renders, _detailsRefreshes, _detailsCloses;
        private static int _forbiddenStoreUiCalls;
        private sealed class Settings { public string DeckyPluginsPath => "fake-plugins"; }
        private static IDisposable BeginNotificationContext(string context) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
        private static string T(string text) => text;
        private static object StyleResource(string name) => name;
        private static void RegisterButton(Button button, bool primary) { }
        private static string PluginStoreKey(DeckyPluginInfo plugin) =>
            string.IsNullOrWhiteSpace(plugin.RepositoryName) ? plugin.Name : plugin.RepositoryName;
        // The real helper is parent-owned; this fixture exercises the calls at each guard.
        private static bool IsIntegratedGamingModePlugin(DeckyPluginInfo plugin) =>
            string.Equals(plugin.Name, "Gaming Mode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plugin.FolderName, "gaming-mode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(plugin.InstalledFolder)),
                "gaming-mode", StringComparison.OrdinalIgnoreCase);
        private static Task<bool> ConfirmAsync(string title, string confirmation)
        {
            _forbiddenStoreUiCalls++;
            throw new InvalidOperationException("Store confirmation is forbidden.");
        }
        private static void SetStatus(string message, InfoBarSeverity severity)
        {
            _forbiddenStoreUiCalls++;
            throw new InvalidOperationException("Store status popups are forbidden.");
        }
        private static string FriendlyError(Exception exception) => exception.Message;
        private static void MarkPluginInstalled(DeckyPluginInfo plugin)
        {
            plugin.IsInstalled = true;
            plugin.HasUpdate = false;
            plugin.InstalledVersion = plugin.Version;
        }
        private static Task CommitPluginInstallStateAsync(DeckyPluginInfo plugin) => Task.CompletedTask;
        // Any reintroduction of the old refresh makes the actual uninstall method fail these tests.
        private static Task RefreshPluginsAsync() => throw new InvalidOperationException("Catalog refresh is forbidden during uninstall.");
        private void InvalidatePluginAllViews() => _viewInvalidations++;
        private void InvalidateFeaturedFrames() => _featuredInvalidations++;
        private void RenderPluginManagementIfNeeded() { if (_pluginManagementDirty) _renders++; }
        private void RenderPluginCardsIfNeeded() { if (_pluginCardsDirty) _renders++; }
        private void RefreshOpenPluginPage() => _detailsRefreshes++;
        private void ClosePluginPage() { _detailsCloses++; _currentPageTag = "plugins"; }

        private static DeckyPluginInfo Plugin(string key = "fixture") => new()
        {
            Name = key, RepositoryName = key, IsInstalled = true, HasUpdate = true,
            InstalledVersion = "1.0.0", Version = "2.0.0", InstalledFolder = "fake-plugins/" + key
        };
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
        private static void Removed(DeckyPluginInfo plugin) => Check(!plugin.IsInstalled && !plugin.HasUpdate &&
            plugin.InstalledVersion == "" && plugin.InstalledFolder == "", "Installed metadata was not fully cleared.");
        private static ProgressRing Ring(Button button) => Descendants((UIElement)button.Content!).OfType<ProgressRing>().Single();
        private static string Tooltip(Button button) => (string?)ToolTipService.GetToolTip(button) ?? "";
        private static void InlineFailure(Button button, string error, bool enabled = true)
        {
            var icon = Descendants((UIElement)button.Content!).OfType<FontIcon>().Single();
            Check(icon.Glyph == ((char)0xE7BA).ToString() && icon.Visibility == Visibility.Visible &&
                !Ring(button).IsActive && button.IsEnabled == enabled && Tooltip(button).Contains(error) &&
                AutomationProperties.GetName(button).Contains(error), "Inline failure/retry state missing: " + error);
        }
        private static IEnumerable<UIElement> Descendants(UIElement element)
        {
            yield return element;
            if (element is Grid grid)
                foreach (var child in grid.Children)
                    foreach (var descendant in Descendants(child)) yield return descendant;
        }

        public static async Task RunUninstallTestsAsync()
        {
            await ServiceCommitAsync();
            await TemporaryDirectoryRemovalAsync();
            await LiveButtonsAsync();
            await FailureAndRetryAsync();
            await GuardsAsync();
            await LocalOnlyAndConcurrentAsync();
            await ReinstallVersionAsync();
            await IntegratedGamingModeGuardsAsync();
            WeakSubscriptions();
            Check(_forbiddenStoreUiCalls == 0, "Store action attempted a confirmation or status popup.");
            Console.WriteLine("PASS all 9 uninstall lifecycle suites (fake controls, no real processes/services; temporary fixture I/O only)");
        }

        private static async Task TemporaryDirectoryRemovalAsync()
        {
            const string prefix = "Playhub.PluginUninstall.Tests-";
            var root = Directory.CreateTempSubdirectory(prefix).FullName;
            var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            void VerifyRoot()
            {
                Check(string.Equals(Path.GetDirectoryName(Path.GetFullPath(root)), tempRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(root).StartsWith(prefix, StringComparison.Ordinal), "Unsafe fixture cleanup root.");
            }
            void VerifyChild(string path)
            {
                VerifyRoot();
                Check(Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    "Removal escaped the isolated fixture root.");
            }
            try
            {
                var folder = Path.Combine(root, "fixture");
                var nested = Path.Combine(folder, "dist");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(folder, "plugin.json"), "{\"name\":\"Temporary fixture\",\"version\":\"1.0.0\"}");
                File.WriteAllText(Path.Combine(nested, "index.js"), "fixture");
                var sibling = Path.Combine(root, "untouched.txt");
                File.WriteAllText(sibling, "sentinel");
                var plugin = Plugin();
                plugin.InstalledFolder = folder;
                var stopCalls = 0;
                await DeckyPluginService.UninstallWithProcessStopAsync(plugin, path =>
                {
                    VerifyChild(path);
                    Check(path == folder, "Wrong fixture directory.");
                    stopCalls++;
                });
                Check(stopCalls == 1 && !Directory.Exists(folder) && File.ReadAllText(sibling) == "sentinel",
                    "Real recursive deletion failed or touched a sibling.");
                Removed(plugin);
                plugin = Plugin();
                plugin.InstalledFolder = folder;
                await DeckyPluginService.UninstallWithProcessStopAsync(plugin,
                    _ => throw new Exception("Absent fixture directory should not stop any process."));
                Removed(plugin);

                Directory.CreateDirectory(folder);
                var lockedFile = Path.Combine(folder, "locked.bin");
                plugin = Plugin();
                plugin.InstalledFolder = folder;
                using (var locked = new FileStream(lockedFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    try
                    {
                        await DeckyPluginService.UninstallWithProcessStopAsync(plugin, VerifyChild);
                        throw new Exception("Locked fixture removal should fail on Windows.");
                    }
                    catch (IOException) { }
                    Check(plugin.IsInstalled && plugin.HasUpdate && plugin.InstalledVersion == "1.0.0" &&
                        plugin.InstalledFolder == folder, "Failed real deletion committed removed state.");
                }
                await DeckyPluginService.UninstallWithProcessStopAsync(plugin, VerifyChild);
                Check(!Directory.Exists(folder), "Retry after releasing fixture lock failed.");
                Removed(plugin);
                Console.WriteLine("PASS real recursive deletion, absent-directory idempotence, locked-file retries, sibling isolation");
            }
            finally
            {
                VerifyRoot();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        private static async Task ServiceCommitAsync()
        {
            var plugin = Plugin();
            var removal = new TaskCompletionSource();
            var task = DeckyPluginService.UninstallAsync(plugin, path =>
            {
                Check(path == "fake-plugins/fixture", "Wrong directory passed to removal.");
                return removal.Task;
            });
            Check(plugin.IsInstalled && plugin.InstalledVersion == "1.0.0", "State committed before removal finished.");
            removal.SetResult();
            await task;
            Removed(plugin);
            Check(plugin.Version == "2.0.0", "Catalog version was lost.");

            plugin = Plugin();
            var failure = new IOException("locked");
            try
            {
                await DeckyPluginService.UninstallAsync(plugin, _ => Task.FromException(failure));
                throw new Exception("Removal failure was swallowed.");
            }
            catch (IOException ex) { Check(ReferenceEquals(ex, failure), "Removal exception changed."); }
            Check(plugin.IsInstalled && plugin.HasUpdate && plugin.InstalledVersion == "1.0.0" &&
                plugin.InstalledFolder == "fake-plugins/fixture", "Failed removal changed installed metadata.");
            foreach (var path in new[] { "", "   " })
            {
                plugin.InstalledFolder = path;
                try
                {
                    await DeckyPluginService.UninstallAsync(plugin, _ => throw new Exception("Missing path reached removal."));
                    throw new Exception("Installed plugin without a path silently reported success.");
                }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("Percorso di installazione mancante") && ex.Message.Contains(plugin.Name),
                        "Missing-path failure was not actionable.");
                }
                Check(plugin.IsInstalled && plugin.HasUpdate && plugin.InstalledVersion == "1.0.0" &&
                    plugin.InstalledFolder == path, "Missing-path failure cleared installed metadata.");
            }
            plugin.IsInstalled = false;
            foreach (var path in new[] { "", "fake-plugins/stale-path" })
            {
                plugin.InstalledFolder = path;
                await DeckyPluginService.UninstallAsync(plugin, _ => throw new Exception("Already-uninstalled plugin reached removal."));
                Check(!plugin.IsInstalled, "Already-uninstalled no-op changed state.");
            }
            Console.WriteLine("PASS service commits only after success; missing installed path fails; already-uninstalled is a no-op");
        }

        private static async Task LiveButtonsAsync()
        {
            var window = new MainWindow();
            var plugin = Plugin();
            var current = Plugin("FIXTURE");
            current.Version = "2.1.0";
            var cached = Plugin();
            var installedButtonModel = Plugin();
            var other = Plugin("other");
            window._plugins.Add(current);
            window._plugins.Add(other);
            var first = window.BindPluginUninstallButton(new Button(), plugin, compact: true);
            var second = window.BindPluginUninstallButton(new Button(), cached, compact: false);
            var install = window.CreatePluginInstallButton(installedButtonModel, compact: true);
            var unrelated = window.BindPluginUninstallButton(new Button(), other, compact: true);
            var removal = new TaskCompletionSource();
            window._pluginService.Remove = _ =>
            {
                Check(window._pluginUninstalls.Contains("fixture") && Ring(first).IsActive && Ring(second).IsActive,
                    "Removal service was entered before all matching buttons became busy.");
                return removal.Task;
            };
            Task? clicked = null;
            first.Click += (_, _) => clicked = window.UninstallStorePluginAsync(plugin);
            first.RaiseClick();
            var task = clicked ?? throw new Exception("Trash click did not start uninstall.");
            Check(!task.IsCompleted && Ring(first).IsActive && Ring(second).IsActive &&
                !first.IsEnabled && !second.IsEnabled && !install.IsEnabled, "Matching buttons were not busy before awaiting removal.");
            Check(unrelated.IsEnabled && !Ring(unrelated).IsActive, "Unrelated plugin was blocked.");
            first.IsEnabled = true;
            Check(!first.IsEnabled, "Async button finally bypassed active uninstall guard.");
            install.IsEnabled = true;
            Check(!install.IsEnabled, "Management builder bypassed install-button uninstall guard.");
            var lateModel = Plugin();
            var late = window.BindPluginUninstallButton(new Button(), lateModel, compact: true);
            Check(Ring(late).IsActive && !late.IsEnabled, "New live view missed active uninstall.");
            for (var i = 0; i < 5; i++) late.RaiseLoaded();
            Check(window._pluginUninstallSubscribers["fixture"].Count == 4, "Loaded duplicated weak subscribers.");
            await window.UninstallStorePluginAsync(cached);
            Check(window._pluginService.RemoveCalls == 1, "Duplicate uninstall escaped guard.");
            Check(!await window.InstallPluginWithProgressAsync(installedButtonModel) && window._pluginService.InstallCalls == 0,
                "Installation ran during uninstall.");
            removal.SetResult();
            await task;
            foreach (var model in new[] { plugin, current, cached, lateModel, installedButtonModel }) Removed(model);
            Check(current.Version == "2.1.0" && other.InstalledVersion == "1.0.0", "Uninstall overwrote fresh or unrelated versions.");
            Check(!Ring(first).IsActive && !Ring(second).IsActive && !Ring(late).IsActive && install.IsEnabled,
                "Buttons did not settle after removal.");
            first.IsEnabled = true;
            Check(!first.IsEnabled, "Removed plugin uninstall button was re-enabled.");
            Check(window._plugins.Count == 2 && window._viewInvalidations == 1 && window._featuredInvalidations == 1 &&
                window._renders == 1 && window._detailsRefreshes == 1 && window._pluginUninstalls.Count == 0,
                "Local commit did not invalidate store, management, and detail state.");
            Console.WriteLine("PASS immediate trash-click removal, matching spinners before first await, local commit without refresh/dialogs");
        }

        private static async Task FailureAndRetryAsync()
        {
            var window = new MainWindow();
            var plugin = Plugin();
            window._plugins.Add(plugin);
            var first = window.BindPluginUninstallButton(new Button(), plugin, compact: true);
            var second = window.BindPluginUninstallButton(new Button(), Plugin(), compact: false);
            var failure = new IOException("locked");
            window._pluginService.Remove = _ => Task.FromException(failure);
            try
            {
                await window.UninstallStorePluginAsync(plugin);
                throw new Exception("UI swallowed removal error.");
            }
            catch (IOException ex) { Check(ReferenceEquals(ex, failure), "UI changed removal error."); }
            Check(plugin.IsInstalled && first.IsEnabled && second.IsEnabled && !Ring(first).IsActive &&
                !Ring(second).IsActive && window._pluginUninstalls.Count == 0 && window._viewInvalidations == 0,
                "Failure left stale busy/installed state.");
            InlineFailure(first, "locked");
            InlineFailure(second, "locked");
            var late = window.BindPluginUninstallButton(new Button(), Plugin("FIXTURE"), compact: false);
            InlineFailure(late, "locked");
            first.RaiseLoaded();
            InlineFailure(first, "locked");
            var unrelated = window.BindPluginUninstallButton(new Button(), Plugin("other"), compact: true);
            Check(!Tooltip(unrelated).Contains("locked"), "Failure leaked to an unrelated plugin.");
            var retry = new TaskCompletionSource();
            window._pluginService.Remove = _ => retry.Task;
            var retryTask = window.UninstallStorePluginAsync(plugin);
            Check(Ring(first).IsActive && Ring(second).IsActive && Ring(late).IsActive &&
                !Tooltip(first).Contains("locked") && !window._pluginUninstallErrors.ContainsKey("fixture"),
                "Retry did not clear old failure and restore busy state.");
            retry.SetResult();
            await retryTask;
            Removed(plugin);
            Check(window._pluginService.RemoveCalls == 2, "Retry was blocked after failure.");
            Check(!Tooltip(late).Contains("locked"), "Successful retry left a stale error.");
            await InstallFailureAndRetryAsync(bulkOperation: false);
            await InstallFailureAndRetryAsync(bulkOperation: true);
            await MissingPathFailureAsync();
            Console.WriteLine("PASS install/uninstall failures remain inline across cached/new views; retry clears errors without popups");
        }

        private static async Task MissingPathFailureAsync()
        {
            var window = new MainWindow();
            var plugin = Plugin();
            plugin.InstalledFolder = "";
            window._plugins.Add(plugin);
            var button = window.BindPluginUninstallButton(new Button(), plugin, compact: false);
            try
            {
                await window.UninstallStorePluginAsync(plugin);
                throw new Exception("Missing installed path reported successful uninstall.");
            }
            catch (InvalidOperationException) { }
            InlineFailure(button, "Percorso di installazione mancante");
            Check(plugin.IsInstalled && plugin.InstalledVersion == "1.0.0" && window._pluginService.RemoveCalls == 0 &&
                window._viewInvalidations == 0 && window._pluginUninstalls.Count == 0,
                "Missing installed path triggered removal, state commit, or a stuck guard.");
            plugin.InstalledFolder = "fake-plugins/fixture";
            window._pluginService.Remove = _ => Task.CompletedTask;
            await window.UninstallStorePluginAsync(plugin);
            Removed(plugin);
            Check(!Tooltip(button).Contains("mancante"), "Retry after path repair kept a stale failure.");
        }

        private static async Task InstallFailureAndRetryAsync(bool bulkOperation)
        {
            var window = new MainWindow { _pluginBulkUpdateRunning = bulkOperation };
            var plugin = Plugin();
            var cached = Plugin("FIXTURE");
            if (!bulkOperation)
            {
                DeckyPluginService.MarkPluginUninstalled(plugin);
                DeckyPluginService.MarkPluginUninstalled(cached);
            }
            window._plugins.Add(plugin);
            var first = window.CreatePluginInstallButton(plugin, compact: true);
            var second = window.CreatePluginInstallButton(cached, compact: false, slim: true);
            var failure = new IOException("download failed");
            window._pluginService.Install = _ => Task.FromException(failure);
            Check(!await window.InstallPluginWithProgressAsync(plugin, bulkOperation), "Failed installation reported success.");
            InlineFailure(first, "download failed", enabled: !bulkOperation);
            InlineFailure(second, "download failed", enabled: !bulkOperation);
            window._pluginBulkUpdateRunning = false;
            first.RaiseLoaded();
            second.RaiseLoaded();
            InlineFailure(first, "download failed");
            InlineFailure(second, "download failed");
            var late = window.CreatePluginInstallButton(plugin, compact: false);
            InlineFailure(late, "download failed");
            var unrelated = window.CreatePluginInstallButton(Plugin("other"), compact: true);
            Check(!Tooltip(unrelated).Contains("download failed"), "Installation failure leaked to unrelated plugin.");
            Check(window._pluginInstallOperations.Count == 0, "Failed install did not release its guard.");
            var retry = new TaskCompletionSource();
            window._pluginService.Install = async model =>
            {
                await retry.Task;
                model.InstalledFolder = "fake-plugins/fixture";
            };
            var retryTask = window.InstallPluginWithProgressAsync(plugin);
            Check(Ring(first).IsActive && Ring(second).IsActive && Ring(late).IsActive &&
                !Tooltip(first).Contains("download failed") && !window._pluginInstallErrors.ContainsKey("fixture"),
                "Installation retry did not clear failure or update every button.");
            retry.SetResult();
            Check(await retryTask && plugin.IsInstalled && cached.IsInstalled && window._pluginService.InstallCalls == 2,
                "Installation retry did not commit installed state.");
            Check(!Tooltip(late).Contains("download failed") && !Ring(first).IsActive && !first.IsEnabled,
                "Successful installation retry left stale error/progress state.");
        }

        private static async Task GuardsAsync()
        {
            foreach (var guard in new[] { "bulk", "install", "uninstall", "removed", "integrated" })
            {
                var window = new MainWindow();
                var plugin = Plugin();
                if (guard == "bulk") window._pluginBulkUpdateRunning = true;
                if (guard == "install") window._pluginInstallOperations.Add("fixture", new() { Plugin = plugin });
                if (guard == "uninstall") window._pluginUninstalls.Add("fixture");
                if (guard == "removed") DeckyPluginService.MarkPluginUninstalled(plugin);
                if (guard == "integrated") plugin.Name = "Gaming Mode";
                await window.UninstallStorePluginAsync(plugin);
                Check(window._pluginService.RemoveCalls == 0 && window._pluginUninstalls.Count == (guard == "uninstall" ? 1 : 0),
                    "Immediate uninstall bypassed or removed an existing guard: " + guard);
            }
            Console.WriteLine("PASS immediate uninstall preserves bulk/install/uninstall/integrated-component guards");
        }

        private static async Task LocalOnlyAndConcurrentAsync()
        {
            var window = new MainWindow { _currentPageTag = "plugin-detail", _pluginPagePluginKey = "local" };
            var local = Plugin("local");
            local.CatalogSource = "installed";
            window._plugins.Add(local);
            window._pluginService.Remove = _ => Task.CompletedTask;
            await window.UninstallStorePluginAsync(local);
            Check(window._plugins.Count == 0 && window._detailsCloses == 1 && window._renders == 1,
                "Last local-only removal left stale detail or management view.");

            window = new MainWindow();
            var first = Plugin("first");
            var second = Plugin("second");
            window._plugins.Add(first);
            window._plugins.Add(second);
            var firstRemoval = new TaskCompletionSource();
            var secondRemoval = new TaskCompletionSource();
            window._pluginService.Remove = path => path.EndsWith("first") ? firstRemoval.Task : secondRemoval.Task;
            var firstTask = window.UninstallStorePluginAsync(first);
            var secondTask = window.UninstallStorePluginAsync(second);
            firstRemoval.SetResult();
            await firstTask;
            var rebuilt = window.BindPluginUninstallButton(new Button(), second, compact: false);
            Check(Ring(rebuilt).IsActive && !rebuilt.IsEnabled && window._pluginUninstalls.Count == 1,
                "First completion cleared another uninstall's busy state.");
            secondRemoval.SetResult();
            await secondTask;
            Removed(first);
            Removed(second);
            Console.WriteLine("PASS local-only detail exit, empty management, independent concurrent removals");
        }

        private static async Task ReinstallVersionAsync()
        {
            var window = new MainWindow();
            var plugin = Plugin();
            var cached = Plugin();
            window._plugins.Add(plugin);
            var uninstall = window.BindPluginUninstallButton(new Button(), cached, compact: false);
            window._pluginService.Remove = _ => Task.CompletedTask;
            await window.UninstallStorePluginAsync(plugin);
            var installation = new TaskCompletionSource();
            window._pluginService.Install = async model =>
            {
                await installation.Task;
                model.Version = "3.2.1";
                model.InstalledVersion = "3.2.1";
                model.InstalledFolder = "fake-plugins/new-folder";
            };
            var installTask = window.InstallPluginWithProgressAsync(plugin);
            Check(!uninstall.IsEnabled && !Ring(uninstall).IsActive, "Uninstall not disabled for install operation.");
            installation.SetResult();
            Check(await installTask, "Fake reinstall failed.");
            Check(uninstall.IsEnabled && cached.IsInstalled && cached.InstalledVersion == "3.2.1" &&
                cached.Version == "3.2.1" && cached.InstalledFolder == "fake-plugins/new-folder",
                "Cached uninstall view retained old installed version/folder after reinstall.");
            Console.WriteLine("PASS fresh installed version/folder reaches cached uninstall views after reinstall");
        }

        private static async Task IntegratedGamingModeGuardsAsync()
        {
            foreach (var identity in new[] { "name", "folder", "installed-folder" })
            {
                var window = new MainWindow();
                var plugin = Plugin();
                if (identity == "name") plugin.Name = "Gaming Mode";
                if (identity == "folder") plugin.FolderName = "gaming-mode";
                if (identity == "installed-folder") plugin.InstalledFolder = "fake-plugins/gaming-mode";
                var uninstall = window.BindPluginUninstallButton(new Button(), plugin, compact: true);
                var install = window.CreatePluginInstallButton(plugin, compact: true);
                uninstall.IsEnabled = install.IsEnabled = true;
                Check(!uninstall.IsEnabled && !install.IsEnabled, "Integrated component exposed enabled management buttons.");
                await window.UninstallStorePluginAsync(plugin);
                Check(!await window.InstallPluginWithProgressAsync(plugin), "Integrated component allowed normal install.");
                Check(!await window.InstallPluginWithProgressAsync(plugin, bulkOperation: true), "Integrated component allowed bulk install.");
                Check(window._pluginService.RemoveCalls == 0 && window._pluginService.InstallCalls == 0,
                    "Integrated component reached a service.");
            }
            Console.WriteLine("PASS parent integrated-component guard protects uninstall/install/bulk operations and button state");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<Button> AddDiscardedButton(MainWindow window) =>
            new(window.BindPluginUninstallButton(new Button(), Plugin(), compact: true));

        private static void WeakSubscriptions()
        {
            var window = new MainWindow();
            var weak = AddDiscardedButton(window);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check(!weak.TryGetTarget(out _), "Weak subscribers retained discarded button.");
            window.PublishPluginUninstallState("fixture");
            window.PublishPluginInstallProgress("fixture", null);
            Check(!window._pluginUninstallSubscribers.ContainsKey("fixture") && !window._pluginInstallSubscribers.ContainsKey("fixture"),
                "Dead weak subscriber buckets were not pruned.");
            Console.WriteLine("PASS weak subscriptions release discarded buttons");
        }
    }
}
