using Playhub.Models;
using Playhub.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

internal static class StoreRefreshTests
{
    internal static Task RunAsync()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        DeckyPluginInfo Plugin(string id, string version = "1") => new()
        { Name = id, FolderName = id, RepositorySlug = "fixture/" + id, Version = version, ShortDescription = "Original publisher text" };
        var first = Plugin("first"); var second = Plugin("second");
        var data = new ObservableCollection<DeckyPluginInfo> { first, second };
        int resets = 0, changes = 0; bool emptied = false;
        data.CollectionChanged += (_, e) => { changes++; if (e.Action == NotifyCollectionChangedAction.Reset) resets++; emptied |= data.Count == 0; };
        for (int repeat = 0; repeat < 6; repeat++)
        {
            var same = PluginCatalogReconciliation.Apply(data, new[] { Plugin("first"), Plugin("second") }, PluginChronology.Identity);
            Check(!same.HasChanges && ReferenceEquals(same.Plugins[0], first), "Identical refresh discarded a retained model.");
            PluginCatalogReconciliation.Synchronize(data, same.Plugins);
        }
        Check(changes == 0 && resets == 0, "Identical refresh notified the collection.");
        first.ReleaseZipUrl = "https://fixture.invalid/old.zip";
        var routing = Plugin("first"); routing.ReleaseZipUrl = "https://fixture.invalid/current.zip";
        var routingChange = PluginCatalogReconciliation.Apply(data, new[] { routing, Plugin("second") }, PluginChronology.Identity);
        Check(!routingChange.HasChanges && first.ReleaseZipUrl == routing.ReleaseZipUrl,
            "An updated download route either remained stale or rebuilt identical presentation.");
        Func<string> installAction = () => first.Version;
        var updated = Plugin("first", "2"); updated.HasUpdate = true; updated.ReleaseNotes = "Developer release notes";
        var delta = PluginCatalogReconciliation.Apply(data, new[] { updated, Plugin("second") }, PluginChronology.Identity);
        Check(delta.Changed.SetEquals(new[] { PluginChronology.Identity(first) }) && !delta.MembershipChanged,
            "Metadata change included an unrelated identity.");
        Check(ReferenceEquals(delta.Plugins[0], first) && ReferenceEquals(delta.Plugins[1], second) &&
            installAction() == "2" && first.HasUpdate && first.ReleaseNotes == updated.ReleaseNotes,
            "A retained action/model did not receive the actual current release data.");
        var extra = Plugin("extra");
        var membership = PluginCatalogReconciliation.Apply(data, new[] { extra, Plugin("second"), updated }, PluginChronology.Identity);
        PluginCatalogReconciliation.Synchronize(data, membership.Plugins);
        Check(membership.MembershipChanged && data.SequenceEqual(new[] { extra, second, first }) && resets == 0 && !emptied,
            "Insert/move caused a reset or emptied nonempty results.");
        PluginCatalogReconciliation.Synchronize(data, new[] { first, second });
        Check(data.SequenceEqual(new[] { first, second }) && resets == 0 && !emptied, "Removal lost retained models or emptied the page.");
        var before = PluginCatalogReconciliation.Snapshot(first);
        try { PluginCatalogReconciliation.Apply(data, new[] { Plugin("first", "99"), Plugin("first", "100") }, PluginChronology.Identity); throw new InvalidOperationException("Duplicate accepted"); }
        catch (ArgumentException) { Check(PluginCatalogReconciliation.Snapshot(first) == before, "Ambiguous source partially mutated an active object."); }
        first.Readme = "Enriched original README"; first.LongDescription = "Enriched developer copy";
        first.Media.Add(new() { Url = "https://fixture.invalid/preview.png", Kind = "image" });
        var enriched = PluginCatalogReconciliation.Apply(data, new[] { updated, Plugin("second") }, PluginChronology.Identity);
        Check(!enriched.HasChanges && first.Readme == "Enriched original README" && first.Media.Count == 1,
            "An offline baseline erased already enriched repository details.");
        var rows = PluginCatalogReconciliation.Rows(Array.Empty<IReadOnlyList<DeckyPluginInfo>>(), new[] { first, second, extra }, 2);
        var reordered = PluginCatalogReconciliation.Rows(rows, new[] { extra, first, second }, 1);
        Check(reordered.Count == 3 && reordered[0][0] == extra, "New row grouping is incorrect.");
        var unchangedRows = PluginCatalogReconciliation.Rows(rows, new[] { first, second, extra }, 2);
        Check(ReferenceEquals(rows[0], unchangedRows[0]) && ReferenceEquals(rows[1], unchangedRows[1]), "Unchanged row groups were reallocated.");

