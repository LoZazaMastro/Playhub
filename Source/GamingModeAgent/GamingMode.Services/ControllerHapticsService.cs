using System.Collections.Concurrent;
using Windows.Devices.Haptics;
using Windows.Gaming.Input;

namespace GamingMode.Services;

public sealed class ControllerHapticsService : IDisposable
{
	private readonly SdlControllerHaptics _sdl;
	private readonly SteamUiHapticsGate _uiGate = new();
	private readonly ConcurrentDictionary<string, int> _pulseGenerations = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Gamepad> _gamepads = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, RawGameController> _rawControllers = new(StringComparer.Ordinal);
	private readonly FileLogger _logger;
	private long _latestRequestId;

	public ControllerHapticsService(FileLogger logger)
	{
		_logger = logger;
		_sdl = new SdlControllerHaptics(logger);
		Gamepad.GamepadAdded += OnGamepadAdded;
		Gamepad.GamepadRemoved += OnGamepadRemoved;
		RawGameController.RawGameControllerAdded += OnRawControllerAdded;
		RawGameController.RawGameControllerRemoved += OnRawControllerRemoved;
		foreach (Gamepad gamepad in Gamepad.Gamepads) CacheGamepad(gamepad);
		foreach (RawGameController raw in RawGameController.RawGameControllers) CacheRawController(raw);
	}

	public ControllerHapticResult PlayPattern(
		int steamControllerIndex,
		int vendorId,
		int productId,
		int side,
		double magnitude,
		string? action,
		long requestId = 0)
	{
		if (!TryAcceptRequest(requestId)) return new(true, "stale-request");
		if (!TryEnterSteamUi(out ControllerHapticResult? blocked)) return blocked!;
		if (!TryHardwareIds(vendorId, productId, out ushort vendor, out ushort product))
		{
			return new(false, "missing-hardware-id");
		}

		double strength = Math.Clamp(magnitude, 0.0, 1.0);
		IReadOnlyList<ControllerHapticStep> pattern = ControllerHapticPatterns.ForAction(action, side);
		try
		{
			if (_sdl.PlayPattern(vendor, product, strength, pattern, _uiGate.CanPlay))
			{
				return new(true, "steam-sdl-pattern", DescribeConnection(vendor, product));
			}
			if (!TryEnterSteamUi(out blocked)) return blocked!;

			ControllerHapticStep first = pattern[0];
			return PulseWindows(
				vendor,
				product,
				first.Side,
				Math.Clamp(strength * first.Scale, 0, 1),
				first.DurationMs);
		}
		catch (Exception exception)
		{
			_logger.Error($"Controller haptic pattern failed for Steam index {steamControllerIndex}, {vendorId:x4}:{productId:x4}.", exception);
			return new(false, "windows-haptic-error");
		}
	}

	public ControllerHapticResult Pulse(
		int steamControllerIndex,
		int vendorId,
		int productId,
		int side,
		double magnitude,
		int durationMs,
		long requestId = 0)
	{
		if (!TryAcceptRequest(requestId)) return new(true, "stale-request");
		if (!TryEnterSteamUi(out ControllerHapticResult? blocked)) return blocked!;
		if (!TryHardwareIds(vendorId, productId, out ushort vendor, out ushort product))
		{
			return new(false, "missing-hardware-id");
		}

		double strength = Math.Clamp(magnitude, 0.0, 1.0);
		int duration = Math.Clamp(durationMs, 8, 250);
		try
		{
			ControllerHapticStep[] pattern = [new(side, 1, duration, 0)];
			if (_sdl.PlayPattern(vendor, product, strength, pattern, _uiGate.CanPlay))
			{
				return new(true, "steam-sdl", DescribeConnection(vendor, product));
			}
			if (!TryEnterSteamUi(out blocked)) return blocked!;
			return PulseWindows(vendor, product, side, strength, duration);
		}
		catch (Exception exception)
		{
			_logger.Error($"Controller haptic failed for Steam index {steamControllerIndex}, {vendorId:x4}:{productId:x4}.", exception);
			return new(false, "windows-haptic-error");
		}
	}

