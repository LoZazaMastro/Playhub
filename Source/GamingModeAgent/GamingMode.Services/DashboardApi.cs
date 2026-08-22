using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GamingMode.Models;

namespace GamingMode.Services;

// IL CONFINE FRA IL PLUGIN E LA PARTE NATIVA.
//
// La Dashboard smette di essere una finestra nostra e diventa una schermata del
// plugin di Steam. Tutto cio' che si vede lo disegna Steam; tutto cio' che
// Steam non puo' sapere lo fornisce l'agente da qui.
//
// Perche' questo confine e' quello giusto. I guasti pesanti di questi giorni
// non venivano dal codice che elenca le finestre o legge i contatori: quello
// ha sempre funzionato. Venivano dall'avere una seconda finestra a schermo
// intero, guidata dal controller, accanto all'unico programma che possiede il
// controller e lo schermo. Contesa del primo piano, cambi di configurazione
// del pad, tasti simulati, ordine delle finestre "sempre in primo piano":
// tutti sintomi di quella convivenza. Dentro il plugin non esistono, non
// perche' siano stati corretti ma perche' non c'e' piu' niente con cui
// litigare.
//
// Quindi qui sotto resta il lavoro utile - enumerazione delle finestre con il
// filtro delle finestre fantasma, artwork di Steam con il suo indice,
// contatori di sistema - e sparisce tutto il resto.
//
// FORMATO. Tutto JSON semplice. Le immagini NON viaggiano dentro gli elenchi:
// un banner da 920x430 pesa parecchio e moltiplicato per le finestre aperte
// renderebbe lenta ogni apertura. Gli elenchi portano il PERCORSO
// dell'immagine, e chi la vuole la chiede a parte con /dash/image.
public static class DashboardApi
{
	public sealed record WindowEntry(
		string Handle,
		int ProcessId,
		string Title,
		string ProcessName,
		bool Minimized,
		bool Foreground,
		bool Primary,
		// Percorso del banner ufficiale di Steam, quando la finestra e' un gioco
		// della libreria. Vuoto = il plugin disegna la sua scheda con l'icona.
		string BannerPath,
		string HeroPath,
		// L'icona e' piccola (44x44): questa viaggia inline, senza un secondo
		// giro di richieste per ogni voce dell'elenco.
		string IconBase64);

	public sealed record DashboardEnvironment(
		string Language,
		string LogoPath,
		bool QuickSettingsInstalled,
		bool Enabled,
		string Mode);

	public sealed record ShortcutEntry(
		string Id,
		string Name,
		string Kind,
		string Target,
		string IconBase64);

	// Kind: "ssd", "hdd" oppure "" quando il dispositivo non risponde.
	public sealed record DiskEntry(string Name, double BytesPerSecond, string Kind);

	// SSD o meccanico. Non si indovina dal nome: si chiede al dispositivo se ha
	// una penalita' di posizionamento, che e' esattamente cio' che distingue un
	// disco che gira da uno che non gira. La risposta non cambia mai finche' il
	// PC e' acceso, quindi si chiede una volta sola per unita'.
	private static readonly Dictionary<string, string> _diskKinds = new(StringComparer.OrdinalIgnoreCase);

	private static string ClassifyDisk(string name)
	{
		// Il contatore puo' riportare piu' lettere per la stessa unita'
		// ("C: D:"): la prima basta a identificare il dispositivo.
		string letter = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
		if (letter.Length < 2 || letter[1] != ':') return "";

		lock (_diskKinds)
		{
			if (_diskKinds.TryGetValue(letter, out string? known)) return known;

			string kind = "";
			nint handle = 0;
			try
			{
				// Nessun diritto di accesso richiesto: si interroga soltanto.
				handle = CreateFile($@"\\.\{letter[0]}:", 0, FileShareRead | FileShareWrite, 0, OpenExisting, 0, 0);
				if (handle != -1 && handle != 0)
				{
					StoragePropertyQuery query = new()
					{
						PropertyId = StorageDeviceSeekPenaltyProperty,
						QueryType = PropertyStandardQuery
					};
					DeviceSeekPenaltyDescriptor descriptor = default;

					int querySize = System.Runtime.InteropServices.Marshal.SizeOf<StoragePropertyQuery>();
					int resultSize = System.Runtime.InteropServices.Marshal.SizeOf<DeviceSeekPenaltyDescriptor>();
					nint queryBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(querySize);
					nint resultBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(resultSize);
					try
					{
						System.Runtime.InteropServices.Marshal.StructureToPtr(query, queryBuffer, false);
						if (DeviceIoControl(handle, IoctlStorageQueryProperty, queryBuffer, querySize,
								resultBuffer, resultSize, out _, 0))
						{
							descriptor = System.Runtime.InteropServices.Marshal.PtrToStructure<DeviceSeekPenaltyDescriptor>(resultBuffer);
							kind = descriptor.IncursSeekPenalty ? "hdd" : "ssd";
						}
					}
					finally
					{
						System.Runtime.InteropServices.Marshal.FreeHGlobal(queryBuffer);
						System.Runtime.InteropServices.Marshal.FreeHGlobal(resultBuffer);
					}
				}
			}
			catch
			{
			}
			finally
			{
				if (handle != 0 && handle != -1) CloseHandle(handle);
			}

			_diskKinds[letter] = kind;
			return kind;
		}
	}

