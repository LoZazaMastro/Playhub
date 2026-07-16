using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamingMode.Services;

public sealed class SystemVolumeKeyService : IDisposable
{
	private delegate nint HookProc(int nCode, nint wParam, nint lParam);

	private readonly struct KeyboardHookStruct
	{
		public readonly uint VirtualKeyCode;

		public readonly uint ScanCode;

		public readonly uint Flags;

		public readonly uint Time;

		public readonly nint ExtraInfo;
	}

	private struct Message
	{
		public nint Window;

		public uint Value;

		public nint WParam;

		public nint LParam;

		public uint Time;

		public Point Position;
	}

	private struct Point
	{
		public int X;

		public int Y;
	}

	private static class SystemVolume
	{
		private enum AudioDataFlow
		{
			Render,
			Capture,
			All
		}

		private enum AudioRole
		{
			Console,
			Multimedia,
			Communications
		}

		[Flags]
		private enum ClassContext : uint
		{
			InprocServer = 1u,
			InprocHandler = 2u,
			LocalServer = 4u,
			RemoteServer = 0x10u,
			All = 0x17u
		}

		[ComImport]
		[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IMMDeviceEnumerator
		{
			[PreserveSig]
			int EnumAudioEndpoints(AudioDataFlow dataFlow, uint stateMask, out object devices);

			[PreserveSig]
			int GetDefaultAudioEndpoint(AudioDataFlow dataFlow, AudioRole role, out IMMDevice endpoint);

			[PreserveSig]
			int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

			[PreserveSig]
			int RegisterEndpointNotificationCallback(nint client);

			[PreserveSig]
			int UnregisterEndpointNotificationCallback(nint client);
		}

		[ComImport]
		[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IMMDevice
		{
			[PreserveSig]
			int Activate(ref Guid iid, ClassContext classContext, nint activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

			[PreserveSig]
			int OpenPropertyStore(int access, out object properties);

			[PreserveSig]
			int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

			[PreserveSig]
			int GetState(out uint state);
		}

		[ComImport]
		[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IAudioEndpointVolume
		{
			[PreserveSig]
			int RegisterControlChangeNotify(nint notify);

			[PreserveSig]
			int UnregisterControlChangeNotify(nint notify);

			[PreserveSig]
			int GetChannelCount(out uint channelCount);

			[PreserveSig]
			int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

			[PreserveSig]
			int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

			[PreserveSig]
			int GetMasterVolumeLevel(out float levelDb);

			[PreserveSig]
			int GetMasterVolumeLevelScalar(out float level);

			[PreserveSig]
			int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

			[PreserveSig]
			int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

			[PreserveSig]
			int GetChannelVolumeLevel(uint channel, out float levelDb);

			[PreserveSig]
			int GetChannelVolumeLevelScalar(uint channel, out float level);

			[PreserveSig]
			int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

			[PreserveSig]
			int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

			[PreserveSig]
			int GetVolumeStepInfo(out uint step, out uint stepCount);

			[PreserveSig]
			int VolumeStepUp(ref Guid eventContext);

			[PreserveSig]
			int VolumeStepDown(ref Guid eventContext);

			[PreserveSig]
			int QueryHardwareSupport(out uint hardwareSupportMask);

			[PreserveSig]
			int GetVolumeRange(out float volumeMinDb, out float volumeMaxDb, out float volumeIncrementDb);
		}

		private static readonly Guid MMDeviceEnumeratorClsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

		public static void StepUp()
		{
			WithEndpoint(delegate(IAudioEndpointVolume endpoint)
			{
				Guid eventContext = Guid.Empty;
				Marshal.ThrowExceptionForHR(endpoint.VolumeStepUp(ref eventContext));
			});
		}

		public static void StepDown()
		{
			WithEndpoint(delegate(IAudioEndpointVolume endpoint)
			{
				Guid eventContext = Guid.Empty;
				Marshal.ThrowExceptionForHR(endpoint.VolumeStepDown(ref eventContext));
			});
		}

		public static void ToggleMute()
		{
			WithEndpoint(delegate(IAudioEndpointVolume endpoint)
			{
				Marshal.ThrowExceptionForHR(endpoint.GetMute(out var mute));
				Guid eventContext = Guid.Empty;
				Marshal.ThrowExceptionForHR(endpoint.SetMute(!mute, ref eventContext));
			});
		}

		private static void WithEndpoint(Action<IAudioEndpointVolume> action)
		{
			object instance = null;
			IMMDevice endpoint = null;
			IMMDeviceEnumerator iMMDeviceEnumerator = null;
			try
			{
				iMMDeviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid, throwOnError: true));
				Marshal.ThrowExceptionForHR(iMMDeviceEnumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, AudioRole.Multimedia, out endpoint));
				Guid iid = typeof(IAudioEndpointVolume).GUID;
				Marshal.ThrowExceptionForHR(endpoint.Activate(ref iid, ClassContext.All, IntPtr.Zero, out instance));
				action((IAudioEndpointVolume)instance);
			}
			finally
			{
				ReleaseCom(instance);
				ReleaseCom(endpoint);
				ReleaseCom(iMMDeviceEnumerator);
			}
		}

		private static void ReleaseCom(object? instance)
		{
			if (instance != null && Marshal.IsComObject(instance))
			{
				Marshal.FinalReleaseComObject(instance);
			}
		}
	}

