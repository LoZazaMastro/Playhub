using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GamingMode.Models;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.Devices.Radios;
using Windows.Management.Deployment;

namespace GamingMode.Services;

public sealed record OverlayQuickSettingsSnapshot(
	bool Available,
	int Volume,
	bool Muted,
	bool BrightnessAvailable,
	int Brightness,
	bool BluetoothAvailable,
	bool BluetoothEnabled,
	bool WifiAvailable,
	bool WifiEnabled);

public sealed class OverlayQuickSettingsClient : IDisposable
{
	private readonly HttpClient _client = new()
	{
		BaseAddress = new Uri("http://127.0.0.1:47993"),
		Timeout = TimeSpan.FromMilliseconds(1400)
	};

	public async Task<OverlayQuickSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _client.GetAsync("/quick-settings", cancellationToken);
			response.EnsureSuccessStatusCode();
			using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			JsonElement root = document.RootElement;
			JsonElement volume = root.TryGetProperty("volume", out JsonElement volumeValue) ? volumeValue : default;
			JsonElement dimmer = root.TryGetProperty("dimmer", out JsonElement dimmerValue) ? dimmerValue : default;
			JsonElement bluetooth = root.TryGetProperty("bluetooth", out JsonElement bluetoothValue) ? bluetoothValue : default;
			JsonElement wifi = root.TryGetProperty("wifi", out JsonElement wifiValue) ? wifiValue : default;
			return new OverlayQuickSettingsSnapshot(
				true,
				ReadInt(volume, "level"),
				ReadBool(volume, "muted"),
				ReadBool(dimmer, "available"),
				100 - ReadInt(dimmer, "level"),
				ReadBool(bluetooth, "available"),
				ReadBool(bluetooth, "enabled"),
				ReadBool(wifi, "available"),
				ReadBool(wifi, "enabled"));
		}
		catch
		{
			return new OverlayQuickSettingsSnapshot(false, 0, false, false, 0, false, false, false, false);
		}
	}

	public Task<bool> SetVolumeAsync(int level, CancellationToken cancellationToken) =>
		PostAsync("/quick-settings/volume", new { level = Math.Clamp(level, 0, 100) }, cancellationToken);

	public Task<bool> SetMutedAsync(bool muted, CancellationToken cancellationToken) =>
		PostAsync("/quick-settings/volume", new { muted }, cancellationToken);

	public Task<bool> SetBrightnessAsync(int level, CancellationToken cancellationToken) =>
		PostAsync("/quick-settings/dimmer", new { level = 100 - Math.Clamp(level, 0, 100) }, cancellationToken);

	public Task<bool> SetBluetoothAsync(bool enabled, CancellationToken cancellationToken) =>
		PostAsync("/quick-settings/bluetooth", new { enabled }, cancellationToken);

	public Task<bool> SetWifiAsync(bool enabled, CancellationToken cancellationToken) =>
		PostAsync("/quick-settings/wifi", new { enabled }, cancellationToken);

	private async Task<bool> PostAsync(string path, object body, CancellationToken cancellationToken)
	{
		try
		{
			using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _client.PostAsync(path, content, cancellationToken);
			return response.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static bool ReadBool(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object
		&& element.TryGetProperty(name, out JsonElement value)
		&& value.ValueKind == JsonValueKind.True;

	private static int ReadInt(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object
		&& element.TryGetProperty(name, out JsonElement value)
		&& value.TryGetInt32(out int result) ? result : 0;

	public void Dispose() => _client.Dispose();
}

public sealed record OverlaySystemSnapshot(int CpuPercent, int MemoryPercent, long MemoryUsedMb, long MemoryTotalMb);

public sealed class OverlaySystemMetrics
{
	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatus
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhysical;
		public ulong AvailablePhysical;
		public ulong TotalPageFile;
		public ulong AvailablePageFile;
		public ulong TotalVirtual;
		public ulong AvailableVirtual;
		public ulong AvailableExtendedVirtual;
	}

	private ulong _lastIdle;
	private ulong _lastKernel;
	private ulong _lastUser;

	public OverlaySystemSnapshot Read()
	{
		int cpu = ReadCpu();
		MemoryStatus memory = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
		if (!GlobalMemoryStatusEx(ref memory)) return new OverlaySystemSnapshot(cpu, 0, 0, 0);
		long total = (long)(memory.TotalPhysical / 1024 / 1024);
		long used = (long)((memory.TotalPhysical - memory.AvailablePhysical) / 1024 / 1024);
		return new OverlaySystemSnapshot(cpu, (int)memory.MemoryLoad, used, total);
	}

	private int ReadCpu()
	{
		if (!GetSystemTimes(out long idleRaw, out long kernelRaw, out long userRaw)) return 0;
		ulong idle = (ulong)idleRaw;
		ulong kernel = (ulong)kernelRaw;
		ulong user = (ulong)userRaw;
		ulong idleDelta = idle - _lastIdle;
		ulong totalDelta = kernel - _lastKernel + user - _lastUser;
		_lastIdle = idle;
		_lastKernel = kernel;
		_lastUser = user;
		if (totalDelta == 0) return 0;
		return Math.Clamp((int)Math.Round(100d * (totalDelta - idleDelta) / totalDelta), 0, 100);
	}

	[DllImport("kernel32.dll")]
	private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

public sealed record OverlayBluetoothDevice(
	string Id,
	string Name,
	bool Paired,
	bool Connected,
	bool CanPair,
	int? SignalStrength);

public static class OverlayBluetoothService
{
	// Bluetooth classico E Bluetooth Low Energy. Cercando solo il primo si vedono
	// i televisori e le cuffie, ma NON i controller moderni (Xbox Wireless,
	// DualSense, Steam Controller), che si presentano come dispositivi LE.
	private const string BluetoothProtocolId = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
	private const string BluetoothLeProtocolId = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
	private static readonly string[] RequestedProperties =
	{
		"System.Devices.Aep.IsPaired",
		"System.Devices.Aep.IsConnected",
		"System.Devices.Aep.SignalStrength",
		"System.Devices.Aep.DeviceAddress",
		"System.Devices.Aep.Bluetooth.Le.IsConnectable"
	};

	public static async Task<IReadOnlyList<OverlayBluetoothDevice>> FindDevicesAsync(CancellationToken cancellationToken)
	{
		string selector =
			$"System.Devices.Aep.ProtocolId:=\"{BluetoothProtocolId}\" OR System.Devices.Aep.ProtocolId:=\"{BluetoothLeProtocolId}\"";
		ConcurrentDictionary<string, DeviceInformation> devices = new(StringComparer.OrdinalIgnoreCase);
		TaskCompletionSource enumerationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
		DeviceWatcher watcher = DeviceInformation.CreateWatcher(
			selector,
			RequestedProperties,
			DeviceInformationKind.AssociationEndpoint);
		watcher.Added += (_, device) => devices[device.Id] = device;
		watcher.Updated += (_, update) =>
		{
			if (devices.TryGetValue(update.Id, out DeviceInformation? device)) device.Update(update);
		};
		watcher.Removed += (sender, update) => devices.TryRemove(update.Id, out DeviceInformation? _);
		watcher.EnumerationCompleted += (_, _) => enumerationFinished.TrySetResult();
		watcher.Stopped += (_, _) => enumerationFinished.TrySetResult();
		watcher.Start();
		try
		{
			await Task.WhenAny(enumerationFinished.Task, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
			cancellationToken.ThrowIfCancellationRequested();
		}
		finally
		{
			if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
			{
				try { watcher.Stop(); } catch { }
			}
		}
		List<OverlayBluetoothDevice> result = new();
		foreach (DeviceInformation device in devices.Values)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(device.Name)) continue;
			bool paired = ReadBool(device, "System.Devices.Aep.IsPaired") || device.Pairing.IsPaired;
			bool connected = ReadBool(device, "System.Devices.Aep.IsConnected");
			int? signal = ReadInt(device, "System.Devices.Aep.SignalStrength");
			result.Add(new OverlayBluetoothDevice(device.Id, device.Name.Trim(), paired, connected, device.Pairing.CanPair, signal));
		}
		return result
			.GroupBy(device => NormalizeDeviceIdentity(device), StringComparer.OrdinalIgnoreCase)
			.Select(group => group.OrderByDescending(device => device.Connected).ThenByDescending(device => device.Paired).First())
			.OrderByDescending(device => device.Connected)
			.ThenByDescending(device => device.Paired)
			.ThenByDescending(device => device.SignalStrength ?? int.MinValue)
			.ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
	}

	public static async Task<bool> SetRadioAsync(bool enabled)
	{
		IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();
		bool changed = false;
		foreach (Radio radio in radios.Where(item => item.Kind == RadioKind.Bluetooth))
		{
			RadioAccessStatus status = await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off);
			changed |= status == RadioAccessStatus.Allowed;
		}
		return changed;
	}

	public static async Task<bool> PairAsync(string id)
	{
		DeviceInformation device = await DeviceInformation.CreateFromIdAsync(
			id,
			RequestedProperties,
			DeviceInformationKind.AssociationEndpoint);
		if (device.Pairing.IsPaired) return true;
		if (!device.Pairing.CanPair) return false;

		DevicePairingKinds supportedKinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch;
		DeviceInformationCustomPairing custom = device.Pairing.Custom;
		void AcceptSafePairing(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
		{
			if (args.PairingKind is DevicePairingKinds.ConfirmOnly or DevicePairingKinds.ConfirmPinMatch)
			{
				args.Accept();
			}
		}
		custom.PairingRequested += AcceptSafePairing;
		DevicePairingResult result;
		try
		{
			result = await custom.PairAsync(supportedKinds, DevicePairingProtectionLevel.Default);
		}
		finally
		{
			custom.PairingRequested -= AcceptSafePairing;
		}
		return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
	}

	public static async Task<bool> UnpairAsync(string id)
	{
		DeviceInformation device = await DeviceInformation.CreateFromIdAsync(
			id,
			RequestedProperties,
			DeviceInformationKind.AssociationEndpoint);
		if (!device.Pairing.IsPaired) return true;
		DeviceUnpairingResult result = await device.Pairing.UnpairAsync();
		return result.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
	}

	private static bool ReadBool(DeviceInformation device, string name) =>
		device.Properties.TryGetValue(name, out object? value) && value is bool flag && flag;

	private static int? ReadInt(DeviceInformation device, string name)
	{
		if (!device.Properties.TryGetValue(name, out object? value) || value is null) return null;
		try { return Convert.ToInt32(value); }
		catch { return null; }
	}

	private static string NormalizeDeviceIdentity(OverlayBluetoothDevice device)
	{
		string id = device.Id.Replace("#BluetoothLEDevice", "#BluetoothDevice", StringComparison.OrdinalIgnoreCase);
		int address = id.IndexOf("Dev_", StringComparison.OrdinalIgnoreCase);
		return address >= 0 ? id[address..] : device.Name;
	}
}

public sealed record OverlayWindowsApp(string Name, string AppUserModelId, string PackageFamilyName);

public static class OverlayAppLauncher
{
	[ComImport]
	[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IApplicationActivationManager
	{
		int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
		int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
		int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, out uint processId);
	}

	public static async Task<IReadOnlyList<OverlayWindowsApp>> FindWindowsAppsAsync(CancellationToken cancellationToken)
	{
		PackageManager manager = new();
		List<OverlayWindowsApp> apps = new();
		foreach (Package package in manager.FindPackagesForUser(string.Empty))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var entries = await package.GetAppListEntriesAsync();
				foreach (var entry in entries)
				{
					string name = entry.DisplayInfo.DisplayName?.Trim() ?? "";
					if (name.Length == 0 || string.IsNullOrWhiteSpace(entry.AppUserModelId)) continue;
					apps.Add(new OverlayWindowsApp(name, entry.AppUserModelId, package.Id.FamilyName));
				}
			}
			catch
			{
			}
		}
		return apps
			.GroupBy(app => app.AppUserModelId, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
	}

	// Logo delle app di Windows. Una scorciatoia a un'app del Microsoft Store
	// non punta a un file ma a un identificativo (AUMID), quindi non c'e' alcuna
	// icona da estrarre: il logo va chiesto al pacchetto. Il risultato resta in
	// memoria per non rienumerare i pacchetti a ogni disegno.
	private static readonly Dictionary<string, ImageSource?> LogoCache = new(StringComparer.OrdinalIgnoreCase);

	public static async Task<Dictionary<string, ImageSource?>> GetAppLogosAsync(IReadOnlyList<string> appUserModelIds, CancellationToken cancellationToken)
	{
		Dictionary<string, ImageSource?> result = new(StringComparer.OrdinalIgnoreCase);
		List<string> pending = new();
		lock (LogoCache)
		{
			foreach (string id in appUserModelIds)
			{
				if (string.IsNullOrWhiteSpace(id)) continue;
				if (LogoCache.TryGetValue(id, out ImageSource? cached)) result[id] = cached;
				else pending.Add(id);
			}
		}
		if (pending.Count == 0) return result;

		HashSet<string> wanted = new(pending, StringComparer.OrdinalIgnoreCase);
		PackageManager manager = new();
		foreach (Package package in manager.FindPackagesForUser(string.Empty))
		{
			if (wanted.Count == 0) break;
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				foreach (AppListEntry entry in await package.GetAppListEntriesAsync())
				{
					string id = entry.AppUserModelId ?? "";
					if (!wanted.Contains(id)) continue;
					wanted.Remove(id);
					ImageSource? logo = await ReadLogoAsync(entry, cancellationToken);
					result[id] = logo;
					lock (LogoCache)
					{
						LogoCache[id] = logo;
					}
				}
			}
			catch
			{
			}
		}
		// Quel che non si trova viene memorizzato come assente, altrimenti si
		// rienumererebbe l'intero sistema a ogni apertura.
		lock (LogoCache)
		{
			foreach (string missing in wanted) LogoCache[missing] = null;
		}
		return result;
	}

	private static async Task<ImageSource?> ReadLogoAsync(AppListEntry entry, CancellationToken cancellationToken)
	{
		try
		{
			// 44x44 e' il riquadro QUADRATO dell'app. Chiedendo 256 Windows
			// restituisce spesso il logo largo del riquadro grande, che dentro
			// uno spazio quadrato appare molto piu' piccolo delle altre icone.
			RandomAccessStreamReference reference = entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(44, 44));
			using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync().AsTask(cancellationToken);
			using MemoryStream buffer = new();
			await stream.AsStreamForRead().CopyToAsync(buffer, cancellationToken);
			buffer.Position = 0;
			BitmapImage image = new();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = buffer;
			image.EndInit();
			image.Freeze();
			// Il margine vuoto del logo viene tolto: senza, l'icona di un'app
			// del Microsoft Store appare molto piu' piccola di quella di un
			// programma a parita' di riquadro.
			return OverlayIconTrim.Trim(image);
		}
		catch
		{
			return null;
		}
	}

	public static bool Launch(GamingOverlayShortcut shortcut)
	{
		return TryLaunch(shortcut, out _);
	}

	public static Task<(bool Ok, int ProcessId)> LaunchAsync(GamingOverlayShortcut shortcut)
	{
		TaskCompletionSource<(bool Ok, int ProcessId)> completion = new(
			TaskCreationOptions.RunContinuationsAsynchronously);
		Thread worker = new(() =>
		{
			try
			{
				if (shortcut.Kind == GamingOverlayShortcutKind.WindowsApp)
				{
					EnsureExplorerForPackagedApp();
				}
				bool ok = TryLaunch(shortcut, out int processId);
				completion.TrySetResult((ok, processId));
			}
			catch (Exception exception)
			{
				completion.TrySetException(exception);
			}
		});
		// IApplicationActivationManager e le API della shell sono COM. Su un
		// worker MTA alcune app pacchettizzate vengono create ma la chiamata non
		// torna e la loro finestra resta sospesa. Un STA dedicato replica il
		// contesto del menu Start.
		worker.SetApartmentState(ApartmentState.STA);
		worker.IsBackground = true;
		worker.Name = "Playhub app activation";
		worker.Start();
		return completion.Task;
	}

	private static void EnsureExplorerForPackagedApp()
	{
		if (Process.GetProcessesByName("explorer").Length > 0) return;
		Process.Start(new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
			UseShellExecute = true,
			WindowStyle = ProcessWindowStyle.Hidden
		});
		// Le app Windows dipendono dai servizi della shell anche quando Playhub
		// e' la shell di Gaming Mode. L'attesa avviene sul worker dedicato e solo
		// al primo avvio di un'app pacchettizzata.
		DateTime deadline = DateTime.UtcNow.AddSeconds(2.5);
		while (DateTime.UtcNow < deadline)
		{
			if (Process.GetProcessesByName("explorer").Length > 0)
			{
				Thread.Sleep(350);
				return;
			}
			Thread.Sleep(100);
		}
	}

	public static bool TryLaunch(GamingOverlayShortcut shortcut, out int processId)
	{
		processId = 0;
		if (shortcut.Kind == GamingOverlayShortcutKind.DesktopProgram)
		{
			if (!File.Exists(shortcut.Target)) return false;
			Process? process = Process.Start(new ProcessStartInfo
			{
				FileName = shortcut.Target,
				Arguments = shortcut.Arguments ?? "",
				WorkingDirectory = Directory.Exists(shortcut.WorkingDirectory)
					? shortcut.WorkingDirectory
					: Path.GetDirectoryName(shortcut.Target) ?? Environment.CurrentDirectory,
				UseShellExecute = true
			});
			processId = process?.Id ?? 0;
			return true;
		}
		Type type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true)!;
		IApplicationActivationManager manager = (IApplicationActivationManager)Activator.CreateInstance(type)!;
		try
		{
			Marshal.ThrowExceptionForHR(manager.ActivateApplication(shortcut.Target, shortcut.Arguments, 0, out uint activatedProcessId));
			processId = unchecked((int)activatedProcessId);
			return true;
		}
		finally
		{
			if (Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager);
		}
	}
}

