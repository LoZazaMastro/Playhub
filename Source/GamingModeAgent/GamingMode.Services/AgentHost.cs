using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Playhub.Shared;

namespace GamingMode.Services;

public static class AgentHost
{
	internal static void MapCurrentModeEndpoint(WebApplication app, JsonStore store)
	{
		app.MapGet("/mode/current", (Func<IResult>)(() => Results.Json(new
		{
			agentRunning = true,
			currentMode = store.LoadState().CurrentMode
		})));
	}

	// Le POST del plugin arrivano con un corpo JSON oppure con la querystring:
	// si accettano entrambe, cosi' chi chiama non deve adeguarsi a noi.
	// IL CORPO DELLA RICHIESTA SI LEGGE UNA VOLTA SOLA.
	//
	// Qui c'era un difetto che ha rotto tre cose diverse senza mai lasciare un
	// errore. La versione precedente prendeva UN campo per volta e, per farlo,
	// leggeva il corpo della richiesta fino in fondo chiudendolo. Alla seconda
	// chiamata, sullo stesso messaggio, non restava piu' niente da leggere: il
	// campo tornava vuoto.
	//
	// Le richieste con un campo solo funzionavano - attivare o chiudere una
	// finestra - e infatti quelle non hanno mai dato problemi. Quelle con due o
	// tre campi fallivano sempre:
	//
	//   rinomina        id arrivava, il nome no  -> nome vuoto, rifiutato
	//   aggiungi app    target arrivava, il tipo no -> trattata come programma
	//                   su disco, percorso inesistente, rifiutata
	//   impostazioni    si leggeva prima l'interruttore, che consumava tutto:
	//                   la combinazione arrivava sempre vuota e non veniva mai
	//                   salvata
	//
	// Da qui in avanti il corpo si legge intero una volta e si tiene a
	// disposizione: i campi si prendono da li'.
	private static async Task<IReadOnlyDictionary<string, string>> ReadFieldsAsync(HttpRequest request)
	{
		Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);

		try
		{
			foreach (var entry in request.Query)
			{
				string value = entry.Value.ToString();
				if (!string.IsNullOrWhiteSpace(value)) fields[entry.Key] = value;
			}

			if (request.ContentLength is not (null or 0))
			{
				using StreamReader reader = new(request.Body);
				string body = await reader.ReadToEndAsync();
				if (!string.IsNullOrWhiteSpace(body))
				{
					using JsonDocument document = JsonDocument.Parse(body);
					if (document.RootElement.ValueKind == JsonValueKind.Object)
					{
						foreach (JsonProperty property in document.RootElement.EnumerateObject())
						{
							// La querystring non vince sul corpo: se un campo
							// arriva in entrambi, quello scritto nel corpo e'
							// quello che il chiamante intendeva davvero.
							fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
								? property.Value.GetString() ?? ""
								: property.Value.ToString();
						}
					}
				}
			}
		}
		catch
		{
			// Corpo illeggibile: restano i campi della querystring, se c'erano.
		}

