using System.Text.Json.Nodes;
using Playhub.Integrations;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
await ArtworkProviderChecks.Run(Check);
string root = Path.Combine(Path.GetTempPath(), "Playhub-pc-artwork-store-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string identity = ApplicationIntegrationIdentity.Create("pc", "xbox:package!app");
    var store = new ApplicationIntegrationDataStore(root);
    await store.PatchAsync(identity, "artwork", new JsonObject { ["hero"] = "first.jpg", ["foreign"] = "preserve" });
    await store.PatchAsync(identity, "artwork", new JsonObject { ["hero"] = "second.jpg" });
    Check((await store.ReadCategoryAsync(identity, "artwork"))["foreign"]!.ToString() == "preserve", "Artwork edits preserve unrelated fields");
    var parallelStore = new ApplicationIntegrationDataStore(root);
    await Task.WhenAll(store.PatchAsync(identity, "artwork", new JsonObject { ["first"] = true }),
        parallelStore.PatchAsync(identity, "artwork", new JsonObject { ["second"] = true }));
    var parallel = await store.ReadCategoryAsync(identity, "artwork");
    Check(parallel["first"]!.GetValue<bool>() && parallel["second"]!.GetValue<bool>(), "Concurrent instances preserve both artwork edits");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await store.PatchAsync(identity, "artwork", new JsonObject { ["foreign"] = "lost" }, cancelled.Token); }
    catch (OperationCanceledException) { }
    Check((await store.ReadCategoryAsync(identity, "artwork"))["foreign"]!.ToString() == "preserve", "Cancelled edits leave the saved artwork draft intact");
    Console.WriteLine($"{passed} PC artwork provider and store checks passed.");
}
finally { Directory.Delete(root, recursive: true); }
