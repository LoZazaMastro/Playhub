using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GamingMode.Services;

internal readonly record struct GameAudioTarget(int ProcessId, long CreationFileTimeUtc);
internal sealed record GameAudioVolumeResult(string Status, int? Level = null, string? Token = null);

internal static class GameAudioVolumeService
{
	internal static GameAudioVolumeResult Read(GameAudioTarget target, Func<bool> stillCurrent) =>
		Access(target, null, null, stillCurrent);

	internal static GameAudioVolumeResult Set(GameAudioTarget target, int level, string expectedToken, Func<bool> stillCurrent) =>
		level is >= 0 and <= 100 && expectedToken is { Length: 64 }
			? Access(target, level, expectedToken, stillCurrent) : new("Unavailable");

	private static GameAudioVolumeResult Access(GameAudioTarget target, int? level, string? expectedToken, Func<bool> stillCurrent)
	{
		if (!OperatingSystem.IsWindows() || target.ProcessId <= 0 || target.CreationFileTimeUtc <= 0) return new("Unavailable");
		try
		{
			using var process = Process.GetProcessById(target.ProcessId);
			using var currentProcess = Process.GetCurrentProcess();
			bool IsCurrent() => !process.HasExited && process.StartTime.ToFileTimeUtc() == target.CreationFileTimeUtc && stillCurrent();
			if (process.SessionId != currentProcess.SessionId || !IsCurrent()) return new("Unavailable");
			using var sessions = new RenderSessions(target.ProcessId);
			return Apply(target, level, expectedToken, IsCurrent, sessions.Matches);
		}
		catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
		{ return new("Unavailable"); }
		catch (COMException) { return new("Failed"); }
	}

