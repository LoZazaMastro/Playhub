using System;

namespace GamingMode.Models;

public sealed class ModeStatus
{
	public bool AgentRunning { get; set; }

	public ModeKind CurrentMode { get; set; }

	public ModeKind DefaultMode { get; set; }

	public ModeKind? NextBootMode { get; set; }

	public DateTimeOffset? LastAppliedAt { get; set; }

	public string? LastAction { get; set; }

	public string? LastError { get; set; }

	public ProcessState Steam { get; set; } = new ProcessState();

	public ProcessState Decky { get; set; } = new ProcessState();

	public ProcessState Sunshine { get; set; } = new ProcessState();

	public ProcessState Explorer { get; set; } = new ProcessState();

	public bool MouseCursorAutoHide { get; set; }

	public bool MouseCursorHidden { get; set; }

	public string? SplashLogoPath { get; set; }

	public string ConfigPath { get; set; } = "";

	public string[] Messages { get; set; } = Array.Empty<string>();
}
