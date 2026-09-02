using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GamingMode.Services;

public sealed record OverlaySteamArtwork(
	string AppId,
	string DisplayName,
	string? HeroPath,
	string? LogoPath,
	string? PortraitPath,
	string? BannerPath);

public static class OverlaySteamArtworkResolver
{
	private static readonly Regex ProcessAdded = new(
		@"AppID\s+(?<app>\d+)\s+adding PID\s+(?<pid>\d+)",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private static readonly Regex ProcessRemoved = new(
		@"AppID\s+(?<app>\d+)\s+no longer tracking PID\s+(?<pid>\d+)",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	// Per risolvere la grafica si usa prima la sessione attiva di Steam e poi,
	// come fallback, il percorso presente nei manifest o in shortcuts.vdf.
	// Per decidere se una finestra puo' attivare il recupero focus, invece, vale
	// soltanto la sessione realmente tracciata da Steam: essere nella libreria
	// non basta a trasformare una normale applicazione in un gioco in uscita.
	// INDICE DELLA LIBRERIA, COSTRUITO UNA VOLTA SOLA.
	//
	// Prima, per OGNI finestra dell'elenco, si rileggevano da capo tutti i
	// manifest di Steam e si riesaminava byte per byte shortcuts.vdf. Con una
	// libreria grande e sei finestre aperte il conto arrivava a quasi quattro
	// secondi, ed e' il rallentamento che restava. Adesso la mappa
	// "eseguibile -> identificativo" si costruisce una volta e vale per tutte
	// le finestre; si rifa' solo se sono passati piu' di due minuti, cioe' se
	// nel frattempo l'utente puo' aver installato qualcosa.
	private static readonly object IndexSync = new();
	private static Dictionary<string, string>? _installIndex;
	private static Dictionary<string, string>? _shortcutIndex;
	private static Dictionary<string, string>? _titleIndex;
	private static long _indexBuiltAt;
	private const long IndexLifetimeMs = 120_000;

	public static OverlaySteamArtwork? Resolve(OverlayWindowInfo window)
	{
		try
		{
			string steamPath = FindSteamPath();
			EnsureIndex(steamPath);
			ActiveSteamGame? activeGame = FindActiveGame(steamPath, window.ProcessId);
			if (activeGame is not null)
			{
				string activeAssetId = AssetId(activeGame.GameId);
				string activeTitle = LookupTitle(activeAssetId) ?? CleanTitle(window.Title, window.ProcessName);
				return BuildArtwork(steamPath, activeAssetId, activeGame.GameId.ToString(), activeTitle);
			}

			if (string.IsNullOrWhiteSpace(window.Path)) return null;
			string fullExecutable;
			try { fullExecutable = Path.GetFullPath(window.Path); }
			catch { return null; }

			string? assetId = LookupInstalled(fullExecutable) ?? LookupShortcut(fullExecutable);
			if (string.IsNullOrWhiteSpace(assetId)) return null;

			string title = LookupTitle(assetId) ?? CleanTitle(window.Title, window.ProcessName);
			return BuildArtwork(steamPath, assetId, null, title);
		}
		catch
		{
			return null;
		}
	}

	public static bool IsSteamGameProcess(int processId)
	{
		try
		{
			string steamPath = FindSteamPath();
			return FindActiveGame(steamPath, processId) is not null;
		}
		catch
		{
			return false;
		}
	}

	// Svuota l'indice: da chiamare quando si sa che la libreria e' cambiata.
	public static void InvalidateIndex()
	{
		lock (IndexSync)
		{
			_installIndex = null;
			_shortcutIndex = null;
			_titleIndex = null;
			_indexBuiltAt = 0;
		}
	}

	private static void EnsureIndex(string steamPath)
	{
		lock (IndexSync)
		{
			long now = Environment.TickCount64;
			if (_installIndex is not null && _shortcutIndex is not null && now - _indexBuiltAt < IndexLifetimeMs) return;
			Dictionary<string, string> titles = new(StringComparer.OrdinalIgnoreCase);
			_installIndex = BuildInstallIndex(steamPath, titles);
			_shortcutIndex = BuildShortcutIndex(steamPath, titles);
			_titleIndex = titles;
			_indexBuiltAt = now;
		}
	}

	// I giochi installati si riconoscono dalla cartella: l'eseguibile sta dentro
	// "common\<installdir>". Qui si tengono le radici, e il confronto e' un
	// semplice "il percorso comincia per".
	private static string? LookupInstalled(string fullExecutable)
	{
		lock (IndexSync)
		{
			if (_installIndex is null) return null;
			foreach ((string root, string appId) in _installIndex)
			{
				if (fullExecutable.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return appId;
			}
		}
		return null;
	}

	private static string? LookupShortcut(string fullExecutable)
	{
		lock (IndexSync)
		{
			if (_shortcutIndex is null) return null;
			if (_shortcutIndex.TryGetValue(fullExecutable, out string? exact)) return exact;
			// Ripiego sul solo nome del file: alcune scorciatoie salvano un
			// percorso diverso da quello con cui il gioco si avvia davvero.
			string name = Path.GetFileName(fullExecutable);
			foreach ((string path, string appId) in _shortcutIndex)
			{
				if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)) return appId;
			}
		}
		return null;
	}

	private static string? LookupTitle(string assetId)
	{
		lock (IndexSync)
		{
			return _titleIndex is not null && _titleIndex.TryGetValue(assetId, out string? title)
				? title
				: null;
		}
	}

	private static Dictionary<string, string> BuildInstallIndex(string steamPath, IDictionary<string, string> titles)
	{
		Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (string steamApps in FindSteamAppsRoots(steamPath))
			{
				foreach (string manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
				{
					try
					{
						string text = File.ReadAllText(manifest);
						string? appId = ReadVdfValue(text, "appid");
						string? installDir = ReadVdfValue(text, "installdir");
						if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(installDir)) continue;
						index[Path.GetFullPath(Path.Combine(steamApps, "common", installDir))] = appId;
						string? title = ReadVdfValue(text, "name")?.Trim();
						if (!string.IsNullOrWhiteSpace(title)) titles[appId] = title;
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
		return index;
	}

	// Giochi non Steam: shortcuts.vdf e' un VDF binario. Le voci hanno chiavi
	// terminate da zero precedute dal tipo (0x00 mappa, 0x01 testo, 0x02 intero).
	// Serve la coppia "appid" + "exe": l'identificativo e' un intero con segno che
	// va letto senza segno, ed e' il nome con cui Steam salva la grafica.
	// SCORCIATOIE (giochi non Steam): shortcuts.vdf e' un VDF binario. Le voci
	// hanno chiavi terminate da zero precedute dal tipo (0x00 mappa, 0x01 testo,
	// 0x02 intero). Serve la coppia "appid" + "exe": l'identificativo e' un
	// intero con segno che va letto senza segno, ed e' il nome con cui Steam
	// salva la grafica.
	//
	// Il file viene letto UNA VOLTA e trasformato in una mappa, invece che
	// riesaminato da capo per ogni finestra dell'elenco.
	private static Dictionary<string, string> BuildShortcutIndex(string steamPath, IDictionary<string, string> titles)
	{
		Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> ambiguousExecutables = new(StringComparer.OrdinalIgnoreCase);
		string userdata = Path.Combine(steamPath, "userdata");
		if (!Directory.Exists(userdata)) return index;
		foreach (string user in Directory.EnumerateDirectories(userdata))
		{
			string file = Path.Combine(user, "config", "shortcuts.vdf");
			if (!File.Exists(file)) continue;
			try
			{
				byte[] data = File.ReadAllBytes(file);
				uint currentAppId = 0;
				bool hasAppId = false;
				int index2 = 0;
				while (index2 < data.Length)
				{
					byte type = data[index2++];
					if (type == 0x08) continue;
					int keyStart = index2;
					while (index2 < data.Length && data[index2] != 0) index2++;
					string key = Encoding.UTF8.GetString(data, keyStart, index2 - keyStart);
					index2++;
					if (type == 0x00)
					{
						// Inizio di una nuova voce: l'identificativo riparte.
						hasAppId = false;
						continue;
					}
					if (type == 0x02)
					{
						if (index2 + 4 > data.Length) break;
						uint value = BitConverter.ToUInt32(data, index2);
						index2 += 4;
						if (key.Equals("appid", StringComparison.OrdinalIgnoreCase))
						{
							currentAppId = value;
							hasAppId = true;
						}
						continue;
					}
					if (type == 0x01)
					{
						int valueStart = index2;
						while (index2 < data.Length && data[index2] != 0) index2++;
						string value = Encoding.UTF8.GetString(data, valueStart, index2 - valueStart);
						index2++;
						if (hasAppId && key.Equals("appname", StringComparison.OrdinalIgnoreCase))
						{
							string title = value.Trim();
							if (!string.IsNullOrWhiteSpace(title)) titles[currentAppId.ToString()] = title;
							continue;
						}
						if (!hasAppId || !key.Equals("exe", StringComparison.OrdinalIgnoreCase)) continue;
						string candidate = ExtractExecutablePath(value);
						if (string.IsNullOrWhiteSpace(candidate)) continue;
						try { candidate = Path.GetFullPath(candidate); }
						catch { continue; }
						string assetId = currentAppId.ToString();
						if (ambiguousExecutables.Contains(candidate)) continue;
						if (index.TryGetValue(candidate, out string? existing) && !existing.Equals(assetId, StringComparison.Ordinal))
						{
							// Emulatori come Dolphin condividono lo stesso eseguibile fra molti
							// collegamenti. In quel caso decide il PID tracciato da Steam, non
							// un'associazione arbitraria all'ultima voce del file.
							index.Remove(candidate);
							ambiguousExecutables.Add(candidate);
							continue;
						}
						index[candidate] = assetId;
						continue;
					}
					break;
				}
			}
			catch
			{
			}
		}
		return index;
	}

	private static string ExtractExecutablePath(string command)
	{
		string value = command.Trim();
		if (value.Length == 0) return "";
		if (value[0] == '"')
		{
			int closingQuote = value.IndexOf('"', 1);
			return closingQuote > 1 ? value[1..closingQuote] : value.Trim('"');
		}

		int extension = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
		if (extension >= 0) return value[..(extension + 4)].Trim();
		int separator = value.IndexOf(' ');
		return separator > 0 ? value[..separator] : value;
	}

	private static ActiveSteamGame? FindActiveGame(string steamPath, int windowProcessId)
	{
		string path = Path.Combine(steamPath, "logs", "gameprocess_log.txt");
		if (!File.Exists(path)) return null;
		string tail = ReadTail(path, 768 * 1024);
		Dictionary<int, ActiveSteamGame> active = new();
		foreach (string line in tail.Split('\n'))
		{
			Match added = ProcessAdded.Match(line);
			if (added.Success
				&& ulong.TryParse(added.Groups["app"].Value, out ulong gameId)
				&& int.TryParse(added.Groups["pid"].Value, out int processId))
			{
				active[processId] = new ActiveSteamGame(gameId, processId);
				continue;
			}
			Match removed = ProcessRemoved.Match(line);
			if (removed.Success && int.TryParse(removed.Groups["pid"].Value, out processId))
			{
				active.Remove(processId);
			}
		}

		foreach (int stale in active.Keys.Where(pid => !ProcessExists(pid)).ToArray()) active.Remove(stale);
		if (active.TryGetValue(windowProcessId, out ActiveSteamGame? direct)) return direct;

		Dictionary<int, int> parents = SnapshotParents();
		// Una finestra puo' appartenere a un processo figlio del launcher tracciato
		// da Steam (o viceversa). Si accettano soltanto relazioni antenato/discendente
		// reali: condividere steam.exe come antenato non e' sufficiente e non puo'
		// quindi attribuire la grafica del gioco ad altre finestre.
		return active.Values.LastOrDefault(game =>
			IsAncestor(game.ProcessId, windowProcessId, parents)
			|| IsAncestor(windowProcessId, game.ProcessId, parents));
	}

	private static bool IsAncestor(int ancestor, int processId, IReadOnlyDictionary<int, int> parents)
	{
		int current = processId;
		for (int depth = 0; depth < 16 && parents.TryGetValue(current, out int parent) && parent > 0; depth++)
		{
			if (parent == ancestor) return true;
			if (parent == current) break;
			current = parent;
		}
		return false;
	}

	private static OverlaySteamArtwork BuildArtwork(string steamPath, string assetId, string? gameId, string title)
	{
		List<string> gridRoots = FindGridRoots(steamPath).ToList();
		string? hero = FindGridAsset(gridRoots, assetId, "_hero");
		string? logo = FindGridAsset(gridRoots, assetId, "_logo");
		string? portrait = FindGridAsset(gridRoots, assetId, "p");
		// Il banner e' l'immagine orizzontale della libreria Steam: nella cartella
		// grid e' il file senza suffisso, nella cache si chiama header.jpg.
		string? banner = FindGridAsset(gridRoots, assetId, "");
		if (!string.IsNullOrWhiteSpace(gameId) && !gameId.Equals(assetId, StringComparison.OrdinalIgnoreCase))
		{
			hero ??= FindGridAsset(gridRoots, gameId, "_hero");
			logo ??= FindGridAsset(gridRoots, gameId, "_logo");
			portrait ??= FindGridAsset(gridRoots, gameId, "p");
			banner ??= FindGridAsset(gridRoots, gameId, "");
		}

		string cache = Path.Combine(steamPath, "appcache", "librarycache", assetId);
		hero ??= Existing(Path.Combine(cache, "library_hero.jpg"));
		logo ??= Existing(Path.Combine(cache, "logo.png"));
		portrait ??= Existing(Path.Combine(cache, "library_600x900.jpg"));
		banner ??= Existing(Path.Combine(cache, "header.jpg"));
		return new OverlaySteamArtwork(assetId, title, hero, logo, portrait, banner);
	}

	private static IEnumerable<string> FindGridRoots(string steamPath)
	{
		string userdata = Path.Combine(steamPath, "userdata");
		if (!Directory.Exists(userdata)) yield break;
		foreach (string user in Directory.EnumerateDirectories(userdata))
		{
			string grid = Path.Combine(user, "config", "grid");
			if (Directory.Exists(grid)) yield return grid;
		}
	}

	private static string? FindGridAsset(IEnumerable<string> roots, string id, string suffix)
	{
		foreach (string root in roots)
		{
			foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
			{
				string? path = Existing(Path.Combine(root, id + suffix + extension));
				if (path is not null) return path;
			}
		}
		return null;
	}

	private static IEnumerable<string> FindSteamAppsRoots(string steamPath)
	{
		HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
		string primary = Path.Combine(steamPath, "steamapps");
		if (Directory.Exists(primary)) roots.Add(primary);
		string libraries = Path.Combine(primary, "libraryfolders.vdf");
		if (File.Exists(libraries))
		{
			string text = File.ReadAllText(libraries);
			foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			{
				string candidate = Path.Combine(match.Groups["path"].Value.Replace("\\\\", "\\"), "steamapps");
				if (Directory.Exists(candidate)) roots.Add(candidate);
			}
		}
		return roots;
	}

	private static string? ReadVdfValue(string text, string key)
	{
		Match match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		return match.Success ? match.Groups["value"].Value : null;
	}

	private static string AssetId(ulong gameId)
	{
		return gameId <= uint.MaxValue ? gameId.ToString() : ((uint)(gameId >> 32)).ToString();
	}

	private static string CleanTitle(string title, string processName)
	{
		string value = string.IsNullOrWhiteSpace(title) ? processName : title.Trim();
		foreach (string suffix in new[] { " - Steam", " on Steam" })
		{
			if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) value = value[..^suffix.Length].Trim();
		}
		return value;
	}

	private static HashSet<int> ProcessFamily(int processId)
	{
		Dictionary<int, int> parents = SnapshotParents();
		HashSet<int> family = new() { processId };
		int current = processId;
		for (int depth = 0; depth < 12 && parents.TryGetValue(current, out int parent) && parent > 0; depth++)
		{
			if (!family.Add(parent)) break;
			current = parent;
		}
		foreach ((int child, int parent) in parents)
		{
			if (family.Contains(parent)) family.Add(child);
		}
		return family;
	}

	private static Dictionary<int, int> SnapshotParents()
	{
		Dictionary<int, int> parents = new();
		foreach (Process process in Process.GetProcesses())
		{
			try
			{
				using (process)
				{
					int parent = ProcessParent.Read(process.Handle);
					if (parent > 0) parents[process.Id] = parent;
				}
			}
			catch
			{
				process.Dispose();
			}
		}
		return parents;
	}

	private static bool ProcessExists(int processId)
	{
		try { using Process process = Process.GetProcessById(processId); return !process.HasExited; }
		catch { return false; }
	}

	private static string? Existing(string path)
	{
		try { return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null; }
		catch { return null; }
	}

	private static string FindSteamPath()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			string? path = key?.GetValue("SteamPath") as string;
			if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path.Replace('/', Path.DirectorySeparatorChar);
		}
		catch
		{
		}
		return @"C:\Program Files (x86)\Steam";
	}

	private static string ReadTail(string path, int maximumBytes)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		long start = Math.Max(0, stream.Length - maximumBytes);
		stream.Position = start;
		byte[] bytes = new byte[stream.Length - start];
		int read = stream.Read(bytes, 0, bytes.Length);
		return Encoding.UTF8.GetString(bytes, 0, read);
	}

	private sealed record ActiveSteamGame(ulong GameId, int ProcessId);

	private static class ProcessParent
	{
		public static int Read(nint handle)
		{
			ProcessBasicInformation info = new();
			return NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0
				? info.InheritedFromUniqueProcessId.ToInt32()
				: 0;
		}

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		private struct ProcessBasicInformation
		{
			public nint Reserved1;
			public nint PebBaseAddress;
			public nint Reserved2A;
			public nint Reserved2B;
			public nint UniqueProcessId;
			public nint InheritedFromUniqueProcessId;
		}

		[System.Runtime.InteropServices.DllImport("ntdll.dll")]
		private static extern int NtQueryInformationProcess(nint processHandle, int processInformationClass, ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);
	}
}