	private const int WhKeyboardLl = 13;

	private const int WmKeyDown = 256;

	private const int WmSysKeyDown = 260;

	private const int WmQuit = 18;

	private const int VkVolumeMute = 173;

	private const int VkVolumeDown = 174;

	private const int VkVolumeUp = 175;

	private readonly FileLogger _logger;

	private readonly object _sync = new object();

	private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(initialState: false);

	private Thread? _thread;

	private HookProc? _hookProc;

	private nint _hook;

	private uint _threadId;

	private DateTimeOffset _lastErrorAt = DateTimeOffset.MinValue;

	public bool Running
	{
		get
		{
			lock (_sync)
			{
				Thread thread = _thread;
				return thread != null && thread.IsAlive && _hook != IntPtr.Zero;
			}
		}
	}

	public SystemVolumeKeyService(FileLogger logger)
	{
		_logger = logger;
	}

	public void Start()
	{
		lock (_sync)
		{
			Thread thread = _thread;
			if (thread != null && thread.IsAlive)
			{
				return;
			}
			_ready.Reset();
			_thread = new Thread(RunHookLoop)
			{
				IsBackground = true,
				Name = "Gaming Mode Volume Keys"
			};
			_thread.Start();
		}
		if (_ready.Wait(TimeSpan.FromSeconds(2.0)) && Running)
		{
			_logger.Info("System volume keys are handled by Gaming Mode.");
		}
	}

	public void Stop()
	{
		Thread thread;
		uint threadId;
		lock (_sync)
		{
			thread = _thread;
			threadId = _threadId;
		}
		if (thread == null)
		{
			return;
		}
		if (threadId != 0)
		{
			PostThreadMessage(threadId, 18, IntPtr.Zero, IntPtr.Zero);
		}
		if (thread.IsAlive && !thread.Join(TimeSpan.FromSeconds(2.0)))
		{
			_logger.Info("System volume key hook did not stop within the expected time.");
		}
		lock (_sync)
		{
			if (_thread == thread)
			{
				_thread = null;
				_threadId = 0u;
			}
		}
	}

	public void Dispose()
	{
		Stop();
		_ready.Dispose();
	}

	private void RunHookLoop()
	{
		_threadId = GetCurrentThreadId();
		_hookProc = HookCallback;
		try
		{
			_hook = SetWindowsHookEx(13, _hookProc, GetModuleHandle(null), 0u);
			if (_hook == IntPtr.Zero)
			{
				_logger.Error($"System volume key hook could not be installed. Win32 error: {Marshal.GetLastWin32Error()}");
				_ready.Set();
			}
			else
			{
				_ready.Set();
				Message lpMsg;
				while (GetMessage(out lpMsg, IntPtr.Zero, 0u, 0u) > 0)
				{
					TranslateMessage(ref lpMsg);
					DispatchMessage(ref lpMsg);
				}
			}
		}
		catch (Exception exception)
		{
			_logger.Error("System volume key service crashed.", exception);
		}
		finally
		{
			if (_hook != IntPtr.Zero)
			{
				UnhookWindowsHookEx(_hook);
			}
			lock (_sync)
			{
				_hook = IntPtr.Zero;
				_threadId = 0u;
				_hookProc = null;
			}
			_ready.Set();
		}
	}

	private nint HookCallback(int nCode, nint wParam, nint lParam)
	{
		if (nCode >= 0 && (wParam == 256 || wParam == 260) && HandleVolumeKey((int)Marshal.PtrToStructure<KeyboardHookStruct>(lParam).VirtualKeyCode))
		{
			return 1;
		}
		return CallNextHookEx(_hook, nCode, wParam, lParam);
	}

	private bool HandleVolumeKey(int virtualKey)
	{
		try
		{
			switch (virtualKey)
			{
			case 175:
				SystemVolume.StepUp();
				return true;
			case 174:
				SystemVolume.StepDown();
				return true;
			case 173:
				SystemVolume.ToggleMute();
				return true;
			default:
				return false;
			}
		}
		catch (Exception exception)
		{
			if (DateTimeOffset.UtcNow - _lastErrorAt > TimeSpan.FromSeconds(10.0))
			{
				_lastErrorAt = DateTimeOffset.UtcNow;
				_logger.Error("System volume key could not be handled.", exception);
			}
			return false;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetMessage(out Message lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref Message lpMsg);

	[DllImport("user32.dll")]
	private static extern nint DispatchMessage(ref Message lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostThreadMessage(uint idThread, int msg, nint wParam, nint lParam);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint GetModuleHandle(string? lpModuleName);
}
