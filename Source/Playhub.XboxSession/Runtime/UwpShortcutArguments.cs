using System;
using System.Collections.Generic;
using System.Text;

namespace Playhub.GameSession;

internal static class UwpShortcutArguments
{
    internal static string Build(string aumid, string executable, string previousOptions)
    {
        var tokens = Parse(previousOptions);
        string arguments;
        if (tokens.Count >= 3 && tokens[0].Value == "--uwp" &&
            string.Equals(tokens[1].Value, aumid, StringComparison.OrdinalIgnoreCase))
        {
            arguments = tokens.Count >= 4 ? tokens[3].Value + previousOptions[tokens[3].End..] : "";
        }
        else if (tokens.Count > 0 && string.Equals(tokens[0].Value, aumid, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = tokens.Count >= 2 && string.Equals(tokens[1].Value, executable, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            arguments = previousOptions[tokens[prefix - 1].End..].TrimStart();
        }
        else arguments = previousOptions;
        return "--uwp " + Quote(aumid) + " " + Quote(executable) + " " + Quote(arguments);
    }

    internal static bool Matches(string options, string aumid)
    {
        var tokens = Parse(options);
        return tokens.Count > 0 && (string.Equals(tokens[0].Value, aumid, StringComparison.OrdinalIgnoreCase) ||
            tokens.Count > 1 && tokens[0].Value == "--uwp" && string.Equals(tokens[1].Value, aumid, StringComparison.OrdinalIgnoreCase));
    }

    internal static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static List<(string Value, int End)> Parse(string command)
    {
        var tokens = new List<(string, int)>();
        var index = 0;
        while (index < command.Length)
        {
            while (index < command.Length && char.IsWhiteSpace(command[index])) index++;
            if (index == command.Length) break;
            var value = new StringBuilder();
            var quoted = false;
            while (index < command.Length && (quoted || !char.IsWhiteSpace(command[index])))
            {
                var slashes = 0;
                while (index < command.Length && command[index] == '\\') { slashes++; index++; }
                if (index < command.Length && command[index] == '"')
                {
                    value.Append('\\', slashes / 2);
                    if (slashes % 2 != 0) value.Append('"');
                    else quoted = !quoted;
                    index++;
                }
                else
                {
                    value.Append('\\', slashes);
                    if (index < command.Length && (quoted || !char.IsWhiteSpace(command[index]))) value.Append(command[index++]);
                }
            }
            tokens.Add((value.ToString(), index));
        }
        return tokens;
    }
}
