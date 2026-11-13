using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

internal readonly record struct GameExecutionQosTarget(int ProcessId, long CreationFileTimeUtc);
internal readonly record struct GameExecutionQosState(uint Control, uint State);

internal interface IGameExecutionQosProcess : IDisposable
{
	bool IsAlive { get; }
	GameExecutionQosState? Read();
	bool Write(GameExecutionQosState state);
}

internal sealed class GameExecutionQosLease(IGameExecutionQosProcess process)
{
	private GameExecutionQosState _expected;
	private bool _external;

	public string Apply(string mode)
	{
		if (!process.IsAlive) return "TargetUnavailable";
		if (_external) return "ExternalControl";
		if (process.Read() is not { } current) return "Unavailable";
		if (current != _expected) return Relinquish();
		var next = mode switch
		{
			"efficiency" => new GameExecutionQosState(1, 1),
			"performance" => new GameExecutionQosState(1, 0),
			_ => default
		};
		if (current == next) return next == default ? "Default" : "Applied";
		if (!process.Write(next)) return "Unavailable";
		_expected = next;
		if (process.Read() is not { } observed) return "Unavailable";
		if (observed == next) return next == default ? "Default" : "Applied";
		if (observed != current) return Relinquish();
		_expected = current;
		return "Unavailable";
	}

	public bool Restore() => _expected == default || Apply("default") is "Default" or "ExternalControl" or "TargetUnavailable";

	private string Relinquish()
	{
		// Never reset a mask written by the game or by another controller.
		_external = true;
		_expected = default;
		return "ExternalControl";
	}
}

internal static class GameExecutionQosOwner
{
	internal const string Command = "game-execution-qos-owner";
	internal static bool IsMode(string mode) => mode is "default" or "efficiency" or "performance";

	internal static Task<int> RunAsync() => Console.IsInputRedirected && Console.IsOutputRedirected
		? RunAsync(Console.In, Console.Out, WindowsGameExecutionQosProcess.Open, WindowsGameExecutionQosProcess.ReadSource,
			TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(1))
		: Task.FromResult(2);

	internal static async Task<int> RunAsync(TextReader input, TextWriter output,
		Func<GameExecutionQosTarget, IGameExecutionQosProcess?> open, Func<string?> source,
		TimeSpan leaseLength, TimeSpan sampleInterval)
	{
		// Console's synchronized reader can block in ReadLineAsync on Windows.
		// Keep it off the loop so a wedged caller cannot prevent lease expiry.
		Task<string?> read = Task.Run(input.ReadLine);
		if (await Task.WhenAny(read, Task.Delay(leaseLength)) != read || await read is not { } line) return 0;
		if (!TryParse(line, out var target, out string ac, out string battery)) return 2;
		using var process = open(target);
		if (process is null)
		{
			await ReplyAsync(output, "TargetUnavailable");
			return 0;
		}
		var lease = new GameExecutionQosLease(process);
		long expiry = Environment.TickCount64 + (long)leaseLength.TotalMilliseconds;
		bool restored = true;
		try
		{
			await ReplyAsync(output, lease.Apply(SelectMode(source(), ac, battery)));
			read = Task.Run(input.ReadLine);
			while (process.IsAlive)
			{
				long remaining = expiry - Environment.TickCount64;
				if (remaining <= 0) break;
				var wait = TimeSpan.FromMilliseconds(Math.Min(remaining, sampleInterval.TotalMilliseconds));
				bool command = await Task.WhenAny(read, Task.Delay(wait)) == read;
				if (Environment.TickCount64 >= expiry) break;
				if (command)
				{
					if (await read is not { } next || !TryParse(next, out var renewed, out ac, out battery) || renewed != target) break;
					expiry = Environment.TickCount64 + (long)leaseLength.TotalMilliseconds;
					await ReplyAsync(output, lease.Apply(SelectMode(source(), ac, battery)));
					read = Task.Run(input.ReadLine);
				}
				else lease.Apply(SelectMode(source(), ac, battery));
			}
		}
		catch (IOException) { /* Agent disconnected. */ }
		finally
		{
			for (int attempt = 0; !(restored = lease.Restore()) && attempt < 2; attempt++)
				await Task.Delay(100);
		}
		return restored ? 0 : 4;
	}

	private static string SelectMode(string? source, string ac, string battery) => source switch
	{
		"ac" => ac,
		"battery" => battery,
		_ => "default"
	};

	private static async Task ReplyAsync(TextWriter output, string status)
	{
		await output.WriteLineAsync(status);
		await output.FlushAsync();
	}

	private static bool TryParse(string line, out GameExecutionQosTarget target, out string ac, out string battery)
	{
		target = default;
		ac = battery = "default";
		if (line.Length > 80 || line.Split(' ') is not [string pid, string birth, string acMode, string batteryMode]
			|| !int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int processId) || processId <= 0
			|| !long.TryParse(birth, NumberStyles.None, CultureInfo.InvariantCulture, out long fileTime) || fileTime <= 0
			|| !IsMode(acMode) || !IsMode(batteryMode)) return false;
		target = new GameExecutionQosTarget(processId, fileTime);
		ac = acMode;
		battery = batteryMode;
		return true;
	}
}

internal sealed class GameExecutionQosController(Func<bool>? allowed = null) : IDisposable
{
	private readonly SemaphoreSlim _gate = new(1);
	private Process? _owner;
	private GameExecutionQosTarget _target;
	private bool _closing;
	private bool _disposed;

