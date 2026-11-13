using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using GamingMode.Services;

internal static class GameExecutionQosTests
{
	internal const string ParentFixtureCommand = "test-game-qos-parent";
	private static readonly GameExecutionQosState Efficient = new(1, 1);
	private static readonly GameExecutionQosState Performance = new(1, 0);
	private static readonly GameExecutionQosTarget Target = new(22, 1234);

	public static void VerifyOwnership()
	{
		var game = new FakeProcess();
		var lease = new GameExecutionQosLease(game);
		Check(lease.Apply("default") == "Default" && game.Writes.Count == 0, "Default made a write.");
		Check(lease.Apply("efficiency") == "Applied" && game.State == Efficient, "Efficiency lacked readback.");
		Check(lease.Apply("performance") == "Applied" && game.State == Performance, "Performance lacked readback.");
		Check(lease.Apply("performance") == "Applied" && game.Writes.Count == 2, "Repeated state was rewritten.");
		Check(lease.Restore() && game.State == default, "Default ownership was not restored.");
		Check(game.Writes.SequenceEqual([Efficient, Performance, default]), "Unexpected native writes.");
	}

	public static void VerifyForeignAndFailedControl()
	{
		var game = new FakeProcess { State = new(2, 2) };
		var lease = new GameExecutionQosLease(game);
		Check(lease.Apply("efficiency") == "ExternalControl" && game.Writes.Count == 0, "An external timer policy was overwritten.");
		game.State = default;
		Check(lease.Apply("efficiency") == "ExternalControl", "A relinquished process was reacquired.");
		lease = new GameExecutionQosLease(game);
		Check(lease.Apply("efficiency") == "Applied", "Fixture setup failed.");
		game.State = new(3, 3);
		Check(lease.Restore() && game.State == new GameExecutionQosState(3, 3) && game.Writes.Count == 1,
			"Cleanup overwrote a new third-party bit.");

		game = new FakeProcess { IgnoreWrites = true };
		lease = new GameExecutionQosLease(game);
		Check(lease.Apply("performance") == "Unavailable" && lease.Restore() && game.Writes.Count == 1,
			"Unconfirmed write was treated as applied.");
		game.IgnoreWrites = false;
		Check(lease.Apply("performance") == "Applied", "Fixture setup failed.");
		game.Readable = false;
		Check(!lease.Restore() && game.Writes.Count == 2, "Cleanup guessed a baseline when native read failed.");
		game.Readable = true;
		game.IsAlive = false;
		Check(lease.Restore() && game.Writes.Count == 2, "Cleanup touched an exited target.");
	}

	public static void VerifyOwnerLifecycle() => VerifyOwnerLifecycleAsync().GetAwaiter().GetResult();

	private static async Task VerifyOwnerLifecycleAsync()
	{
		using var input = new PipeReader();
		using var output = new PipeWriter();
		var game = new FakeProcess();
		string? source = "ac";
		int opens = 0;
		Task<int> owner = GameExecutionQosOwner.RunAsync(input, output, identity =>
		{
			Check(identity == Target, "Owner accepted a wrong identity.");
			opens++;
			return game;
		}, () => Volatile.Read(ref source), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(15));
		input.Send("22 1234 default default");
		Check(await output.NextAsync() == "Default" && game.Writes.Count == 0, "Default startup touched QoS.");
		input.Send("22 1234 performance efficiency");
		Check(await output.NextAsync() == "Applied" && game.State == Performance, "AC command was not confirmed.");
		Volatile.Write(ref source, "battery");
		await UntilAsync(() => game.State == Efficient);
		Volatile.Write(ref source, null);
		await UntilAsync(() => game.State == default);
		Volatile.Write(ref source, "battery");
		await UntilAsync(() => game.State == Efficient);
		input.End();
		Check(await owner.WaitAsync(TimeSpan.FromSeconds(2)) == 0 && game.State == default && game.Disposed && opens == 1,
			"Owner disconnect did not restore and close the exact process.");

		using var timeoutInput = new PipeReader();
		using var timeoutOutput = new PipeWriter();
		var timedGame = new FakeProcess();
		Task<int> timedOwner = GameExecutionQosOwner.RunAsync(timeoutInput, timeoutOutput, _ => timedGame,
			() => "ac", TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(15));
		timeoutInput.Send("22 1234 efficiency default");
		Check(await timeoutOutput.NextAsync() == "Applied", "Timeout fixture never applied.");
		Check(await timedOwner.WaitAsync(TimeSpan.FromSeconds(2)) == 0 && timedGame.State == default,
			"A stalled client prevented lease expiry and cleanup.");
		timeoutInput.End();

		using var malformed = new StringReader("22 1234 performance turbo\n");
		Check(await GameExecutionQosOwner.RunAsync(malformed, TextWriter.Null, _ => throw new Exception("Invalid command opened a process."),
			() => "ac", TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(15)) == 2, "Invalid first command was not rejected.");
	}

