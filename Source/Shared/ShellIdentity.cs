using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Playhub.Shared;

internal static class ShellIdentity
{
    internal const string AppId = "LoZazaMastro.Playhub";
    private static readonly uint[] WindowKeys = [2, 4, 3, 5];

    internal static void SetMainProcess(Action<string>? log = null)
    {
        try { Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId)); }
        catch (Exception error) { log?.Invoke("Shell process identity: " + error.Message); }
    }

    internal static IDisposable? SetMainWindow(nint window, string executable, string icon, Action<string>? log = null)
    {
        IPropertyStore? store = null;
        try
        {
            if (window == 0 || !ValidMainFiles(executable, icon)) return null;
            Guid iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, in iid, out store));
            ApplyWindow((id, value) => Set(store!, id, value), executable, icon);
            return new WindowRegistration(window, log);
        }
        catch (Exception error)
        {
            if (store is not null) foreach (uint id in WindowKeys) { try { Set(store, id, null); } catch { } }
            log?.Invoke("Shell window identity: " + error.Message);
            return null;
        }
        finally { if (store is not null) Marshal.ReleaseComObject(store); }
    }

    internal static void ApplyWindow(Action<uint, string?> write, string executable, string icon)
    {
        // The complete relaunch tuple precedes the explicit ID; Shell observes SetValue immediately.
        write(2, "\"" + Path.GetFullPath(executable) + "\"");
        write(4, "Playhub");
        write(3, Path.GetFullPath(icon) + ",0");
        write(5, AppId);
    }

    internal static bool IsMainTarget(string? target, string expected)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(target) && Path.IsPathFullyQualified(target)
                && Path.IsPathFullyQualified(expected) && Path.GetFileName(expected).Equals("Playhub.exe", StringComparison.OrdinalIgnoreCase)
                && Path.GetFullPath(target).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static bool UpdateOwnedShortcut(string path, string executable, string icon, Action<string>? log = null)
    {
        object? link = null;
        try
        {
            if (!File.Exists(path) || !ValidMainFiles(executable, icon)
                || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
            var file = (IPersistFile)link;
            file.Load(Path.GetFullPath(path), 2);
            var shell = (IShellLink)link;
            var target = new StringBuilder(32768);
            Marshal.ThrowExceptionForHR(shell.GetPath(target, target.Capacity, 0, 4));
            if (!IsMainTarget(Environment.ExpandEnvironmentVariables(target.ToString()), executable)) return false;
            // Loading and saving the same ShellLink preserves arguments, working directory,
            // hotkey, display options and unrelated property-store values.
            Marshal.ThrowExceptionForHR(shell.SetIconLocation(Path.GetFullPath(icon), 0));
            Set((IPropertyStore)link, 5, AppId);
            file.Save(Path.GetFullPath(path), true);
            return true;
        }
        catch (Exception error) { log?.Invoke("Shell shortcut identity: " + error.Message); return false; }
        finally { if (link is not null) Marshal.ReleaseComObject(link); }
    }

    private static bool ValidMainFiles(string executable, string icon) => IsMainTarget(executable, executable)
        && File.Exists(executable) && Path.IsPathFullyQualified(icon) && File.Exists(icon);

    private static void Set(IPropertyStore store, uint id, string? text)
    {
        PropertyKey key = new(id);
        PropVariant value = default;
        try
        {
            if (text is not null) { value.Type = 31; value.Pointer = Marshal.StringToCoTaskMemUni(text); }
            Marshal.ThrowExceptionForHR(store.SetValue(in key, in value));
        }
        finally { PropVariantClear(ref value); }
    }

    private sealed class WindowRegistration(nint window, Action<string>? log) : IDisposable
    {
        private nint _window = window;
        public void Dispose()
        {
            nint current = System.Threading.Interlocked.Exchange(ref _window, 0);
            if (current == 0) return;
            IPropertyStore? store = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(current, in iid, out store));
                if (store is null) return;
                // Window properties must be cleared before HWND destruction (SHGetPropertyStoreForWindow contract).
                foreach (uint id in WindowKeys) { try { Set(store, id, null); } catch (Exception error) { log?.Invoke("Shell identity cleanup: " + error.Message); } }
            }
            catch (Exception error) { log?.Invoke("Shell identity cleanup: " + error.Message); }
            finally { if (store is not null) Marshal.ReleaseComObject(store); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(uint id)
    {
        public readonly Guid Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        public readonly uint Id = id;
    }
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
        [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, nint data, uint flags);
        [PreserveSig] int GetIDList(out nint value);
        [PreserveSig] int SetIDList(nint value);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetHotkey(out short value);
        [PreserveSig] int SetHotkey(short value);
        [PreserveSig] int GetShowCmd(out int value);
        [PreserveSig] int SetShowCmd(int value);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(nint window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint window, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
