// Playhub Steam Session. Distributed under GPL-3.0-only.
using Microsoft.Win32.SafeHandles;

// Input and presentation need an exact retained lifetime, not a particular
// Windows trust model. Each implementation validates its own trust boundary.
internal interface IRetainedSteamGameTarget
{
    uint Pid { get; }
    long Creation { get; }
    string Aumid { get; }
    SafeProcessHandle ProcessHandle { get; }
    bool HasExited { get; }
    bool IsCurrent();
}