        // Exercise the same deferred presenter controller used by native slots.
        var pending = new Queue<Action>(); bool loaded = true, focused = false, accepting = true, fail = false;
        string desired = "initial", presented = desired; int builds = 0, errors = 0;
        Action? duringBuild = null;
        var refresh = new PluginViewRefresh(() => loaded && !focused, () => desired, snapshot =>
        {
            builds++; duringBuild?.Invoke(); duringBuild = null;
            if (fail) throw new InvalidOperationException("fixture build failure");
            if (!loaded || focused || snapshot != desired) return false;
            presented = snapshot; return true;
        }, action => { if (!accepting) return false; pending.Enqueue(action); return true; }, _ => errors++, desired);
        void Drain() { for (int i = 0; pending.Count > 0 && i < 20; i++) pending.Dequeue()(); Check(pending.Count == 0, "Deferred refresh spun indefinitely."); }
        refresh.Start(); Drain(); Check(builds == 0, "Initial unchanged content was rebuilt.");
        desired = "release2"; for (int i = 0; i < 6; i++) refresh.Request();
        Check(pending.Count == 1 && presented == "initial", "Requests did not coalesce or old content was removed before replacement.");
        Drain(); Check(builds == 1 && presented == "release2", "Changed metadata did not actually update visible content.");
        focused = true; desired = "release3"; refresh.Request(); Drain();
        Check(builds == 1 && presented == "release2", "Focused content was replaced.");
        focused = false; refresh.Request(); Drain(); Check(presented == "release3", "Focus departure did not apply the pending data.");
        desired = "stale"; duringBuild = () => desired = "latest"; refresh.Request(); Drain();
        Check(presented == "latest", "An obsolete build replaced newer metadata.");
        desired = "unloaded"; refresh.Request(); loaded = false; refresh.Stop(); Drain();
        Check(presented == "latest", "An unloaded generation applied a late callback.");
        loaded = true; refresh.Start(); Drain(); Check(presented == "unloaded", "Reload did not catch up with latest metadata.");
        accepting = false; desired = "enqueue-later"; refresh.Request(); accepting = true; refresh.Request(); Drain();
        Check(presented == "enqueue-later", "An enqueue failure permanently blocked refresh.");
        fail = true; desired = "failed"; refresh.Request(); Drain();
        Check(errors == 1 && presented == "enqueue-later", "Build failure cleared old content or escaped the guarded callback.");
        fail = false; refresh.Request(); Drain(); Check(presented == "failed", "A recoverable build failure blocked subsequent updates.");

        // Actual grid rows: inserting one card changes every row group. Keep a
        // focused row 2 alive until focus leaves, and discard obsolete snapshots.
        var third = Plugin("third"); var fourth = Plugin("fourth");
        var grid = new ObservableCollection<IReadOnlyList<DeckyPluginInfo>>(
            PluginCatalogReconciliation.Rows(Array.Empty<IReadOnlyList<DeckyPluginInfo>>(),
                new[] { first, second, third, fourth }, 2));
        var focusedRow = grid[1]; focused = true; loaded = true;
        var list = new ObservableCollection<DeckyPluginInfo> { first, second, third, fourth };
        int collectionApplies = 0;
        var collection = new PluginCollectionRefresh<DeckyPluginInfo>(() => loaded && !focused, desiredItems =>
        {
            collectionApplies++;
            PluginCatalogReconciliation.Synchronize(grid, PluginCatalogReconciliation.Rows(grid, desiredItems, 2));
            PluginCatalogReconciliation.Synchronize(list, desiredItems);
        }, action => { pending.Enqueue(action); return true; }, _ => errors++);
        collection.Start();
        collection.Set(new[] { extra, first, second, third, fourth }); Drain();
        Check(collectionApplies == 0 && ReferenceEquals(grid[1], focusedRow) && list[2] == third,
            "A front insertion removed a focused grid row or list item.");
        collection.Set(new[] { third, second, first }); collection.Set(new[] { fourth, third, second, first }); Drain();
        Check(collectionApplies == 0 && ReferenceEquals(grid[1], focusedRow),
            "A newer removal/reorder destroyed the focused row before departure.");
        focused = false; collection.Resume(); Drain();
        Check(collectionApplies == 1 && list.SequenceEqual(new[] { fourth, third, second, first }) &&
            grid.SelectMany(row => row).SequenceEqual(list), "Focus departure did not apply only the latest desired results.");
        collection.Set(new[] { extra, first }); loaded = false; collection.Stop(); Drain();
        Check(collectionApplies == 1 && list.Count == 4, "Page unload allowed a queued collection update.");
        collection.Set(new[] { second, first }); loaded = true; collection.Start(); Drain();
        Check(collectionApplies == 2 && list.SequenceEqual(new[] { second, first }),
            "Reload applied an obsolete snapshot instead of the latest retained results.");

        var applying = false; var nestedApply = false; var reentrantApplies = 0;
        string reentrantShown = "initial";
        PluginCollectionRefresh<string>? reentrant = null;
        reentrant = new(() => true, desiredItems =>
        {
            nestedApply |= applying; applying = true; reentrantApplies++;
            if (desiredItems[0] == "older")
            {
                reentrant!.Set(new[] { "newer" });
                // Simulate a WinRT notification pumping the same dispatcher.
                while (pending.Count > 0) pending.Dequeue()();
            }
            reentrantShown = desiredItems[0]; applying = false;
        }, action => { pending.Enqueue(action); return true; }, _ => errors++);
        reentrant.Start(); reentrant.Set(new[] { "older" }); Drain();
        Check(!nestedApply && reentrantApplies == 2 && reentrantShown == "newer",
            "A nested dispatcher applied newer results before an older transaction completed.");
        Console.WriteLine($"PASS {checks} catalog/presenter behavior checks (no native UI).");
        return Task.CompletedTask;
    }
}
