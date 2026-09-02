using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GamingMode.Services;

internal sealed class SdlControllerHaptics : IDisposable
{
	private const uint InputSubsystems = 0x00002200;
	private readonly Dictionary<uint, IntPtr> _openGamepads = new();
	private readonly ConcurrentDictionary<uint, int> _pulseGenerations = new();
	private readonly object _sync = new();
	private IntPtr _library;
	private readonly SdlGetGamepads? _getGamepads;
	private readonly SdlGetGamepadVendorForId? _getVendor;
	private readonly SdlGetGamepadProductForId? _getProduct;
	private readonly SdlOpenGamepad? _openGamepad;
	private readonly SdlRumbleGamepad? _rumbleGamepad;
	private readonly SdlUpdateGamepads? _updateGamepads;
	private readonly SdlCloseGamepad? _closeGamepad;
	private readonly SdlFree? _free;
	private readonly SdlQuitSubSystem? _quitSubSystem;

	public SdlControllerHaptics(FileLogger logger)
	{
		try
		{
			string? path = FindLibrary();
			if (path is null) return;
			_library = NativeLibrary.Load(path);
			SdlSetHint? setHint = TryLoad<SdlSetHint>("SDL_SetHint");
			if (setHint is not null && !setHint("SDL_JOYSTICK_ENHANCED_REPORTS", "1"))
			{
				logger.Info("SDL enhanced controller reports could not be enabled.");
			}

			var init = Load<SdlInit>("SDL_Init");
			_getGamepads = Load<SdlGetGamepads>("SDL_GetGamepads");
			_getVendor = Load<SdlGetGamepadVendorForId>("SDL_GetGamepadVendorForID");
			_getProduct = Load<SdlGetGamepadProductForId>("SDL_GetGamepadProductForID");
			_openGamepad = Load<SdlOpenGamepad>("SDL_OpenGamepad");
			_rumbleGamepad = Load<SdlRumbleGamepad>("SDL_RumbleGamepad");
			_updateGamepads = TryLoad<SdlUpdateGamepads>("SDL_UpdateGamepads");
			_closeGamepad = Load<SdlCloseGamepad>("SDL_CloseGamepad");
			_free = Load<SdlFree>("SDL_free");
			_quitSubSystem = Load<SdlQuitSubSystem>("SDL_QuitSubSystem");
			if (!init(InputSubsystems))
			{
				Dispose();
				return;
			}
			_updateGamepads?.Invoke();
		}
		catch (Exception exception)
		{
			logger.Error("SDL controller haptics are unavailable.", exception);
			Dispose();
		}
	}

	public bool Pulse(ushort vendorId, ushort productId, int side, double magnitude, int durationMs)
	{
		ControllerHapticStep[] pattern =
		[
			new(side, 1, Math.Clamp(durationMs, 8, 250), 0)
		];
		return PlayPattern(vendorId, productId, magnitude, pattern);
	}

	public bool PlayPattern(
		ushort vendorId,
		ushort productId,
		double magnitude,
		IReadOnlyList<ControllerHapticStep> pattern,
		Func<bool>? canContinue = null)
	{
		if (!IsReady || pattern.Count == 0 || canContinue?.Invoke() == false) return false;

		lock (_sync)
		{
			if (!TryOpen(vendorId, productId, out uint id, out IntPtr gamepad)) return false;
			int generation = _pulseGenerations.AddOrUpdate(id, 1, (_, current) => unchecked(current + 1));
			StopOpen(gamepad);
			if (!SendPulse(gamepad, pattern[0], magnitude))
			{
				StopAndClose(id, gamepad);
				return false;
			}

			SchedulePattern(id, gamepad, generation, magnitude, pattern, canContinue);
			return true;
		}
	}

	public bool Stop(ushort vendorId = 0, ushort productId = 0)
	{
		lock (_sync)
		{
			bool stopped = false;
			foreach ((uint id, IntPtr gamepad) in _openGamepads.ToArray())
			{
				if (vendorId != 0 && _getVendor?.Invoke(id) != vendorId) continue;
				if (productId != 0 && _getProduct?.Invoke(id) != productId) continue;
				_pulseGenerations.AddOrUpdate(id, 1, (_, current) => unchecked(current + 1));
				StopAndClose(id, gamepad);
				stopped = true;
			}
			return stopped;
		}
	}

	private bool IsReady =>
		_library != IntPtr.Zero &&
		_getGamepads is not null &&
		_getVendor is not null &&
		_getProduct is not null &&
		_openGamepad is not null &&
		_rumbleGamepad is not null &&
		_free is not null;

	private bool TryOpen(ushort vendorId, ushort productId, out uint id, out IntPtr gamepad)
	{
		id = 0;
		gamepad = IntPtr.Zero;
		if (!IsReady) return false;
		_updateGamepads?.Invoke();
		IntPtr ids = _getGamepads!(out int count);
		try
		{
			int matches = 0;
			for (int index = 0; index < count; index++)
			{
				uint candidate = unchecked((uint)Marshal.ReadInt32(ids, index * sizeof(uint)));
				if (_getVendor!(candidate) != vendorId || _getProduct!(candidate) != productId) continue;
				id = candidate;
				matches++;
			}

			if (matches != 1) return false;
			if (!_openGamepads.TryGetValue(id, out gamepad) || gamepad == IntPtr.Zero)
			{
				gamepad = _openGamepad!(id);
				if (gamepad == IntPtr.Zero) return false;
				_openGamepads[id] = gamepad;
				_updateGamepads?.Invoke();
			}
			return true;
		}
		finally
		{
			if (ids != IntPtr.Zero) _free!(ids);
		}
	}

