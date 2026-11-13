using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

internal static class UwpFrameWindow
{
	private static readonly string FrameHost = Path.Combine(Environment.SystemDirectory, "ApplicationFrameHost.exe");
	private static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
	private const uint QueryLimitedInformation = 0x1000;
	private const uint RootAncestor = 2;
	private const ushort UnicodeString = 31;

	internal static bool Matches(nint window, string aumid) => !string.IsNullOrEmpty(aumid) &&
		string.Equals(ReadAppId(window), aumid, StringComparison.OrdinalIgnoreCase);

	internal static string? ReadAppId(nint window)
	{
		if (window == 0 || GetAncestor(window, RootAncestor) != window) return null;
		var className = new StringBuilder(32);
		if (GetClassName(window, className, className.Capacity) == 0 || className.ToString() != "ApplicationFrameWindow") return null;
		uint thread = GetWindowThreadProcessId(window, out uint pid);
		if (thread == 0 || pid == 0) return null;

		using SafeProcessHandle owner = OpenProcess(QueryLimitedInformation, false, pid);
		if (owner.IsInvalid) return null;
		var image = new StringBuilder(1024);
		uint length = (uint)image.Capacity;
		if (!QueryFullProcessImageName(owner, 0, image, ref length) ||
			!string.Equals(image.ToString(), FrameHost, StringComparison.OrdinalIgnoreCase)) return null;

		// A packaged app may have no child HWND in the Windows-owned frame.
		// Use the shell's app identity on the frame, with no title heuristic.
		IPropertyStore? store = null;
		PropVariant value = default;
		try
		{
			Guid iid = typeof(IPropertyStore).GUID;
			if (SHGetPropertyStoreForWindow(window, in iid, out store) != 0 || store is null ||
				store.GetValue(in AppUserModelId, out value) != 0 || value.Type != UnicodeString || value.Pointer == 0) return null;
			string? aumid = Marshal.PtrToStringUni(value.Pointer);
			return !string.IsNullOrEmpty(aumid) && GetWindowThreadProcessId(window, out uint currentPid) == thread && currentPid == pid
				? aumid : null;
		}
		catch (COMException) { return null; }
		finally
		{
			PropVariantClear(ref value);
			if (store is not null) Marshal.ReleaseComObject(store);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct PropertyKey(Guid format, uint id)
	{
		public readonly Guid Format = format;
		public readonly uint Id = id;
	}

	// The agent targets x64. The largest union member holds a DWORD and a pointer.
	[StructLayout(LayoutKind.Explicit, Size = 24)]
	private struct PropVariant
	{
		[FieldOffset(0)] public ushort Type;
		[FieldOffset(8)] public nint Pointer;
	}

	[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyStore
	{
		[PreserveSig] int GetCount(out uint count);
		[PreserveSig] int GetAt(uint index, out PropertyKey key);
		[PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
	}

	[DllImport("shell32.dll")]
	private static extern int SHGetPropertyStoreForWindow(nint window, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
	[DllImport("ole32.dll")]
	private static extern int PropVariantClear(ref PropVariant value);
	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint window, uint flags);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint window, StringBuilder name, int capacity);
	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
	[DllImport("kernel32.dll")]
	private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint length);
}
