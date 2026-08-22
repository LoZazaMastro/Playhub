using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamingMode.Services;

// Utilizzo globale del sistema letto dai contatori di prestazione di Windows
// (PDH), gli stessi usati dal Gestione attivita'. Nessuna dipendenza esterna:
// pdh.dll fa parte del sistema. I contatori richiedono due campionamenti, il
// primo serve solo a fissare il punto di partenza.
public sealed record OverlayDiskUsage(string Name, double BytesPerSecond);

public sealed record OverlayUsageSnapshot(
	double CpuPercent,
	double GpuPercent,
	double DiskBytesPerSecond,
	double NetworkBytesPerSecond,
	bool GpuAvailable,
	IReadOnlyList<OverlayDiskUsage> Disks);

public static class OverlaySystemUsage
{
	[StructLayout(LayoutKind.Sequential)]
	private struct CounterValue
	{
		public uint Status;
		public uint Padding;
		public double Value;
	}

	private const uint FormatDouble = 0x00000200;
	private const uint FormatNoCap100 = 0x00008000;
	private const int Success = 0;

	private const string CpuCounter = @"\Processor Information(_Total)\% Processor Time";
	private const string DiskCounter = @"\PhysicalDisk(*)\Disk Bytes/sec";
	private const string NetworkCounter = @"\Network Interface(*)\Bytes Total/sec";
	private const string GpuCounter = @"\GPU Engine(*engtype_3D)\Utilization Percentage";

	// LETTURA CONTINUA.
	// La query PDH resta APERTA fra un aggiornamento e l'altro: i contatori
	// calcolano da soli la differenza rispetto alla lettura precedente, quindi
	// non serve piu' aprire, attendere 320 ms e richiudere a ogni giro. E' il
	// motivo per cui l'aggiornamento pesava sulle animazioni.
	private static readonly object LiveSync = new();
	private static nint _liveQuery;
	private static nint _liveCpu;
	private static nint _liveDisk;
	private static nint _liveNetwork;
	private static nint _liveGpu;
	private static bool _livePrimed;

	public static OverlayUsageSnapshot ReadLive()
	{
		lock (LiveSync)
		{
			try
			{
				if (_liveQuery == 0)
				{
					if (PdhOpenQuery(null, IntPtr.Zero, out _liveQuery) != Success) return Empty();
					_liveCpu = AddCounter(_liveQuery, CpuCounter);
					_liveDisk = AddCounter(_liveQuery, DiskCounter);
					_liveNetwork = AddCounter(_liveQuery, NetworkCounter);
					_liveGpu = AddCounter(_liveQuery, GpuCounter);
					_livePrimed = false;
				}
				if (PdhCollectQueryData(_liveQuery) != Success) return Empty();
				if (!_livePrimed)
				{
					// La prima raccolta fissa solo il punto di partenza.
					_livePrimed = true;
					Thread.Sleep(120);
					if (PdhCollectQueryData(_liveQuery) != Success) return Empty();
				}

				double gpuValue = SumArray(_liveGpu, out bool gpuAvailable);
				List<OverlayDiskUsage> disks = ReadArray(_liveDisk)
					.Where(item => !item.Name.Contains("_Total", StringComparison.OrdinalIgnoreCase))
					.Select(item => new OverlayDiskUsage(CleanDiskName(item.Name), item.Value))
					.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
					.ToList();
				return new OverlayUsageSnapshot(
					ReadSingle(_liveCpu),
					Math.Min(100, gpuValue),
					disks.Sum(item => item.BytesPerSecond),
					SumArray(_liveNetwork, out _),
					gpuAvailable,
					disks);
			}
			catch
			{
				if (_liveQuery != 0)
				{
					try { PdhCloseQuery(_liveQuery); } catch { }
					_liveQuery = 0;
				}
				return Empty();
			}
		}
	}

