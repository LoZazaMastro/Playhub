internal static class SteamControllerTranslationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        long tick = 1000;
        int reads = 0;
        string? json = "{\"Gaming\":{}}";
        var preference = new SteamControllerTranslationPreference(() => { ++reads; return json; }, () => tick);
        check(preference.Read() == new SteamControllerTranslationPreference.State(true, ""), "legacy config keeps the existing translation default");
        json = "{\"Gaming\":{\"SteamControllerTranslationEnabled\":false}}";
        check(preference.Read().Enabled && reads == 1, "preference disk reads are cached for at most100ms");
        tick += 100;
        check(!preference.Read().Enabled && reads == 2, "persisted disable revokes the cached preference");
        const string revision = "aabbccddeeff00112233445566778899";
        json = "{\"gaming\":{\"steamControllerTranslationEnabled\":true,\"steamControllerTranslationRevision\":\"" + revision + "\"}}";
        tick += 100;
        check(preference.Read() == new SteamControllerTranslationPreference.State(true, revision), "case-insensitive config preserves the exact revocation version");
        foreach (var malformed in new[] { "{", "[]", "{\"Gaming\":false}", "{\"Gaming\":{\"SteamControllerTranslationEnabled\":1}}",
            "{\"Gaming\":{},\"gaming\":{}}", "{\"Gaming\":{\"SteamControllerTranslationRevision\":\"bad\"}}" })
        {
            json = malformed; tick += 100;
            check(preference.Read() is { Enabled: false, Known: false }, "unknown or ambiguous preference cannot enable a translator");
        }
        json = null; tick += 100;
        check(preference.Read() is { Enabled: false, Known: false }, "a missing previously present config is not permission to re-enable");
        json = "{\"Gaming\":{\"SteamControllerTranslationEnabled\":false}}"; tick -= 1000;
        check(!preference.Read().Enabled, "clock reversal rereads the preference without stale enablement");
        var unavailable = new SteamControllerTranslationPreference(() => throw new IOException("inert"), () => 0);
        check(unavailable.Read() is { Enabled: false, Known: false }, "IO failure is an unknown disabled preference");
        var legacy = new SteamControllerTranslationPreference(() => null, () => 0);
        check(legacy.Read().Enabled, "an initial missing legacy config preserves the existing behavior");

        var state = new SteamControllerTranslationPreference.State(true, "first");
        var events = new List<string>();
        bool alive = true;
        int runs = 0;
        SteamControllerTranslationLifecycle.Run(() => alive, () => state, (enabled, current) =>
        {
            ++runs;
            if (runs == 1)
            {
                check(enabled && current(), "first relay is admitted only under the current preference");
                events.Add("own-PDO1"); state = new(false, "off");
                check(!current(), "disable revokes the running reader scope immediately on observation");
                events.AddRange(["neutral1", "drain-reader1", "detach-own-PDO1", "own-0ACK1"]);
                return true;
            }
            if (runs == 2)
            {
                check(!enabled && current(), "disabled mode serves ordinary menus without a translated pad");
                state = new(true, "enabled-again"); return true;
            }
            check(enabled && current(), "re-enable receives a new current scope");
            events.Add("fresh-proof2"); events.Add("own-PDO2"); alive = false; return true;
        }, _ => throw new Exception("unexpected wait"));
        check(runs == 3 && events.IndexOf("own-0ACK1") < events.IndexOf("fresh-proof2"), "old owned output and0ACK finish before fresh re-enable admission");
        check(events.All(item => !item.Contains("foreign", StringComparison.Ordinal)), "the preference lifecycle never addresses another pad");

        alive = true; runs = 0; state = new(true, "original");
        SteamControllerTranslationLifecycle.Run(() => alive, () => state, (enabled, current) =>
        {
            ++runs;
            if (runs == 1)
            {
                state = new(true, "off-then-on");
                check(!current(), "rapid OFF-ON version change still drains the old run even if the final bool matches");
                return true;
            }
            check(current() && enabled, "rapid re-enable requires a new scope"); alive = false; return true;
        }, _ => throw new Exception("unexpected wait"));
        check(runs == 2, "an explicit version change creates exactly one replacement run");

        runs = 0; alive = true; int waits = 0;
        SteamControllerTranslationLifecycle.Run(() => alive, () => new(true, "stable"), (_, _) => { ++runs; return true; }, _ => { if (++waits == 3) alive = false; });
        check(runs == 1 && waits == 3, "a stable reader failure never causes a spawn or output retry loop");
        runs = 0;
        SteamControllerTranslationLifecycle.Run(() => true, () => new(true, "stable"), (_, _) => { ++runs; return false; }, _ => throw new Exception("unconfirmed cleanup cannot wait or retry"));
        check(runs == 1, "unconfirmed owned PDO cleanup or0ACK forbids any replacement run");
        runs = 0;
        SteamControllerTranslationLifecycle.Run(() => false, () => throw new Exception("owner has exited"), (_, _) => { ++runs; return true; }, _ => { });
        check(runs == 0, "exited or cancelled retained owner cannot start a translation run");
    }
}
