using System.Text.Json;
using Playhub.Models;
using Playhub.Services;

var failures = 0;
var count = 0;
void Test(string name, Action action)
{
    count++;
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
void Equal(double actual, double expected)
    => Require(Math.Abs(actual - expected) < 0.000001, $"expected {expected}, got {actual}");
void Use(SupportUsageClock clock, ref double now, double seconds)
{
    while (seconds > 0)
    {
        var step = Math.Min(30, seconds);
        now += step;
        clock.Sample(now, true, 0);
        seconds -= step;
    }
}

Test("first launch counts no installation age or pre-initialization time", () =>
{
    var clock = new SupportUsageClock();
    clock.Sample(1_000_000, false, 0);
    clock.Sample(2_000_000, true, 0);
    Equal(clock.UsageSeconds, 0);
    Require(!clock.IsDue, "new install starts due");
});

Test("exactly two hours of active samples becomes due", () =>
{
    var clock = new SupportUsageClock();
    double now = 0;
    clock.Sample(now, true, 0);
    Use(clock, ref now, 7199.5);
    Require(!clock.IsDue, "reminder early");
    Use(clock, ref now, .5);
    Equal(clock.UsageSeconds, 7200);
    Require(clock.IsDue, "reminder not due");
});

Test("deactivation saves exact preceding usage; background/minimized/locked time is excluded", () =>
{
    var clock = new SupportUsageClock();
    clock.Sample(0, true, 0);
    clock.Sample(12.25, false, 0);
    Equal(clock.UsageSeconds, 12.25);
    clock.Sample(30, false, 0);
    clock.Sample(10_000, false, 0);
    clock.Sample(20_000, true, 0);
    Equal(clock.UsageSeconds, 12.25);
    clock.Sample(20_010, true, 0);
    Equal(clock.UsageSeconds, 22.25);
});

Test("usage and fractional seconds survive a new process clock origin", () =>
{
    var first = new SupportUsageClock();
    double now = 400;
    first.Sample(now, true, 0);
    Use(first, ref now, 3600.125);
    first.Sample(now, false, 0);
    var json = JsonSerializer.Serialize(new PlayhubSettings { SupportReminderUsageSeconds = first.UsageSeconds });
    var settings = JsonSerializer.Deserialize<PlayhubSettings>(json)!;
    var next = new SupportUsageClock(settings.SupportReminderUsageSeconds);
    now = 0;
    next.Sample(now, true, 0);
    Equal(next.UsageSeconds, 3600.125);
    Use(next, ref now, 3599.875);
    Require(next.IsDue, "restart lost cumulative usage");
    Equal(JsonSerializer.Deserialize<PlayhubSettings>("{}")!.SupportReminderUsageSeconds, 0);
});

Test("idle cutoff credits only the active part of the crossing sample", () =>
{
    var clock = new SupportUsageClock();
    clock.Sample(0, true, 290);
    clock.Sample(30, true, 320);
    Equal(clock.UsageSeconds, 10);
    Require(!clock.IsActive, "idle sample considered active");
    clock.Sample(60, true, 350);
    Equal(clock.UsageSeconds, 10);
});

Test("long idle does not accrue and new input resumes only its recent tail", () =>
{
    var clock = new SupportUsageClock();
    clock.Sample(0, true, 0);
    for (var now = 30; now <= 900; now += 30) clock.Sample(now, true, now);
    Equal(clock.UsageSeconds, 300);
    clock.Sample(930, true, 10);
    Equal(clock.UsageSeconds, 310);
    Require(clock.IsActive, "recent input failed to resume usage");
});

Test("input after an idle interval does not fill the idle gap", () =>
{
    var clock = new SupportUsageClock();
    clock.Sample(0, true, 290);
    clock.Sample(30, true, 1);
    Equal(clock.UsageSeconds, 11);
});

Test("sleep and unexpected dispatcher gaps never add hours", () =>
{
    var clock = new SupportUsageClock(100);
    clock.Sample(0, true, 0);
    clock.Sample(30, true, 0);
    clock.Sample(20_000, true, 0);
    Equal(clock.UsageSeconds, 130);
    clock.Sample(20_030, true, 0);
    Equal(clock.UsageSeconds, 160);
    clock.Sample(20_121, true, 0);
    Equal(clock.UsageSeconds, 160);
});

Test("deferral and failed dialog attempts leave one reminder due without a backlog", () =>
{
    var clock = new SupportUsageClock(7200);
    double now = 0;
    clock.Sample(now, true, 0);
    Use(clock, ref now, 14_400);
    Equal(clock.UsageSeconds, 7200);
    Require(clock.IsDue, "deferred reminder was consumed");
    clock.Sample(now, false, 0);
    var reopened = new SupportUsageClock(clock.UsageSeconds);
    Require(reopened.IsDue, "deferred reminder lost on restart");
});

Test("actual Opened resets once and the next interval requires another two hours", () =>
{
    var clock = new SupportUsageClock(7200);
    double now = 0;
    clock.Sample(now, true, 0);
    Require(clock.MarkReminderOpened(now, true, 0), "due opening was not consumed");
    Equal(clock.UsageSeconds, 0);
    Require(!clock.MarkReminderOpened(now, true, 0), "duplicate Opened consumed another interval");
    Use(clock, ref now, 7199);
    Require(!clock.IsDue, "second interval early");
    Use(clock, ref now, 1);
    Require(clock.IsDue, "second interval missing");
});

Test("persisted invalid counts are sanitized and timestamps cannot run backwards", () =>
{
    foreach (var bad in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        Equal(new SupportUsageClock(bad).UsageSeconds, 0);
    Equal(new SupportUsageClock(double.MaxValue).UsageSeconds, 7200);
    var clock = new SupportUsageClock();
    clock.Sample(10, true, 0);
    clock.Sample(20, true, 0);
    clock.Sample(5, true, 0);
    clock.Sample(30, true, 0);
    Equal(clock.UsageSeconds, 20);
});

Test("unavailable idle telemetry fails closed", () =>
{
    foreach (var bad in new[] { -1d, double.NaN, double.PositiveInfinity })
    {
        var clock = new SupportUsageClock(40);
        clock.Sample(0, true, 0);
        clock.Sample(30, true, bad);
        Equal(clock.UsageSeconds, 40);
        Require(!clock.IsActive, "invalid input telemetry counted as active");
    }
});

Test("reminder text and donation command are localized for all supported languages", () =>
{
    var keys = new[] { "Ti piace Playhub?", "Playhub è sviluppato e mantenuto da una sola persona.",
        "Se ti va di sostenerlo, anche una piccola donazione significa molto per me.", "Fai una donazione" };
    foreach (var language in LocalizationService.Languages)
        foreach (var key in keys)
        {
            var value = LocalizationService.Translate(language.Key, key);
            Require(!string.IsNullOrWhiteSpace(value), "empty localized text");
            Require(language.Key == "it" || value != key, "missing translation for " + language.Key + ": " + key);
        }
});

Test("integration has forced-preview, review isolation, activity and operation guards", () =>
{
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SupportReminderSource.txt"));
    Require(source.Contains("ShowSupportReminderAsync(bool force = false)"), "parent preview hook missing");
    Require(source.Contains("if (!force && _supportUsageClock is not null)"), "forced preview could consume real usage");
    Require(source.IndexOf("dialog.Opened +=", StringComparison.Ordinal) < source.IndexOf(".MarkReminderOpened(", StringComparison.Ordinal), "reset is not tied to Opened");
    Require(source.Contains("#if !PLAYHUB_UI_REVIEW") && source.Contains("BuildSupportReminderForReview"), "review isolation/hook missing");
    Require(source.Contains("GetOpenPopupsForXamlRoot") && source.Contains("_playhubUpdateDialogActionPending") &&
        source.Contains("_pluginInstallOperations.Count") && source.Contains("_supportReminderOperationDepth"), "operation/dialog deferral missing");
    Require(!source.Contains("!_installButton.IsEnabled"), "unmet installer prerequisites suppress the reminder");
    Require(source.Contains("GetLastInputInfo") && source.Contains("GetForegroundWindow") &&
        source.Contains("OverlappedPresenterState.Minimized") && source.Contains("IsHostVisible"), "actual-use checks missing");
    Require(source.Contains("TimeSpan.FromSeconds(30)") && !source.Contains("DateTime"), "wall-clock or per-frame timer introduced");
    Require(source.Contains("await SaveSettingsSilentlyAsync()"), "shared settings-save path missing");
    Require(source.Contains("VirtualKey.Escape") && source.Contains("Stretch = Stretch.Uniform"), "popup close/image contract missing");
});

Console.WriteLine($"{count - failures}/{count} passed; pure/offline tests only, no WinUI build or launch.");
return failures == 0 ? 0 : 1;