	public static OverlayUsageSnapshot Read(int sampleMilliseconds = 320)
	{
		nint query = 0;
		try
		{
			if (PdhOpenQuery(null, IntPtr.Zero, out query) != Success) return Empty();
			nint cpu = AddCounter(query, CpuCounter);
			nint disk = AddCounter(query, DiskCounter);
			nint network = AddCounter(query, NetworkCounter);
			nint gpu = AddCounter(query, GpuCounter);
			if (PdhCollectQueryData(query) != Success) return Empty();
			Thread.Sleep(sampleMilliseconds);
			if (PdhCollectQueryData(query) != Success) return Empty();

			double gpuValue = SumArray(gpu, out bool gpuAvailable);
			// I dischi vengono tenuti separati: sapere il totale non dice quale
			// unita' sta lavorando.
			List<OverlayDiskUsage> disks = ReadArray(disk)
				.Where(item => !item.Name.Contains("_Total", StringComparison.OrdinalIgnoreCase))
				.Select(item => new OverlayDiskUsage(CleanDiskName(item.Name), item.Value))
				.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
			return new OverlayUsageSnapshot(
				ReadSingle(cpu),
				Math.Min(100, gpuValue),
				disks.Sum(item => item.BytesPerSecond),
				SumArray(network, out _),
				gpuAvailable,
				disks);
		}
		catch
		{
			return Empty();
		}
		finally
		{
			if (query != 0) PdhCloseQuery(query);
		}
	}

	private static OverlayUsageSnapshot Empty() => new(0, 0, 0, 0, false, Array.Empty<OverlayDiskUsage>());

	private static nint AddCounter(nint query, string path)
	{
		return PdhAddEnglishCounter(query, path, IntPtr.Zero, out nint counter) == Success ? counter : 0;
	}

	private static double ReadSingle(nint counter)
	{
		if (counter == 0) return 0;
		if (PdhGetFormattedCounterValue(counter, FormatDouble | FormatNoCap100, out _, out CounterValue value) != Success) return 0;
		return value.Value;
	}

	// I contatori con carattere jolly restituiscono una voce per istanza: qui
	// interessa la somma (motori GPU, schede di rete, dischi fisici).
	private static double SumArray(nint counter, out bool available)
	{
		List<(string Name, double Value)> items = ReadArray(counter);
		available = items.Count > 0;
		return items.Sum(item => item.Value);
	}

	// I contatori con carattere jolly restituiscono una voce per istanza, con il
	// nome dell'istanza in testa alla struttura.
	private static List<(string Name, double Value)> ReadArray(nint counter)
	{
		List<(string Name, double Value)> items = new();
		if (counter == 0) return items;
		uint size = 0;
		uint count = 0;
		PdhGetFormattedCounterArray(counter, FormatDouble | FormatNoCap100, ref size, ref count, IntPtr.Zero);
		if (size == 0 || count == 0) return items;
		nint buffer = Marshal.AllocHGlobal((int)size);
		try
		{
			if (PdhGetFormattedCounterArray(counter, FormatDouble | FormatNoCap100, ref size, ref count, buffer) != Success) return items;
			int itemSize = IntPtr.Size + Marshal.SizeOf<CounterValue>();
			for (int i = 0; i < count; i++)
			{
				nint entry = buffer + (i * itemSize);
				nint namePointer = Marshal.ReadIntPtr(entry);
				string name = namePointer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(namePointer) ?? "";
				CounterValue value = Marshal.PtrToStructure<CounterValue>(entry + IntPtr.Size);
				if (value.Status != 0) continue;
				items.Add((name, value.Value));
			}
			return items;
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	// "0 C:" diventa "C:"; se l'unita' non ha lettera resta il numero del disco.
	private static string CleanDiskName(string instance)
	{
		string[] parts = instance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length >= 2) return string.Join(" ", parts.Skip(1));
		return instance;
	}

	public static string FormatRate(double bytesPerSecond)
	{
		if (bytesPerSecond >= 1024d * 1024 * 1024) return $"{bytesPerSecond / (1024d * 1024 * 1024):0.0} GB/s";
		if (bytesPerSecond >= 1024d * 1024) return $"{bytesPerSecond / (1024d * 1024):0.0} MB/s";
		if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0} KB/s";
		return $"{bytesPerSecond:0} B/s";
	}

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	private static extern int PdhOpenQuery(string? dataSource, nint userData, out nint query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	private static extern int PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

	[DllImport("pdh.dll")]
	private static extern int PdhCollectQueryData(nint query);

	[DllImport("pdh.dll")]
	private static extern int PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode)]
	private static extern int PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, ref uint itemCount, nint items);

	[DllImport("pdh.dll")]
	private static extern int PdhCloseQuery(nint query);
}
