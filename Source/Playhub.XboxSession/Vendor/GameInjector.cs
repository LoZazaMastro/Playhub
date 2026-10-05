// Modified by Playhub on 2026-10-03: retained exact process identity guard before each remote operation. GPL-3.0-only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WSGM.PackagedLaunch;

/// <summary>Loads Steam's own components into a game Windows started outside Steam's tree.</summary>
/// <remarks>
///     <para>
///         Every operation here is a remote thread in another process. Two rules govern all of them.
///     </para>
///     <para>
///         <strong>An uncertain operation latches the process off.</strong> A remote thread that does
///         not return within its budget has not failed â€” it is still running, holding memory this
///         process allocated, and may complete at any moment. Freeing that memory or starting another
///         operation beside it is how a game gets corrupted rather than merely unhooked. So the first
///         timeout ends all remote work on that process for the session, and nothing is retried.
///     </para>
///     <para>
///         <strong>Nothing is ever retried after an uncertain result.</strong> This is the same rule
///         the repository applies to device writes, for the same reason: a second attempt on top of a
///         first whose outcome is unknown compounds the damage instead of repairing it.
///     </para>
/// </remarks>
internal sealed class GameInjector(PrivilegeJournal privileges, Func<int, IntPtr, bool> targetAllowed)
{
    /// <summary>How long a remote <c>LoadLibraryW</c> may take before its result is unknown.</summary>
    private const uint LoadBudgetMs = 15_000;

    /// <summary>How long a remote export call may take. Longer: it may talk to Steam's pipe.</summary>
    private const uint CallBudgetMs = 20_000;

    /// <summary>How long the small environment stub may take.</summary>
    private const uint EnvironmentBudgetMs = 10_000;

    private readonly HashSet<string> _attempted = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<int> _latched = [];

    /// <summary>Whether remote work on this process has been latched off by an uncertain result.</summary>
    /// <param name="processId">The process to ask about.</param>
    internal bool Latched(int processId)
    {
        return _latched.Contains(processId);
    }

