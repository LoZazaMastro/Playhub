using System;

namespace GamingMode.Models;

public sealed class ModeState
{
	public ModeKind CurrentMode { get; set; }

	public DateTimeOffset? LastAppliedAt { get; set; }

	public string? LastAction { get; set; }

	public string? LastError { get; set; }
}
