using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using GamingMode.Models;

namespace GamingMode.Services;

public sealed class JsonStore
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	private readonly AppPaths _paths;

	private readonly FileLogger _logger;

	private static readonly Mutex StoreMutex = new(false, "Local\\PlayhubGamingModeJsonStore");

	public JsonStore(AppPaths paths, FileLogger logger)
	{
		_paths = paths;
		_logger = logger;
	}

	public ModeConfig LoadConfig()
	{
		return WithStoreLock(() => LoadConfigUnlocked());
	}

	public void SaveConfig(ModeConfig config)
	{
		WithStoreLock(() => AtomicWrite(_paths.ConfigPath, JsonSerializer.Serialize(Normalize(config), Options)));
	}

	public ModeState LoadState()
	{
		return WithStoreLock(() => LoadStateUnlocked());
	}

	public void SaveState(ModeState state)
	{
		WithStoreLock(() => AtomicWrite(_paths.StatePath, JsonSerializer.Serialize(state, Options)));
	}

	private ModeConfig LoadConfigUnlocked()
	{
		Directory.CreateDirectory(_paths.ConfigDirectory);
		string backupPath = _paths.ConfigPath + ".bak";
		if (TryRead(_paths.ConfigPath, out ModeConfig? config, out Exception? primaryError))
		{
			return Normalize(config!);
		}
		if (primaryError != null)
		{
			_logger.Error("Config could not be loaded. Trying the backup.", primaryError);
		}
		if (TryRead(backupPath, out config, out Exception? backupError))
		{
			RestoreBackup(backupPath, _paths.ConfigPath);
			_logger.Info("Config recovered from the automatic backup.");
			return Normalize(config!);
		}
		if (backupError != null)
		{
			_logger.Error("Config backup could not be loaded.", backupError);
		}
		ModeConfig defaults = new();
		if (!File.Exists(_paths.ConfigPath) && !File.Exists(backupPath))
		{
			AtomicWrite(_paths.ConfigPath, JsonSerializer.Serialize(defaults, Options));
		}
		else
		{
			_logger.Error("Config and backup are invalid. Defaults are temporary; existing files were preserved.");
		}
		return defaults;
	}

	private ModeState LoadStateUnlocked()
	{
		Directory.CreateDirectory(_paths.ConfigDirectory);
		string backupPath = _paths.StatePath + ".bak";
		if (TryRead(_paths.StatePath, out ModeState? state, out Exception? primaryError))
		{
			return state!;
		}
		if (primaryError != null)
		{
			_logger.Error("State could not be loaded. Trying the backup.", primaryError);
		}
		if (TryRead(backupPath, out state, out Exception? backupError))
		{
			RestoreBackup(backupPath, _paths.StatePath);
			_logger.Info("State recovered from the automatic backup.");
			return state!;
		}
		if (backupError != null)
		{
			_logger.Error("State backup could not be loaded.", backupError);
		}
		return new ModeState();
	}

	private static ModeConfig Normalize(ModeConfig config)
	{
		config.Gaming ??= new GamingSettings();
		config.Gaming.Splash ??= new GamingSplashSettings();
		config.Gaming.CustomStartupApps ??= new System.Collections.Generic.List<GamingStartupApp>();
		config.Safety ??= new SafetySettings();
		return config;
	}

	private static bool TryRead<T>(string path, out T? value, out Exception? error) where T : class
	{
		value = null;
		error = null;
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
				?? throw new InvalidDataException($"{Path.GetFileName(path)} contains no data.");
			return true;
		}
		catch (Exception exception)
		{
			error = exception;
			return false;
		}
	}

	private static void AtomicWrite(string path, string contents)
	{
		string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The settings path has no directory.");
		Directory.CreateDirectory(directory);
		string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
		string backupPath = path + ".bak";
		try
		{
			WriteThrough(temporaryPath, contents);
			if (File.Exists(path))
			{
				try
				{
					File.Replace(temporaryPath, path, backupPath, true);
					return;
				}
				catch (PlatformNotSupportedException)
				{
				}
				catch (IOException)
				{
				}
				File.Copy(path, backupPath, true);
				File.Move(temporaryPath, path, true);
				return;
			}
			File.Move(temporaryPath, path);
			File.Copy(path, backupPath, true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static void RestoreBackup(string backupPath, string destinationPath)
	{
		string directory = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("The settings path has no directory.");
		string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.restore.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
		try
		{
			File.Copy(backupPath, temporaryPath, true);
			Flush(temporaryPath);
			File.Move(temporaryPath, destinationPath, true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static void WriteThrough(string path, string contents)
	{
		using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
		using StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false), 4096, true);
		writer.Write(contents);
		writer.Flush();
		stream.Flush(true);
	}

	private static void Flush(string path)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
		stream.Flush(true);
	}

	private static T WithStoreLock<T>(Func<T> action)
	{
		bool acquired = false;
		try
		{
			try
			{
				acquired = StoreMutex.WaitOne(TimeSpan.FromSeconds(5));
			}
			catch (AbandonedMutexException)
			{
				acquired = true;
			}
			if (!acquired)
			{
				throw new TimeoutException("Timed out waiting for the Gaming Mode settings lock.");
			}
			return action();
		}
		finally
		{
			if (acquired)
			{
				StoreMutex.ReleaseMutex();
			}
		}
	}

	private static void WithStoreLock(Action action)
	{
		WithStoreLock(() =>
		{
			action();
			return true;
		});
	}
}