    /// <summary>Loads each component into the game, in order, stopping at the first failure.</summary>
    /// <param name="processId">The target process.</param>
    /// <param name="components">Absolute paths, in load order.</param>
    /// <returns>Whether every component loaded.</returns>
    internal bool LoadAll(int processId, IReadOnlyList<string> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        foreach (var component in components)
        {
            if (!Load(processId, component))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Loads one component into the game.</summary>
    /// <param name="processId">The target process.</param>
    /// <param name="path">The absolute path of the component.</param>
    /// <returns>Whether it loaded.</returns>
    internal bool Load(int processId, string path)
    {
        if (Latched(processId))
        {
            return false;
        }

        if (!_attempted.Add($"{processId}|{path}"))
        {
            // Already done for this process. Loading the same image twice would only bump its
            // reference count, but the attempt would also re-run the remote thread for nothing.
            return true;
        }

        if (!File.Exists(path))
        {
            PackagedLaunchLog.Error($"Cannot load {Path.GetFileName(path)}: it is not at {path}.");
            return false;
        }

        var process = NativeMethods.OpenProcess(NativeMethods.InjectorAccess, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            privileges.Record($"loading {Path.GetFileName(path)}", "PROCESS_VM_WRITE|CREATE_THREAD",
                false, error);
            PackagedLaunchLog.Error(
                $"Cannot open process {processId} to load {Path.GetFileName(path)} (error {error}). "
                + "The obstacle is process access, not the file.");
            return false;
        }

        try
        {
            return LoadThrough(process, processId, path);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>Calls a zero-argument export inside the game.</summary>
    /// <param name="processId">The target process.</param>
    /// <param name="path">The component holding the export, already loaded.</param>
    /// <param name="export">The export's name.</param>
    /// <returns>Its return value, or null when the call could not be made or did not return.</returns>
    internal uint? Call(int processId, string path, string export)
    {
        if (Latched(processId))
        {
            return null;
        }

        var remoteBase = RemoteModuleBase(processId, Path.GetFileName(path));
        if (remoteBase == IntPtr.Zero)
        {
            PackagedLaunchLog.Error(
                $"{Path.GetFileName(path)} is not loaded in process {processId}, so {export} cannot be called.");
            return null;
        }

        // The export is found in this process and translated by relative virtual address, because
        // the same image is mapped at a different base in the target.
        var local = NativeMethods.LoadLibraryW(path);
        if (local == IntPtr.Zero)
        {
            PackagedLaunchLog.Error(
                $"Could not load {Path.GetFileName(path)} here to resolve {export} "
                + $"(error {Marshal.GetLastWin32Error()}).");
            return null;
        }

        IntPtr remoteExport;
        try
        {
            var localExport = NativeMethods.GetProcAddress(local, export);
            if (localExport == IntPtr.Zero)
            {
                PackagedLaunchLog.Error($"{Path.GetFileName(path)} has no export named {export}.");
                return null;
            }

            remoteExport = new IntPtr(remoteBase.ToInt64() + (localExport.ToInt64() - local.ToInt64()));
        }
        finally
        {
            NativeMethods.FreeLibrary(local);
        }

        var process = NativeMethods.OpenProcess(NativeMethods.InjectorAccess, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            privileges.Record($"calling {export}", "PROCESS_VM_WRITE|CREATE_THREAD", false, error);
            return null;
        }

        try
        {
            return RunRemote(process, processId, remoteExport, IntPtr.Zero, CallBudgetMs, export);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>Sets environment variables inside the game, one remote call per variable.</summary>
    /// <param name="processId">The target process.</param>
    /// <param name="variables">Variables in <c>name=value</c> form.</param>
    /// <returns>Whether every variable was set.</returns>
    /// <remarks>
    ///     Through <c>SetEnvironmentVariableW</c> rather than by replacing the process parameters'
    ///     environment block. The wholesale replacement works on a settled process and kills a
    ///     starting one about a second later, and starting is exactly when this has to happen: the
    ///     renderer reads the session identity when it loads, and it has to load before the game
    ///     builds its swap chain. Going through the API lets ntdll own the block's allocation as
    ///     usual.
    /// </remarks>
    internal bool SetEnvironment(int processId, IReadOnlyList<string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        if (variables.Count == 0 || Latched(processId))
        {
            return !Latched(processId);
        }

        var kernel32 = NativeMethods.GetModuleHandleW("kernel32.dll");
        var setter = kernel32 == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetProcAddress(kernel32, "SetEnvironmentVariableW");
        if (setter == IntPtr.Zero)
        {
            PackagedLaunchLog.Error("Could not resolve SetEnvironmentVariableW.");
            return false;
        }

        var process = NativeMethods.OpenProcess(NativeMethods.InjectorAccess, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            privileges.Record("setting the game's environment", "PROCESS_VM_WRITE|CREATE_THREAD",
                false, error);
            PackagedLaunchLog.Error(
                $"Cannot open process {processId} to carry Steam's session across (error {error}).");
            return false;
        }

        try
        {
            var applied = 0;
            foreach (var variable in variables)
            {
                var separator = variable.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (!SetOne(process, processId, setter, variable[..separator], variable[(separator + 1)..]))
                {
                    break;
                }

                applied++;
            }

            // Names and counts only. The values are the Steam session's identity and must never
            // reach the log.
            PackagedLaunchLog.Info(
                $"Carried {applied} of {variables.Count} Steam session variable(s) into process {processId}.");
            return applied == variables.Count;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private bool LoadThrough(IntPtr process, int processId, string path)
    {
        if (!targetAllowed(processId, process)) return false;
        var kernel32 = NativeMethods.GetModuleHandleW("kernel32.dll");
        var loadLibrary = kernel32 == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetProcAddress(kernel32, "LoadLibraryW");
        if (loadLibrary == IntPtr.Zero)
        {
            PackagedLaunchLog.Error("Could not resolve LoadLibraryW.");
            return false;
        }

        var bytes = Encoding.Unicode.GetBytes(path + "\0");
        var remote = NativeMethods.VirtualAllocEx(
            process, IntPtr.Zero, (UIntPtr)bytes.Length,
            NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
        if (remote == IntPtr.Zero)
        {
            PackagedLaunchLog.Error(
                $"Could not allocate in process {processId} (error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        var freed = false;
        try
        {
            if (!NativeMethods.WriteProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out _))
            {
                PackagedLaunchLog.Error(
                    $"Could not write to process {processId} (error {Marshal.GetLastWin32Error()}).");
                return false;
            }

            var result = RunRemote(
                process, processId, loadLibrary, remote, LoadBudgetMs, Path.GetFileName(path));
            if (result is null)
            {
                // Latched or refused. The allocation is deliberately not freed when the thread may
                // still be reading from it.
                freed = Latched(processId);
                return false;
            }

            // The exit code is the low 32 bits of the returned HMODULE: it proves a load happened,
            // not where. Zero means the loader refused the file outright.
            if (result == 0)
            {
                PackagedLaunchLog.Error(
                    $"The game refused to load {Path.GetFileName(path)}: unreadable to it, the wrong "
                    + "architecture, or blocked by policy.");
                return false;
            }

            PackagedLaunchLog.Info($"Loaded {Path.GetFileName(path)} into process {processId}.");
            return true;
        }
        finally
        {
            if (!freed && !Latched(processId))
            {
                NativeMethods.VirtualFreeEx(process, remote, UIntPtr.Zero, NativeMethods.MemRelease);
            }
        }
    }

    private bool SetOne(IntPtr process, int processId, IntPtr setter, string name, string value)
    {
        if (!targetAllowed(processId, process)) return false;
        var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
        var valueBytes = Encoding.Unicode.GetBytes(value + "\0");

        // Layout: [name][value][stub]. The strings come first so their addresses are known before
        // the stub that references them is assembled.
        var valueOffset = nameBytes.Length;
        var codeOffset = valueOffset + valueBytes.Length;
        var total = codeOffset + 64;

        var block = NativeMethods.VirtualAllocEx(
            process, IntPtr.Zero, (UIntPtr)total,
            NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageExecuteReadWrite);
        if (block == IntPtr.Zero)
        {
            PackagedLaunchLog.Error(
                $"Could not allocate for {name} in process {processId} (error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        var leave = false;
        try
        {
            var buffer = new byte[total];
            Array.Copy(nameBytes, 0, buffer, 0, nameBytes.Length);
            Array.Copy(valueBytes, 0, buffer, valueOffset, valueBytes.Length);

            // CreateRemoteThread passes one argument and SetEnvironmentVariableW takes two, so the
            // call goes through a stub with both pointers and the function address baked in as
            // immediates, around a standard x64 shadow-space frame.
            List<byte> code =
            [
                0x48, 0x83, 0xEC, 0x28, // sub rsp, 0x28
                0x48, 0xB9 // mov rcx, imm64
            ];
            code.AddRange(BitConverter.GetBytes(block.ToInt64()));
            code.AddRange([0x48, 0xBA]); // mov rdx, imm64
            code.AddRange(BitConverter.GetBytes(block.ToInt64() + valueOffset));
            code.AddRange([0x48, 0xB8]); // mov rax, imm64
            code.AddRange(BitConverter.GetBytes(setter.ToInt64()));
            code.AddRange([0xFF, 0xD0]); // call rax
            code.AddRange([0x48, 0x83, 0xC4, 0x28]); // add rsp, 0x28
            code.Add(0xC3); // ret
            code.CopyTo(buffer, codeOffset);

            if (!NativeMethods.WriteProcessMemory(process, block, buffer, (UIntPtr)buffer.Length, out _))
            {
                PackagedLaunchLog.Error(
                    $"Could not write {name} into process {processId} (error {Marshal.GetLastWin32Error()}).");
                return false;
            }

            var result = RunRemote(
                process, processId, new IntPtr(block.ToInt64() + codeOffset), IntPtr.Zero,
                EnvironmentBudgetMs, name);
            leave = Latched(processId);
            return result is not null and not 0;
        }
        finally
        {
            if (!leave)
            {
                NativeMethods.VirtualFreeEx(process, block, UIntPtr.Zero, NativeMethods.MemRelease);
            }
        }
    }

    /// <summary>Runs one remote thread, latching the process off if it does not return in time.</summary>
    /// <returns>Its exit code, or null when it could not be started or did not return.</returns>
    private uint? RunRemote(
        IntPtr process, int processId, IntPtr start, IntPtr parameter, uint budgetMs, string what)
    {
        if (!targetAllowed(processId, process)) return null;
        var thread = NativeMethods.CreateRemoteThread(
            process, IntPtr.Zero, UIntPtr.Zero, start, parameter, 0, IntPtr.Zero);
        if (thread == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            privileges.Record($"running {what} in the game", "CREATE_THREAD", false, error);
            PackagedLaunchLog.Error(
                $"The game refused a foreign thread for {what} (error {error}).");
            return null;
        }

        try
        {
            if (NativeMethods.WaitForSingleObject(thread, budgetMs) != 0)
            {
                _latched.Add(processId);
                PackagedLaunchLog.Error(
                    $"{what} did not return within {budgetMs / 1000}s in process {processId}. Its "
                    + "outcome is unknown, so no further work is done in that process this session "
                    + "and nothing is retried.");
                return null;
            }

            return NativeMethods.GetExitCodeThread(thread, out var exitCode) ? exitCode : null;
        }
        finally
        {
            NativeMethods.CloseHandle(thread);
        }
    }

    private static IntPtr RemoteModuleBase(int processId, string fileName)
    {
        var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            var handles = new IntPtr[1024];
            if (!NativeMethods.K32EnumProcessModulesEx(
                    process, handles, (uint)(handles.Length * IntPtr.Size), out var needed,
                    NativeMethods.ListModulesAll))
            {
                return IntPtr.Zero;
            }

            var count = Math.Min(handles.Length, (int)(needed / IntPtr.Size));
            StringBuilder buffer = new(NativeMethods.MaxPath * 2);
            for (var index = 0; index < count; index++)
            {
                buffer.Clear();
                if (NativeMethods.K32GetModuleFileNameExW(
                        process, handles[index], buffer, (uint)buffer.Capacity) == 0)
                {
                    continue;
                }

                if (Path.GetFileName(buffer.ToString()).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return handles[index];
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>Whether a module is loaded in a process, by file name.</summary>
    /// <param name="processId">The process to look in.</param>
    /// <param name="fileName">The module's file name.</param>
    internal static bool HasModule(int processId, string fileName)
    {
        return RemoteModuleBase(processId, fileName) != IntPtr.Zero;
    }
}
