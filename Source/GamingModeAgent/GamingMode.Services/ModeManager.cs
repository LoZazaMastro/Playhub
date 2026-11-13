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
	private readonly SemaphoreSlim _modeSwitch = new(1, 1);
	private readonly object _applyMode = new();
	private readonly SteamModeCommandState _steamModeCommands = new();
	public event Action? ModeTransitionStarted;
	public event Action<bool>? ModeTransitionCompleted;
	public SteamModeCommand? ReadSteamModeCommand() => _steamModeCommands.Read();
	public bool AcknowledgeSteamModeCommand(long revision, string token, bool completed) =>
		_steamModeCommands.Acknowledge(revision, token, completed);

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
	// Si ripristinano i servizi e, se manca, la shell necessaria ai broker.
	// Non si riavviano Steam/Explorer e non si sposta il primo piano.
	public void ResumeGamingServices()
	{
		try
		{
			ModeConfig config = _store.LoadConfig();
			if (!_processTools.StartExplorer(background: true))
				throw new InvalidOperationException("The desktop shell required by Gaming Mode overlays is unavailable.");
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

	public Task<ApiResult> ApplyModeAsync(ModeKind mode, string action, bool interactive = true, bool restoreStartupApps = true, bool updateShell = false)
	{
		lock (_applyMode) return ApplyMode(mode, action, interactive, restoreStartupApps, updateShell);
	}

	private Task<ApiResult> ApplyMode(ModeKind mode, string action, bool interactive, bool restoreStartupApps, bool updateShell)
	{
		ModeConfig config = _store.LoadConfig();
		ModeState modeState = _store.LoadState();
		List<string> messages = new List<string>();
		try
		{
			_windowFocus.SetPostGameReturnTransitioning(true);
			ModeTransitionStarted?.Invoke();
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
		finally
		{
			_windowFocus.SetPostGameReturnTransitioning(false);
			ModeTransitionCompleted?.Invoke(_store.LoadState().CurrentMode == ModeKind.Gaming);
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

	internal Func<bool>? StartupForegroundGuard { get; set; }

	public async Task<ApiResult> OpenSteamBigPictureAsync()
	{
		if (!await _modeSwitch.WaitAsync(0)) return ApiResult.Failure("A mode switch is already in progress.", GetStatus());
		try
		{
			var config = _store.LoadConfig();
			var open = _steamModeCommands.BeginOpen();
			using SplashScreenService curtain = new(_logger);
			using SteamThemeStartupCover theme = new(curtain, _logger, config.Gaming.SteamPath,
				Math.Min(config.Gaming.Splash.MinVisibleMs, 7000), 8000, settlingDeliveredExit: open.Command?.Kind == "barrier");
			curtain.ShowTransition(ModeKind.Gaming, config.Language, config.Gaming.Splash, failSafeCloseMs: 8000);
			if (open.Command is not null)
			{
				var settled = await SteamModeSettlement.WaitAsync(open.Completed, () => theme.CanProceed, theme.DeadlineTick);
				if (settled != SteamModeSettlementResult.Complete) return ApiResult.Failure(
					settled == SteamModeSettlementResult.Cancelled ? "Steam opening was cancelled." : "Steam could not complete the previous mode switch.", GetStatus());
			}
			if (!theme.CanProceed) return ApiResult.Failure("Steam opening was cancelled.", GetStatus());
			// This explicit request launches one URI. It never changes the current
			// session mode, Explorer, startup preferences or Winlogon configuration.
			if (!_processTools.OpenUri("steam://open/bigpicture"))
				return ApiResult.Failure("Steam could not be opened.", GetStatus());
			await theme.WaitAsync();
			return ApiResult.Success("Steam Big Picture opening requested.", GetStatus());
		}
		finally { _modeSwitch.Release(); }
	}

	public async Task<ApiResult> SwitchToModeAsync(ModeKind mode, bool forceApply = false)
	{
		if (!await _modeSwitch.WaitAsync(0))
			return ApiResult.Failure("A mode switch is already in progress.", GetStatus());
		try
		{
			// Una richiesta duplicata non deve riaprire l'animazione o rilanciare processi.
            var currentState = _store.LoadState();
            if (!forceApply && currentState.CurrentMode == mode && currentState.LastAppliedAt.HasValue && string.IsNullOrEmpty(currentState.LastError) && ProcessTools.IsExplorerShellRunning())
                return ApiResult.Success($"Already in {mode} Mode.", GetStatus());
            using SplashScreenService transition = new(_logger);
            SteamThemeStartupCover? themedStartup = null;
			try
			{
				// A session switch must not touch DefaultMode, NextBootMode or Winlogon.
				var transitionConfig = _store.LoadConfig();
                SteamModeCommandWait? open = null;
				if (mode == ModeKind.Gaming)
                {
                    open = _steamModeCommands.BeginOpen();
                    themedStartup = new(transition, _logger, transitionConfig.Gaming.SteamPath,
                        Math.Min(transitionConfig.Gaming.Splash.MinVisibleMs, 7000), 8000,
                        settlingDeliveredExit: open.Command?.Kind == "barrier");
                }
                transition.ShowTransition(mode, transitionConfig.Language, transitionConfig.Gaming.Splash,
                    failSafeCloseMs: mode == ModeKind.Gaming ? 8000 : 90000);
				if (themedStartup is not null)
				{
					if (open!.Command is not null)
					{
						var settled = await SteamModeSettlement.WaitAsync(open.Completed, () => themedStartup.CanProceed, themedStartup.DeadlineTick);
						if (settled != SteamModeSettlementResult.Complete) return ApiResult.Failure(
							settled == SteamModeSettlementResult.Cancelled ? "Gaming Mode opening was cancelled." : "Steam could not complete the previous mode switch.", GetStatus());
					}
					if (!themedStartup.CanProceed) return ApiResult.Failure("Gaming Mode opening was cancelled.", GetStatus());
					StartupForegroundGuard = () => themedStartup.MayLaunch;
				}
				if (mode == ModeKind.Desktop)
				{
					_windowFocus.SetPostGameReturnTransitioning(true);
					var exit = _steamModeCommands.RequestDesktop();
					// Ask the still-running frontend before changing services/shell.
					// Explorer recovery remains bounded when Steam is unavailable;
					// the same pending exit can still settle on a later native read.
					for (int attempt = 0; attempt < 24 && !exit.Completed.IsCompleted; attempt++)
						await Task.Delay(50);
					if (!exit.Completed.IsCompletedSuccessfully || !exit.Completed.Result)
						_logger.Info("Steam desktop exit is pending; continuing Explorer recovery with Decky running.");
				}
				ApiResult result = await Task.Run(() => ApplyModeAsync(mode, $"Switched to {mode} Mode", updateShell: false));
				if (result.Ok && themedStartup is not null) await themedStartup.WaitAsync();
				return result;
			}
			finally
			{
				StartupForegroundGuard = null;
				if (mode == ModeKind.Desktop) _windowFocus.SetPostGameReturnTransitioning(false);
				if (mode == ModeKind.Gaming) { themedStartup?.Dispose(); transition.CancelImmediately(); }
                else await transition.HideAsync(minVisibleMs: 700, fade: true, fadeMs: 300);
			}
		}
		finally { _modeSwitch.Release(); }
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
		ProcessState[] processes = ModeStatusProcessSnapshot.Capture(Process.GetProcesses,
			process => process.Id, process => process.ProcessName,
			process => process.MainModule?.FileName, process => process.Dispose());
		modeStatus.Steam = processes[0];
		modeStatus.Decky = processes[1];
		modeStatus.Sunshine = processes[2];
		modeStatus.Explorer = processes[3];
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
		var recovery = new DesktopRecoveryPolicy();
		Func<bool> isGaming = () => _store.LoadState().CurrentMode == ModeKind.Gaming;
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken);
			if (IsSystemShuttingDown())
			{
				recovery.Reset();
				continue;
			}
			try
			{
			deckyCleanupTicks++;
			if (deckyCleanupTicks >= 3)
			{
				deckyCleanupTicks = 0;
				try { _processTools.CleanupDeckyOrphanedForks(); }
				catch (Exception error) { _logger.Error("Decky cleanup failed; Desktop safety remains active.", error); }
			}
				// An Explorer folder window is not the desktop shell. Recovery must
				// also work after a configuration change or a failed plugin startup.
				if (recovery.ShouldRestore(ProcessTools.IsExplorerShellRunning, isGaming, IsSteamProcessRunning, IsSystemShuttingDown))
					await ApplyModeAsync(ModeKind.Desktop, "Safety watchdog restored Desktop Mode", interactive: false, restoreStartupApps: false, updateShell: false);
			}
			catch (Exception error)
			{
				recovery.Reset();
				_logger.Error("Desktop safety check failed; it will retry on the next check.", error);
			}
		}
	}

	private static bool IsSteamProcessRunning()
	{
		var processes = Process.GetProcessesByName("steam");
		try { return processes.Length > 0; }
		finally { foreach (var process in processes) process.Dispose(); }
	}

	private static bool IsSystemShuttingDown()
	{
		return GetSystemMetrics(8192) != 0;
	}

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	private void ApplyGamingMode(ModeConfig config, ICollection<string> messages)
	{
		// The exact Explorer shell is a runtime dependency of both Win32 and
		// packaged-game overlay/HUD brokers. Keep it behind the fullscreen UI.
		if (!_processTools.StartExplorer(background: true))
			throw new InvalidOperationException("The desktop shell required by Gaming Mode overlays is unavailable.");
		messages.Add("Desktop shell is available in the background for overlay compatibility.");
		if (config.Gaming.EnsureSunshineCompatibilityInGamingMode)
		{
			_processTools.EnsureSunshineCompatibilityServices();
		}
		_volumeKeys.Start();
		_windowFocus.Start(config.Gaming.BorderlessFullscreenWindowsInGamingMode);
		if (config.Gaming.CloseExplorerInGamingMode)
			messages.Add("The legacy shell-close request was not applied because overlays require the desktop shell.");
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
		if (config.Gaming.EnsureHandheldVendorCompatibilityInGamingMode)
		{
			// Preserve the user's chosen OEM or alternative hardware controller.
			int vendor = _processTools.EnsureHandheldVendorCompatibilityServices();
			if (vendor > 0) messages.Add($"Observed {vendor} running handheld vendor service(s).");
		}
		_processTools.EnsureDeckyPluginHelperCompatibilityServices();
		int num = _processTools.StartCustomGamingApps(config.Gaming.CustomStartupApps);
		if (num > 0)
		{
			messages.Add($"Started {num} custom gaming app(s).");
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
		if (StartupForegroundGuard?.Invoke() == false)
			throw new OperationCanceledException("Gaming Mode opening was cancelled after the foreground window changed.");
		bool flag4 = _processTools.LaunchOrFocusSteamGamepad(config.Gaming.SteamPath, _processTools.GetSteamFallbackPaths(), config.Gaming.SteamArguments);
		messages.Add(flag4 ? "Steam is running in gamepad mode." : "Steam was not found. Configure SteamPath in config.json if needed.");
		if (!flag4)
		{
			throw new InvalidOperationException("Steam could not be opened; Gaming Mode was not applied.");
		}

	}

	private void ApplyDesktopMode(ModeConfig config, ICollection<string> messages, bool interactive, bool restoreStartupApps)
	{
		// A healthy loader belongs to Steam in both modes. Stopping it here
		// invalidates the outstanding native Exit readback and reloads every
		// plugin. The owned DeckyHost health monitor handles proven EOF/hangs.
		_volumeKeys.Stop();
		_windowFocus.Stop();
		_cursorAutoHide.Stop();
		messages.Add("Mouse cursor was restored.");
		if (config.Gaming.RestoreExplorerOnDesktop || !ProcessTools.IsExplorerShellRunning())
		{
			bool flag = _processTools.StartExplorer();
			messages.Add(flag ? "Explorer is running." : "Explorer could not be started.");
			if (!flag) throw new InvalidOperationException("Explorer could not be restored.");
		}
		if (restoreStartupApps && config.Gaming.RestoreStartupAppsOnDesktop)
		{
			int num = _processTools.RunUserStartupApps();
			messages.Add((num > 0) ? $"Restored {num} startup item(s)." : "No startup items needed restoring.");
		}
	}
}

internal static class ModeStatusProcessSnapshot
{
	private static readonly string[][] Aliases =
	{
		new[] { "steam" }, new[] { "PluginLoader", "PluginLoader_noconsole" },
		new[] { "sunshine", "apollo", "vibepollo", "vibeshine" }, new[] { "explorer" }
	};

	// One enumeration per status response. Keep handles alive only while reading
	// the selected paths, then release every process, including unrelated ones.
	internal static ProcessState[] Capture<TProcess>(Func<TProcess[]> enumerate,
		Func<TProcess, int> readId, Func<TProcess, string> readName,
		Func<TProcess, string?> readPath, Action<TProcess> dispose)
	{
		TProcess[] snapshot = enumerate();
		try
		{
			var selected = new List<(TProcess Process, int Id, string Name)>();
			foreach (TProcess process in snapshot)
			{
				try
				{
					string name = readName(process);
					if (Aliases.Any(group => group.Contains(name, StringComparer.OrdinalIgnoreCase)))
						selected.Add((process, readId(process), name));
				}
				catch { } // A process can exit between enumeration and identity reads.
			}
			return Aliases.Select(group =>
			{
				var matches = group.SelectMany(alias => selected.Where(process =>
					process.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))).DistinctBy(process => process.Id).ToArray();
				string? path = null;
				foreach (var process in matches)
				{
					try { path = readPath(process.Process); } catch { path = null; }
					if (!string.IsNullOrWhiteSpace(path)) break;
				}
				return new ProcessState { Running = matches.Length != 0,
					ProcessIds = matches.Select(process => process.Id).Order().ToArray(), Path = path };
			}).ToArray();
		}
		finally
		{
			foreach (TProcess process in snapshot)
			{
				try { dispose(process); } catch { }
			}
		}
	}
}
