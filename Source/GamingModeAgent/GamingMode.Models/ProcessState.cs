using System;

namespace GamingMode.Models;

public sealed class ProcessState
{
	public bool Running { get; set; }

	public int[] ProcessIds { get; set; } = Array.Empty<int>();

	public string? Path { get; set; }
}