	public ControllerHapticResult Stop(int vendorId = 0, int productId = 0, long requestId = 0)
	{
		if (!TryAcceptRequest(requestId)) return new(true, "stale-request");
		if ((vendorId != 0 || productId != 0) &&
			!TryHardwareIds(vendorId, productId, out _, out _))
		{
			return new(false, "missing-hardware-id");
		}

		ushort vendor = (ushort)vendorId;
		ushort product = (ushort)productId;
		bool stopped = _sdl.Stop(vendor, product);
		foreach (Gamepad gamepad in Gamepad.Gamepads) CacheGamepad(gamepad);
		foreach ((string identity, Gamepad gamepad) in _gamepads)
		{
			try
			{
				RawGameController? raw = RawGameController.FromGameController(gamepad);
				if (raw is null || !Matches(raw, vendor, product)) continue;
				_pulseGenerations.AddOrUpdate(identity, 1, (_, current) => unchecked(current + 1));
				gamepad.Vibration = new GamepadVibration(0, 0, 0, 0);
				stopped = true;
			}
			catch { }
		}

		foreach (RawGameController raw in RawGameController.RawGameControllers) CacheRawController(raw);
		foreach (RawGameController raw in _rawControllers.Values)
		{
			if (!Matches(raw, vendor, product)) continue;
			foreach (SimpleHapticsController haptics in raw.SimpleHapticsControllers)
			{
				string key = raw.NonRoamableId + ":" + haptics.Id;
				_pulseGenerations.AddOrUpdate(key, 1, (_, current) => unchecked(current + 1));
				try
				{
					haptics.StopFeedback();
					stopped = true;
				}
				catch { }
			}
		}

		return new(true, stopped ? "stopped" : "already-stopped");
	}

	private bool TryEnterSteamUi(out ControllerHapticResult? blocked)
	{
		SteamUiHapticsState state = _uiGate.ReadState();
		if (state == SteamUiHapticsState.Allowed)
		{
			blocked = null;
			return true;
		}

		_sdl.Stop();
		StopWindows();
		blocked = new(true, "steam-ui-only", state.ToString());
		return false;
	}

	private static string? DescribeConnection(ushort vendor, ushort product)
	{
		return vendor == 0x054c && product is 0x0ce6 or 0x0df2
			? "DualSense (basic rumble)"
			: null;
	}

	private bool TryAcceptRequest(long requestId)
	{
		if (requestId <= 0) return true;
		while (true)
		{
			long current = Interlocked.Read(ref _latestRequestId);
			if (requestId <= current) return false;
			if (Interlocked.CompareExchange(ref _latestRequestId, requestId, current) == current) return true;
		}
	}

	private ControllerHapticResult PulseWindows(
		ushort vendor,
		ushort product,
		int side,
		double strength,
		int duration)
	{
		foreach (Gamepad gamepad in Gamepad.Gamepads) CacheGamepad(gamepad);
		var gamepads = _gamepads.Values
			.Select(gamepad => new { Gamepad = gamepad, Raw = RawGameController.FromGameController(gamepad) })
			.Where(item => item.Raw is not null && Matches(item.Raw, vendor, product))
			.ToArray();

		if (gamepads.Length > 1) return new(false, "ambiguous-gamepad");
		if (gamepads.Length == 1)
		{
			var selected = gamepads[0];
			string identity = selected.Raw!.NonRoamableId;
			(double left, double right) = Motors(side, strength);
			selected.Gamepad.Vibration = new GamepadVibration(left, right, 0, 0);
			ScheduleGamepadStop(selected.Gamepad, identity, duration);
			return new(true, "windows-gamepad", selected.Raw.DisplayName);
		}

		foreach (RawGameController raw in RawGameController.RawGameControllers) CacheRawController(raw);
		var rawControllers = _rawControllers.Values
			.Where(raw => Matches(raw, vendor, product) && raw.SimpleHapticsControllers.Count > 0)
			.ToArray();

		if (rawControllers.Length > 1) return new(false, "ambiguous-raw-controller");
		if (rawControllers.Length == 0) return new(false, "unsupported-by-windows");

		RawGameController rawController = rawControllers[0];
		bool sent = false;
		foreach (SimpleHapticsController haptics in rawController.SimpleHapticsControllers)
		{
			SimpleHapticsControllerFeedback? feedback = PreferredFeedback(haptics);
			if (feedback is null) continue;
			if (haptics.IsPlayDurationSupported)
			{
				haptics.SendHapticFeedbackForDuration(feedback, strength, TimeSpan.FromMilliseconds(duration));
			}
			else
			{
				haptics.SendHapticFeedback(feedback, strength);
				ScheduleSimpleHapticsStop(haptics, rawController.NonRoamableId, duration);
			}
			sent = true;
		}

		return sent
			? new(true, "windows-simple-haptics", rawController.DisplayName)
			: new(false, "no-supported-waveform", rawController.DisplayName);
	}

	private static bool TryHardwareIds(int vendorId, int productId, out ushort vendor, out ushort product)
	{
		vendor = 0;
		product = 0;
		if (vendorId is <= 0 or > ushort.MaxValue || productId is <= 0 or > ushort.MaxValue) return false;
		vendor = (ushort)vendorId;
		product = (ushort)productId;
		return true;
	}

