using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;

namespace GamingMode.Services;

public sealed class ModeManager
{
	private readonly AppPaths _paths;

	private readonly JsonStore _store;

	private readonly ProcessTools _processTools;

	private readonly ShellTools _shellTools;

	private readonly CursorAutoHideService _cursorAutoHide;

	private readonly GamingWindowFocusService _windowFocus;

	private readonly SystemVolumeKeyService _volumeKeys;

	private readonly FileLogger _logger;

	public ModeManager(AppPaths paths, JsonStore store, ProcessTools processTools, ShellTools shellTools, CursorAutoHideService cursorAutoHide, GamingWindowFocusService windowFocus, SystemVolumeKeyService volumeKeys, FileLogger logger)
	{
		_paths = paths;
		_store = store;
		_processTools = processTools;
		_shellTools = shellTools;
		_cursorAutoHide = cursorAutoHide;
		_windowFocus = windowFocus;
		_volumeKeys = volumeKeys;
		_logger = logger;
	}

	public async Task ApplyBootModeAsync(bool isShellHost)
	{
		_logger.Info(isShellHost ? "Applying boot mode from Gaming Mode shell." : "Applying boot mode from startup agent.");
		ModeConfig config = _store.LoadConfig();
		ModeKind? requestedNextBootMode = config.NextBootMode;
		ModeKind mode = requestedNextBootMode ?? config.DefaultMode;
		if (SafeModeGuard.ShouldForceDesktop(_paths))
		{
			SafeModeGuard.ApplySafeDefaults(config);
			_store.SaveConfig(config);
			_shellTools.RestoreExplorerShell();
			requestedNextBootMode = null;
			mode = ModeKind.Desktop;
			_logger.Info("Safe desktop bypass was triggered.");
		}
		if (config.NextBootMode.HasValue)
		{
			config.NextBootMode = null;
			_store.SaveConfig(config);
		}
		await ApplyModeAsync(mode, $"Applied {mode} at login", interactive: false, restoreStartupApps: false);
		ModeKind modeKind = ((!requestedNextBootMode.HasValue) ? mode : config.DefaultMode);
		_shellTools.SetShellForMode(modeKind);
		_logger.Info($"Future sign-in shell set to {modeKind}.");
	}

	// RIACCENSIONE DEI SERVIZI QUANDO L'AGENTE RIPARTE.
	//
	// Dopo ogni "installa o aggiorna" l'agente viene riavviato con il solo
	// argomento "agent", quindi non passa da ApplyBootModeAsync. Senza questo,
	// il servizio delle finestre senza bordi, i tasti del volume e la scomparsa
	// del puntatore restavano spenti anche con il PC gia' in Gaming Mode: il
	// borderless smetteva di funzionare e per riaverlo bisognava riapplicare la
	// modalita' a mano.
	//
	// Qui si riaccendono SOLO i servizi: non si tocca Explorer, non si avvia
	// Steam, non si sposta nulla.
	public void ResumeGamingServices()
	{
		try
		{
			ModeConfig config = _store.LoadConfig();
			_logger.Info($"Resuming Gaming Mode services after agent restart; borderless={config.Gaming.BorderlessFullscreenWindowsInGamingMode}.");
			_volumeKeys.Start();
			_windowFocus.Start(config.Gaming.BorderlessFullscreenWindowsInGamingMode);
			if (config.Gaming.AutoHideMouseCursorInGamingMode)
			{
				_cursorAutoHide.Start(config.Gaming.AutoHideMouseCursorAfterMs);
			}
		}
		catch (Exception exception)
		{
			_logger.Error("Gaming Mode services could not be resumed after the agent restart.", exception);
		}
	}

	public Task<ApiResult> ApplyModeAsync(ModeKind mode, string action, bool interactive = true, bool restoreStartupApps = true, bool updateShell = true)
	{
		ModeConfig config = _store.LoadConfig();
		ModeState modeState = _store.LoadState();
		List<string> messages = new List<string>();
		try
		{
			if (updateShell)
			{
				_shellTools.SetShellForMode(mode);
			}
			if (mode == ModeKind.Gaming)
			{
				ApplyGamingMode(config, messages);
			}
			else
			{
				ApplyDesktopMode(config, messages, interactive, restoreStartupApps);
			}
			modeState.CurrentMode = mode;
			modeState.LastAppliedAt = DateTimeOffset.Now;
			modeState.LastAction = action;
			modeState.LastError = null;
			_store.SaveState(modeState);
			_logger.Info(action);
			ModeStatus status = GetStatus(messages);
			return Task.FromResult(ApiResult.Success(action, status));
		}
		catch (Exception ex)
		{
			modeState.LastError = ex.Message;
			_store.SaveState(modeState);
			_logger.Error($"Failed to apply {mode}.", ex);
			ModeStatus status2 = GetStatus(messages);
			return Task.FromResult(ApiResult.Failure(ex.Message, status2));
		}
	}

