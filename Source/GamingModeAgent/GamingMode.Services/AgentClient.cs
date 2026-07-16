using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GamingMode.Models;

namespace GamingMode.Services;

public sealed class AgentClient
{
	private readonly HttpClient _httpClient;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	private readonly int _port;

	public AgentClient(int port = 47991)
	{
		_port = port;
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri($"http://127.0.0.1:{port}"),
			Timeout = TimeSpan.FromSeconds(2.0)
		};
	}

	public async Task<bool> EnsureAgentRunningAsync()
	{
		if (await IsAgentRunningAsync())
		{
			return true;
		}
		string processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath))
		{
			return false;
		}
		Process.Start(new ProcessStartInfo
		{
			FileName = processPath,
			Arguments = "agent",
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = AppContext.BaseDirectory
		});
		for (int i = 0; i < 40; i++)
		{
			await Task.Delay(250);
			if (await IsAgentRunningAsync())
			{
				return true;
			}
		}
		return false;
	}

	public async Task<bool> IsAgentRunningAsync()
	{
		try
		{
			using HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync("/health");
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	public async Task<ModeStatus?> GetStatusAsync()
	{
		return await _httpClient.GetFromJsonAsync<ModeStatus>("/status", JsonOptions);
	}

	public Task<ApiResult?> ApplyGamingModeAsync()
	{
		return PostAsync("/mode/gaming");
	}

	public Task<ApiResult?> ApplyDesktopModeAsync()
	{
		return PostAsync("/mode/desktop");
	}

	public Task<ApiResult?> SwitchToGamingModeAsync()
	{
		return PostAsync("/mode/gaming/switch");
	}

	public Task<ApiResult?> SwitchToDesktopModeAsync()
	{
		return PostAsync("/mode/desktop/switch");
	}

	public Task<ApiResult?> RestartInGamingModeAsync()
	{
		return PostAsync("/mode/gaming/restart");
	}

	public Task<ApiResult?> RestartInDesktopModeAsync()
	{
		return PostAsync("/mode/desktop/restart");
	}

	public Task<ApiResult?> SetDefaultGamingAsync()
	{
		return PostAsync("/default/gaming");
	}

	public Task<ApiResult?> SetDefaultDesktopAsync()
	{
		return PostAsync("/default/desktop");
	}

	public async Task<ApiResult?> SetSplashLogoAsync(string? path)
	{
		using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/config/splash/logo", new SplashLogoRequest
		{
			Path = path
		}, JsonOptions);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<ApiResult>(JsonOptions);
	}

	public Task<ApiResult?> RestartSteamAsync()
	{
		return PostAsync("/restart/steam");
	}

	public Task<ApiResult?> RestartDeckyAsync()
	{
		return PostAsync("/restart/decky");
	}

	public Task<ApiResult?> StartCursorAutoHideAsync()
	{
		return PostAsync("/cursor/autohide/start");
	}

	public Task<ApiResult?> StopCursorAutoHideAsync()
	{
		return PostAsync("/cursor/autohide/stop");
	}

	private async Task<ApiResult?> PostAsync(string path)
	{
		using HttpResponseMessage response = await _httpClient.PostAsync(path, null);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<ApiResult>(JsonOptions);
	}
}
