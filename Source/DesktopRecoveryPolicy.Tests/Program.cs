using GamingMode.Services;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
var policy = new DesktopRecoveryPolicy();
Check(!policy.ShouldRestore(true, false, false, false), "One transient Steam bootstrap gap does not restore");
Check(policy.ShouldRestore(true, false, false, false), "Steam exit with only a folder process restores desktop");
Check(!policy.ShouldRestore(true, false, false, false), "Recovery resets and does not spin");
Check(!policy.ShouldRestore(true, true, false, false), "Live Steam without shell remains Gaming Mode");
Check(!policy.ShouldRestore(true, false, false, false), "Steam returning resets the gap count");
Check(!policy.ShouldRestore(true, false, true, false), "Actual desktop shell prevents recovery");
Check(!policy.ShouldRestore(true, false, false, false), "Shell recovery resets previous failure");
Check(!policy.ShouldRestore(true, false, false, true), "No desktop relaunch during shutdown");
Check(!policy.ShouldRestore(true, false, false, false), "Shutdown resets previous absence");
Check(policy.ShouldRestore(true, false, false, false), "Persistent Steam exit recovered after two samples");
Check(!policy.ShouldRestore(false, true, false, false), "Desktop mode missing shell waits one sample");
Check(policy.ShouldRestore(false, true, false, false), "Desktop mode restores missing shell even with Steam open");
policy.Reset();
Check(!policy.ShouldRestore(false, false, false, false), "Explicit reset clears observations");
Check(policy.ShouldRestore(false, false, false, false), "Desktop shell recovered independent of plugin and config flags");
Console.WriteLine($"Desktop recovery: {passed} checks passed; no process, shell, registry or UI mutations.");