	private bool SendPulse(IntPtr gamepad, ControllerHapticStep step, double magnitude)
	{
		(double left, double right) = Motors(step.Side, Math.Clamp(magnitude * step.Scale, 0, 1));
		bool sent = _rumbleGamepad!(
			gamepad,
			(ushort)Math.Round(left * ushort.MaxValue),
			(ushort)Math.Round(right * ushort.MaxValue),
			(uint)Math.Clamp(step.DurationMs, 8, 80));
		_updateGamepads?.Invoke();
		return sent;
	}

	private void SchedulePattern(
		uint id,
		IntPtr gamepad,
		int generation,
		double magnitude,
		IReadOnlyList<ControllerHapticStep> pattern,
		Func<bool>? canContinue)
	{
		_ = Task.Run(async () =>
		{
			for (int index = 0; index < pattern.Count; index++)
			{
				ControllerHapticStep step = pattern[index];
				if (!await DelayWhileAllowed(Math.Clamp(step.DurationMs, 8, 80), canContinue).ConfigureAwait(false))
				{
					lock (_sync) StopAndClose(id, gamepad);
					return;
				}
				lock (_sync)
				{
					if (!IsCurrent(id, gamepad, generation)) return;
					StopOpen(gamepad);
					if (index == pattern.Count - 1)
					{
						StopAndClose(id, gamepad);
						return;
					}
				}

				if (step.GapMs > 0 && !await DelayWhileAllowed(Math.Clamp(step.GapMs, 1, 80), canContinue).ConfigureAwait(false))
				{
					lock (_sync) StopAndClose(id, gamepad);
					return;
				}
				lock (_sync)
				{
					if (!IsCurrent(id, gamepad, generation) || canContinue?.Invoke() == false)
					{
						StopAndClose(id, gamepad);
						return;
					}
					if (!SendPulse(gamepad, pattern[index + 1], magnitude))
					{
						StopAndClose(id, gamepad);
						return;
					}
				}
			}
		});
	}

	private static async Task<bool> DelayWhileAllowed(int durationMs, Func<bool>? canContinue)
	{
		long deadline = Environment.TickCount64 + durationMs;
		while (true)
		{
			if (canContinue?.Invoke() == false) return false;
			int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
			if (remaining == 0) return true;
			await Task.Delay(Math.Min(8, remaining)).ConfigureAwait(false);
		}
	}

	private bool IsCurrent(uint id, IntPtr gamepad, int generation) =>
		_library != IntPtr.Zero &&
		_pulseGenerations.TryGetValue(id, out int current) &&
		current == generation &&
		_openGamepads.TryGetValue(id, out IntPtr open) &&
		open == gamepad;

	private void StopOpen(IntPtr gamepad)
	{
		try { _rumbleGamepad?.Invoke(gamepad, 0, 0, 0); } catch { }
		try { _updateGamepads?.Invoke(); } catch { }
	}

	private void StopAndClose(uint id, IntPtr gamepad)
	{
		if (!_openGamepads.TryGetValue(id, out IntPtr open) || open != gamepad) return;
		StopOpen(gamepad);
		try { _closeGamepad?.Invoke(gamepad); } catch { }
		_openGamepads.Remove(id);
	}

	private T Load<T>(string name) where T : Delegate =>
		Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

	private T? TryLoad<T>(string name) where T : Delegate =>
		NativeLibrary.TryGetExport(_library, name, out IntPtr address)
			? Marshal.GetDelegateForFunctionPointer<T>(address)
			: null;

	private static string? FindLibrary()
	{
		var candidates = new List<string>();
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			string? steamPath = key?.GetValue("SteamPath") as string;
			if (!string.IsNullOrWhiteSpace(steamPath)) candidates.Add(Path.Combine(steamPath, "SDL3.dll"));
		}
		catch { }

		string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		if (!string.IsNullOrWhiteSpace(programFiles)) candidates.Add(Path.Combine(programFiles, "Steam", "SDL3.dll"));
		return candidates.FirstOrDefault(File.Exists);
	}

	private static (double Left, double Right) Motors(int side, double strength) => side switch
	{
		0 => (strength, strength * 0.04),
		1 => (strength * 0.04, strength),
		_ => (strength, strength)
	};

	public void Dispose()
	{
		lock (_sync)
		{
			foreach ((uint id, IntPtr gamepad) in _openGamepads.ToArray())
			{
				_pulseGenerations.AddOrUpdate(id, 1, (_, current) => unchecked(current + 1));
				StopAndClose(id, gamepad);
			}
			try { _quitSubSystem?.Invoke(InputSubsystems); } catch { }
			if (_library != IntPtr.Zero)
			{
				try { NativeLibrary.Free(_library); } catch { }
				_library = IntPtr.Zero;
			}
		}
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool SdlSetHint(
		[MarshalAs(UnmanagedType.LPUTF8Str)] string name,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string value);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool SdlInit(uint flags);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr SdlGetGamepads(out int count);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate ushort SdlGetGamepadVendorForId(uint id);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate ushort SdlGetGamepadProductForId(uint id);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr SdlOpenGamepad(uint id);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private delegate bool SdlRumbleGamepad(IntPtr gamepad, ushort lowFrequency, ushort highFrequency, uint durationMs);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SdlUpdateGamepads();
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SdlCloseGamepad(IntPtr gamepad);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SdlFree(IntPtr memory);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SdlQuitSubSystem(uint flags);
}