	public async Task<string> RenewAsync(GameExecutionQosTarget target, string ac, string battery)
	{
		if (!GameExecutionQosOwner.IsMode(ac) || !GameExecutionQosOwner.IsMode(battery)) return "InvalidProfile";
		if (!_gate.Wait(0)) return "Busy";
		try
		{
			if (_disposed) return "Unavailable";
			if (allowed?.Invoke() == false) return "TargetUnavailable";
			bool defaults = ac == "default" && battery == "default";
			if (_owner is not null && (_closing || target != _target || defaults || _owner.HasExited))
			{
				if (!await StopOwnerAsync()) return "RestoreUnavailable";
			}
			if (defaults) return "Default";
			if (allowed?.Invoke() == false) return "TargetUnavailable";
			if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || Environment.ProcessPath is not { } exe)
				return "Unavailable";
			if (_owner is null)
			{
				_owner = Process.Start(new ProcessStartInfo(exe, GameExecutionQosOwner.Command)
				{
					UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
				});
				if (_owner is null) return "Unavailable";
				_target = target;
			}
			if (allowed?.Invoke() == false) return "TargetUnavailable";
			await _owner.StandardInput.WriteLineAsync(FormattableString.Invariant($"{target.ProcessId} {target.CreationFileTimeUtc} {ac} {battery}"));
			return await _owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)) ?? "Unavailable";
		}
		catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
		{
			CloseInput();
			return "Unavailable";
		}
		finally { _gate.Release(); }
	}

	public async Task<string> StopAsync()
	{
		await _gate.WaitAsync();
		try { return await StopOwnerAsync() ? "Default" : "RestoreUnavailable"; }
		finally { _gate.Release(); }
	}

	private void CloseInput()
	{
		if (_owner is null || _closing) return;
		_closing = true;
		try { _owner.StandardInput.Dispose(); }
		catch (IOException) { }
	}

	private async Task<bool> StopOwnerAsync()
	{
		if (_owner is null) return true;
		CloseInput();
		try { await _owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
		catch (TimeoutException) { return false; }
		if (_owner.ExitCode != 0 && _owner.ExitCode != 2 && !TargetExited()) return false;
		_owner.Dispose();
		_owner = null;
		_closing = false;
		return true;
	}

	private bool TargetExited()
	{
		try
		{
			using var process = Process.GetProcessById(_target.ProcessId);
			return process.HasExited || process.StartTime.ToFileTimeUtc() != _target.CreationFileTimeUtc;
		}
		catch (ArgumentException) { return true; }
		catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
	}

	public void Dispose()
	{
		_gate.Wait();
		try
		{
			_disposed = true;
			StopOwnerAsync().GetAwaiter().GetResult();
			_owner?.Dispose();
			_owner = null;
		}
		finally { _gate.Release(); }
	}
}

internal sealed class WindowsGameExecutionQosProcess(SafeProcessHandle handle) : IGameExecutionQosProcess
{
	private const int ProcessPowerThrottling = 4;
	private const uint Synchronize = 0x100000;
	private const uint QueryLimitedInformation = 0x1000;
	private const uint SetInformation = 0x0200;
	private const uint WaitTimeout = 258;

	internal static IGameExecutionQosProcess? Open(GameExecutionQosTarget target)
	{
		if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return null;
		SafeProcessHandle process = OpenProcess(Synchronize | QueryLimitedInformation | SetInformation, false, target.ProcessId);
		if (!process.IsInvalid && GetProcessTimes(process, out long created, out _, out _, out _)
			&& created == target.CreationFileTimeUtc && WaitForSingleObject(process, 0) == WaitTimeout)
			return new WindowsGameExecutionQosProcess(process);
		process.Dispose();
		return null;
	}

	public bool IsAlive => WaitForSingleObject(handle, 0) == WaitTimeout;
	public void Dispose() => handle.Dispose();

	public GameExecutionQosState? Read()
	{
		var state = new PowerThrottling { Version = 1 };
		return GetProcessInformation(handle, ProcessPowerThrottling, ref state, 12)
			? new GameExecutionQosState(state.Control, state.State) : null;
	}

	public bool Write(GameExecutionQosState desired)
	{
		var state = new PowerThrottling { Version = 1, Control = desired.Control, State = desired.State };
		return SetProcessInformation(handle, ProcessPowerThrottling, ref state, 12);
	}

	internal static string? ReadSource()
	{
		if (!GetSystemPowerStatus(out var status)) return null;
		if (status.Ac == 1) return "ac";
		return status.Ac == 0 && (status.BatteryFlags & 128) == 0 ? "battery" : null;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PowerThrottling { public uint Version, Control, State; }

	[StructLayout(LayoutKind.Sequential)]
	private struct PowerStatus
	{
		public byte Ac, BatteryFlags, BatteryPercent, Saving;
		public uint RemainingTime, FullTime;
	}

	[DllImport("kernel32.dll")]
	private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
	[DllImport("kernel32.dll")]
	private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
	[DllImport("kernel32.dll")]
	private static extern uint WaitForSingleObject(SafeProcessHandle process, uint timeout);
	[DllImport("kernel32.dll")]
	private static extern bool GetProcessInformation(SafeProcessHandle process, int information, ref PowerThrottling state, uint size);
	[DllImport("kernel32.dll")]
	private static extern bool SetProcessInformation(SafeProcessHandle process, int information, ref PowerThrottling state, uint size);
	[DllImport("kernel32.dll")]
	private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