	private static bool Matches(RawGameController raw, ushort vendor, ushort product) =>
		(vendor == 0 || raw.HardwareVendorId == vendor) &&
		(product == 0 || raw.HardwareProductId == product);

	private static (double Left, double Right) Motors(int side, double strength) => side switch
	{
		0 => (strength, strength * 0.04),
		1 => (strength * 0.04, strength),
		_ => (strength, strength)
	};

	private void OnGamepadAdded(object? sender, Gamepad gamepad) => CacheGamepad(gamepad);

	private void OnGamepadRemoved(object? sender, Gamepad gamepad)
	{
		try
		{
			RawGameController? raw = RawGameController.FromGameController(gamepad);
			if (raw is not null) _gamepads.TryRemove(raw.NonRoamableId, out _);
		}
		catch { }
	}

	private void OnRawControllerAdded(object? sender, RawGameController raw) => CacheRawController(raw);

	private void OnRawControllerRemoved(object? sender, RawGameController raw)
	{
		_rawControllers.TryRemove(raw.NonRoamableId, out _);
	}

	private void CacheGamepad(Gamepad gamepad)
	{
		try
		{
			RawGameController? raw = RawGameController.FromGameController(gamepad);
			if (raw is not null) _gamepads[raw.NonRoamableId] = gamepad;
		}
		catch { }
	}

	private void CacheRawController(RawGameController raw)
	{
		try { _rawControllers[raw.NonRoamableId] = raw; }
		catch { }
	}

	private static SimpleHapticsControllerFeedback? PreferredFeedback(SimpleHapticsController controller)
	{
		ushort[] preference =
		[
			KnownSimpleHapticsControllerWaveforms.Click,
			KnownSimpleHapticsControllerWaveforms.Press,
			KnownSimpleHapticsControllerWaveforms.RumbleContinuous,
			KnownSimpleHapticsControllerWaveforms.BuzzContinuous
		];
		foreach (ushort waveform in preference)
		{
			SimpleHapticsControllerFeedback? match = controller.SupportedFeedback.FirstOrDefault(item => item.Waveform == waveform);
			if (match is not null) return match;
		}
		return null;
	}

	private void ScheduleGamepadStop(Gamepad gamepad, string identity, int durationMs)
	{
		int generation = _pulseGenerations.AddOrUpdate(identity, 1, (_, current) => unchecked(current + 1));
		_ = Task.Run(async () =>
		{
			await WaitUntilStoppedOrUnsafe(durationMs).ConfigureAwait(false);
			if (_pulseGenerations.TryGetValue(identity, out int current) && current == generation)
			{
				try { gamepad.Vibration = new GamepadVibration(0, 0, 0, 0); }
				catch { }
			}
		});
	}

	private void ScheduleSimpleHapticsStop(SimpleHapticsController controller, string identity, int durationMs)
	{
		string key = identity + ":" + controller.Id;
		int generation = _pulseGenerations.AddOrUpdate(key, 1, (_, current) => unchecked(current + 1));
		_ = Task.Run(async () =>
		{
			await WaitUntilStoppedOrUnsafe(durationMs).ConfigureAwait(false);
			if (_pulseGenerations.TryGetValue(key, out int current) && current == generation)
			{
				try { controller.StopFeedback(); }
				catch { }
			}
		});
	}

	private async Task WaitUntilStoppedOrUnsafe(int durationMs)
	{
		long deadline = Environment.TickCount64 + durationMs;
		while (_uiGate.CanPlay())
		{
			int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
			if (remaining == 0) return;
			await Task.Delay(Math.Min(8, remaining)).ConfigureAwait(false);
		}
	}

	private void StopWindows()
	{
		foreach (Gamepad gamepad in Gamepad.Gamepads)
		{
			try { gamepad.Vibration = new GamepadVibration(0, 0, 0, 0); }
			catch { }
		}
		foreach (RawGameController raw in RawGameController.RawGameControllers)
		{
			foreach (SimpleHapticsController haptics in raw.SimpleHapticsControllers)
			{
				try { haptics.StopFeedback(); }
				catch { }
			}
		}
	}

	public void Dispose()
	{
		Stop();
		_sdl.Dispose();
		Gamepad.GamepadAdded -= OnGamepadAdded;
		Gamepad.GamepadRemoved -= OnGamepadRemoved;
		RawGameController.RawGameControllerAdded -= OnRawControllerAdded;
		RawGameController.RawGameControllerRemoved -= OnRawControllerRemoved;
	}
}

public sealed record ControllerHapticResult(bool Handled, string Path, string? Device = null);