	private const uint FileShareRead = 0x00000001;
	private const uint FileShareWrite = 0x00000002;
	private const uint OpenExisting = 3;
	private const uint IoctlStorageQueryProperty = 0x002D1400;
	private const int StorageDeviceSeekPenaltyProperty = 7;
	private const int PropertyStandardQuery = 0;

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct StoragePropertyQuery
	{
		public int PropertyId;
		public int QueryType;
		public byte AdditionalParameters;
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct DeviceSeekPenaltyDescriptor
	{
		public uint Version;
		public uint Size;
		[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.U1)]
		public bool IncursSeekPenalty;
	}

	[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateFile(string name, uint access, uint share, nint security,
		uint disposition, uint flags, nint template);

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint device, uint code, nint inBuffer, int inSize,
		nint outBuffer, int outSize, out int returned, nint overlapped);

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);

	public sealed record UsageReport(
		double CpuPercent,
		double GpuPercent,
		bool GpuAvailable,
		double MemoryPercent,
		double MemoryUsedMb,
		double MemoryTotalMb,
		double NetworkBytesPerSecond,
		IReadOnlyList<DiskEntry> Disks);

	// ---------- FINESTRE ----------

	public static IReadOnlyList<WindowEntry> ListWindows(nint primaryWindow = default)
	{
		nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
		List<WindowEntry> entries = new();
		foreach (OverlayWindowInfo window in OverlayWindowTools.Enumerate())
		{
			OverlaySteamArtwork? artwork = null;
			try { artwork = OverlaySteamArtworkResolver.Resolve(window); }
			catch { }
			entries.Add(new WindowEntry(
				window.Handle.ToString(),
				window.ProcessId,
				artwork?.DisplayName ?? window.Title,
				window.ProcessName,
				window.IsMinimized,
				window.Handle == foreground,
				window.Handle == (primaryWindow == 0 ? foreground : primaryWindow),
				artwork?.BannerPath ?? "",
				artwork?.HeroPath ?? "",
				EncodeIcon(window.Icon)));
		}
		return entries
			.OrderByDescending(entry => entry.Primary)
			.ThenByDescending(entry => entry.Foreground)
			.ToArray();
	}

	public static bool ActivateWindow(string handle)
		=> TryParseHandle(handle, out nint value) && OverlayWindowTools.Activate(value);

	public static bool CloseWindow(string handle)
		=> TryParseHandle(handle, out nint value) && OverlayWindowTools.RequestClose(value);

	// A real window preview is requested separately from the window list. This
	// keeps opening the Dashboard cheap and lets the frontend refresh only the
	// cards that are actually visible. JPEG is intentional here: a task switcher
	// needs a crisp glanceable frame, not a multi-megabyte lossless screenshot.
	public static string ReadWindowPreviewAsBase64(string handle, int width = 720, int height = 405)
	{
		try
		{
			if (!TryParseHandle(handle, out nint value) || !OverlayWindowTools.IsUsableWindow(value)) return "";
			width = Math.Clamp(width, 240, 960);
			height = Math.Clamp(height, 135, 540);
			if (OverlayWindowTools.CapturePreview(value, width, height) is not BitmapSource bitmap) return "";

			JpegBitmapEncoder encoder = new() { QualityLevel = 82 };
			encoder.Frames.Add(BitmapFrame.Create(bitmap));
			using MemoryStream stream = new();
			encoder.Save(stream);
			return Convert.ToBase64String(stream.ToArray());
		}
		catch
		{
			return "";
		}
	}

	public static DashboardEnvironment ReadEnvironment(JsonStore store)
	{
		ModeConfig config = store.LoadConfig();
		string language = string.IsNullOrWhiteSpace(config.Language)
			? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
			: config.Language!;
		language = language.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "en";

		string plugins = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"homebrew",
			"plugins");
		bool quickSettingsInstalled = Directory.Exists(Path.Combine(plugins, "quick-settings"))
			|| Directory.Exists(Path.Combine(plugins, "Quick Settings"));

		return new DashboardEnvironment(
			language.ToLowerInvariant(),
			config.Gaming.Splash.LogoPath ?? "",
			quickSettingsInstalled,
			config.Gaming.DashboardEnabled,
			config.DefaultMode.ToString());
	}

	// ---------- SCORCIATOIE (le "preferite") ----------

	public static IReadOnlyList<ShortcutEntry> ListShortcuts(JsonStore store)
	{
		List<GamingOverlayShortcut> shortcuts = store.LoadConfig().Gaming.DashboardShortcuts;

		// LE APP DELLO STORE NON HANNO UN FILE DA CUI PRENDERE L'ICONA.
		//
		// Una preferita che punta a un'app di Windows non e' un percorso, e' un
		// identificativo: non c'e' nessun eseguibile da cui estrarre l'icona, e
		// infatti restava senza. Il logo va chiesto al pacchetto, che e' la
		// stessa strada usata per mostrarle nell'elenco, dove l'icona si vedeva.
		Dictionary<string, ImageSource?> logos = new(StringComparer.OrdinalIgnoreCase);
		string[] appIds = shortcuts
			.Where(item => item.Kind == GamingOverlayShortcutKind.WindowsApp)
			.Select(item => item.Target)
			.ToArray();
		if (appIds.Length > 0)
		{
			try
			{
				logos = OverlayAppLauncher.GetAppLogosAsync(appIds, System.Threading.CancellationToken.None)
					.GetAwaiter().GetResult();
			}
			catch
			{
			}
		}

		List<ShortcutEntry> entries = new();
		foreach (GamingOverlayShortcut shortcut in shortcuts)
		{
			ImageSource? icon = null;
			try
			{
				if (shortcut.Kind == GamingOverlayShortcutKind.DesktopProgram)
				{
					icon = OverlayWindowTools.LoadFileIcon(shortcut.Target);
				}
				else
				{
					logos.TryGetValue(shortcut.Target, out icon);
				}
			}
			catch
			{
			}
			entries.Add(new ShortcutEntry(
				shortcut.Id,
				shortcut.Name,
				shortcut.Kind.ToString(),
				shortcut.Target,
				EncodeIcon(icon)));
		}
		return entries;
	}

	public static bool LaunchShortcut(JsonStore store, string id)
	{
		GamingOverlayShortcut? shortcut = store.LoadConfig().Gaming.DashboardShortcuts
			.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
		if (shortcut is null) return false;
		// L'attivazione di una app dello Store puo' restare appesa a lungo: qui
		// siamo su un thread del servizio web, non sull'interfaccia di nessuno,
		// quindi non blocca niente.
		return OverlayAppLauncher.Launch(shortcut);
	}

	public static async Task<bool> LaunchShortcutAndActivateAsync(JsonStore store, string id, FileLogger logger, CancellationToken cancellationToken)
	{
		GamingOverlayShortcut? shortcut = store.LoadConfig().Gaming.DashboardShortcuts
			.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
		if (shortcut is null) return false;

		HashSet<string> previousHandles = ListWindows().Select(window => window.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
		logger.Info($"Playhub Dashboard launch '{shortcut.Name}' ({shortcut.Kind}) started.");
		Task<(bool Ok, int ProcessId)> launchTask = OverlayAppLauncher.LaunchAsync(shortcut);
		Task launchTimeout = Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
		Task completed = await Task.WhenAny(launchTask, launchTimeout);
		if (completed != launchTask)
		{
			logger.Info($"Playhub Dashboard launch '{shortcut.Name}' did not return within 6 seconds.");
			return false;
		}
		(bool launched, int launchedProcessId) = await launchTask;
		if (!launched)
		{
			logger.Info($"Playhub Dashboard launch '{shortcut.Name}' was rejected by Windows.");
			return false;
		}
		logger.Info($"Playhub Dashboard launch '{shortcut.Name}' returned PID {launchedProcessId}.");

		string expectedProcess = shortcut.Kind == GamingOverlayShortcutKind.DesktopProgram
			? Path.GetFileNameWithoutExtension(OverlayWindowTools.ResolveShortcut(shortcut.Target))
			: "";
		for (int attempt = 0; attempt < 36; attempt++)
		{
			await Task.Delay(attempt == 0 ? 140 : 110, cancellationToken);
			IReadOnlyList<WindowEntry> windows = ListWindows();
			WindowEntry? candidate = windows.FirstOrDefault(window => launchedProcessId > 0
				&& window.ProcessId == launchedProcessId);
			if (shortcut.Kind == GamingOverlayShortcutKind.DesktopProgram)
			{
				candidate ??= windows.FirstOrDefault(window =>
					!previousHandles.Contains(window.Handle)
					&& !IsLaunchInfrastructure(window.ProcessName));
				candidate ??= windows.FirstOrDefault(window => !string.IsNullOrWhiteSpace(expectedProcess)
					&& window.ProcessName.Equals(expectedProcess, StringComparison.OrdinalIgnoreCase));
				candidate ??= windows.FirstOrDefault(window => window.Foreground
					&& !IsLaunchInfrastructure(window.ProcessName));
			}
			if (candidate is not null && ActivateWindow(candidate.Handle))
			{
				logger.Info($"Playhub Dashboard activated '{shortcut.Name}' as {candidate.ProcessName} PID {candidate.ProcessId} on attempt {attempt + 1}.");
				return true;
			}
		}
		// L'avvio puo' essere valido anche per utility senza finestra. Non si
		// trasforma quel caso in un errore e soprattutto non si blocca la UI.
		logger.Info($"Playhub Dashboard launched '{shortcut.Name}', but no activatable top-level window appeared within 4 seconds.");
		return true;
	}

	private static bool IsLaunchInfrastructure(string processName)
	{
		return processName.Equals("steam", StringComparison.OrdinalIgnoreCase)
			|| processName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase)
			|| processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
			|| processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
			|| processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
			|| processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase);
	}

	public static bool RemoveShortcut(JsonStore store, string id)
	{
		ModeConfig config = store.LoadConfig();
		int removed = config.Gaming.DashboardShortcuts.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
		if (removed == 0) return false;
		store.SaveConfig(config);
		return true;
	}

	public static bool RenameShortcut(JsonStore store, string id, string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return false;
		ModeConfig config = store.LoadConfig();
		GamingOverlayShortcut? shortcut = config.Gaming.DashboardShortcuts
			.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
		if (shortcut is null) return false;
		shortcut.Name = name.Trim();
		store.SaveConfig(config);
		return true;
	}

	// ---------- IMPOSTAZIONI DELLA DASHBOARD ----------

	public sealed record DashboardSettings(
		bool KeyboardShortcutEnabled,
		string Hotkey,
		string DefaultMode);

	public static DashboardSettings ReadSettings(JsonStore store)
	{
		ModeConfig config = store.LoadConfig();
		return new DashboardSettings(
			config.Gaming.DashboardKeyboardShortcutEnabled,
			config.Gaming.DashboardHotkey,
			config.DefaultMode.ToString());
	}

	// I campi assenti non vengono toccati: il plugin manda solo quello che
	// l'utente ha cambiato.
	public static DashboardSettings WriteSettings(JsonStore store, bool? keyboardEnabled, string? hotkey)
	{
		ModeConfig config = store.LoadConfig();
		if (keyboardEnabled.HasValue) config.Gaming.DashboardKeyboardShortcutEnabled = keyboardEnabled.Value;
		if (!string.IsNullOrWhiteSpace(hotkey)) config.Gaming.DashboardHotkey = hotkey.Trim();
		store.SaveConfig(config);
		return ReadSettings(store);
	}

	// ---------- CONTATORI DI SISTEMA ----------

	public static UsageReport ReadUsage()
	{
		OverlayUsageSnapshot usage = OverlaySystemUsage.ReadLive();
		(double load, double usedMb, double totalMb) memory = ReadMemory();
		return new UsageReport(
			Math.Round(usage.CpuPercent, 1),
			Math.Round(usage.GpuPercent, 1),
			usage.GpuAvailable,
			Math.Round(memory.load, 1),
			Math.Round(memory.usedMb, 0),
			Math.Round(memory.totalMb, 0),
			usage.NetworkBytesPerSecond,
			usage.Disks.Select(disk => new DiskEntry(disk.Name, disk.BytesPerSecond, ClassifyDisk(disk.Name))).ToArray());
	}

	// ---------- ATTIVITA' IN CORSO ----------

	public sealed record ProcessEntry(
		int Id,
		string Name,
		double CpuPercent,
		double MemoryMb,
		double DiskBytesPerSecond);

	// Il consumo di processore non si legge: si misura fra due istanti. Qui si
	// tiene il campione precedente, e la prima chiamata dopo l'apertura della
	// pagina torna quindi con lo zero: e' corretto, non e' un errore. Il plugin
	// richiama ogni due secondi e dal secondo giro i numeri sono veri.
	private static Dictionary<int, (TimeSpan Cpu, ulong Io)> _previousSample = new();
	private static long _previousSampleAt;
	private static readonly object _sampleLock = new();

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
	private static extern bool GetProcessIoCounters(nint process, out IoCounters counters);

	public static IReadOnlyList<ProcessEntry> ListProcesses(int limit = 24)
	{
		lock (_sampleLock)
		{
			Dictionary<int, (TimeSpan Cpu, ulong Io)> current = new();
			List<(int Id, string Name, long Memory)> snapshot = new();

			foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcesses())
			{
				try
				{
					// I primi identificativi sono i processi di sistema inerti, e
					// l'agente non ha senso che elenchi se stesso.
					if (process.Id <= 4 || process.Id == Environment.ProcessId) continue;

					TimeSpan cpu = TimeSpan.Zero;
					ulong io = 0;
					try { cpu = process.TotalProcessorTime; } catch { }
					try
					{
						if (GetProcessIoCounters(process.Handle, out IoCounters counters))
						{
							io = counters.ReadTransferCount + counters.WriteTransferCount;
						}
					}
					catch
					{
					}

					current[process.Id] = (cpu, io);
					snapshot.Add((process.Id, process.ProcessName, process.WorkingSet64));
				}
				catch
				{
					// Un processo puo' sparire mentre lo si legge, o appartenere a
					// un altro utente: si salta e basta.
				}
				finally
				{
					process.Dispose();
				}
			}

			long now = Environment.TickCount64;
			double seconds = _previousSampleAt == 0 ? 0 : (now - _previousSampleAt) / 1000d;
			int cores = Math.Max(1, Environment.ProcessorCount);

			List<ProcessEntry> rows = new();
			foreach ((int id, string name, long memory) in snapshot)
			{
				double cpu = 0;
				double disk = 0;
				if (seconds > 0.05 &&
					_previousSample.TryGetValue(id, out var before) &&
					current.TryGetValue(id, out var after))
				{
					cpu = (after.Cpu - before.Cpu).TotalSeconds / (seconds * cores) * 100;
					disk = after.Io >= before.Io ? (after.Io - before.Io) / seconds : 0;
				}
				rows.Add(new ProcessEntry(
					id,
					name,
					Math.Round(Math.Clamp(cpu, 0, 100), 1),
					Math.Round(memory / 1024d / 1024d, 0),
					Math.Round(disk, 0)));
			}

			_previousSample = current;
			_previousSampleAt = now;

			// Chi consuma di piu' sta in cima: e' l'ordine per cui si apre questa
			// pagina. A parita' di processore decide la memoria.
			return rows
				.OrderByDescending(item => item.CpuPercent)
				.ThenByDescending(item => item.MemoryMb)
				.Take(Math.Clamp(limit, 1, 200))
				.ToArray();
		}
	}

	// Chiusura gentile: si chiede alla finestra di chiudersi, cosi' il programma
	// puo' salvare e chiedere conferma. E' la strada giusta per prima.
	public static bool CloseProcess(int processId)
	{
		try
		{
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
			return process.CloseMainWindow();
		}
		catch
		{
			return false;
		}
	}

	// Terminazione: il programma non ha piu' voce in capitolo. Da offrire solo
	// dopo la chiusura gentile, e con una conferma davanti.
	public static bool KillProcess(int processId)
	{
		try
		{
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
			process.Kill(entireProcessTree: false);
			return true;
		}
		catch
		{
			return false;
		}
	}

	// ---------- PROGRAMMI DA AGGIUNGERE ALLE PREFERITE ----------

	// Kind: "DesktopProgram" oppure "WindowsApp", come le preferite.
	public sealed record ProgramEntry(string Name, string Target, string Kind, string IconBase64);

	// Dentro Steam non si puo' aprire una finestra "Sfoglia": sarebbe una
	// finestra di Windows davanti a Big Picture, cioe' il problema da cui
	// veniamo. Si legge invece la cartella "Applicazioni" della shell, la stessa
	// che Windows mostra in "Tutte le app": contiene sia i programmi desktop sia
	// le app dello Store, con il nome e l'icona che l'utente gia' conosce.
	//
	// PERCHE' SU UN THREAD DEDICATO. Tutto quello che sta qui sotto e' COM della
	// shell, e la shell vuole un apartment a thread singolo. L'agente serve le
	// richieste web su thread di pool, che sono MTA: la prima versione di questo
	// elenco girava li' sopra, ogni chiamata COM falliva in silenzio e la pagina
	// mostrava "nessun programma". Da qui in avanti si passa sempre da un thread
	// STA nostro.
	// L'elenco si costruisce una volta e si tiene da parte: leggere la cartella
	// delle applicazioni e tutto il menu Start puo' costare secondi, e chi entra
	// e riesce dalla schermata non deve riaspettare ogni volta. Cinque minuti
	// bastano: i programmi non si installano a raffica.
	private static IReadOnlyList<ProgramEntry>? _programCache;
	private static long _programCacheAt;
	private static readonly object _programLock = new();
	private static readonly System.Threading.ManualResetEventSlim _programScanCompleted = new(initialState: true);
	private static bool _programScanRunning;
	private static string _programScanNote = "";

	// La risposta porta con se' come e' andata. Una schermata vuota puo'
	// significare cose diverse - la cartella delle applicazioni non risponde,
	// il menu Start non e' dove me lo aspetto, e' scaduto il tempo - e dirlo e'
	// molto piu' utile che indovinare una causa a caso.
	public sealed record ProgramList(IReadOnlyList<ProgramEntry> Items, string Note, bool Pending = false);

	public static void PrewarmPrograms()
	{
		lock (_programLock)
		{
			if (_programCache is not null && Environment.TickCount64 - _programCacheAt < 300_000) return;
			StartProgramScanLocked();
		}
	}

	private static bool StartProgramScanLocked()
	{
		if (_programScanRunning) return false;
		_programScanRunning = true;
		_programScanNote = "";
		_programScanCompleted.Reset();

		System.Threading.Thread worker = new(() =>
		{
			List<ProgramEntry> result = new();
			string note = "";
			try { result = CollectPrograms(out note); }
			catch (Exception exception) { note = "Lettura non riuscita: " + exception.Message; }
			finally
			{
				lock (_programLock)
				{
					if (result.Count > 0)
					{
						_programCache = result;
						_programCacheAt = Environment.TickCount64;
					}
					_programScanNote = note;
					_programScanRunning = false;
					_programScanCompleted.Set();
				}
			}
		});
		worker.SetApartmentState(System.Threading.ApartmentState.STA);
		worker.IsBackground = true;
		worker.Priority = System.Threading.ThreadPriority.BelowNormal;
		worker.Start();
		return true;
	}

	public static ProgramList ListPrograms()
	{
		bool startedHere;
		lock (_programLock)
		{
			if (_programCache is not null && Environment.TickCount64 - _programCacheAt < 300_000)
			{
				return new ProgramList(_programCache, "", false);
			}
			startedHere = StartProgramScanLocked();
		}

		// Una richiesta che avvia la scansione le concede il tempo maggiore.
		// Le richieste successive si limitano ad attendere brevemente la stessa
		// scansione: non vengono mai creati worker duplicati.
		_programScanCompleted.Wait(startedHere ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(1));
		lock (_programLock)
		{
			if (_programCache is not null)
				return new ProgramList(_programCache, _programScanNote, false);
			return new ProgramList(Array.Empty<ProgramEntry>(),
				_programScanRunning ? "La libreria delle app e' ancora in preparazione." : _programScanNote,
				_programScanRunning);
		}
	}

	private static List<ProgramEntry> CollectPrograms(out string note)
	{
		note = "";
		List<ProgramEntry> found = new();
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		string appsFolderProblem = "";
		string storeProblem = "";
		int fromStoreApps = 0;
		int fromAppsFolder = 0;
		int fromStartMenu = 0;

		// 1) LE APP DEL MICROSOFT STORE.
		//
		// Si chiedono al gestore dei pacchetti, che e' esattamente quello che
		// faceva la Dashboard prima di diventare un plugin - e funzionava. Ero
		// passato dalla cartella virtuale delle applicazioni pensando di
		// prendere due piccioni con una fava: quella strada elenca i programmi,
		// ma le app dello Store non le restituisce come mi aspettavo. Meglio
		// tornare al codice che era gia' qui e gia' collaudato.
		try
		{
			IReadOnlyList<OverlayWindowsApp> storeApps =
				OverlayAppLauncher.FindWindowsAppsAsync(System.Threading.CancellationToken.None)
					.GetAwaiter().GetResult();

			Dictionary<string, ImageSource?> logos =
				OverlayAppLauncher.GetAppLogosAsync(
					storeApps.Select(app => app.AppUserModelId).ToArray(),
					System.Threading.CancellationToken.None)
				.GetAwaiter().GetResult();

			foreach (OverlayWindowsApp app in storeApps)
			{
				if (IsNoise(app.Name)) continue;
				if (!seen.Add(app.AppUserModelId)) continue;

				logos.TryGetValue(app.AppUserModelId, out ImageSource? logo);
				found.Add(new ProgramEntry(
					app.Name,
					app.AppUserModelId,
					nameof(GamingOverlayShortcutKind.WindowsApp),
					EncodeIcon(logo)));
				fromStoreApps++;
			}
		}
		catch (Exception exception)
		{
			storeProblem = exception.Message;
		}

		// 2) I programmi registrati, dalla cartella virtuale delle applicazioni:
		//    prende anche quelli che nel menu Start non hanno un collegamento.
		try
		{
			Type? shellType = Type.GetTypeFromProgID("Shell.Application");
			if (shellType is not null && Activator.CreateInstance(shellType) is object shell)
			{
				object? folder = shellType.InvokeMember("NameSpace",
					System.Reflection.BindingFlags.InvokeMethod, null, shell,
					new object[] { "shell:AppsFolder" });

				if (folder is not null)
				{
					object? items = folder.GetType().InvokeMember("Items",
						System.Reflection.BindingFlags.InvokeMethod, null, folder, null);

					int count = (int)(items?.GetType().InvokeMember("Count",
						System.Reflection.BindingFlags.GetProperty, null, items, null) ?? 0);

					for (int index = 0; index < count; index++)
					{
						try
						{
							object? item = items?.GetType().InvokeMember("Item",
								System.Reflection.BindingFlags.InvokeMethod, null, items, new object[] { index });
							if (item is null) continue;

							string name = (string?)item.GetType().InvokeMember("Name",
								System.Reflection.BindingFlags.GetProperty, null, item, null) ?? "";
							string id = (string?)item.GetType().InvokeMember("Path",
								System.Reflection.BindingFlags.GetProperty, null, item, null) ?? "";

							if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
							if (IsNoise(name)) continue;
							if (!seen.Add(id)) continue;

							// COME SI DISTINGUE UNA APP DELLO STORE DA UN PROGRAMMA.
							//
							// Nella cartella delle applicazioni convivono due cose
							// diverse. Un'app dello Store e' identificata da
							// "FamigliaDelPacchetto!Applicazione": il punto
							// esclamativo c'e' sempre e non puo' stare in un
							// percorso di file. Tutto il resto e' un programma
							// normale, che ha un percorso su disco.
							//
							// Tenerle separate conta: si aggiungono in modo
							// diverso e, soprattutto, l'utente le cerca in due
							// posti mentali diversi.
							bool isApp = id.Contains('!');
							bool isDesktop = !isApp;

							// Le app dello Store le ha gia' portate il gestore dei
							// pacchetti, con i loro logo: qui sarebbero doppioni
							// senza icona.
							if (isApp) continue;

							// Un programma senza un file dietro non si puo'
							// mostrare come si deve: niente icona, e spesso
							// nemmeno un avvio affidabile. Il menu Start, subito
							// sotto, lo ripesca con l'icona giusta.
							if (!File.Exists(id)) continue;

							ImageSource? icon = null;
							if (isDesktop && File.Exists(id))
							{
								try { icon = OverlayWindowTools.LoadFileIcon(id); } catch { }
							}

							found.Add(new ProgramEntry(
								name,
								id,
								isDesktop ? nameof(GamingOverlayShortcutKind.DesktopProgram)
										  : nameof(GamingOverlayShortcutKind.WindowsApp),
								EncodeIcon(icon)));
							fromAppsFolder++;
						}
						catch
						{
						}
					}
				}
				else
				{
					appsFolderProblem = "la cartella delle applicazioni non ha risposto";
				}
			}
			else
			{
				appsFolderProblem = "Shell.Application non e' disponibile";
			}
		}
		catch (Exception exception)
		{
			appsFolderProblem = exception.Message;
		}

		// 3) Il menu Start come rete di sicurezza.
		//
		// ATTENZIONE A COME SI PERCORRE. Directory.EnumerateFiles con
		// AllDirectories e' pigro: l'eccezione non nasce dove lo si chiama, ma
		// piu' tardi, dentro il ciclo che lo consuma. Una sola cartella
		// vietata - e nel menu Start ce n'e' sempre qualcuna - faceva quindi
		// esplodere l'intero metodo e buttava via ANCHE le app di Windows gia'
		// raccolte sopra. Il sintomo era "non trova le app di Windows", mentre
		// il guasto stava tutto qui.
		//
		// Qui si scende cartella per cartella, e una vietata la si salta senza
		// che il resto ne risenta.
		try
		{
			foreach (string root in new[]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
				Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
			})
			{
				if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

				Stack<string> pending = new();
				pending.Push(root);
				int visited = 0;

				while (pending.Count > 0 && visited < 4000)
				{
					string directory = pending.Pop();
					visited++;

					try
					{
						foreach (string child in Directory.GetDirectories(directory))
						{
							pending.Push(child);
						}
					}
					catch
					{
						// Cartella vietata o sparita: si salta solo lei.
					}

					string[] links;
					try { links = Directory.GetFiles(directory, "*.lnk"); }
					catch { continue; }

					foreach (string link in links)
					{
						try
						{
							string name = Path.GetFileNameWithoutExtension(link);
							if (string.IsNullOrWhiteSpace(name) || IsNoise(name)) continue;

							string target = OverlayWindowTools.ResolveShortcut(link);
							if (string.IsNullOrWhiteSpace(target) || !File.Exists(target)) continue;
							if (!seen.Add(target)) continue;
							if (found.Any(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase))) continue;

							ImageSource? icon = null;
							try { icon = OverlayWindowTools.LoadFileIcon(target); } catch { }
							found.Add(new ProgramEntry(name, target, nameof(GamingOverlayShortcutKind.DesktopProgram), EncodeIcon(icon)));
							fromStartMenu++;
						}
						catch
						{
						}
					}
				}
			}
		}
		catch
		{
			// Qualunque cosa succeda qui, quello che si e' gia' raccolto resta.
		}

		if (found.Count == 0)
		{
			note = "Non ho trovato niente."
				+ (string.IsNullOrEmpty(storeProblem) ? "" : " App dello Store: " + storeProblem + ".")
				+ (string.IsNullOrEmpty(appsFolderProblem) ? "" : " Programmi registrati: " + appsFolderProblem + ".");
		}

		return found.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	// Disinstallatori, guide e leggimi non sono programmi che si vogliono
	// lanciare da una Dashboard.
	private static bool IsNoise(string name) =>
		name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
		name.Contains("disinstall", StringComparison.OrdinalIgnoreCase) ||
		name.Contains("readme", StringComparison.OrdinalIgnoreCase) ||
		name.Contains("leggimi", StringComparison.OrdinalIgnoreCase);

	public static bool AddShortcut(JsonStore store, string target, string name, string kind)
	{
		if (string.IsNullOrWhiteSpace(target)) return false;

		try
		{
			bool isApp = string.Equals(kind, nameof(GamingOverlayShortcutKind.WindowsApp), StringComparison.OrdinalIgnoreCase);

			// Un programma desktop si salva risolto: cosi' l'icona non ha la
			// freccetta e l'avvio non dipende dal collegamento. Un'app di Windows
			// invece E' il suo identificativo, e non va toccata.
			string resolved = target;
			if (!isApp)
			{
				resolved = ResolveShortcutOnStaThread(target);
				if (!File.Exists(resolved)) return false;
			}

			ModeConfig config = store.LoadConfig();
			if (config.Gaming.DashboardShortcuts.Any(item =>
				string.Equals(item.Target, resolved, StringComparison.OrdinalIgnoreCase)))
			{
				return true; // gia' fra le preferite: non e' un errore
			}

			config.Gaming.DashboardShortcuts.Add(new GamingOverlayShortcut
			{
				Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(resolved) : name.Trim(),
				Kind = isApp ? GamingOverlayShortcutKind.WindowsApp : GamingOverlayShortcutKind.DesktopProgram,
				Target = resolved,
				WorkingDirectory = isApp ? "" : Path.GetDirectoryName(resolved) ?? ""
			});
			store.SaveConfig(config);
			return true;
		}
		catch
		{
			return false;
		}
	}

	// Stessa ragione di CollectPrograms: IShellLink e' COM della shell e vuole
	// un thread STA.
	private static string ResolveShortcutOnStaThread(string path)
	{
		string resolved = path;
		System.Threading.Thread worker = new(() =>
		{
			try { resolved = OverlayWindowTools.ResolveShortcut(path); }
			catch { }
		});
		worker.SetApartmentState(System.Threading.ApartmentState.STA);
		worker.IsBackground = true;
		worker.Start();
		worker.Join(TimeSpan.FromSeconds(5));
		return resolved;
	}

	// ---------- IMMAGINI ----------

	// Un file di immagine letto e restituito in base64. Serve al plugin per
	// mostrare banner e copertine: l'interfaccia di Steam non puo' leggere il
	// disco, e passare per il backend Python del plugin e' la strada che gli
	// altri plugin di Playhub gia' usano.
	public static string ReadImageAsBase64(string path, long maximumBytes = 6 * 1024 * 1024)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path)) return "";
			string full = Path.GetFullPath(path);
			if (!File.Exists(full)) return "";
			FileInfo info = new(full);
			if (info.Length > maximumBytes) return "";
			return Convert.ToBase64String(File.ReadAllBytes(full));
		}
		catch
		{
			return "";
		}
	}

	private static string EncodeIcon(ImageSource? icon)
	{
		try
		{
			if (icon is not BitmapSource bitmap) return "";
			PngBitmapEncoder encoder = new();
			encoder.Frames.Add(BitmapFrame.Create(bitmap));
			using MemoryStream stream = new();
			encoder.Save(stream);
			return Convert.ToBase64String(stream.ToArray());
		}
		catch
		{
			return "";
		}
	}

	private static bool TryParseHandle(string handle, out nint value)
	{
		value = 0;
		if (string.IsNullOrWhiteSpace(handle)) return false;
		string text = handle.Trim();
		bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
		if (hex) text = text[2..];
		if (!long.TryParse(
				text,
				hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture,
				out long parsed))
		{
			return false;
		}
		value = (nint)parsed;
		return true;
	}

	private static (double Load, double UsedMb, double TotalMb) ReadMemory()
	{
		try
		{
			MemoryStatus status = new() { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
			if (!GlobalMemoryStatusEx(ref status)) return (0, 0, 0);
			double totalMb = status.TotalPhysical / 1024d / 1024d;
			double freeMb = status.AvailablePhysical / 1024d / 1024d;
			return (status.MemoryLoad, totalMb - freeMb, totalMb);
		}
		catch
		{
			return (0, 0, 0);
		}
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
