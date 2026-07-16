using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.Win32;

namespace GamingMode.Services;

public sealed class ShellTools
{
	private const string WinlogonPath = "Software\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon";

	private readonly FileLogger _logger;

	public ShellTools(FileLogger logger)
	{
		_logger = logger;
	}

	public void SetShellForMode(ModeKind mode)
	{
		if (mode == ModeKind.Gaming)
		{
			SetGamingShell();
		}
		else
		{
			RestoreExplorerShell();
		}
	}

	public void SetGamingShell()
	{
		string text = "\"" + ResolveSelfExecutable() + "\" shell";
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon");
		registryKey.SetValue("Shell", text, RegistryValueKind.String);
		_logger.Info("Gaming shell configured: " + text);
	}

	public void RestoreExplorerShell()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon", writable: true);
		registryKey?.DeleteValue("Shell", throwOnMissingValue: false);
		_logger.Info("Explorer shell restored for next sign-in.");
	}

	public void BeginLogoff()
	{
		Task.Run(async delegate
		{
			await Task.Delay(750);
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/l",
					UseShellExecute = false,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden
				});
				_logger.Info("Windows sign-out requested.");
			}
			catch (Exception exception)
			{
				_logger.Error("Failed to request Windows sign-out.", exception);
			}
		});
	}

	public void BeginRestart()
	{
		Task.Run(async delegate
		{
			await Task.Delay(750);
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/r /t 0",
					UseShellExecute = false,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden
				});
				_logger.Info("Windows restart requested.");
			}
			catch (Exception exception)
			{
				_logger.Error("Failed to request Windows restart.", exception);
			}
		});
	}

	private static string ResolveSelfExecutable()
	{
		string processPath = Environment.ProcessPath;
		if (!string.IsNullOrWhiteSpace(processPath) && Path.GetExtension(processPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return processPath;
		}
		return Path.Combine(AppContext.BaseDirectory, "GamingMode.exe");
	}
}