// Luminosita' dello schermo INDIPENDENTE da Quick Settings, via DDC/CI: e' il
// canale standard verso i monitor e non richiede pacchetti aggiuntivi. Quando
// Quick Settings e' installato la Dashboard usa quello; questa e' la via
// autonoma per tutti gli altri casi.
public static class OverlayDisplayBrightness
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct PhysicalMonitor
	{
		public nint Handle;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;
	}

	private const uint MonitorDefaultToPrimary = 1u;

	public static bool IsAvailable()
	{
		return TryRead(out _, out _, out _);
	}

	public static int GetPercentage()
	{
		if (!TryRead(out uint minimum, out uint current, out uint maximum)) return -1;
		if (maximum <= minimum) return -1;
		return (int)Math.Round((current - minimum) * 100d / (maximum - minimum));
	}

	// I pannelli dei portatili non rispondono a DDC/CI: il loro livello sta nei
	// contatori WMI. La lettura e' lenta, quindi e' asincrona e serve solo come
	// ripiego quando la via diretta non da' un valore.
	public static async Task<int> GetPercentageAsync(CancellationToken cancellationToken)
	{
		int direct = GetPercentage();
		if (direct >= 0) return direct;
		try
		{
			ProcessStartInfo info = new()
			{
				FileName = "powershell.exe",
				Arguments = "-NoProfile -NonInteractive -Command \"(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction Stop).CurrentBrightness\"",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process? process = Process.Start(info);
			if (process is null) return -1;
			string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
			await process.WaitForExitAsync(cancellationToken);
			string first = output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
			return int.TryParse(first, out int value) ? Math.Clamp(value, 0, 100) : -1;
		}
		catch
		{
			return -1;
		}
	}

	// Anche la scrittura ha il suo ripiego WMI.
	public static async Task<bool> SetPercentageAsync(int percentage, CancellationToken cancellationToken)
	{
		if (SetPercentage(percentage)) return true;
		try
		{
			ProcessStartInfo info = new()
			{
				FileName = "powershell.exe",
				Arguments = $"-NoProfile -NonInteractive -Command \"(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods).WmiSetBrightness(1,{Math.Clamp(percentage, 0, 100)})\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process? process = Process.Start(info);
			if (process is null) return false;
			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	public static bool SetPercentage(int percentage)
	{
		percentage = Math.Clamp(percentage, 0, 100);
		nint[] handles = OpenMonitors();
		if (handles.Length == 0) return false;
		bool applied = false;
		try
		{
			foreach (nint handle in handles)
			{
				if (!GetMonitorBrightness(handle, out uint minimum, out _, out uint maximum) || maximum <= minimum) continue;
				uint target = (uint)Math.Round(minimum + (maximum - minimum) * (percentage / 100d));
				if (SetMonitorBrightness(handle, target)) applied = true;
			}
		}
		catch
		{
		}
		finally
		{
			CloseMonitors(handles);
		}
		return applied;
	}

	private static bool TryRead(out uint minimum, out uint current, out uint maximum)
	{
		minimum = current = maximum = 0;
		nint[] handles = OpenMonitors();
		if (handles.Length == 0) return false;
		try
		{
			return GetMonitorBrightness(handles[0], out minimum, out current, out maximum);
		}
		catch
		{
			return false;
		}
		finally
		{
			CloseMonitors(handles);
		}
	}

	private static nint[] OpenMonitors()
	{
		try
		{
			nint monitor = MonitorFromWindow(0, MonitorDefaultToPrimary);
			if (monitor == 0) return Array.Empty<nint>();
			if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0) return Array.Empty<nint>();
			PhysicalMonitor[] monitors = new PhysicalMonitor[count];
			if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return Array.Empty<nint>();
			return monitors.Select(item => item.Handle).Where(handle => handle != 0).ToArray();
		}
		catch
		{
			return Array.Empty<nint>();
		}
	}

	private static void CloseMonitors(nint[] handles)
	{
		try
		{
			if (handles.Length == 0) return;
			PhysicalMonitor[] monitors = handles.Select(handle => new PhysicalMonitor { Handle = handle, Description = "" }).ToArray();
			DestroyPhysicalMonitors((uint)monitors.Length, monitors);
		}
		catch
		{
		}
	}

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint window, uint flags);

	[DllImport("dxva2.dll", SetLastError = true)]
	private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);

	[DllImport("dxva2.dll", SetLastError = true)]
	private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);

	[DllImport("dxva2.dll", SetLastError = true)]
	private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

	[DllImport("dxva2.dll", SetLastError = true)]
	private static extern bool GetMonitorBrightness(nint monitor, out uint minimum, out uint current, out uint maximum);

	[DllImport("dxva2.dll", SetLastError = true)]
	private static extern bool SetMonitorBrightness(nint monitor, uint brightness);
}

