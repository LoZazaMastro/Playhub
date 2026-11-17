// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Collections;
using System.Runtime.InteropServices;

namespace Playhub.GameSession;

internal sealed class StoreLaunchEnvironment : IDisposable
{
    internal IntPtr Pointer { get; private set; }
    private readonly int bytes;
    private StoreLaunchEnvironment(string? block)
    {
        if (block is null) return;
        bytes = checked(block.Length * 2);
        Pointer = Marshal.StringToHGlobalUni(block);
    }
    internal static StoreLaunchEnvironment Create(IReadOnlyDictionary<string, string>? overrides) =>
        new(overrides is null || overrides.Count == 0 ? null : Compose(overrides,
            Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase)));
    internal static string Compose(IReadOnlyDictionary<string, string> overrides, IReadOnlyDictionary<string, string> inherited)
    {
        if (overrides.Count > 128) throw new InvalidDataException("Invalid store launch environment.");
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in inherited) values[key] = value;
        foreach (var (key, value) in overrides)
        {
            if (key.Length is < 1 or > 256 || key.Contains('=') || key.Any(char.IsControl) || value is null || value.Contains('\0') || value.Length > 32767)
                throw new InvalidDataException("Invalid store launch environment.");
            // Preserve Steam's own identity, overlay and controller session variables.
            if (!key.StartsWith("Steam", StringComparison.OrdinalIgnoreCase)) values[key] = value;
        }
        return string.Join('\0', values.Select(e => e.Key + "=" + e.Value)) + "\0\0";
    }
    public void Dispose()
    {
        if (Pointer == IntPtr.Zero) return;
        Marshal.Copy(new byte[bytes], 0, Pointer, bytes);
        Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero;
    }
}