	public ApiResult SetDefaultMode(ModeKind mode)
	{
		try
		{
			ModeConfig modeConfig = _store.LoadConfig();
			modeConfig.DefaultMode = mode;
			modeConfig.NextBootMode = null;
			_store.SaveConfig(modeConfig);
			_shellTools.SetShellForMode(mode);
			return ApiResult.Success($"Default startup mode set to {mode}.", GetStatus());
		}
		catch (Exception ex)
		{
			_logger.Error($"Failed to set default mode to {mode}.", ex);
			return ApiResult.Failure("Could not set default startup mode: " + ex.Message, GetStatus());
		}
	}

	public ApiResult SetNextBootMode(ModeKind mode)
	{
		ModeConfig modeConfig = _store.LoadConfig();
		modeConfig.NextBootMode = mode;
		_store.SaveConfig(modeConfig);
		return ApiResult.Success($"Next boot mode set to {mode}.", GetStatus());
	}

	public ApiResult RestartInMode(ModeKind mode)
	{
		try
		{
			ModeConfig modeConfig = _store.LoadConfig();
			modeConfig.NextBootMode = mode;
			_store.SaveConfig(modeConfig);
			_shellTools.SetShellForMode(mode);
			ModeState modeState = _store.LoadState();
			modeState.LastAction = $"Restarting into {mode} Mode";
			modeState.LastError = null;
			_store.SaveState(modeState);
			List<string> messages = new List<string>
			{
				(mode == ModeKind.Gaming) ? "Gaming shell will run after restart." : "Explorer shell will run after restart.",
				"Windows restart requested."
			};
			_shellTools.BeginRestart();
			return ApiResult.Success($"Restarting into {mode} Mode.", GetStatus(messages));
		}
		catch (Exception ex)
		{
			_logger.Error("Failed to restart Windows.", ex);
			return ApiResult.Failure("Could not restart Windows: " + ex.Message, GetStatus());
		}
	}

	public ApiResult SwitchToMode(ModeKind mode)
	{
		return RestartInMode(mode);
	}

	public async Task<ApiResult> RestartSteamAsync()
	{
		try
		{
			ModeConfig modeConfig = _store.LoadConfig();
			string configuredPath = modeConfig.Gaming.SteamPath ?? "";
			string[] fallbackPaths = _processTools.GetSteamFallbackPaths();
			_processTools.RestartProcesses(configuredPath, fallbackPaths.FirstOrDefault() ?? "", modeConfig.Gaming.SteamArguments, false, "steam");
			for (int launchAttempt = 0; launchAttempt < 2; launchAttempt++)
			{
				int stableTicks = 0;
				for (int attempt = 0; attempt < 30; attempt++)
				{
					if (Process.GetProcessesByName("steam").Length > 0)
					{
						stableTicks++;
						if (stableTicks >= 6)
						{
							_logger.Info("Steam restarted and remained stable without reapplying Gaming Mode.");
							return ApiResult.Success("Steam restarted.", GetStatus());
						}
					}
					else
					{
						stableTicks = 0;
					}
					await Task.Delay(500);
				}

				if (launchAttempt == 0)
				{
					_logger.Info("Steam bootstrap did not remain alive; performing one clean retry.");
					_processTools.EnsureProcess(configuredPath, fallbackPaths, modeConfig.Gaming.SteamArguments, "steam");
				}
			}
			_logger.Error("Steam did not remain alive within the restart timeout.");
			return ApiResult.Failure("Steam did not restart successfully within 30 seconds.", GetStatus());
		}
		catch (Exception ex)
		{
			_logger.Error("Failed to restart Steam.", ex);
			return ApiResult.Failure("Could not restart Steam: " + ex.Message, GetStatus());
		}
	}

