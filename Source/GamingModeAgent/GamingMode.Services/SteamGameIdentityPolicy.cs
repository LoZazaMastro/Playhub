using System;
using System.Collections.Generic;
using System.IO;

namespace GamingMode.Services;

internal static class SteamGameIdentityPolicy
{
	public static bool IsSteamClient(string? processName)
	{
		string name = Path.GetFileNameWithoutExtension(processName ?? "");
		return name.Equals("steam", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("steamservice", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("GameOverlayUI", StringComparison.OrdinalIgnoreCase);
	}

	// Tracking is logged after process creation, to second precision. A newer
	// process with the same PID cannot inherit an earlier game's identity.
	public static bool IsCurrentProcess(DateTime processStarted, DateTime trackedAt)
		=> processStarted <= trackedAt.AddSeconds(2);

	public static bool IsTrackedWindow(int trackedProcessId, int windowProcessId, IReadOnlyDictionary<int, int> parents)
	{
		if (trackedProcessId == windowProcessId) return true;
		int current = windowProcessId;
		HashSet<int> visited = new() { current };
		for (int depth = 0; depth < 16 && parents.TryGetValue(current, out int parent) && parent > 0; depth++)
		{
			if (parent == trackedProcessId) return true;
			if (!visited.Add(parent)) break;
			current = parent;
		}
		return false;
	}
}
