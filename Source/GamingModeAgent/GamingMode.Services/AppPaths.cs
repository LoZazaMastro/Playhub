using System;
using System.IO;

namespace GamingMode.Services;

public sealed class AppPaths
{
	public string ConfigDirectory { get; }

	public string ConfigPath { get; }

	public string StatePath { get; }

	public string LogPath { get; }

	private AppPaths(string configDirectory)
	{
		ConfigDirectory = configDirectory;
		ConfigPath = Path.Combine(configDirectory, "config.json");
		StatePath = Path.Combine(configDirectory, "state.json");
		LogPath = Path.Combine(configDirectory, "agent.log");
	}

	public static AppPaths Create()
	{
		return new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode"));
	}
}