	public static void VerifyWindowsOwnerAndProcessIdentity() => VerifyWindowsOwnerAsync().GetAwaiter().GetResult();

	private static async Task VerifyWindowsOwnerAsync()
	{
		if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
		using var self = Process.GetCurrentProcess();
		var target = new GameExecutionQosTarget(self.Id, self.StartTime.ToFileTimeUtc());
		using var native = WindowsGameExecutionQosProcess.Open(target) ?? throw new Exception("Could not open isolated test process.");
		Check(WindowsGameExecutionQosProcess.Open(target with { CreationFileTimeUtc = target.CreationFileTimeUtc - 1 }) is null,
			"The same PID was accepted with another birth.");
		Check(native.Read() == default(GameExecutionQosState), "Test process is already under external QoS control.");
		using var controller = new GameExecutionQosController();
		try
		{
			Check(await controller.RenewAsync(target, "default", "default") == "Default", "Default attempted to start an owner.");
			Check(await controller.RenewAsync(target, "efficiency", "efficiency") == "Applied" && native.Read() == Efficient,
				"Independent Windows child did not confirm EcoQoS.");
			Check(await controller.RenewAsync(target, "performance", "performance") == "Applied" && native.Read() == Performance,
				"Independent Windows child did not confirm HighQoS.");
			await controller.StopAsync();
			Check(native.Read() == default(GameExecutionQosState), "Stopping the child failed to return ownership to Windows.");
			using var parent = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
				FormattableString.Invariant($"{ParentFixtureCommand} {target.ProcessId} {target.CreationFileTimeUtc}"))
			{
				UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
			}) ?? throw new Exception("Could not launch the owned crash fixture.");
			try
			{
				Check(await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) == "Applied"
					&& native.Read() == Efficient, "Crash fixture never acquired the process mask.");
				parent.Kill(entireProcessTree: false);
				await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
				await UntilAsync(() => native.Read() == default(GameExecutionQosState));
			}
			finally { if (!parent.HasExited) parent.Kill(entireProcessTree: false); }
		}
		finally
		{
			// The only process this test changes is its own disposable executable.
			if (native.Read() is { } observed && (observed == Efficient || observed == Performance)) native.Write(default);
		}
	}

	internal static async Task<int> RunParentFixtureAsync(GameExecutionQosTarget target)
	{
		using var owner = new GameExecutionQosController();
		string result = await owner.RenewAsync(target, "efficiency", "efficiency");
		Console.WriteLine(result);
		if (result != "Applied") return 1;
		await Task.Delay(TimeSpan.FromSeconds(8));
		return 0;
	}

	private static async Task UntilAsync(Func<bool> predicate)
	{
		for (int attempt = 0; attempt < 100; attempt++)
		{
			if (predicate()) return;
			await Task.Delay(10);
		}
		throw new Exception("Expected owner state never arrived.");
	}

	private static void Check(bool valid, string reason)
	{
		if (!valid) throw new Exception(reason);
	}

	private sealed class FakeProcess : IGameExecutionQosProcess
	{
		private readonly object _stateLock = new();
		private GameExecutionQosState _state;
		public GameExecutionQosState State { get { lock (_stateLock) return _state; } set { lock (_stateLock) _state = value; } }
		public ConcurrentQueue<GameExecutionQosState> Writes { get; } = new();
		public bool IsAlive { get; set; } = true;
		public bool Readable { get; set; } = true;
		public bool IgnoreWrites { get; set; }
		public bool Disposed { get; private set; }
		public GameExecutionQosState? Read() => Readable ? State : null;
		public bool Write(GameExecutionQosState state)
		{
			Writes.Enqueue(state);
			if (!IgnoreWrites) State = state;
			return true;
		}
		public void Dispose() => Disposed = true;
	}

	private sealed class PipeReader : TextReader
	{
		private readonly BlockingCollection<string> _commands = new();
		public void Send(string command) => _commands.Add(command);
		public void End() => _commands.CompleteAdding();
		public override string? ReadLine() => _commands.TryTake(out string? command, Timeout.Infinite) ? command : null;
	}

	private sealed class PipeWriter : StringWriter
	{
		private readonly System.Threading.Channels.Channel<string> _replies = System.Threading.Channels.Channel.CreateUnbounded<string>();
		public override Task WriteLineAsync(string? value)
		{
			_replies.Writer.TryWrite(value!);
			return Task.CompletedTask;
		}
		public async Task<string> NextAsync() => await _replies.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
	}
}
