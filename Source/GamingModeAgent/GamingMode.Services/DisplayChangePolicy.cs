using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingMode.Services;

// LE REGOLE DEL RECUPERO DELLO SCHERMO, SENZA WIN32.
//
// Stanno qui, separate dal servizio che parla con Windows, perche' devono poter
// essere provate: decidono quando l'agente tocca Steam, e un errore in questo
// punto significa o uno schermo nero che resta nero o un lampo inutile.
public enum DisplayChangeKind
{
	None,
	// Cambia solo risoluzione, frequenza o posizione: Steam lo gestisce da se'.
	ModeOnly,
	// Cambia lo stato HDR / profondita' colore sullo stesso monitor.
	ColorOnly,
	// Un monitor e' comparso, sparito o e' stato ricreato da Windows: e' il
	// caso della TV accesa dopo l'avvio.
	Hotplug
}

public sealed record DisplayMonitorEntry(
	string Device,
	long Handle,
	int Width,
	int Height,
	bool? AdvancedColor,
	int BitsPerColor);

public sealed record DisplaySnapshot(IReadOnlyList<DisplayMonitorEntry> Monitors)
{
	public static readonly DisplaySnapshot Empty = new(Array.Empty<DisplayMonitorEntry>());

	public bool SameAs(DisplaySnapshot other)
		=> DisplayChangePolicy.Classify(this, other) == DisplayChangeKind.None;

	public string Describe()
	{
		if (Monitors.Count == 0) return "nessun monitor attivo";
		return string.Join("; ", Monitors.Select(m =>
			$"{m.Device} {m.Width}x{m.Height} hdr={(m.AdvancedColor.HasValue ? (m.AdvancedColor.Value ? "on" : "off") : "?")} bpc={m.BitsPerColor} h=0x{m.Handle:X}"));
	}
}

public static class DisplayChangePolicy
{
	public static DisplayChangeKind Classify(DisplaySnapshot before, DisplaySnapshot after)
	{
		var a = Sorted(before);
		var b = Sorted(after);
		if (a.Count != b.Count) return DisplayChangeKind.Hotplug;
		for (int i = 0; i < a.Count; i++)
		{
			if (!string.Equals(a[i].Device, b[i].Device, StringComparison.OrdinalIgnoreCase)) return DisplayChangeKind.Hotplug;
			// Stesso nome (\\.\DISPLAY1) ma handle diverso: Windows ha distrutto
			// e ricreato il monitor. Succede quando la TV esce dallo standby.
			if (a[i].Handle != b[i].Handle) return DisplayChangeKind.Hotplug;
		}
		bool color = false;
		bool mode = false;
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].AdvancedColor != b[i].AdvancedColor || a[i].BitsPerColor != b[i].BitsPerColor) color = true;
			if (a[i].Width != b[i].Width || a[i].Height != b[i].Height) mode = true;
		}
		if (color) return DisplayChangeKind.ColorOnly;
		return mode ? DisplayChangeKind.ModeOnly : DisplayChangeKind.None;
	}

	// Il cambio piu' grave vince: un hotplug seguito da un cambio HDR resta un
	// hotplug, un cambio di sola risoluzione non cancella un recupero atteso.
	public static DisplayChangeKind Strongest(DisplayChangeKind a, DisplayChangeKind b)
		=> (DisplayChangeKind)Math.Max((int)a, (int)b);

	public static bool NeedsRecovery(DisplayChangeKind kind)
		=> kind is DisplayChangeKind.Hotplug or DisplayChangeKind.ColorOnly;

	// Hotplug: si interviene sempre, la TV si sta accendendo e un lampo non si
	// vede. Solo HDR (per esempio dal pannello rapido): si interviene soltanto
	// se lo schermo risulta nero, o se non e' stato possibile verificarlo.
	public static bool ShouldRecover(DisplayChangeKind kind, bool? screenBlack)
		=> kind switch
		{
			DisplayChangeKind.Hotplug => true,
			DisplayChangeKind.ColorOnly => screenBlack != false,
			_ => false
		};

	public const uint InvalidPixel = 0xFFFFFFFF;

	// Campioni COLORREF (0x00BBGGRR). null = non verificabile.
	public static bool? IsBlack(IReadOnlyCollection<uint> samples, int threshold = 12)
	{
		if (samples.Count == 0) return null;
		if (samples.Any(s => s == InvalidPixel)) return null;
		return samples.All(s =>
			(s & 0xFF) <= threshold &&
			((s >> 8) & 0xFF) <= threshold &&
			((s >> 16) & 0xFF) <= threshold);
	}

	// steamwebhelper.exe e' un Chromium: il processo GPU ha "--type=gpu-process",
	// il processo principale (il browser) non ha alcun "--type=".
	public static bool IsGpuProcess(string? commandLine)
		=> !string.IsNullOrEmpty(commandLine) &&
		   commandLine.Contains("--type=gpu-process", StringComparison.OrdinalIgnoreCase);

	public static bool IsBrowserProcess(string? commandLine)
		=> !string.IsNullOrEmpty(commandLine) &&
		   !commandLine.Contains("--type=", StringComparison.OrdinalIgnoreCase);

	private static List<DisplayMonitorEntry> Sorted(DisplaySnapshot snapshot)
		=> snapshot.Monitors
			.OrderBy(m => m.Device, StringComparer.OrdinalIgnoreCase)
			.ThenBy(m => m.Handle)
			.ToList();
}
