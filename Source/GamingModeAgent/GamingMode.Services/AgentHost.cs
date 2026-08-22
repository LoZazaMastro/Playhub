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

namespace GamingMode.Services;

public static class AgentHost
{
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
			try
			{
				using System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
				currentProcess.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
				logger.Info("Agent process priority lowered to BelowNormal.");
			}
			catch
			{
			}
			JsonStore store = new JsonStore(paths, logger);
			ProcessTools processTools = new ProcessTools(logger);
			ShellTools shellTools = new ShellTools(logger);
			CursorAutoHideService cursorAutoHide = new CursorAutoHideService(logger);
			try
			{
				using GamingWindowFocusService windowFocus = new GamingWindowFocusService(logger);
				using SplashScreenService splashScreen = new SplashScreenService(logger);
				using SystemVolumeKeyService volumeKeys = new SystemVolumeKeyService(logger);
				using OverlayQuickSettingsClient quickSettings = new OverlayQuickSettingsClient();
				ModeManager manager = new ModeManager(paths, store, processTools, shellTools, cursorAutoHide, windowFocus, volumeKeys, logger);
				// La Playhub Dashboard e' una schermata del plugin di Steam. Qui
				// resta solo cio' che il plugin non puo' fare da dentro Steam: la
				// scorciatoia da tastiera per aprirla e l'indicatore del volume.
				// NESSUN controller viene letto o toccato.
				using DashboardShortcutService dashboard = new DashboardShortcutService(
					store,
					volumeKeys,
					() => windowFocus.IsLaunchCurtainOnScreen,
					logger);
				dashboard.Start();
				ModeConfig modeConfig = store.LoadConfig();
				string value = (modeConfig.Safety.AllowRemoteApi ? "0.0.0.0" : "127.0.0.1");
				string url = $"http://{value}:{modeConfig.Safety.ApiPort}";
				bool flag = args.Any((string arg) => arg.Equals("shell", StringComparison.OrdinalIgnoreCase));
				if (flag || args.Any((string arg) => arg.Equals("--boot", StringComparison.OrdinalIgnoreCase) || arg.Equals("boot", StringComparison.OrdinalIgnoreCase)))
				{
					bool showSplash = flag && (modeConfig.NextBootMode ?? modeConfig.DefaultMode) == ModeKind.Gaming && !SafeModeGuard.ShouldForceDesktop(paths);
					int minSplashVisibleMs = Math.Clamp(modeConfig.Gaming.Splash.MinVisibleMs, 0, 120000);
					int maxSplashVisibleMs = Math.Clamp(modeConfig.Gaming.Splash.MaxVisibleMs, 5000, 120000);
					bool waitForSteamFullscreen = false;
					if (showSplash)
					{
						splashScreen.Show(modeConfig.Gaming.Splash, maxSplashVisibleMs + 15000);
					}
					try
					{
						processTools.CleanupDeckyOrphanedForks();
						await manager.ApplyBootModeAsync(flag);
						waitForSteamFullscreen = true;
					}
					catch (Exception exception)
					{
						logger.Error("Boot mode application failed. Continuing agent startup in Desktop-safe state.", exception);
					}
					finally
					{
						if (showSplash)
						{
							bool steamRunning = waitForSteamFullscreen && manager.GetStatus().Steam.Running;
							if (steamRunning)
							{
								int firstWaitMs = Math.Min(15000, maxSplashVisibleMs);
								bool detected = await SteamFullscreenDetector.WaitForFullscreenAsync(TimeSpan.FromMilliseconds(firstWaitMs), logger, suppressDesktopWindows: true);
								if (!detected)
								{
									processTools.OpenUri("steam://open/bigpicture");
									int remainingMs = maxSplashVisibleMs - firstWaitMs;
									if (remainingMs > 0)
									{
										await SteamFullscreenDetector.WaitForFullscreenAsync(TimeSpan.FromMilliseconds(remainingMs), logger, suppressDesktopWindows: true);
									}
								}
							}
							await splashScreen.HideAsync(minSplashVisibleMs, fade: true);
							if (steamRunning)
							{
								SteamFullscreenDetector.TryFocusSteamWindow(logger);
							}
						}
					}
				}
				else
				{
					processTools.CleanupDeckyOrphanedForks();
				}
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
				app.MapGet("/status", (Func<IResult>)(() => Results.Json(manager.GetStatus())));
				app.MapPost("/mode/gaming", (Func<Task<IResult>>)(async () => Results.Json(await manager.ApplyModeAsync(ModeKind.Gaming, "Applied Gaming Mode"))));
				app.MapPost("/mode/desktop", (Func<Task<IResult>>)(async () => Results.Json(await manager.ApplyModeAsync(ModeKind.Desktop, "Applied Desktop Mode"))));
				app.MapPost("/mode/gaming/switch", (Func<IResult>)(() => Results.Json(manager.SwitchToMode(ModeKind.Gaming))));
				app.MapPost("/mode/desktop/switch", (Func<IResult>)(() => Results.Json(manager.SwitchToMode(ModeKind.Desktop))));
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
				// Meta' di questa storia vive dentro Steam, dove la console non
				// la vede nessuno, e meta' qui. Finche' le due meta' scrivevano
				// in posti diversi, ogni guasto si poteva solo supporre. Con
				// questa rotta le righe finiscono tutte nello stesso file, in
				// ordine di tempo: si legge cosa e' successo invece di dedurlo.
				app.MapPost("/dash/log", (Func<HttpRequest, Task<IResult>>)(async request =>
				{
					var fields = await ReadFieldsAsync(request);
					logger.Info("[PLUGIN] " + Field(fields, "message"));
					return Results.Json(new { ok = true });
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
					return Results.Json(new { open });
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
					Results.Json(new ApiResult { Ok = DashboardApi.CloseWindow(Field(await ReadFieldsAsync(request), "handle")) })));
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
					logger.Info($"Playhub Dashboard settings: enabled='{keyboard}' hotkey='{hotkey}'.");
					DashboardApi.DashboardSettings updated = DashboardApi.WriteSettings(
						store,
						bool.TryParse(keyboard, out bool keyboardValue) ? keyboardValue : null,
						hotkey);
					// RegisterHotKey lega la combinazione al thread una volta
					// sola: senza questa riga la nuova resta scritta nella
					// configurazione e muta fino al riavvio dell'agente.
					dashboard.ReloadHotkey();
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
				app.Lifetime.ApplicationStopping.Register(cursorAutoHide.Stop);
				app.Lifetime.ApplicationStopping.Register(windowFocus.Stop);
				app.Lifetime.ApplicationStopping.Register(volumeKeys.Stop);
				app.Lifetime.ApplicationStopping.Register(splashScreen.Dispose);
				Task.Run(async delegate
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
				// Prepara l'elenco delle app dopo che l'agente ha finito il lavoro
				// critico di avvio. La scansione usa un solo thread STA a priorita'
				// bassa e popola la cache senza rallentare Steam o il primo accesso
				// alla Dashboard.
				_ = Task.Run(async () =>
				{
					try
					{
						await Task.Delay(TimeSpan.FromSeconds(4), app.Lifetime.ApplicationStopping);
						DashboardApi.PrewarmPrograms();
					}
					catch (OperationCanceledException)
					{
					}
				});
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
}
