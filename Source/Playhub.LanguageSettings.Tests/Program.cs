using Playhub;
using Playhub.Services;

if (args.Length > 0) return await RestartProcesses.RunChildAsync(args);

var failures = 0;
var count = 0;
async Task Test(string name, Func<Task> test)
{
    count++;
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

await Test("all 132 language directions save before requesting one restart", async () =>
{
    foreach (var from in LocalizationService.Languages)
    foreach (var to in LocalizationService.Languages.Where(language => language.Key != from.Key))
    {
        var window = new MainWindow { Language = from.Key };
        var saved = false;
        window.Save = async () =>
        {
            Require(window.Language == to.Key && window.Busy && !window.Enabled, "Wrong in-flight state");
            await Task.Yield();
            saved = true;
        };
        window.Restart = () =>
        {
            Require(saved && window.Language == to.Key, "Restart preceded save");
            return true;
        };
        await window.SelectAsync(to.Key);
        Require(window.Saves == 1 && window.Restarts == 1, $"Missed/duplicate restart {from.Key}->{to.Key}");
        Require(window.Busy && !window.Enabled, "Pending restart allowed reentry");
    }
});

await Test("same language, aliases, loading and cleared selections are ignored", async () =>
{
    foreach (var language in LocalizationService.Languages)
    {
        var window = new MainWindow { Language = language.Key };
        await window.SelectAsync(language.Key.ToUpperInvariant() + "-XX");
        await window.SelectAsync(null);
        await window.SelectAsync(" ");
        window.Loading = true;
        await window.SelectAsync(language.Key == "it" ? "en" : "it");
        Require(window.Saves == 0 && window.Restarts == 0, "Programmatic selection restarted");
    }
});

await Test("rapid/reentrant selections cannot overwrite an in-flight language save", async () =>
{
    var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var window = new MainWindow { Language = "it", Save = () => save.Task, Restart = () => true };
    var pending = window.SelectAsync("ja");
    await window.SelectAsync("en");
    await window.SelectAsync(null);
    Require(window.Language == "ja" && window.Saves == 1 && window.Restarts == 0, "Save was reentered");
    save.SetResult();
    await pending;
    await window.SelectAsync("it");
    Require(window.Saves == 1 && window.Restarts == 1, "Pending restart was reentered");
});

await Test("save failures restore settings and selection, report error, and permit retry", async () =>
{
    foreach (var error in new Exception[] { new IOException("disk full"), new UnauthorizedAccessException("denied") })
    {
        var window = new MainWindow { Language = "it", Save = () => Task.FromException(error) };
        await window.SelectAsync("en");
        Require(window.Language == "it" && window.Selection == "it", "Failed save left new language selected");
        Require(window.Restarts == 0 && window.Errors == 1 && !window.Busy && window.Enabled, "Failure was not recoverable");
        window.Save = () => Task.CompletedTask;
        await window.SelectAsync("en");
        Require(window.Restarts == 1 && window.Language == "en", "Retry was ignored");
    }
});

await Test("restart failure keeps saved settings and unlocks the selector", async () =>
{
    var window = new MainWindow { Language = "it", Restart = () => false };
    await window.SelectAsync("en");
    Require(window.Language == "en" && window.Enabled && !window.Busy, "Restart failure lost saved language or locked UI");
    await window.SelectAsync("fr");
    Require(window.Saves == 2 && window.Restarts == 2, "Next request blocked after failure");
});

await Test("four Decky strings and adjacent controls translate in all 11 non-Italian languages", () =>
{
    string[] keys =
    [
        "Scegli una versione di DeckyLoader",
        "Usa questa opzione solo se ti serve una versione precisa.",
        "DeckyLoader con console",
        "Mostra una finestra con il registro in tempo reale. Utile per diagnosi e sviluppo.",
        "Installa questa versione", "Installa la versione con console", "Scegli una versione"
    ];
    foreach (var language in LocalizationService.Languages.Where(language => language.Key != "it"))
    foreach (var key in keys)
    {
        var result = LocalizationService.Translate(language.Key, key);
        Require(!string.IsNullOrWhiteSpace(result) && result != key, $"Untranslated {language.Key}: {key}");
        Require(LocalizationService.Translate(language.Key.ToUpperInvariant() + "-XX", key) == result, "Regional alias differs");
        Require(LocalizationService.Translate("it", key) == key, "Italian source changed");
    }
    return Task.CompletedTask;
});

await Test("production heading/body traversal and restart failure handling", () =>
{
    SourceContracts.Run();
    return Task.CompletedTask;
});

await Test("real SDK restart waits for old test process and reacquires the isolated instance key", RestartProcesses.RunAsync);

Console.WriteLine($"{count - failures}/{count} test groups passed");
return failures == 0 ? 0 : 1;
