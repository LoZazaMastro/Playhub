using GamingMode.Services;

internal static class GameAudioVolumeTests
{
	private static readonly GameAudioTarget Target = new(42, 123_456);

	public static void Verify()
	{
		var session = new Session();
		GameAudioVolumeResult Apply(int? level = null, string? token = null, Func<bool>? current = null) =>
			GameAudioVolumeService.Apply(Target, level, token, current ?? (() => true), [session]);
		var initial = Apply();
		Check(initial is { Status: "Ready", Level: 47, Token.Length: 64 }, "Read did not return the exact session volume.");
		string token = initial.Token!;
		Check(Apply(101, token).Status == "Unavailable" && session.Writes == 0, "An invalid level was sent to Core Audio.");
		Check(Apply(0, "foreign").Status == "Unavailable" && session.Writes == 0, "A foreign token could write.");
		Check(GameAudioVolumeService.Apply(Target with { CreationFileTimeUtc = Target.CreationFileTimeUtc + 1 },
			25, token, () => true, [session]).Status == "Unavailable" && session.Writes == 0, "A recycled PID accepted an old token.");
		var other = new Session { EndpointId = "hdmi" };
		Check(GameAudioVolumeService.Apply(Target, 25, token, () => true, [other]).Status == "Unavailable" && other.Writes == 0,
			"A new audio route accepted the old endpoint token.");
		Check(GameAudioVolumeService.Apply(Target, 25, token, () => true, [session, other]).Status == "Ambiguous" && session.Writes == 0 && other.Writes == 0,
			"Multiple sessions led to a partial write.");
		int rechecks = 0;
		Check(Apply(25, token, () => ++rechecks == 1).Status == "Unavailable" && session.Writes == 0, "An exited game was changed after enumeration.");
		session.Owner = 43;
		Check(Apply(25, token).Status == "Unavailable" && session.Writes == 0, "A different audio process was changed.");
		session.Owner = 42;
		Check(Apply(26, token) is { Status: "Ready", Level: 26 } && session.Writes == 1, "The exact session change lacked verified readback.");
		session.IgnoreWrites = true;
		Check(Apply(27, token).Status == "Failed" && session.Writes == 2, "Different native readback was accepted.");
		session.Readable = false;
		Check(Apply().Status == "Failed" && session.Writes == 2, "Failed readback reported success.");
	}

	private static void Check(bool valid, string message)
	{
		if (!valid) throw new Exception(message);
	}

	private sealed class Session : GameAudioVolumeService.IGameAudioSession
	{
		public string EndpointId { get; init; } = "speakers";
		public string InstanceId => "the-game-session";
		internal int Owner = 42;
		internal int Writes;
		internal bool Readable = true;
		internal bool IgnoreWrites;
		private float _level = .47f;
		public bool IsExact(int pid) => pid == Owner;
		public bool SetLevel(float value) { if (!IgnoreWrites) _level = value; Writes++; return true; }
		public bool TryReadLevel(out float level) { level = _level; return Readable; }
	}
}