public sealed record OverlaySpeedTestResult(bool Ok, double DownloadMbps, double UploadMbps, int LatencyMs, string Error);

// Prova di connessione breve: latenza, download e upload misurati su pochi
// megabyte, con timeout stretti per non bloccare mai l'interfaccia.
public static class OverlaySpeedTest
{
	private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=8000000";
	private const string UploadUrl = "https://speed.cloudflare.com/__up";
	private const string LatencyUrl = "https://speed.cloudflare.com/__down?bytes=1000";

	public static async Task<OverlaySpeedTestResult> RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
			client.DefaultRequestHeaders.ConnectionClose = false;

			int latency = -1;
			Stopwatch watch = Stopwatch.StartNew();
			using (HttpResponseMessage warmup = await client.GetAsync(LatencyUrl, cancellationToken).ConfigureAwait(false))
			{
				await warmup.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
				latency = (int)watch.ElapsedMilliseconds;
			}

			watch.Restart();
			long downloaded = 0;
			using (HttpResponseMessage response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
			{
				using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
				byte[] buffer = new byte[81920];
				int read;
				while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
				{
					downloaded += read;
					if (watch.ElapsedMilliseconds > 6000) break;
				}
			}
			double downloadSeconds = Math.Max(0.15, watch.Elapsed.TotalSeconds);
			double downloadMbps = downloaded * 8d / downloadSeconds / 1_000_000d;

			watch.Restart();
			byte[] payload = new byte[2_000_000];
			using (ByteArrayContent content = new(payload))
			{
				using HttpResponseMessage response = await client.PostAsync(UploadUrl, content, cancellationToken).ConfigureAwait(false);
				_ = response.StatusCode;
			}
			double uploadSeconds = Math.Max(0.15, watch.Elapsed.TotalSeconds);
			double uploadMbps = payload.Length * 8d / uploadSeconds / 1_000_000d;

			return new OverlaySpeedTestResult(true, downloadMbps, uploadMbps, latency, "");
		}
		catch (OperationCanceledException)
		{
			return new OverlaySpeedTestResult(false, 0, 0, -1, "cancelled");
		}
		catch (Exception error)
		{
			return new OverlaySpeedTestResult(false, 0, 0, -1, error.Message);
		}
	}
}
