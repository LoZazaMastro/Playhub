using System.Text;
using System.Runtime.InteropServices;

namespace Playhub.GameSession;

internal static class Win32LaunchCommand
{
    // Environment.CommandLine may reconstruct managed argument tokens. Games
    // using GetCommandLineW need the real Windows string instead.
    internal static string ReadProcessCommandLine() => Marshal.PtrToStringUni(GetCommandLine()) ?? "";

    // Inspect only the wrapper's three prefix tokens. Everything after the
    // target is the game's own command line, not another wrapper option list.
    internal static bool TryReadRawArguments(string commandLine, string target, out string arguments)
    {
        arguments = "";
        int position = 0;
        if (ReadToken(commandLine, ref position) is not { Length: > 0 }
            || ReadToken(commandLine, ref position) != "--game"
            || !string.Equals(ReadToken(commandLine, ref position), target, StringComparison.Ordinal)) return false;
        arguments = commandLine[position..].TrimStart(' ', '\t');
        return true;
    }

    private static string? ReadToken(string command, ref int position)
    {
        while (position < command.Length && command[position] is ' ' or '\t') position++;
        if (position == command.Length) return null;
        var value = new StringBuilder();
        bool quoted = false;
        while (position < command.Length && (quoted || command[position] is not (' ' or '\t')))
        {
            int slashes = 0;
            while (position < command.Length && command[position] == '\\') { slashes++; position++; }
            if (position < command.Length && command[position] == '"')
            {
                value.Append('\\', slashes / 2);
                if ((slashes & 1) != 0) { value.Append('"'); position++; }
                else if (quoted && position + 1 < command.Length && command[position + 1] == '"')
                { value.Append('"'); position += 2; }
                else { quoted = !quoted; position++; }
            }
            else
            {
                value.Append('\\', slashes);
                if (position < command.Length && (quoted || command[position] is not (' ' or '\t')))
                    value.Append(command[position++]);
            }
        }
        return value.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    private static extern nint GetCommandLine();
}