		return fields;
	}

	private static string Field(IReadOnlyDictionary<string, string> fields, string name)
		=> fields.TryGetValue(name, out string? value) ? value : "";

	// Lo Steam in esecuzione, se c'e'. Il motore si aggancia al suo renderer: senza
	// Steam non ha niente a cui agganciarsi, e a ogni Steam nuovo va riavviato.
	private static int? SteamProcessId()
	{
		using var current = System.Diagnostics.Process.GetCurrentProcess();
		System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("steam");
		try
		{
			var ids = new List<int>();
			foreach (var process in processes)
			{
				try { if (process.SessionId == current.SessionId && !process.HasExited) ids.Add(process.Id); }
				catch (InvalidOperationException) { /* Processo terminato durante la lettura. */ }
			}
			return ids.Count == 0 ? null : ids.Min();
		}
		finally { foreach (var process in processes) process.Dispose(); }
		// Gli altri errori arrivano al watcher: un'enumerazione fallita non prova
		// che Steam sia chiuso e non deve spegnere un motore funzionante.
	}

	public static async Task RunAsync(AppPaths paths, FileLogger logger, string[] args)
	{
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "GamingMode.Agent", out createdNew))
		{
			if (!createdNew)
			{
				logger.Info("Agent is already running.");
				return;
			}
			JsonStore store = new JsonStore(paths, logger);
			ModeConfig modeConfig = store.LoadConfig();
			bool flag = args.Any((string arg) => arg.Equals("shell", StringComparison.OrdinalIgnoreCase));
			bool bootRequested = flag || args.Any((string arg) => arg.Equals("--boot", StringComparison.OrdinalIgnoreCase) || arg.Equals("boot", StringComparison.OrdinalIgnoreCase));
			bool showSplash = bootRequested && (modeConfig.NextBootMode ?? modeConfig.DefaultMode) == ModeKind.Gaming && !SafeModeGuard.ShouldForceDesktop(paths);
			int minSplashVisibleMs = Math.Clamp(modeConfig.Gaming.Splash.MinVisibleMs, 0, 120000);
			int maxSplashVisibleMs = Math.Clamp(modeConfig.Gaming.Splash.MaxVisibleMs, 5000, 120000);
			using SplashScreenService splashScreen = new SplashScreenService(logger);
			using SteamThemeStartupCover? themedStartup = showSplash
				? new(splashScreen, logger, modeConfig.Gaming.SteamPath, minSplashVisibleMs, maxSplashVisibleMs) : null;
			// Paint the login curtain before constructing any input, audio or dashboard service.
			if (showSplash)
			{
				splashScreen.Show(modeConfig.Gaming.Splash, maxSplashVisibleMs);
			}
			ProcessTools processTools = new ProcessTools(logger);
			ShellTools shellTools = new ShellTools(logger);
			CursorAutoHideService cursorAutoHide = new CursorAutoHideService(logger);
			try
			{
				using GamingWindowFocusService windowFocus = new GamingWindowFocusService(logger, modeConfig.Gaming.SteamPath);
				// Schermo nero con l'audio dopo aver acceso la TV: vedi SteamDisplayRecoveryService.
				// Parte prima della modalita' di avvio, cosi' la fotografia iniziale dei monitor
				// e' quella dell'accensione, TV spenta compresa.
				using SteamDisplayRecoveryService displayRecovery = new SteamDisplayRecoveryService(logger,
					() => store.LoadState().CurrentMode == ModeKind.Gaming,
					() => processTools.GetSteamFallbackPaths().Prepend(store.LoadConfig().Gaming.SteamPath).FirstOrDefault(File.Exists));
				displayRecovery.Start();
				// Chain Free Engine is suspended for this release: Decky remains the host.
				// Do not create its watcher, even for an existing opt-in descriptor.
				// Keep the descriptor and engine sources intact for a future release.
				using SystemVolumeKeyService volumeKeys = new SystemVolumeKeyService(logger);
				using OverlayQuickSettingsClient quickSettings = new OverlayQuickSettingsClient();
				ModeManager manager = new ModeManager(paths, store, processTools, shellTools, cursorAutoHide, windowFocus, volumeKeys, logger);
				using SteamTaskbarFullscreenService steamTaskbar = new(logger, modeConfig.Gaming.SteamPath);
				manager.ModeTransitionStarted += steamTaskbar.ModeChanging;
				manager.ModeTransitionCompleted += steamTaskbar.ModeCompleted;
				using WindowsQamFocusAdapter qamWindows = new(windowFocus, modeConfig.Gaming.SteamPath);
				using ControlledQamFocusService qamFocus = new(qamWindows, store.LoadState().CurrentMode == ModeKind.Gaming);
				qamWindows.Changed += qamFocus.WindowChanged;
				manager.ModeTransitionStarted += qamFocus.ModeChanging;
				manager.ModeTransitionCompleted += qamFocus.ModeCompleted;
				using ControllerHapticsService controllerHaptics = new ControllerHapticsService(logger);
				// La Playhub Dashboard e' una schermata del plugin di Steam. Qui
				// resta solo cio' che il plugin non puo' fare da dentro Steam: la
				// scorciatoia da tastiera per aprirla.
				using DashboardShortcutService dashboard = new DashboardShortcutService(
					store,
					() => windowFocus.IsLaunchCurtainOnScreen,
					logger);
				string value = (modeConfig.Safety.AllowRemoteApi ? "0.0.0.0" : "127.0.0.1");
				string url = $"http://{value}:{modeConfig.Safety.ApiPort}";
				if (bootRequested)
				{
					bool waitForSteamFullscreen = false;
					try
					{
						processTools.CleanupDeckyOrphanedForks();
						manager.StartupForegroundGuard = showSplash && modeConfig.Gaming.Splash.Enabled && themedStartup is not null
							? () => themedStartup.MayLaunch : null;
						await manager.ApplyBootModeAsync(flag);
						waitForSteamFullscreen = true;
					}
					catch (Exception exception)
					{
						logger.Error("Boot mode application failed. Continuing agent startup in Desktop-safe state.", exception);
					}
					finally
					{
						manager.StartupForegroundGuard = null;
						if (showSplash)
						{
							bool steamRunning = waitForSteamFullscreen && manager.GetStatus().Steam.Running;
							if (steamRunning && themedStartup is not null)
								await themedStartup.WaitAsync();
							else
								splashScreen.CancelImmediately();
						}
					}
				}
				else
				{
					processTools.CleanupDeckyOrphanedForks();
				}
				try
				{
					using System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
					currentProcess.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
					logger.Info("Agent process priority lowered to BelowNormal after startup.");
				}
				catch { }
				dashboard.Start();
				// Se il PC e' gia' in Gaming Mode ma l'agente e' appena ripartito (e
				// succede a ogni installa/aggiorna), i servizi vanno riaccesi:
				// altrimenti il borderless resta spento senza che nulla lo dica.
				if (manager.GetStatus().CurrentMode == ModeKind.Gaming)
				{
					manager.ResumeGamingServices();
				}
				WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
				{
					Args = args,
					ContentRootPath = AppContext.BaseDirectory
				});
				webApplicationBuilder.Services.Configure(delegate(JsonOptions options)
				{
					options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
					options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
				});
				webApplicationBuilder.WebHost.UseUrls(url);
				WebApplication app = webApplicationBuilder.Build();
				XboxShellBroker xboxShell = new(paths.ConfigDirectory, ProcessTools.IsExplorerShellRunning, () =>
				{
					using var explorer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
						Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
					{
						UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
					}) ?? throw new InvalidOperationException("Unable to start the Windows desktop shell.");
					logger.Info("Xbox shell preparation requested by the game-session helper.");
				});
				app.Use(async delegate(HttpContext context, Func<Task> next)
				{
					context.Response.Headers["Access-Control-Allow-Origin"] = "*";
					context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
					context.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
					if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
					{
						context.Response.StatusCode = 204;
					}
					else
					{
						await next();
					}
				});
				app.MapGet("/health", (Func<IResult>)(() => Results.Text("ok")));
				app.MapPost("/xbox/shell/ensure", (Func<HttpContext, IResult>)((HttpContext context) =>
				{
					return xboxShell.RequestShell(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())
						? Results.Json(new { ok = true }) : Results.StatusCode(401);
				}));
				var gameBarGuide = new GameBarGuideService(WindowsXboxGameBar.ToggleVerified, () => Environment.TickCount64);
				var frameCollector = new PresentMonFrameCollector(Path.Combine(AppContext.BaseDirectory, "PresentMon", "PresentMon.exe"));
				await using var livePerformance = new LivePerformanceController(new LivePerformanceSettingsStore(),
					new LivePerformanceRuntime(windowFocus, Path.Combine(AppContext.BaseDirectory, "PresentMon", "PresentMon.exe")));
				using var livePerformanceLifetime = new LivePerformanceLifetime(livePerformance, app.Lifetime.ApplicationStopping);
				manager.ModeTransitionStarted += livePerformance.Invalidate;
				LivePerformanceEndpoints.Map(app, xboxShell, livePerformance);
				await using var launchCurtainCover = new LaunchCurtainCoverController(windowFocus, app.Lifetime.ApplicationStopping);
				manager.ModeTransitionStarted += launchCurtainCover.Invalidate;
				using var launchCurtainShutdown = app.Lifetime.ApplicationStopping.Register(launchCurtainCover.Invalidate);
				LaunchCurtainCoverEndpoints.Map(app, xboxShell, launchCurtainCover,
					context => LaunchCurtainPublisherChannel.Read(context, modeConfig.Gaming.DeckyPath, processTools.GetDeckyFallbackPaths()));
				using var ownedLaunchShutdown = app.Lifetime.ApplicationStopping.Register(Win32SteamSessionAssociation.DisposeOwned);
				OwnedWin32RouteEndpoints.Map(app, xboxShell, logger.Info);
				NativeGameOverlayEndpoints.Map(app, xboxShell, logger.Info);
				int gamingSessionEnabled = store.LoadState().CurrentMode == ModeKind.Gaming ? 1 : 0;
				bool GamingSessionEnabled() => Volatile.Read(ref gamingSessionEnabled) != 0 && !app.Lifetime.ApplicationStopping.IsCancellationRequested;
				using var gameQos = new GameExecutionQosController(GamingSessionEnabled);
				using var downloadPower = new SteamDownloadPowerLease(() => GamingSessionEnabled() &&
					SteamInstallationPath.FindRoot(modeConfig.Gaming.SteamPath) is { } steamRoot &&
					SteamInstallationPath.ReadVerifiedClientBirth(steamRoot) is { } clientBirth &&
					OverlaySteamArtworkResolver.HasActiveGameForRecovery(steamRoot, clientBirth) == false);
				void StopSessionOptions()
				{
					Volatile.Write(ref gamingSessionEnabled, 0);
					downloadPower.Revoke();
					gameQos.StopAsync().GetAwaiter().GetResult();
				}
				manager.ModeTransitionStarted += StopSessionOptions;
				manager.ModeTransitionCompleted += gaming =>
				{
					if (gaming && !app.Lifetime.ApplicationStopping.IsCancellationRequested) Volatile.Write(ref gamingSessionEnabled, 1);
					else StopSessionOptions();
				};
				using var shutdownSessionOptions = app.Lifetime.ApplicationStopping.Register(StopSessionOptions);

				object gameAudioGate = new();
				app.MapPost("/session/steam/game-audio", (Func<HttpContext, IResult>)(context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
						return Results.StatusCode(401);
					var query = context.Request.Query;
					if (!uint.TryParse(query["appId"], out uint appId) || appId == 0) return Results.StatusCode(400);
					bool write = query.ContainsKey("level");
					string token = query["token"].ToString();
					int level = 0;
					if (write && (!int.TryParse(query["level"], out level) || level is < 0 or > 100 ||
						token.Length != 64 || !token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))) return Results.StatusCode(400);
					if (!write && query.ContainsKey("token")) return Results.StatusCode(400);

					lock (gameAudioGate)
					{
						if (app.Lifetime.ApplicationStopping.IsCancellationRequested || !windowFocus.TryGetQamSource(out nint window, out uint gamePid))
							return Results.Json(new { status = "Unavailable" });

						try
						{
							using var process = System.Diagnostics.Process.GetProcessById((int)gamePid);
							long birth = process.StartTime.ToFileTimeUtc();
							bool StillCurrent() => !context.RequestAborted.IsCancellationRequested && !app.Lifetime.ApplicationStopping.IsCancellationRequested &&
								!process.HasExited &&
								windowFocus.TryGetQamSource(out nint current, out uint currentPid) && current == window && currentPid == gamePid &&
								(OverlaySteamArtworkResolver.IsTrackedAppProcess(process.Id, appId) || UwpSteamSessionAssociation.Find(gamePid, appId) is not null);

							if (!StillCurrent()) return Results.Json(new { status = "Unavailable" });
							var target = new GameAudioTarget(process.Id, birth);
							return Results.Json(write ? GameAudioVolumeService.Set(target, level, token, StillCurrent)
								: GameAudioVolumeService.Read(target, StillCurrent));
						}
						catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
						{ return Results.Json(new { status = "Unavailable" }); }
					}
				}));

				app.MapPost("/session/steam/game-qos", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					if (!uint.TryParse(context.Request.Query["appId"], out uint appId) || appId == 0) return Results.StatusCode(400);
					if (!GamingSessionEnabled() || !windowFocus.TryGetQamSource(out _, out uint gamePid))
						return Results.Json(new { status = "TargetUnavailable" });
					GameExecutionQosTarget target;
					try
					{
						using var process = System.Diagnostics.Process.GetProcessById((int)gamePid);
						long birth = process.StartTime.ToFileTimeUtc();
						if (process.HasExited || (!OverlaySteamArtworkResolver.IsTrackedAppProcess(process.Id, appId) &&
							UwpSteamSessionAssociation.Find(gamePid, appId) is null)) return Results.Json(new { status = "TargetUnavailable" });
						target = new GameExecutionQosTarget(process.Id, birth);
					}
					catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
					{ return Results.Json(new { status = "TargetUnavailable" }); }
					if (!GamingSessionEnabled()) return Results.Json(new { status = "TargetUnavailable" });
					string status = await gameQos.RenewAsync(target, context.Request.Query["ac"].ToString(), context.Request.Query["battery"].ToString());
					if (!GamingSessionEnabled())
					{
						await gameQos.StopAsync();
						return Results.Json(new { status = "TargetUnavailable" });
					}
					return Results.Json(new { status });
				}));
				app.MapPost("/session/steam/game-qos/stop", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					string status = await gameQos.StopAsync();
					return Results.Json(new { ok = status == "Default", status });
				}));
				app.MapPost("/session/steam/download-power", (Func<HttpContext, IResult>)(context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					string action = context.Request.Query["action"].ToString();
					string lease = context.Request.Query["lease"].ToString();
					if (action == "stop" && Guid.TryParseExact(lease, "N", out _)) return Results.Json(new { ok = downloadPower.Stop(lease) });
					if (!uint.TryParse(context.Request.Query["appId"], out uint appId) || appId == 0) return Results.StatusCode(400);
					return action switch
					{
						"start" => Results.Json(downloadPower.Start(appId)),
						"renew" when Guid.TryParseExact(lease, "N", out _) => Results.Json(downloadPower.Renew(lease, appId)),
						_ => Results.StatusCode(400)
					};
				}));
				app.MapPost("/session/frame-measurement", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					if (!uint.TryParse(context.Request.Query["appId"], out uint appId) || appId == 0) return Results.StatusCode(400);
					if (!GamingSessionEnabled())
						return Results.Json(new FrameMeasurement(FrameMeasurementStatus.TargetUnavailable));
					using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
					bool gameplay = context.Request.Query["gameplay"] == "1";
					var preparation = gameplay ? TimeSpan.FromSeconds(3) : TimeSpan.Zero;
					Task<FrameMeasurement> CapturePackaged(UwpSessionIdentity game)
					{
						bool IsGameForeground()
						{
							if (!GamingSessionEnabled()) return false;
							nint window = OverlayWindowTools.GetForegroundWindowHandle();
							if (!OverlayWindowTools.IsVisibleGameSurface(window)) return false;
							string? frame = UwpFrameWindow.ReadAppId(window);
							return FrameMeasurementTargetPolicy.MatchesPackagedForeground(game.GamePid, game.Aumid,
								OverlayWindowTools.WindowOwnerProcessId(window), frame,
								frame is not null && UwpSteamSessionAssociation.IsOnlyRunningInstance(game))
								&& UwpSteamSessionAssociation.Find(game.GamePid, appId) == game
								&& OverlayWindowTools.GetForegroundWindowHandle() == window;
						}
						return frameCollector.CaptureAsync(new PresentMonTarget((int)game.GamePid, game.GameBirth),
							cancellation.Token, preparation, IsGameForeground);
					}
					try
					{
						if (appId >= 0x80000000)
						{
							var packaged = UwpSteamSessionAssociation.FindForApp(appId);
							if (packaged.Count > 1) return Results.Json(new FrameMeasurement(FrameMeasurementStatus.TargetUnavailable));
							if (packaged is [var game]) return Results.Json(await CapturePackaged(game));
						}
						if (!windowFocus.TryGetQamSource(out _, out uint gamePid))
							return Results.Json(new FrameMeasurement(FrameMeasurementStatus.TargetUnavailable));
						using var process = System.Diagnostics.Process.GetProcessById((int)gamePid);
						if (!FrameMeasurementTargetPolicy.CanMeasureWindowOwner(process.ProcessName))
							return Results.Json(new FrameMeasurement(FrameMeasurementStatus.TargetUnavailable));
						if (UwpSteamSessionAssociation.Find(gamePid, appId) is { } packagedGame)
							return Results.Json(await CapturePackaged(packagedGame));
						if (!OverlaySteamArtworkResolver.IsTrackedAppProcess(process.Id, appId))
							return Results.Json(new FrameMeasurement(FrameMeasurementStatus.TargetUnavailable));
						var target = new PresentMonTarget(process.Id, process.StartTime.ToFileTimeUtc());
						if (!gameplay) return Results.Json(await frameCollector.CaptureAsync(target, cancellation.Token));
						return Results.Json(await frameCollector.CaptureAsync(target, cancellation.Token, preparation,
							() => GamingSessionEnabled() &&
								windowFocus.TryGetQamGameProcess(OverlayWindowTools.GetForegroundWindowHandle(), out uint currentPid) && currentPid == gamePid));
					}
					catch (Exception error) when (error is InvalidOperationException or ArgumentException or
						System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException)
					{
						return Results.Json(new FrameMeasurement(FrameMeasurementStatus.TargetUnavailable));
					}
				}));
				app.MapPost("/session/steam/arm-return", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					if (!uint.TryParse(context.Request.Query["appId"], out uint appId) || appId == 0) return Results.StatusCode(400);
					uint gamePid = 0;
					if (context.Request.Query.TryGetValue("gamePid", out var requestedPid)
						&& (!uint.TryParse(requestedPid, out gamePid) || gamePid == 0)) return Results.StatusCode(400);
					return Results.Json(new { ok = await windowFocus.ArmBigPictureReturnAsync(appId, gamePid, context.RequestAborted) });
				}));
				app.MapPost("/session/uwp/steam-overlay", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
						return Results.StatusCode(401);
					var query = context.Request.Query;
					if (!uint.TryParse(query["appId"], out uint appId) || appId < 0x80000000
						|| !uint.TryParse(query["overlayPid"], out uint overlayPid)
						|| !bool.TryParse(query["keyboard"], out bool keyboard)) return Results.StatusCode(400);
					bool menu = false;
					if (query.TryGetValue("menu", out var menuValue) && !bool.TryParse(menuValue, out menu)) return Results.StatusCode(400);
					if (overlayPid == 0 && (keyboard || menu)) return Results.StatusCode(400);
					return Results.Json(await XboxCompanionOverlaySession.CheckAsync(appId, overlayPid, keyboard, menu, context.RequestAborted, logger.Info));
				}));
                app.MapPost("/session/uwp/steam-overlay/snapshot", (Func<HttpContext, Task<IResult>>)(async context =>
                {
                    if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
                        return Results.StatusCode(401);
                    var query = context.Request.Query;
                    if (!uint.TryParse(query["appId"], out uint appId) || appId < 0x80000000
                        || !uint.TryParse(query["overlayPid"], out uint overlayPid) || overlayPid == 0)
                        return Results.StatusCode(400);
                    return Results.Json(await XboxCompanionOverlaySession.ReadSnapshotAsync(appId, overlayPid, context.RequestAborted, logger.Info));
                }));
				app.MapPost("/session/uwp/steam-overlay/open", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
						return Results.StatusCode(401);
					var fields = await ReadFieldsAsync(context.Request);
					if (!uint.TryParse(Field(fields, "appId"), out uint appId) || appId < 0x80000000)
						return Results.StatusCode(400);
					var decision = await XboxCompanionOverlaySession.RequestOpenAsync(appId, context.RequestAborted, logger.Info);
					return Results.Json(decision);
				}));
                app.MapPost("/session/uwp/steam-overlay/mirror/prepare", (Func<HttpContext, Task<IResult>>)(async context =>
                {
                    if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
                        return Results.StatusCode(401);
                    var fields = await ReadFieldsAsync(context.Request);
                    if (!uint.TryParse(Field(fields, "appId"), out uint appId) || appId < 0x80000000)
                        return Results.StatusCode(400);
                    return Results.Json(await XboxCompanionOverlaySession.RequestMirrorPreparationAsync(appId,
                        context.RequestAborted, logger.Info));
                }));
                app.MapPost("/session/uwp/steam-overlay/mirror", (Func<HttpContext, Task<IResult>>)(async context =>
                {
                    if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
                        return Results.StatusCode(401);
                    if (!uint.TryParse(context.Request.Query["appId"], out uint appId) || appId < 0x80000000
                        || !uint.TryParse(context.Request.Query["overlayPid"], out uint overlayPid)) return Results.StatusCode(400);
                    return Results.Json(await XboxCompanionOverlaySession.ReadMirrorPresentationAsync(appId, overlayPid,
                        context.RequestAborted, logger.Info));
                }));
                app.MapPost("/session/uwp/steam-overlay/mirror/control", (Func<HttpContext, Task<IResult>>)(async context =>
                {
                    if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
                        return Results.StatusCode(401);
                    if (context.Request.ContentLength is > 4096) return Results.StatusCode(413);
                    try
                    {
                        using var document = await JsonDocument.ParseAsync(context.Request.Body,
                            new JsonDocumentOptions { MaxDepth = 8 }, context.RequestAborted);
                        var body = document.RootElement;
                        if (body.ValueKind != JsonValueKind.Object
                            || !body.TryGetProperty("appId", out var appValue) || !appValue.TryGetUInt32(out uint appId) || appId < 0x80000000
                            || !body.TryGetProperty("overlayPid", out var pid) || !pid.TryGetUInt32(out uint overlayPid) || overlayPid == 0
                            || !body.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String
                            || action.GetString() is not ("show" or "close")
                            || !body.TryGetProperty("expectedMirror", out var expected) || expected.ValueKind != JsonValueKind.Object)
                            return Results.StatusCode(400);
                        return Results.Json(await XboxCompanionOverlaySession.SetMirrorPresentationAsync(appId, overlayPid,
                            action.GetString()!, expected, context.RequestAborted, logger.Info));
                    }
                    catch (JsonException) { return Results.StatusCode(400); }
                    catch (InvalidOperationException) { return Results.StatusCode(400); }
                }));
				app.MapPost("/session/uwp/resume", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString()))
						return Results.StatusCode(401);
					if (!uint.TryParse(context.Request.Query["appId"], out uint appId) || appId < 0x80000000)
						return Results.StatusCode(400);
					using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
					return Results.Json(new { ok = await ResumeXboxGameAsync(appId, modeConfig.Gaming.SteamPath, cancellation.Token) });
				}));
				app.MapGet("/session/steam/return-focus", (Func<HttpContext, IResult>)(context =>
				{
					var proof = int.TryParse(context.Request.Query["version"], out int version)
						? windowFocus.ConfirmedBigPictureReturn(version) : null;
					return Results.Json(new { ok = proof.HasValue, appId = proof?.AppId ?? 0, usedCompanion = proof?.UsedCompanion ?? false,
                        gamePid = proof?.GamePid ?? 0, gameBirth = (proof?.GameBirth ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture) });
				}));
				app.MapPost("/session/uwp/gamebar", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					var fields = await ReadFieldsAsync(context.Request);
					uint.TryParse(Field(fields, "appId"), out uint appId);
					if (!bool.TryParse(Field(fields, "pressed"), out bool pressed)) return Results.StatusCode(400);
					return Results.Json(gameBarGuide.Handle(Field(fields, "requestId"), appId, pressed));
				}));
				app.MapGet("/session/uwp", (Func<HttpContext, IResult>)(context =>
				{
					if (!xboxShell.IsAuthorized(context.Connection.RemoteIpAddress, context.Request.Headers["X-Playhub-Shell-Token"].ToString())) return Results.StatusCode(401);
					uint.TryParse(context.Request.Query["appId"], out uint appId);
					var identities = UwpSteamSessionAssociation.FindForApp(appId);
					var handles = identities.Count == 0 ? Array.Empty<nint>() : OverlayWindowTools.EnumerateTopLevelHandles();
					return Results.Json(new { appId, sessions = identities.Select(identity => new {
						identity.GamePid, identity.GameBirth, identity.WrapperPid, identity.WrapperBirth,
						identity.PackageFamily, identity.Aumid, identity.Executable,
						windows = handles.Where(h => OverlayWindowTools.IsVisibleGameSurface(h) && MatchesXboxGameWindow(h, identity))
							.Select(h => new { hwnd = h.ToString(), pid = identity.GamePid, process = Path.GetFileName(identity.Executable), title = Path.GetFileNameWithoutExtension(identity.Executable) })
					}) });
				}));
				app.MapGet("/status", (Func<IResult>)(() => Results.Json(manager.GetStatus())));
				app.MapPost("/qam/focus/acquire", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					uint.TryParse(Field(fields, "appId"), out uint appId);
					return Results.Json(qamFocus.Acquire(Field(fields, "requestId"), appId));
				}));
				app.MapPost("/qam/focus/close", (Func<HttpRequest, Task<IResult>>)(async request =>
					Results.Json(qamFocus.Close(Field(await ReadFieldsAsync(request), "requestId")))));
				app.MapPost("/qam/focus/release", (Func<HttpRequest, Task<IResult>>)(async request =>
					Results.Json(qamFocus.Release(Field(await ReadFieldsAsync(request), "requestId")))));
				MapCurrentModeEndpoint(app, store);
				app.MapPost("/steam/bigpicture/open", (Func<Task<IResult>>)(async () => Results.Json(await manager.OpenSteamBigPictureAsync())));
				app.MapPost("/mode/gaming", (Func<Task<IResult>>)(async () => Results.Json(await manager.SwitchToModeAsync(ModeKind.Gaming, forceApply: true))));
				app.MapPost("/mode/desktop", (Func<Task<IResult>>)(async () => Results.Json(await manager.ApplyModeAsync(ModeKind.Desktop, "Applied Desktop Mode"))));
				app.MapPost("/mode/gaming/switch", (Func<Task<IResult>>)(async () => Results.Json(await manager.SwitchToModeAsync(ModeKind.Gaming))));
				app.MapPost("/mode/desktop/switch", (Func<Task<IResult>>)(async () => Results.Json(await manager.SwitchToModeAsync(ModeKind.Desktop))));
				app.MapPost("/mode/gaming/restart", (Func<IResult>)(() => Results.Json(manager.RestartInMode(ModeKind.Gaming))));
				app.MapPost("/mode/desktop/restart", (Func<IResult>)(() => Results.Json(manager.RestartInMode(ModeKind.Desktop))));
				app.MapPost("/default/gaming", (Func<IResult>)(() => Results.Json(manager.SetDefaultMode(ModeKind.Gaming))));
				app.MapPost("/default/desktop", (Func<IResult>)(() => Results.Json(manager.SetDefaultMode(ModeKind.Desktop))));
				app.MapPost("/config/splash/logo", (Func<SplashLogoRequest, IResult>)delegate(SplashLogoRequest request)
				{
					ModeConfig modeConfig2 = store.LoadConfig();
					if (string.IsNullOrWhiteSpace(request.Path))
					{
						modeConfig2.Gaming.Splash.LogoPath = null;
						store.SaveConfig(modeConfig2);
						return Results.Json(ApiResult.Success("Splash logo reset.", manager.GetStatus()));
					}
					string text = Environment.ExpandEnvironmentVariables(request.Path).Trim().Trim('"');
					if (!File.Exists(text))
					{
						return Results.Json(ApiResult.Failure("Splash logo file was not found.", manager.GetStatus()));
					}
					modeConfig2.Gaming.Splash.LogoPath = text;
					store.SaveConfig(modeConfig2);
					return Results.Json(ApiResult.Success("Splash logo updated.", manager.GetStatus()));
				});
				// ----------------------------------------------------------
				// CONFINE CON IL PLUGIN DI STEAM.
				// Tutto cio' che la Dashboard mostra e che Steam non puo' sapere
				// da solo passa di qui. Nessuna finestra nostra, nessun input
				// intercettato, nessuna contesa.
				// ----------------------------------------------------------
				// IL PLUGIN SCRIVE NEL LOG DELL'AGENTE.
				//
				app.MapPost("/dash/log", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					logger.Info("[PLUGIN] " + Field(fields, "message"));
					return Results.Json(new { ok = true });
				}));
				app.MapPost("/dash/haptic", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					int.TryParse(Field(fields, "vendorId"), out int vendorId);
					int.TryParse(Field(fields, "productId"), out int productId);
					long.TryParse(Field(fields, "requestId"), out long requestId);
					if (bool.TryParse(Field(fields, "stop"), out bool stop) && stop)
					{
						return Results.Json(controllerHaptics.Stop(vendorId, productId, requestId));
					}

					int.TryParse(Field(fields, "controllerIndex"), out int controllerIndex);
					int.TryParse(Field(fields, "side"), out int side);
					double.TryParse(Field(fields, "magnitude"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double magnitude);
					string pattern = Field(fields, "pattern");
					if (!string.IsNullOrWhiteSpace(pattern))
					{
						ControllerHapticResult patterned = controllerHaptics.PlayPattern(
							controllerIndex, vendorId, productId, side, magnitude, pattern, requestId);
						return Results.Json(patterned);
					}

					int.TryParse(Field(fields, "durationMs"), out int durationMs);
					ControllerHapticResult result = controllerHaptics.Pulse(
						controllerIndex, vendorId, productId, side, magnitude, durationMs, requestId);
					return Results.Json(result);
				}));

				// ----------------------------------------------------------
				// COMPATIBILITA' CON BUILD PRECEDENTI.
				//
				// Il mirror DWM e' stato ritirato: una superficie nativa separata
				// poteva sopravvivere alla finestra sorgente e lasciare sul display
				// l'ultimo frame di un gioco. Gli endpoint restano per permettere a
				// un vecchio frontend di fallire senza bloccare Steam.
				// ----------------------------------------------------------
				app.MapPost("/dash/overlay/prepare", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					_ = await ReadFieldsAsync(request);
					return Results.Json(new { ok = false, disabled = true, retired = true });
				}));

				app.MapPost("/dash/overlay/show", (Func<IResult>)(() =>
					Results.Json(new { ok = false, disabled = true, retired = true })));

				app.MapPost("/dash/overlay/hide", (Func<IResult>)(() =>
					Results.Json(new { ok = true, open = false })));

				app.MapPost("/dash/overlay/heartbeat", (Func<IResult>)(() =>
					Results.Json(new { open = false, retired = true })));

				app.MapPost("/dash/overlay/switch", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string handle = Field(await ReadFieldsAsync(request), "handle");
					return Results.Json(new { ok = DashboardApi.ActivateWindow(handle), open = false });
				}));

				app.MapPost("/dash/overlay/launch", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string id = Field(await ReadFieldsAsync(request), "id");
					bool ok = await DashboardApi.LaunchShortcutAndActivateAsync(store, id, logger, request.HttpContext.RequestAborted);
					return Results.Json(new { ok, open = false });
				}));

				app.MapGet("/dash/overlay/state", (Func<IResult>)(() =>
					Results.Json(new
					{
						open = false,
						primaryHandle = "",
						retired = true
					})));

				app.MapPost("/dash/overlay/previews", (Func<IResult>)(() =>
					Results.Json(new { ok = false, count = 0, retired = true })));

				app.MapGet("/dash/open-requested", (Func<IResult>)(() =>
				{
					bool open = dashboard.ConsumeOpenRequest();
					if (open) logger.Info("Playhub Dashboard: il plugin ha raccolto la richiesta di apertura.");
					return Results.Json(new
					{
						open,
						exitBigPicture = false,
						steamModeCommand = manager.ReadSteamModeCommand(),
						focusRecovery = windowFocus.SteamFocusRecoveryVersion,
						steamForeground = OverlayWindowTools.IsSteamForeground()
					});
				}));
				app.MapPost("/steam/mode/ack", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool valid = long.TryParse(Field(fields, "revision"), out long revision) && revision > 0
						&& Guid.TryParseExact(Field(fields, "token"), "N", out _)
						&& bool.TryParse(Field(fields, "completed"), out bool completed);
					return Results.Json(new { ok = valid && manager.AcknowledgeSteamModeCommand(
						revision, Field(fields, "token"), Field(fields, "completed").Equals("true", StringComparison.OrdinalIgnoreCase)) });
				}));
				app.MapPost("/dash/open", (Func<IResult>)delegate
				{
					return Results.Json(new ApiResult { Ok = dashboard.RequestOpen("richiesta esterna") });
				});
				app.MapGet("/dash/windows", (Func<IResult>)(() =>
					Results.Json(DashboardApi.ListWindows(dashboard.PrimaryWindowHandle))));
				app.MapGet("/dash/windows/preview", (Func<HttpRequest, IResult>)(request =>
				{
					int.TryParse(request.Query["width"].ToString(), out int width);
					int.TryParse(request.Query["height"].ToString(), out int height);
					return Results.Json(new
					{
						data = DashboardApi.ReadWindowPreviewAsBase64(
							request.Query["handle"].ToString(),
							width <= 0 ? 720 : width,
							height <= 0 ? 405 : height)
					});
				}));
				app.MapPost("/dash/windows/activate", (Func<HttpRequest, Task<IResult>>)(async request =>
					Results.Json(new ApiResult { Ok = DashboardApi.ActivateWindow(Field(await ReadFieldsAsync(request), "handle")) })));
				app.MapPost("/dash/windows/close", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					int.TryParse(Field(fields, "processId"), out int processId);
					return Results.Json(await DashboardApi.CloseWindowAsync(Field(fields, "handle"), processId));
				}));
				app.MapGet("/dash/shortcuts", (Func<IResult>)(() => Results.Json(DashboardApi.ListShortcuts(store))));
				app.MapPost("/dash/shortcuts/launch", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string id = Field(await ReadFieldsAsync(request), "id");
					return Results.Json(new ApiResult { Ok = await DashboardApi.LaunchShortcutAndActivateAsync(store, id, logger, request.HttpContext.RequestAborted) });
				}));
				app.MapPost("/dash/shortcuts/remove", (Func<HttpRequest, Task<IResult>>)(async request =>
					Results.Json(new ApiResult { Ok = DashboardApi.RemoveShortcut(store, Field(await ReadFieldsAsync(request), "id")) })));
				app.MapPost("/dash/shortcuts/rename", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = DashboardApi.RenameShortcut(store, Field(fields, "id"), Field(fields, "name"));
					logger.Info($"Playhub Dashboard rename '{Field(fields, "id")}' -> '{Field(fields, "name")}': {(ok ? "fatto" : "rifiutato")}.");
					return Results.Json(new ApiResult { Ok = ok });
				}));
				app.MapGet("/dash/usage", (Func<IResult>)(() => Results.Json(DashboardApi.ReadUsage())));
				app.MapGet("/dash/environment", (Func<IResult>)(() => Results.Json(DashboardApi.ReadEnvironment(store))));

				// Quick Settings remains the owner of device controls. The Dashboard
				// only proxies its local agent, and only exposes this tab when the
				// plugin is installed.
				app.MapGet("/dash/quick", (Func<HttpContext, Task<IResult>>)(async context =>
					Results.Json(await quickSettings.GetAsync(context.RequestAborted))));
				app.MapPost("/dash/quick/volume", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = int.TryParse(Field(fields, "level"), out int level)
						&& await quickSettings.SetVolumeAsync(level, request.HttpContext.RequestAborted);
					return Results.Json(new { ok });
				}));
				app.MapPost("/dash/shortcuts/sdl3", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					string value = Field(fields, "enabled");
					bool? enabled = string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
						? null
						: bool.TryParse(value, out bool parsed) ? parsed : null;
					return Results.Json(new ApiResult { Ok = DashboardApi.SetShortcutSdl3(store, Field(fields, "id"), enabled) });
				}));
				app.MapPost("/dash/quick/mute", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = bool.TryParse(Field(fields, "muted"), out bool muted)
						&& await quickSettings.SetMutedAsync(muted, request.HttpContext.RequestAborted);
					return Results.Json(new { ok });
				}));
				app.MapPost("/dash/quick/brightness", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = int.TryParse(Field(fields, "level"), out int level)
						&& await quickSettings.SetBrightnessAsync(level, request.HttpContext.RequestAborted);
					return Results.Json(new { ok });
				}));
				app.MapPost("/dash/quick/bluetooth", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = bool.TryParse(Field(fields, "enabled"), out bool enabled)
						&& await quickSettings.SetBluetoothAsync(enabled, request.HttpContext.RequestAborted);
					return Results.Json(new { ok });
				}));
				app.MapPost("/dash/quick/wifi", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = bool.TryParse(Field(fields, "enabled"), out bool enabled)
						&& await quickSettings.SetWifiAsync(enabled, request.HttpContext.RequestAborted);
					return Results.Json(new { ok });
				}));

				app.MapGet("/dash/bluetooth", (Func<HttpContext, Task<IResult>>)(async context =>
				{
					using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
					timeout.CancelAfter(TimeSpan.FromSeconds(5));
					try { return Results.Json(await OverlayBluetoothService.FindDevicesAsync(timeout.Token)); }
					catch (OperationCanceledException) { return Results.Json(Array.Empty<OverlayBluetoothDevice>()); }
				}));
				app.MapPost("/dash/bluetooth/radio", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					bool ok = bool.TryParse(Field(fields, "enabled"), out bool enabled)
						&& await OverlayBluetoothService.SetRadioAsync(enabled);
					return Results.Json(new { ok });
				}));
				app.MapPost("/dash/bluetooth/pair", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string id = Field(await ReadFieldsAsync(request), "id");
					bool ok = !string.IsNullOrWhiteSpace(id) && await OverlayBluetoothService.PairAsync(id);
					return Results.Json(new { ok });
				}));
				app.MapPost("/dash/bluetooth/unpair", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string id = Field(await ReadFieldsAsync(request), "id");
					bool ok = !string.IsNullOrWhiteSpace(id) && await OverlayBluetoothService.UnpairAsync(id);
					return Results.Json(new { ok });
				}));

				// Attivita' in corso. L'elenco costa un giro su tutto il sistema:
				// lo chiede la pagina quando e' aperta, non l'agente da solo.
				app.MapGet("/dash/processes", (Func<IResult>)(() => Results.Json(DashboardApi.ListProcesses())));
				app.MapPost("/dash/processes/close", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string id = Field(await ReadFieldsAsync(request), "id");
					return Results.Json(new { ok = int.TryParse(id, out int pid) && DashboardApi.CloseProcess(pid) });
				}));
				app.MapPost("/dash/processes/kill", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					string id = Field(await ReadFieldsAsync(request), "id");
					return Results.Json(new { ok = int.TryParse(id, out int pid) && DashboardApi.KillProcess(pid) });
				}));

				// Programmi installati, per aggiungere una preferita senza aprire
				// una finestra di Windows davanti a Big Picture.
				app.MapGet("/dash/programs", (Func<IResult>)(() =>
				{
					DashboardApi.ProgramList list = DashboardApi.ListPrograms();
					// Scritto anche nel log: se un giorno l'elenco torna vuoto,
					// la causa e' qui e non serve tirare a indovinare.
					logger.Info($"Playhub Dashboard programs: {list.Items.Count} found. {list.Note}");
					return Results.Json(list);
				}));

				// La finestra "Apri" di Windows, per i programmi che in nessun
				// elenco compaiono: un .exe portatile, un emulatore scompattato.
				app.MapPost("/dash/programs/pick", (Func<IResult>)(() =>
					Results.Json(new { path = dashboard.PickProgramFile() })));
				app.MapPost("/dash/shortcuts/add", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					string target = Field(fields, "target");
					string name = Field(fields, "name");
					string kind = Field(fields, "kind");
					bool ok = DashboardApi.AddShortcut(store, target, name, kind);
					logger.Info($"Playhub Dashboard add '{name}' ({kind}) -> {(ok ? "fatto" : "rifiutato")}: {target}");
					return Results.Json(new { ok });
				}));
				app.MapGet("/dash/settings", (Func<IResult>)(() => Results.Json(DashboardApi.ReadSettings(store))));
				app.MapPost("/dash/settings", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					string keyboard = Field(fields, "keyboardShortcutEnabled");
					string hotkey = Field(fields, "hotkey");
					string haptics = Field(fields, "navigationHapticsEnabled");
					string hapticsIntensity = Field(fields, "navigationHapticsIntensity");
					string sdl3 = Field(fields, "sdl3NativeControllerEnabled");
					if (!string.IsNullOrWhiteSpace(keyboard) || !string.IsNullOrWhiteSpace(hotkey))
					{
						logger.Info($"Playhub Dashboard shortcut settings: enabled='{keyboard}' hotkey='{hotkey}'.");
					}
					DashboardApi.DashboardSettings updated = DashboardApi.WriteSettings(
						store,
						bool.TryParse(keyboard, out bool keyboardValue) ? keyboardValue : null,
						hotkey,
						bool.TryParse(haptics, out bool hapticsValue) ? hapticsValue : null,
						int.TryParse(hapticsIntensity, out int intensityValue) ? intensityValue : null,
						bool.TryParse(sdl3, out bool sdl3Value) ? sdl3Value : null);
					// RegisterHotKey lega la combinazione al thread una volta
					// sola: senza questa riga la nuova resta scritta nella
					// configurazione e muta fino al riavvio dell'agente.
					if (!string.IsNullOrWhiteSpace(keyboard) || !string.IsNullOrWhiteSpace(hotkey))
					{
						dashboard.ReloadHotkey();
					}
					return Results.Json(updated);
				}));

				// Imparare la combinazione. La cattura non puo' stare dentro
				// l'interfaccia di Steam, che i tasti li consuma prima: qui
				// l'agente ascolta la tastiera per qualche secondo e riporta
				// quello che e' stato premuto.
				app.MapPost("/dash/hotkey/learn", (Func<IResult>)(() =>
				{
					dashboard.BeginLearn();
					return Results.Json(new { ok = true });
				}));
				app.MapGet("/dash/hotkey/learn", (Func<IResult>)(() =>
				{
					(string state, string combo) = dashboard.ReadLearnState();
					return Results.Json(new { state, combo });
				}));
				// Un'immagine dal disco in base64: l'interfaccia di Steam non
				// puo' leggere il disco, quindi banner e copertine passano di qui.
				app.MapGet("/dash/image", (Func<HttpRequest, IResult>)(request =>
					Results.Json(new { data = DashboardApi.ReadImageAsBase64(request.Query["path"].ToString()) })));

				app.MapPost("/restart/steam", (Func<Task<IResult>>)(async () => Results.Json(await manager.RestartSteamAsync())));
				app.MapPost("/restart/decky", (Func<Task<IResult>>)(async () => Results.Json(await manager.RestartDeckyAsync())));
				app.MapPost("/cursor/autohide/start", (Func<IResult>)delegate
				{
					ModeConfig modeConfig2 = store.LoadConfig();
					cursorAutoHide.Start(modeConfig2.Gaming.AutoHideMouseCursorAfterMs);
					return Results.Json(ApiResult.Success("Mouse cursor auto-hide enabled.", manager.GetStatus()));
				});
				app.MapPost("/cursor/autohide/stop", (Func<IResult>)delegate
				{
					cursorAutoHide.Stop();
					return Results.Json(ApiResult.Success("Mouse cursor restored.", manager.GetStatus()));
				});
				app.Lifetime.ApplicationStopped.Register(delegate
				{
					logger.Info("Agent stopped.");
				});
				app.Lifetime.ApplicationStopping.Register(dashboard.Stop);
				app.Lifetime.ApplicationStopping.Register(qamFocus.ModeChanging);
				app.Lifetime.ApplicationStopping.Register(cursorAutoHide.Stop);
				app.Lifetime.ApplicationStopping.Register(windowFocus.Stop);
				app.Lifetime.ApplicationStopping.Register(volumeKeys.Stop);
				app.Lifetime.ApplicationStopping.Register(splashScreen.Dispose);
				_ = Task.Run(async delegate
				{
					try
					{
						await manager.RunSafetyWatchdogAsync(app.Lifetime.ApplicationStopping);
					}
					catch (OperationCanceledException)
					{
					}
					catch (Exception exception3)
					{
						logger.Error("Safety watchdog crashed.", exception3);
					}
				});
				// Installed-program discovery starts when the user opens Add app.
				// Do not enumerate applications on every Windows login.
				try
				{
					logger.Info("Agent listening on " + url + ".");
					await app.RunAsync();
				}
				catch (Exception exception2)
				{
					logger.Error("Agent host crashed.", exception2);
					throw;
				}
			}
			finally
			{
				if (cursorAutoHide != null)
				{
					((IDisposable)cursorAutoHide).Dispose();
				}
			}
		}
	}

	internal static async Task<bool> ResumeXboxGameAsync(uint appId, string? steamPath, CancellationToken stop)
	{
		if (WindowsQamFocusAdapter.CaptureSteam(steamPath) is not { } steam ||
			OverlayWindowTools.GetForegroundWindowHandle() != steam.Handle) return false;

		// Steam raises its own target first. The fallback only runs while the same
		// Big Picture window still owns the foreground.
		try { await Task.Delay(200, stop); }
		catch (OperationCanceledException) when (stop.IsCancellationRequested) { return false; }
		bool StillInSteam() => !stop.IsCancellationRequested &&
			OverlayWindowTools.GetForegroundWindowHandle() == steam.Handle && WindowsQamFocusAdapter.IsWindowOwnerCurrent(steam);
		if (!StillInSteam() || UwpSteamSessionAssociation.FindForApp(appId) is not [var game]) return false;

		using var process = OverlayWindowTools.ProcessLease.TryOpen((int)game.GamePid);
		if (process is null || !process.Alive || process.CreatedAt != game.GameBirth) return false;
		var windows = OverlayWindowTools.EnumerateTopLevelHandles()
			.Where(window => OverlayWindowTools.HasVisibleWindow(window) && OverlayWindowTools.IsUsableWindow(window)
				&& MatchesXboxGameWindow(window, game))
			.Take(2).ToArray();
		if (windows is not [var window] || WindowsQamFocusAdapter.Capture(window, game.GamePid) is not { } target ||
			target.GameBirth != game.GameBirth) return false;
		if (UwpSteamSessionAssociation.Find(game.GamePid, appId) != game ||
			(OverlayWindowTools.WindowOwnerProcessId(window) != game.GamePid && !UwpSteamSessionAssociation.IsOnlyRunningInstance(game))) return false;

		return WindowsQamFocusAdapter.ActivateWindow(window, () => StillInSteam() && process.Alive &&
			WindowsQamFocusAdapter.IsWindowOwnerCurrent(target) && MatchesXboxGameWindow(window, game));
	}

	private static bool MatchesXboxGameWindow(nint window, UwpSessionIdentity game) =>
		OverlayWindowTools.WindowOwnerProcessId(window) == game.GamePid || UwpFrameWindow.Matches(window, game.Aumid);
}
