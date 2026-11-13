using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace GamingMode.Services;

internal static class SteamInstallationPath
{
	private const string ExecutableName = "steam.exe";
	private static readonly object Sync = new();
	private static string? _registeredRoot;
	private static long _refreshAt;

	internal static string? FindRoot(string? configuredPath = null)
	{
		if (!string.IsNullOrWhiteSpace(configuredPath) && ResolveRoot(configuredPath) is { } configuredRoot) return configuredRoot;
		lock (Sync)
		{
			long now = Environment.TickCount64;
			if (now < _refreshAt) return _registeredRoot;
			_registeredRoot = ReadRegisteredRoot();
			_refreshAt = now + 5_000;
			return _registeredRoot;
		}
	}

	internal static string? ResolveRoot(params string?[] candidates)
	{
		foreach (string? candidate in candidates)
		{
			if (string.IsNullOrWhiteSpace(candidate)) continue;
			try
			{
				string path = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
				if (!Path.IsPathFullyQualified(path)) continue;
				path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
				string executable = string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase)
					? path
					: Path.Combine(path, ExecutableName);
				if (File.Exists(executable)) return Path.GetDirectoryName(executable);
			}
			catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
		}
		return null;
	}

	internal static DateTime? ReadVerifiedClientBirth(string root)
	{
		string executable = Path.Combine(root, ExecutableName);
		using Process current = Process.GetCurrentProcess();
		var candidates = Process.GetProcessesByName("steam");
		DateTime? birth = null;
		try
		{
			foreach (var process in candidates)
			{
				try
				{
					if (process.SessionId != current.SessionId || process.HasExited) continue;
					if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
					if (birth.HasValue) return null;
					birth = process.StartTime;
				}
				catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
			}
			return birth;
		}
		finally { foreach (var process in candidates) process.Dispose(); }
	}

	private static string? ReadRegisteredRoot()
	{
		string? executable = null;
		string? directory = null;
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			executable = key?.GetValue("SteamExe") as string;
			directory = key?.GetValue("SteamPath") as string;
		}
		catch (Exception error) when (error is IOException or SecurityException or UnauthorizedAccessException) { }

		return ResolveRoot(executable, directory,
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
	}
}