	internal static GameAudioVolumeResult Apply(GameAudioTarget target, int? level, string? expectedToken, Func<bool> stillCurrent,
		IReadOnlyList<IGameAudioSession> sessions)
	{
		if (!stillCurrent()) return new("Unavailable");
		if (sessions.Count > 1) return new("Ambiguous");
		if (sessions.Count != 1) return new("Unavailable");
		var session = sessions[0];
		string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
			$"{target.ProcessId}:{target.CreationFileTimeUtc}\n{session.EndpointId}\n{session.InstanceId}")))).ToLowerInvariant();
		if (level.HasValue)
		{
			if (level.Value is < 0 or > 100 || token != expectedToken || !session.IsExact(target.ProcessId) || !stillCurrent())
				return new("Unavailable");
			if (!session.SetLevel(level.Value / 100f)) return new("Failed");
		}
		if (!session.IsExact(target.ProcessId) || !session.TryReadLevel(out float value) || !float.IsFinite(value) || value is < 0f or > 1f)
			return new("Failed");
		if (!stillCurrent()) return new("Unavailable");
		int actualLevel = (int)MathF.Round(value * 100);
		return level.HasValue && level.Value != actualLevel ? new("Failed") : new("Ready", actualLevel, token);
	}

	internal interface IGameAudioSession
	{
		string EndpointId { get; }
		string InstanceId { get; }
		bool IsExact(int processId);
		bool SetLevel(float level);
		bool TryReadLevel(out float level);
	}

	private sealed class RenderSessions : IDisposable
	{
		private readonly List<IGameAudioSession> _matches = [];
		private readonly List<object> _objects = [];
		internal IReadOnlyList<IGameAudioSession> Matches => _matches;

		internal RenderSessions(int pid)
		{
			try
			{
				var deviceClass = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!;
				var enumerator = Own((IMMDeviceEnumerator)Activator.CreateInstance(deviceClass)!);
				Check(enumerator.EnumAudioEndpoints(0, 1, out var endpoints)); // eRender, DEVICE_STATE_ACTIVE
				Own(endpoints);
				Check(endpoints.GetCount(out uint endpointCount));
				for (uint index = 0; index < endpointCount; index++)
				{
					Check(endpoints.Item(index, out var endpoint));
					Own(endpoint);
					Check(endpoint.GetId(out string endpointId));
					Guid iid = typeof(IAudioSessionManager2).GUID;
					Check(endpoint.Activate(in iid, 23, 0, out object manager)); // CLSCTX_ALL
					Own(manager);
					Check(((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions));
					Own(sessions);
					Check(sessions.GetCount(out int count));
					for (int sessionIndex = 0; sessionIndex < count; sessionIndex++)
					{
						Check(sessions.GetSession(sessionIndex, out var controlObject));
						Own(controlObject);
						if (controlObject is not IAudioSessionControl2 control || !IsOwned(control, pid)) continue;
						if (controlObject is not ISimpleAudioVolume volume) throw new COMException("Audio session volume is unavailable.");
						Check(control.GetSessionInstanceIdentifier(out string instanceId));
						if (string.IsNullOrEmpty(instanceId)) throw new COMException("Audio session identity is unavailable.");
						_matches.Add(new Session(control, volume, endpointId, instanceId));
					}
				}
			}
			catch { Dispose(); throw; }
		}

		private T Own<T>(T instance) where T : class { _objects.Add(instance); return instance; }
		private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

		public void Dispose()
		{
			for (int index = _objects.Count - 1; index >= 0; index--)
				Marshal.ReleaseComObject(_objects[index]);
			_objects.Clear();
		}
	}

	private static bool IsOwned(IAudioSessionControl2 control, int pid) =>
		control.GetProcessId(out uint owner) == 0 && owner == (uint)pid && control.IsSystemSoundsSession() == 1 &&
		control.GetState(out int state) == 0 && state is 0 or 1;

	private sealed class Session(IAudioSessionControl2 control, ISimpleAudioVolume volume, string endpointId, string instanceId) : IGameAudioSession
	{
		public string EndpointId => endpointId;
		public string InstanceId => instanceId;
		public bool IsExact(int processId) => IsOwned(control, processId) &&
			control.GetSessionInstanceIdentifier(out string current) == 0 && current == instanceId;
		public bool SetLevel(float level) => volume.SetMasterVolume(level, 0) == 0;
		public bool TryReadLevel(out float level) => volume.GetMasterVolume(out level) == 0;
	}

	[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection devices);
	}

	[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceCollection
	{
		[PreserveSig] int GetCount(out uint count);
		[PreserveSig] int Item(uint index, out IMMDevice device);
	}

	[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDevice
	{
		[PreserveSig] int Activate(in Guid iid, uint context, nint activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
		[PreserveSig] int OpenPropertyStore(uint access, out nint store);
		[PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
	}

	[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionManager2
	{
		[PreserveSig] int GetAudioSessionControl(nint session, uint flags, out nint control);
		[PreserveSig] int GetSimpleAudioVolume(nint session, uint flags, out nint volume);
		[PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
	}

	[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionEnumerator
	{
		[PreserveSig] int GetCount(out int count);
		[PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
	}

	[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionControl2
	{
		[PreserveSig] int GetState(out int state);
		[PreserveSig] int GetDisplayName(out nint name);
		[PreserveSig] int SetDisplayName(nint name, nint context);
		[PreserveSig] int GetIconPath(out nint path);
		[PreserveSig] int SetIconPath(nint path, nint context);
		[PreserveSig] int GetGroupingParam(out Guid grouping);
		[PreserveSig] int SetGroupingParam(in Guid grouping, nint context);
		[PreserveSig] int RegisterAudioSessionNotification(nint events);
		[PreserveSig] int UnregisterAudioSessionNotification(nint events);
		[PreserveSig] int GetSessionIdentifier(out nint id);
		[PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
		[PreserveSig] int GetProcessId(out uint pid);
		[PreserveSig] int IsSystemSoundsSession();
	}

	[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ISimpleAudioVolume
	{
		[PreserveSig] int SetMasterVolume(float level, nint context);
		[PreserveSig] int GetMasterVolume(out float level);
	}
}
