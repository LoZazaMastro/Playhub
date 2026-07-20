using System;
using System.IO;
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
				ModeManager manager = new ModeManager(paths, store, processTools, shellTools, cursorAutoHide, windowFocus, volumeKeys, logger);
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