	public async Task<ApiResult> RestartDeckyAsync()
	{
		ModeConfig modeConfig = _store.LoadConfig();
		_processTools.CleanupDeckyOrphanedForks();
		_processTools.EnsureDeckyPluginHelperCompatibilityServices();
		_processTools.RestartProcessesWithEnvironment(modeConfig.Gaming.DeckyPath ?? "", _processTools.GetDeckyFallbackPaths().FirstOrDefault() ?? "", "", true, _processTools.BuildDeckyPluginHelperEnvironment(), "PluginLoader", "PluginLoader_noconsole");
		_processTools.CleanupDeckyOrphanedForks();
		// NON si riapplica la modalita'. Prima qui c'era
		// ApplyModeAsync(Gaming): riavviare Decky faceva ripartire l'INTERA
		// procedura di ingresso in Gaming Mode, shell e Steam compresi. E' un
		// rischio sproporzionato per un'operazione che deve solo far ripartire
		// un processo. A ricaricare l'interfaccia pensa il plugin, che vive
		// dentro Steam e puo' chiedergli di rigenerare il proprio contesto.
		await Task.CompletedTask;
		return new ApiResult
		{
			Ok = true,
			Message = "Decky Loader restarted. The Steam interface will reload.",
			Status = GetStatus()
		};
	}

	public ModeStatus GetStatus(IReadOnlyCollection<string>? messages = null)
	{
		ModeConfig modeConfig = _store.LoadConfig();
		ModeState modeState = _store.LoadState();
		ModeStatus modeStatus = new ModeStatus();
		modeStatus.AgentRunning = true;
		modeStatus.CurrentMode = modeState.CurrentMode;
		modeStatus.DefaultMode = modeConfig.DefaultMode;
		modeStatus.NextBootMode = modeConfig.NextBootMode;
		modeStatus.LastAppliedAt = modeState.LastAppliedAt;
		modeStatus.LastAction = modeState.LastAction;
		modeStatus.LastError = modeState.LastError;
		modeStatus.Steam = _processTools.GetState("steam");
		modeStatus.Decky = _processTools.GetState("PluginLoader", "PluginLoader_noconsole");
		modeStatus.Sunshine = _processTools.GetState("sunshine", "apollo", "vibepollo", "vibeshine");
		modeStatus.Explorer = _processTools.GetState("explorer");
		modeStatus.MouseCursorAutoHide = _cursorAutoHide.Running;
		modeStatus.MouseCursorHidden = _cursorAutoHide.CursorHidden;
		modeStatus.SplashLogoPath = modeConfig.Gaming.Splash.LogoPath;
		modeStatus.ConfigPath = _paths.ConfigPath;
		modeStatus.Messages = messages?.ToArray() ?? Array.Empty<string>();
		return modeStatus;
	}

