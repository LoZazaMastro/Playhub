using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using GamingMode.Models;
using Microsoft.Win32;

namespace GamingMode.Services;

public sealed class ProcessTools
{
	private readonly record struct ServiceQueryState(bool Exists, bool Running);

	private readonly record struct ScResult(int ExitCode, string Output);

	private static readonly string[] LaunchableStartupExtensions = new string[5] { ".lnk", ".exe", ".bat", ".cmd", ".ps1" };

	private static readonly string[] BlockedStartupTokens = new string[6] { "Gaming Mode Agent", "GamingMode", "PluginLoader", "PluginLoader_noconsole", "Decky Loader", "decky-loader" };

	private static readonly (string Name, string Label)[] InputCompatibilityServices = new(string, string)[11]
	{
		("hidserv", "Human Interface Device Service"),
		("PlugPlay", "Plug and Play"),
		("DeviceAssociationService", "Device Association Service"),
		("DeviceInstall", "Device Install Service"),
		("DsmSvc", "Device Setup Manager"),
		("GameInputSvc", "GameInput Service"),
		("XboxGipSvc", "Xbox Accessory Management Service"),
		("bthserv", "Bluetooth Support Service"),
		("BthAvctpSvc", "Bluetooth AVCTP Service"),
		("BTAGService", "Bluetooth Audio Gateway Service"),
		("Steam Client Service", "Steam Client Service")
	};

	private static readonly (string Name, string Label)[] SunshineCompatibilityServices = new(string, string)[23]
	{
		("AudioEndpointBuilder", "Windows Audio Endpoint Builder"),
		("Audiosrv", "Windows Audio"),
		("MMCSS", "Multimedia Class Scheduler"),
		("QWAVE", "Quality Windows Audio Video Experience"),
		("ApxSvc", "Windows Virtual Audio Device Proxy"),
		("SunshineService", "Sunshine Service"),
		("ApolloService", "Apollo Service"),
		("VibepolloService", "Vibepollo Service"),
		("DisplayEnhancementService", "Display Enhancement Service"),
		("GraphicsPerfSvc", "Graphics Performance Service"),
		("DolbyDAXAPI", "Dolby DAX API Service"),
		("DolbyDAXAPIService", "Dolby DAX API Service"),
		("DolbyDAX2API", "Dolby DAX 2 API Service"),
		("DolbyDAX2APIService", "Dolby DAX 2 API Service"),
		("DolbyDAX3API", "Dolby DAX 3 API Service"),
		("DolbyDAX3APIService", "Dolby DAX 3 API Service"),
		("DolbyDAX4API", "Dolby DAX 4 API Service"),
		("DolbyDAX4APIService", "Dolby DAX 4 API Service"),
		("RtkAudioUniversalService", "Realtek Audio Universal Service"),
		("NahimicService", "Nahimic Service"),
		("A-Volute.Nahimic", "A-Volute Nahimic Service"),
		("BthAvctpSvc", "Bluetooth AVCTP Service"),
		("BTAGService", "Bluetooth Audio Gateway Service")
	};

	private static readonly (string Name, string Label)[] DeckyPluginHelperCompatibilityServices = new(string, string)[10]
	{
		("Dnscache", "DNS Client"),
		("Dhcp", "DHCP Client"),
		("NlaSvc", "Network Location Awareness"),
		("netprofm", "Network List Service"),
		("Wcmsvc", "Windows Connection Manager"),
		("WinHttpAutoProxySvc", "WinHTTP Web Proxy Auto-Discovery Service"),
		("CryptSvc", "Cryptographic Services"),
		("BITS", "Background Intelligent Transfer Service"),
		("LanmanWorkstation", "Workstation"),
		("Winmgmt", "Windows Management Instrumentation")
	};

	private readonly FileLogger _logger;

	public ProcessTools(FileLogger logger)
	{
		_logger = logger;
	}

