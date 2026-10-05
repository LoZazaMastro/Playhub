using Playhub.Services;
int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch { Check(true,name); return; } throw new Exception(name); }
var calls = new List<string>();
bool running = true;
Task Delay(int ms) => Task.CompletedTask;
await RestartSequence.SteamAsync("steam.exe", _ => true, () => running, _ => { calls.Add("exit"); running=false; }, _ => { calls.Add("start"); running=true; }, Delay);
Check(calls.SequenceEqual(new[]{"exit","start"}), "graceful exit precedes relaunch");
calls.Clear(); running = false;
await RestartSequence.SteamAsync("steam.exe", _ => true, () => running, _ => calls.Add("exit"), _ => {calls.Add("start");running=true;}, Delay);
Check(calls.SequenceEqual(new[]{"start"}), "closed Steam starts without exit request");
await Reject(() => RestartSequence.SteamAsync(null, _ => false, () => false, _ => {}, _ => {}, Delay), "missing executable fails");
calls.Clear();
await Reject(() => RestartSequence.SteamAsync("steam.exe", _=>true, ()=>true, _=>calls.Add("exit"), _=>calls.Add("start"), Delay), "stuck Steam fails");
Check(calls.SequenceEqual(new[]{"exit"}), "stuck Steam never relaunches or force kills");
await Reject(() => RestartSequence.SteamAsync("steam.exe", _=>true, ()=>false, _=>{}, _=>{}, Delay), "failed Steam launch fails");
calls.Clear();
var success = await RestartSequence.SteamAndDeckyAsync(true, ()=>calls.Add("stop-decky"), ()=>{calls.Add("steam");return Task.CompletedTask;}, ()=>{calls.Add("start-decky");return true;}, Delay, _=>{});
Check(success && calls.SequenceEqual(new[]{"stop-decky","steam","start-decky"}), "Decky restores after successful Steam restart");
calls.Clear();
success = await RestartSequence.SteamAndDeckyAsync(true, ()=>calls.Add("stop-decky"), ()=>throw new TimeoutException(), ()=>{calls.Add("restore-decky");return true;}, Delay, _=>calls.Add("failure"));
Check(!success && calls.SequenceEqual(new[]{"stop-decky","failure","restore-decky"}), "failed Steam restart restores Decky but never reports success");
success = await RestartSequence.SteamAndDeckyAsync(true, ()=>{}, ()=>Task.CompletedTask, ()=>false, Delay, _=>{});
Check(!success, "Decky startup failure remains failure");
calls.Clear();
success = await RestartSequence.SteamAndDeckyAsync(false, ()=>calls.Add("stop"), ()=>Task.CompletedTask, ()=>true, Delay, _=>{});
Check(!success && calls.Count==0, "missing Decky changes no processes");
Console.WriteLine($"{count}/{count} passed; fake callbacks only.");