	public async Task RunSafetyWatchdogAsync(CancellationToken cancellationToken)
	{
		int deckyCleanupTicks = 0;
		int steamDownTicks = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken);
			if (IsSystemShuttingDown())
			{
				steamDownTicks = 0;
				continue;
			}
			deckyCleanupTicks++;
			if (deckyCleanupTicks >= 3)
			{
				deckyCleanupTicks = 0;
				_processTools.CleanupDeckyOrphanedForks();
			}
			ModeConfig modeConfig = _store.LoadConfig();
			if (_store.LoadState().CurrentMode == ModeKind.Gaming && modeConfig.Gaming.CloseExplorerInGamingMode && modeConfig.Gaming.AllowExplorerCloseInGamingMode)
			{
				ProcessState state = _processTools.GetState("steam");
				ProcessState state2 = _processTools.GetState("explorer");
				if (!state.Running && !state2.Running)
				{
					steamDownTicks++;
					if (steamDownTicks >= 2 && !IsSystemShuttingDown())
					{
						steamDownTicks = 0;
						await ApplyModeAsync(ModeKind.Desktop, "Safety watchdog restored Desktop Mode", interactive: false, restoreStartupApps: false, updateShell: false);
					}
				}
				else
				{
					steamDownTicks = 0;
				}
			}
			else
			{
				steamDownTicks = 0;
			}
		}
	}

	private static bool IsSystemShuttingDown()
	{
		return GetSystemMetrics(8192) != 0;
	}

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	private void ApplyGamingMode(ModeConfig config, ICollection<string> messages)
	{
		bool flag = config.Gaming.CloseExplorerInGamingMode && config.Gaming.AllowExplorerCloseInGamingMode;
		if (config.Gaming.EnsureSunshineCompatibilityInGamingMode)
		{
			_processTools.EnsureSunshineCompatibilityServices();
		}
		_volumeKeys.Start();
		_windowFocus.Start(config.Gaming.BorderlessFullscreenWindowsInGamingMode);
		if (config.Gaming.CloseExplorerInGamingMode && !config.Gaming.AllowExplorerCloseInGamingMode)
		{
			config.Gaming.CloseExplorerInGamingMode = false;
			_store.SaveConfig(config);
			messages.Add("Desktop shell hiding was ignored because the advanced safety flag is disabled.");
		}
		if (config.Gaming.AutoHideMouseCursorInGamingMode)
		{
			_cursorAutoHide.Start(config.Gaming.AutoHideMouseCursorAfterMs);
			messages.Add("Mouse cursor will hide while idle.");
		}
		else
		{
			_cursorAutoHide.Stop();
			messages.Add("Mouse cursor auto-hide is disabled.");
		}
		if (config.Gaming.EnsureInputCompatibilityInGamingMode)
		{
			int value = _processTools.EnsureInputCompatibilityServices();
			messages.Add($"DirectInput compatibility checked ({value} service(s) ready).");
		}
		_processTools.EnsureDeckyPluginHelperCompatibilityServices();
		int num = _processTools.StartCustomGamingApps(config.Gaming.CustomStartupApps);
		if (num > 0)
		{
			messages.Add($"Started {num} custom gaming app(s).");
		}
		if (flag)
		{
			_processTools.StopExplorer();
			messages.Add("Desktop shell was stopped for Gaming Mode.");
		}
		if (config.Gaming.SunshineRequired)
		{
			bool flag2 = _processTools.EnsureProcess(config.Gaming.SunshinePath, _processTools.GetSunshineFallbackPaths(), "", "sunshine", "apollo", "vibepollo", "vibeshine");
			messages.Add(flag2 ? "Remote-play host is running." : "No remote-play host was found. Configure SunshinePath in config.json if needed.");
		}
		if (config.Gaming.DeckyRequired)
		{
			_processTools.CleanupDeckyOrphanedForks();
			IReadOnlyDictionary<string, string> environment = _processTools.BuildDeckyPluginHelperEnvironment();
			bool flag3 = _processTools.EnsureProcessWithEnvironment(config.Gaming.DeckyPath, _processTools.GetDeckyFallbackPaths(), "", environment, "PluginLoader", "PluginLoader_noconsole");
			messages.Add(flag3 ? "Decky Loader is running." : "Decky Loader was not found. Configure DeckyPath in config.json if needed.");
			if (flag3 && config.Gaming.DelaySteamAfterDeckyMs > 0)
			{
				Thread.Sleep(config.Gaming.DelaySteamAfterDeckyMs);
			}
		}
		bool flag4 = _processTools.LaunchOrFocusSteamGamepad(config.Gaming.SteamPath, _processTools.GetSteamFallbackPaths(), config.Gaming.SteamArguments);
		messages.Add(flag4 ? "Steam is running in gamepad mode." : "Steam was not found. Configure SteamPath in config.json if needed.");
		if (flag && !flag4)
		{
			_processTools.StartExplorer();
			messages.Add("Desktop shell was restored because Steam did not start.");
		}
	}

	private void ApplyDesktopMode(ModeConfig config, ICollection<string> messages, bool interactive, bool restoreStartupApps)
	{
		_volumeKeys.Stop();
		_windowFocus.Stop();
		_cursorAutoHide.Stop();
		messages.Add("Mouse cursor was restored.");
		if (config.Gaming.RestoreExplorerOnDesktop)
		{
			bool flag = _processTools.StartExplorer();
			messages.Add(flag ? "Explorer is running." : "Explorer could not be started.");
		}
		if (restoreStartupApps && config.Gaming.RestoreStartupAppsOnDesktop)
		{
			int num = _processTools.RunUserStartupApps();
			messages.Add((num > 0) ? $"Restored {num} startup item(s)." : "No startup items needed restoring.");
		}
	}
}