	public ProcessState GetState(params string[] processNames)
	{
		Process[] array = processNames.SelectMany(Process.GetProcessesByName).DistinctBy((Process process) => process.Id).ToArray();
		return new ProcessState
		{
			Running = (array.Length != 0),
			ProcessIds = array.Select((Process process) => process.Id).Order().ToArray(),
			Path = array.Select(TryGetMainModulePath).FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path))
		};
	}

	public bool EnsureProcess(string? configuredPath, string[] fallbackPaths, string arguments, params string[] processNames)
	{
		return EnsureProcessCore(configuredPath, fallbackPaths, arguments, null, processNames);
	}

	public bool EnsureProcessWithEnvironment(string? configuredPath, string[] fallbackPaths, string arguments, IReadOnlyDictionary<string, string> environment, params string[] processNames)
	{
		return EnsureProcessCore(configuredPath, fallbackPaths, arguments, environment, processNames);
	}

	private bool EnsureProcessCore(string? configuredPath, string[] fallbackPaths, string arguments, IReadOnlyDictionary<string, string>? environment, params string[] processNames)
	{
		if (GetState(processNames).Running)
		{
			return true;
		}
		string text = ResolvePath(configuredPath, fallbackPaths);
		if (text == null)
		{
			_logger.Info("No executable found for " + string.Join("/", processNames) + ".");
			return false;
		}
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = text,
				Arguments = arguments,
				UseShellExecute = false,
				WorkingDirectory = (Path.GetDirectoryName(text) ?? Environment.CurrentDirectory)
			};
			if (environment != null)
			{
				foreach (KeyValuePair<string, string> item in environment)
				{
					processStartInfo.Environment[item.Key] = item.Value;
				}
			}
			Process.Start(processStartInfo);
			_logger.Info(("Started " + text + " " + arguments).Trim());
			return true;
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to start " + text + ".", exception);
			return false;
		}
	}

	public int StartCustomGamingApps(IEnumerable<GamingStartupApp> apps)
	{
		int num = 0;
		foreach (GamingStartupApp item in apps.Where((GamingStartupApp app) => app.Enabled))
		{
			if (string.IsNullOrWhiteSpace(item.Path))
			{
				_logger.Info("Skipped custom gaming app " + DisplayName(item) + " because no path is configured.");
				continue;
			}
			string text = Environment.ExpandEnvironmentVariables(item.Path).Trim().Trim('"');
			if (!File.Exists(text))
			{
				_logger.Info($"Skipped custom gaming app {DisplayName(item)} because the path was not found: {text}.");
				continue;
			}
			string text2 = ResolveCustomProcessName(item, text);
			if (!string.IsNullOrWhiteSpace(text2) && Process.GetProcessesByName(text2).Length != 0)
			{
				_logger.Info($"Skipped custom gaming app {DisplayName(item)} because {text2} is already running.");
				continue;
			}
			try
			{
				string text3 = Environment.ExpandEnvironmentVariables(item.WorkingDirectory ?? "").Trim().Trim('"');
				if (string.IsNullOrWhiteSpace(text3) || !Directory.Exists(text3))
				{
					text3 = Path.GetDirectoryName(text) ?? Environment.CurrentDirectory;
				}
				Process.Start(new ProcessStartInfo
				{
					FileName = text,
					Arguments = Environment.ExpandEnvironmentVariables(item.Arguments ?? ""),
					WorkingDirectory = text3,
					UseShellExecute = true,
					WindowStyle = (item.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal)
				});
				num++;
				_logger.Info("Started custom gaming app " + DisplayName(item) + ".");
				if (item.DelayAfterStartMs > 0)
				{
					Thread.Sleep(item.DelayAfterStartMs);
				}
			}
			catch (Exception exception)
			{
				_logger.Error("Failed to start custom gaming app " + DisplayName(item) + ".", exception);
			}
		}
		return num;
	}

	public int CleanupDeckyOrphanedForks()
	{
		if (!GetState("PluginLoader", "PluginLoader_noconsole").Running)
		{
			return 0;
		}
		int num = RunPowerShellInteger("$items = @(Get-CimInstance Win32_Process -Filter \"Name='PluginLoader_noconsole.exe' OR Name='PluginLoader.exe'\" -ErrorAction SilentlyContinue)\nif ($items.Count -eq 0) {\n  [Console]::Out.WriteLine('0')\n  exit 0\n}\n\n$ids = @{}\nforeach ($item in $items) {\n  $ids[[int]$item.ProcessId] = $true\n}\n\n$count = 0\nforeach ($item in $items) {\n  $commandLine = [string]$item.CommandLine\n  if ($commandLine -notlike '*--multiprocessing-fork*') {\n    continue\n  }\n\n  $parentId = [int]$item.ParentProcessId\n  $parentIsPluginLoader = $ids.ContainsKey($parentId)\n  if (-not $parentIsPluginLoader) {\n    $parent = Get-Process -Id $parentId -ErrorAction SilentlyContinue\n    if ($null -ne $parent -and $parent.ProcessName -like 'PluginLoader*') {\n      $parentIsPluginLoader = $true\n    }\n  }\n\n  if ($parentIsPluginLoader) {\n    continue\n  }\n\n  Stop-Process -Id ([int]$item.ProcessId) -Force -ErrorAction SilentlyContinue\n  $count++\n}\n\n[Console]::Out.WriteLine($count)", "Decky orphan fork cleanup");
		if (num > 0)
		{
			_logger.Info($"Cleaned {num} orphaned Decky multiprocessing process(es).");
		}
		return num;
	}

	public bool LaunchOrFocusSteamGamepad(string? configuredPath, string[] fallbackPaths, string arguments)
	{
		if (!GetState("steam").Running)
		{
			return EnsureProcess(configuredPath, fallbackPaths, BuildBigPictureArguments(arguments), "steam");
		}
		if (OpenUri("steam://open/bigpicture"))
		{
			return true;
		}
		return EnsureProcess(configuredPath, fallbackPaths, BuildBigPictureArguments(arguments), "steam");
	}

	// Steam deve partire direttamente in Big Picture. Ci limitiamo a garantire
	// l'ingresso in Big Picture (-bigpicture) se l'utente non usa gia' -gamepadui
	// o -bigpicture. NON aggiungiamo MAI "-silent": quel flag fa partire Steam
	// senza portare la finestra in primo piano, quindi la Big Picture resta senza
	// focus di input (controller e tastiera morti finche' non si clicca col
	// mouse). Era la causa della regressione.
	private static string BuildBigPictureArguments(string arguments)
	{
		string text = (arguments ?? "").Trim();
		bool entersBigPicture = text.Contains("-gamepadui", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("-bigpicture", StringComparison.OrdinalIgnoreCase);
		if (!entersBigPicture)
		{
			text = (text + " -bigpicture").Trim();
		}
		return text;
	}

	public bool StartExplorer()
	{
		if (GetState("explorer").Running)
		{
			return true;
		}
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				UseShellExecute = true
			});
			_logger.Info("Explorer started.");
			return true;
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to start Explorer.", exception);
			return false;
		}
	}

	public int RunUserStartupApps()
	{
		int num = 0;
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
		if (Directory.Exists(folderPath))
		{
			foreach (string item in Directory.EnumerateFiles(folderPath))
			{
				if (!ShouldSkipStartupFile(item) && StartShellTarget(item))
				{
					num++;
				}
			}
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (registryKey != null)
			{
				string[] valueNames = registryKey.GetValueNames();
				foreach (string text in valueNames)
				{
					string text2 = registryKey.GetValue(text)?.ToString();
					if (!ShouldSkipStartupCommand(text, text2) && text2 != null && StartCommandLine(text2))
					{
						num++;
					}
				}
			}
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to restore registry startup apps.", exception);
		}
		return num;
	}

	public bool OpenUri(string uri)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = uri,
				UseShellExecute = true
			});
			_logger.Info("Opened URI " + uri + ".");
			return true;
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to open URI " + uri + ".", exception);
			return false;
		}
	}

	public int EnsureInputCompatibilityServices()
	{
		return EnsureServices(InputCompatibilityServices, "input compatibility");
	}

	public int EnsureSunshineCompatibilityServices()
	{
		return EnsureServices(SunshineCompatibilityServices, "Sunshine compatibility");
	}

	public int EnsureDeckyPluginHelperCompatibilityServices()
	{
		return EnsureServices(DeckyPluginHelperCompatibilityServices, "Decky plugin helper compatibility");
	}

	public IReadOnlyDictionary<string, string> BuildDeckyPluginHelperEnvironment()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string folderPath3 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string value = Path.GetTempPath().TrimEnd('\\');
		string path = Path.Combine(folderPath, "homebrew");
		string[] helperPaths = new string[7]
		{
			Path.Combine(path, "plugins", "ThemeDeck"),
			Path.Combine(path, "plugins", "ThemeDeck", "bin"),
			Path.Combine(path, "plugins", "launch-curtain"),
			Path.Combine(path, "plugins", "launch-curtain", "helpers"),
			Path.Combine(path, "plugins", "Launch Curtain"),
			Path.Combine(path, "plugins", "Launch Curtain", "helpers"),
			Path.Combine(path, "services")
		};
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["PATH"] = BuildDesktopLikePath(helperPaths),
			["Path"] = BuildDesktopLikePath(helperPaths),
			["USERPROFILE"] = folderPath,
			["HOME"] = folderPath,
			["APPDATA"] = folderPath2,
			["LOCALAPPDATA"] = folderPath3,
			["TEMP"] = value,
			["TMP"] = value,
			["PYTHONIOENCODING"] = "utf-8",
			["PYTHONUTF8"] = "1"
		};
		_logger.Info("Prepared Decky plugin helper environment for Gaming Mode.");
		return result;
	}

	private int EnsureServices((string Name, string Label)[] services, string purpose)
	{
		int num = 0;
		for (int i = 0; i < services.Length; i++)
		{
			(string, string) tuple = services[i];
			ServiceQueryState serviceQueryState = QueryService(tuple.Item1);
			if (!serviceQueryState.Exists)
			{
				_logger.Info(tuple.Item2 + " (" + tuple.Item1 + ") is not installed.");
				continue;
			}
			if (serviceQueryState.Running)
			{
				num++;
				_logger.Info(tuple.Item2 + " (" + tuple.Item1 + ") is already running.");
				continue;
			}
			if (IsServiceDisabled(tuple.Item1))
			{
				RunSc("config", tuple.Item1, "start=", "demand");
			}
			if (!StartService(tuple.Item1))
			{
				_logger.Info(tuple.Item2 + " (" + tuple.Item1 + ") could not be started without elevation or is disabled.");
				continue;
			}
			num++;
			_logger.Info($"{tuple.Item2} ({tuple.Item1}) started for {purpose}.");
		}
		return num;
	}

	private bool StartShellTarget(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true,
				WindowStyle = ProcessWindowStyle.Minimized
			});
			_logger.Info("Started startup item " + path + ".");
			return true;
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to start startup item " + path + ".", exception);
			return false;
		}
	}

	private bool StartCommandLine(string command)
	{
		try
		{
			if (!TrySplitCommandLine(command, out string fileName, out string arguments))
			{
				_logger.Info("Skipped unreadable startup command " + command + ".");
				return false;
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Minimized
			});
			_logger.Info("Started startup command " + command + ".");
			return true;
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to start startup command " + command + ".", exception);
			return false;
		}
	}

	private bool ShouldSkipStartupFile(string path)
	{
		try
		{
			string fileName = Path.GetFileName(path);
			if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
			{
				_logger.Info("Skipped startup desktop.ini.");
				return true;
			}
			FileAttributes attributes = File.GetAttributes(path);
			if (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System))
			{
				_logger.Info("Skipped hidden/system startup item " + path + ".");
				return true;
			}
			string extension = Path.GetExtension(path);
			if (!LaunchableStartupExtensions.Contains<string>(extension, StringComparer.OrdinalIgnoreCase))
			{
				_logger.Info("Skipped non-launchable startup item " + path + ".");
				return true;
			}
			string text = (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? TryResolveShortcutTarget(path) : path);
			if (IsBlockedStartupText(fileName) || IsBlockedStartupText(text))
			{
				_logger.Info("Skipped Gaming Mode/Decky startup item " + path + ".");
				return true;
			}
			if (!string.IsNullOrWhiteSpace(text) && IsProcessAlreadyRunningForPath(text))
			{
				_logger.Info("Skipped already-running startup target " + text + ".");
				return true;
			}
			return false;
		}
		catch (Exception exception)
		{
			_logger.Error("Failed to inspect startup item " + path + ".", exception);
			return true;
		}
	}

	private bool ShouldSkipStartupCommand(string valueName, string? command)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return true;
		}
		if (IsBlockedStartupText(valueName) || IsBlockedStartupText(command))
		{
			_logger.Info("Skipped Gaming Mode/Decky startup command " + valueName + ".");
			return true;
		}
		if (TrySplitCommandLine(command, out string fileName, out string _) && IsProcessAlreadyRunningForPath(fileName))
		{
			_logger.Info("Skipped already-running startup command " + valueName + ".");
			return true;
		}
		return false;
	}

	private static bool IsBlockedStartupText(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return BlockedStartupTokens.Any((string token) => value.Contains(token, StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	private static bool IsProcessAlreadyRunningForPath(string path)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path.Trim('"'));
		if (!string.IsNullOrWhiteSpace(fileNameWithoutExtension))
		{
			return Process.GetProcessesByName(fileNameWithoutExtension).Length != 0;
		}
		return false;
	}

	private static string ResolveCustomProcessName(GamingStartupApp app, string path)
	{
		if (!string.IsNullOrWhiteSpace(app.ProcessName))
		{
			return Path.GetFileNameWithoutExtension(app.ProcessName.Trim().Trim('"'));
		}
		if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			string text = TryResolveShortcutTarget(path);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return Path.GetFileNameWithoutExtension(text);
			}
		}
		return Path.GetFileNameWithoutExtension(path);
	}

	private static string DisplayName(GamingStartupApp app)
	{
		if (!string.IsNullOrWhiteSpace(app.Name))
		{
			return app.Name;
		}
		return app.Path ?? "unnamed app";
	}

	private static string? TryResolveShortcutTarget(string shortcutPath)
	{
		object obj = null;
		object obj2 = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			if ((object)typeFromProgID == null)
			{
				return null;
			}
			obj = Activator.CreateInstance(typeFromProgID);
			obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { shortcutPath });
			return obj2?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, obj2, null)?.ToString();
		}
		catch
		{
			return null;
		}
		finally
		{
			ReleaseComObject(obj2);
			ReleaseComObject(obj);
		}
	}

	private static bool TrySplitCommandLine(string command, out string fileName, out string arguments)
	{
		string text = Environment.ExpandEnvironmentVariables(command).Trim();
		fileName = "";
		arguments = "";
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2;
		int num2;
		if (text[0] == '"')
		{
			int num = text.IndexOf('"', 1);
			if (num <= 1)
			{
				return false;
			}
			fileName = text.Substring(1, num - 1);
			text2 = text;
			num2 = num + 1;
			arguments = text2.Substring(num2, text2.Length - num2).Trim();
			return true;
		}
		int num3 = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
		if (num3 >= 0)
		{
			int num4 = num3 + 4;
			fileName = text.Substring(0, num4).Trim();
			text2 = text;
			num2 = num4;
			arguments = text2.Substring(num2, text2.Length - num2).Trim();
			return true;
		}
		int num5 = text.IndexOf(' ');
		if (num5 < 0)
		{
			fileName = text;
			return true;
		}
		fileName = text.Substring(0, num5).Trim();
		text2 = text;
		num2 = num5 + 1;
		arguments = text2.Substring(num2, text2.Length - num2).Trim();
		return !string.IsNullOrWhiteSpace(fileName);
	}

	private static void ReleaseComObject(object? value)
	{
		if (value != null && Marshal.IsComObject(value))
		{
			Marshal.FinalReleaseComObject(value);
		}
	}

	public void StopExplorer()
	{
		Process[] processesByName = Process.GetProcessesByName("explorer");
		foreach (Process process in processesByName)
		{
			try
			{
				process.CloseMainWindow();
				if (!process.WaitForExit(3000))
				{
					process.Kill(entireProcessTree: false);
				}
			}
			catch (Exception exception)
			{
				_logger.Error($"Failed to stop Explorer process {process.Id}.", exception);
			}
		}
	}

	private static ServiceQueryState QueryService(string serviceName)
	{
		ScResult scResult = RunSc("query", serviceName);
		if (scResult.ExitCode != 0)
		{
			return new ServiceQueryState(Exists: false, Running: false);
		}
		string output = scResult.Output;
		return new ServiceQueryState(Exists: true, output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsServiceDisabled(string serviceName)
	{
		ScResult scResult = RunSc("qc", serviceName);
		if (scResult.ExitCode == 0)
		{
			return scResult.Output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool StartService(string serviceName)
	{
		RunSc("start", serviceName);
		Thread.Sleep(250);
		return QueryService(serviceName).Running;
	}

	private static ScResult RunSc(string command, params string[] arguments)
	{
		try
		{
			using Process process = new Process();
			process.StartInfo = new ProcessStartInfo
			{
				FileName = "sc.exe",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			process.StartInfo.ArgumentList.Add(command);
			foreach (string item in arguments)
			{
				process.StartInfo.ArgumentList.Add(item);
			}
			process.Start();
			string text = process.StandardOutput.ReadToEnd();
			string text2 = process.StandardError.ReadToEnd();
			if (!process.WaitForExit(3000))
			{
				process.Kill(entireProcessTree: true);
				return new ScResult(-1, text + text2);
			}
			return new ScResult(process.ExitCode, text + text2);
		}
		catch (Exception ex)
		{
			return new ScResult(-1, ex.Message);
		}
	}

	public void RestartProcesses(string configuredPath, string fallbackPath, string arguments, bool killEntireProcessTree, params string[] processNames)
	{
		RestartProcessesCore(configuredPath, fallbackPath, arguments, killEntireProcessTree, null, processNames);
	}

	public void RestartProcessesWithEnvironment(string configuredPath, string fallbackPath, string arguments, bool killEntireProcessTree, IReadOnlyDictionary<string, string> environment, params string[] processNames)
	{
		RestartProcessesCore(configuredPath, fallbackPath, arguments, killEntireProcessTree, environment, processNames);
	}

	private void RestartProcessesCore(string configuredPath, string fallbackPath, string arguments, bool killEntireProcessTree, IReadOnlyDictionary<string, string>? environment, params string[] processNames)
	{
		foreach (Process item in processNames.SelectMany(Process.GetProcessesByName).DistinctBy((Process process) => process.Id))
		{
			try
			{
				item.CloseMainWindow();
				if (!item.WaitForExit(5000))
				{
					item.Kill(killEntireProcessTree);
				}
			}
			catch (Exception exception)
			{
				_logger.Error($"Failed to stop {item.ProcessName} ({item.Id}).", exception);
			}
		}
		if (environment == null)
		{
			EnsureProcess(configuredPath, new string[1] { fallbackPath }, arguments, processNames);
		}
		else
		{
			EnsureProcessWithEnvironment(configuredPath, new string[1] { fallbackPath }, arguments, environment, processNames);
		}
	}

	public string[] GetSteamFallbackPaths()
	{
		string text = TryReadRegistryValue(Registry.CurrentUser, "Software\\Valve\\Steam", "SteamExe");
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		return new string[3]
		{
			text ?? "",
			Path.Combine(folderPath, "Steam", "steam.exe"),
			Path.Combine(folderPath2, "Steam", "steam.exe")
		};
	}

	public string[] GetDeckyFallbackPaths()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string folderPath3 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		return new string[6]
		{
			Path.Combine(folderPath, "homebrew", "services", "PluginLoader_noconsole.exe"),
			Path.Combine(folderPath, "homebrew", "services", "PluginLoader.exe"),
			Path.Combine(folderPath3, "Decky Loader", "PluginLoader_noconsole.exe"),
			Path.Combine(folderPath3, "Decky Loader", "PluginLoader.exe"),
			Path.Combine(folderPath2, "Programs", "Decky Loader", "PluginLoader_noconsole.exe"),
			Path.Combine(folderPath2, "Programs", "decky-loader", "PluginLoader_noconsole.exe")
		};
	}

	public string[] GetSunshineFallbackPaths()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		return new string[10]
		{
			Path.Combine(folderPath, "Sunshine", "sunshine.exe"),
			Path.Combine(folderPath, "LizardByte", "Sunshine", "sunshine.exe"),
			Path.Combine(folderPath, "Apollo", "sunshine.exe"),
			Path.Combine(folderPath, "Apollo", "apollo.exe"),
			Path.Combine(folderPath, "Vibepollo", "sunshine.exe"),
			Path.Combine(folderPath, "Vibepollo", "vibepollo.exe"),
			Path.Combine(folderPath2, "Programs", "Apollo", "sunshine.exe"),
			Path.Combine(folderPath2, "Programs", "Apollo", "apollo.exe"),
			Path.Combine(folderPath2, "Programs", "Vibepollo", "sunshine.exe"),
			Path.Combine(folderPath2, "Programs", "Vibepollo", "vibepollo.exe")
		};
	}

	private static string? ResolvePath(string? configuredPath, string[] fallbackPaths)
	{
		return new string[1] { configuredPath ?? "" }.Concat(fallbackPaths).FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
	}

	private static string BuildDesktopLikePath(IEnumerable<string> helperPaths)
	{
		List<string> list = new List<string>();
		AddPathSegments(list, helperPaths.Where(Directory.Exists));
		AddPathSegments(list, Environment.GetEnvironmentVariable("PATH"));
		AddPathSegments(list, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine));
		AddPathSegments(list, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
		return string.Join(Path.PathSeparator, (from path in list.Select(Environment.ExpandEnvironmentVariables)
			where !string.IsNullOrWhiteSpace(path)
			select path).Distinct<string>(StringComparer.OrdinalIgnoreCase));
	}

	private static void AddPathSegments(ICollection<string> paths, IEnumerable<string> segments)
	{
		foreach (string segment in segments)
		{
			if (!string.IsNullOrWhiteSpace(segment))
			{
				paths.Add(segment.Trim().Trim('"'));
			}
		}
	}

	private static void AddPathSegments(ICollection<string> paths, string? pathValue)
	{
		if (!string.IsNullOrWhiteSpace(pathValue))
		{
			AddPathSegments(paths, pathValue.Split(Path.PathSeparator));
		}
	}

	private int RunPowerShellInteger(string script, string operation)
	{
		try
		{
			using Process process = new Process();
			process.StartInfo = new ProcessStartInfo
			{
				FileName = "powershell.exe",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			process.StartInfo.ArgumentList.Add("-NoProfile");
			process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
			process.StartInfo.ArgumentList.Add("Bypass");
			process.StartInfo.ArgumentList.Add("-Command");
			process.StartInfo.ArgumentList.Add(script);
			process.Start();
			string text = process.StandardOutput.ReadToEnd();
			string text2 = process.StandardError.ReadToEnd();
			if (!process.WaitForExit(7000))
			{
				process.Kill(entireProcessTree: true);
				_logger.Info(operation + " timed out.");
				return 0;
			}
			if (process.ExitCode != 0)
			{
				_logger.Info(operation + " failed: " + text2.Trim());
				return 0;
			}
			int result;
			return int.TryParse(text.Trim().Split(Environment.NewLine).LastOrDefault(), out result) ? result : 0;
		}
		catch (Exception exception)
		{
			_logger.Error(operation + " failed.", exception);
			return 0;
		}
	}

	private static string? TryGetMainModulePath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch
		{
			return null;
		}
	}

	private static string? TryReadRegistryValue(RegistryKey root, string keyPath, string valueName)
	{
		try
		{
			using RegistryKey registryKey = root.OpenSubKey(keyPath);
			return registryKey?.GetValue(valueName)?.ToString();
		}
		catch
		{
			return null;
		}
	}
}
