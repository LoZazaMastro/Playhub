using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

internal static class SteamCdpCapture
{
	private sealed class DevToolsTarget
	{
		[JsonPropertyName("title")]
		public string Title { get; set; } = "";

		[JsonPropertyName("url")]
		public string Url { get; set; } = "";

		[JsonPropertyName("webSocketDebuggerUrl")]
		public string WebSocketDebuggerUrl { get; set; } = "";
	}

	private static readonly HttpClient HttpClient = new()
	{
		Timeout = TimeSpan.FromMilliseconds(650),
	};

	public static string TryCaptureJpegBase64(FileLogger logger)
	{
		try
		{
			return CaptureAsync().WaitAsync(TimeSpan.FromMilliseconds(1400)).GetAwaiter().GetResult();
		}
		catch (Exception exception)
		{
			logger.Info($"[DASH] cattura Steam CEF non disponibile: {exception.GetType().Name}: {exception.Message}");
			return "";
		}
	}

	private static async Task<string> CaptureAsync()
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(1250));
		string payload = await HttpClient.GetStringAsync("http://127.0.0.1:8080/json", timeout.Token).ConfigureAwait(false);
		List<DevToolsTarget>? targets = JsonSerializer.Deserialize<List<DevToolsTarget>>(payload);
		DevToolsTarget? target = targets?.FirstOrDefault(item =>
			item.Title.Contains("Big Picture", StringComparison.OrdinalIgnoreCase)
			|| (item.Url.Contains("browserType=3", StringComparison.OrdinalIgnoreCase)
				&& !item.Url.Contains("browserviewpopup", StringComparison.OrdinalIgnoreCase)));
		if (target is null || string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl)) return "";

		using ClientWebSocket socket = new();
		await socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), timeout.Token).ConfigureAwait(false);
		byte[] request = JsonSerializer.SerializeToUtf8Bytes(new
		{
			id = 1,
			method = "Page.captureScreenshot",
			@params = new
			{
				format = "jpeg",
				quality = 82,
				fromSurface = true,
				captureBeyondViewport = false,
			},
		});
		await socket.SendAsync(request, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);

		byte[] buffer = new byte[65536];
		while (socket.State == WebSocketState.Open && !timeout.IsCancellationRequested)
		{
			using MemoryStream message = new();
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close) return "";
				message.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);

			using JsonDocument response = JsonDocument.Parse(message.ToArray());
			if (!response.RootElement.TryGetProperty("id", out JsonElement id) || id.GetInt32() != 1) continue;
			if (!response.RootElement.TryGetProperty("result", out JsonElement protocolResult)
				|| !protocolResult.TryGetProperty("data", out JsonElement data)) return "";
			return data.GetString() ?? "";
		}
		return "";
	}
}
