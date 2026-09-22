using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

public static class BackgroundProcessPolicy
{
    public static bool IsHiddenPowerShell(string path, string? arguments)
    {
        string executable = Path.GetFileName(path);
        return (executable.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                executable.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) &&
            Regex.IsMatch(arguments ?? "", @"(?:^|\s)-WindowStyle\s+Hidden(?:\s|$)", RegexOptions.IgnoreCase);
    }
}
