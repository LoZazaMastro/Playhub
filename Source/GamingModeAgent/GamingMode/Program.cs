using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using GamingMode.Models;
using GamingMode.Services;

namespace GamingMode;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		try
		{
			return MainAsync(args).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			try
			{
				AppPaths appPaths = AppPaths.Create();
				Directory.CreateDirectory(appPaths.ConfigDirectory);
				new FileLogger(appPaths.LogPath).Error("Gaming Mode crashed.", ex);
			}
			catch
			{
			}
			if (!IsBackgroundEntry(args))
			{
				try
				{
					MessageBox.Show("Gaming Mode could not start.\n\n" + ex.Message, "Gaming Mode", MessageBoxButton.OK, MessageBoxImage.Hand);
				}
				catch
				{
				}
			}
			return 1;
		}
	}

	private static async Task<int> MainAsync(string[] args)
	{
		AppPaths appPaths = AppPaths.Create();
		Directory.CreateDirectory(appPaths.ConfigDirectory);
		FileLogger logger = new FileLogger(appPaths.LogPath);

		// QUALE ESEGUIBILE STA GIRANDO.
		//
		// Serve piu' di quanto sembri. E' gia' successo di passare mezz'ora a
		// leggere log per capire perche' una correzione non aveva effetto: la
		// correzione c'era, ma l'agente in esecuzione era quello di prima,
		// perche' l'aggiornamento non lo aveva sostituito. Con questa riga in
		// cima al log il dubbio non si pone: si guarda la data e si sa.
		try
		{
			string exePath = Environment.ProcessPath ?? "";
			string built = File.Exists(exePath)
				? File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm:ss")
				: "sconosciuta";
			logger.Info($"Gaming Mode agent avviato. Eseguibile: {exePath} (compilato il {built}).");
		}
		catch
		{
		}

		if (args.Length != 0)
		{
			string text = args[0].ToLowerInvariant();
			if (text == "agent" || text == "shell")
			{
				await AgentHost.RunAsync(appPaths, logger, args);
				return 0;
			}
			if (await RunCliCommandAsync(text))
			{
				return 0;
			}
		}
		Application application = new Application();
		application.ShutdownMode = ShutdownMode.OnMainWindowClose;
		application.Run(new MainWindow());
		return 0;
	}

	private static bool IsBackgroundEntry(string[] args)
	{
		if (args.Length == 0)
		{
			return false;
		}
		string text = args[0].ToLowerInvariant();
		if (text == "agent" || text == "shell")
		{
			return true;
		}
		return false;
	}

	private static async Task<bool> RunCliCommandAsync(string command)
	{
		AgentClient client = new AgentClient();
		await client.EnsureAgentRunningAsync();
		ApiResult apiResult = command switch
		{
			"gaming" => await client.ApplyGamingModeAsync(), 
			"desktop" => await client.ApplyDesktopModeAsync(), 
			"switch-gaming" => await client.SwitchToGamingModeAsync(), 
			"switch-desktop" => await client.SwitchToDesktopModeAsync(), 
			"restart-gaming" => await client.RestartInGamingModeAsync(), 
			"restart-desktop" => await client.RestartInDesktopModeAsync(), 
			"default-gaming" => await client.SetDefaultGamingAsync(), 
			"default-desktop" => await client.SetDefaultDesktopAsync(), 
			"restart-steam" => await client.RestartSteamAsync(), 
			"restart-decky" => await client.RestartDeckyAsync(), 
			"cursor-auto" => await client.StartCursorAutoHideAsync(), 
			"cursor-show" => await client.StopCursorAutoHideAsync(), 
			"status" => ApiResult.Success("Status", await client.GetStatusAsync()), 
			_ => null, 
		};
		if (apiResult == null)
		{
			return false;
		}
		MessageBox.Show(apiResult.Message, "Gaming Mode");
		return true;
	}
}
