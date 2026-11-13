using System;
using System.Globalization;

namespace WSGM.PackagedLaunch;

/// <summary>Which Steam objects the overlay broker will open on a game's behalf.</summary>
/// <remarks>
///     <para>
///         This is the whole security boundary of the AppContainer route. The broker runs as the
///         desktop user and duplicates handles into a low-integrity game, so what it is willing to
///         open is the only thing standing between "the overlay works" and "a sandboxed process can
///         ask a privileged helper for any named object it likes".
///     </para>
///     <para>
///         So it is a pure predicate with its own tests, not a condition inside the worker. Every
///         admitted name is scoped to this game's process id or this session's Steam game id, and
///         the list is exactly the objects observed in the recorded trials. A name that is merely
///         Steam-shaped is refused.
///     </para>
/// </remarks>
/// <param name="processId">The game process the broker is answering for.</param>
/// <param name="gameId">The Steam game id this session was launched with.</param>
public sealed class OverlayObjectAllowList(int processId, string gameId)
{
    /// <summary>The suffix Steam's renderer appends to every brokered stream object.</summary>
    private const string WrapperSuffix = "-IPCWrapper";

    /// <summary>The four objects that make up one of Steam's shared streams.</summary>
    private static readonly string[] StreamParts = ["_mem", "_mutex", "_written", "_avail"];

    /// <summary>Whether the broker may open this object on the game's behalf.</summary>
    /// <param name="name">The object name the game asked for.</param>
    /// <returns>True only for an object this session is known to need.</returns>
    public bool Admits(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Steam's CEF paint event. Getting exactly this one wrong is what produced "Failed creating
        // CEF paint event: 5" in the renderer's log and an overlay that could not survive Alt-Tab.
        if (name == "SteamWebHelper_GPUProcRenderEvent")
        {
            return true;
        }

        if (!name.EndsWith(WrapperSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var plain = name[..^WrapperSuffix.Length];
        if (plain == $"SteamOverlayRunning_{gameId}")
        {
            return true;
        }

        var pid = processId.ToString(CultureInfo.InvariantCulture);
        string[] streams =
        [
            "SteamXInput",
            "GameOverlayRender_PIDStream",
            "GameOverlayRender_DetourErrorStream",
            $"GameOverlay_InputEventStream_{pid}",
            $"GameOverlay_ScreenshotStream_{pid}",
            $"GameOverlayRender_PaintCmdStream_{pid}",
            $"SteamGameStream_{pid}"
        ];

        foreach (var stream in streams)
        {
            foreach (var part in StreamParts)
            {
                if (plain == stream + part)
                {
                    return true;
                }
            }
        }

        return plain == $"GameOverlay_VGUIPaintingCompleted_{pid}"
               || plain == $"GameOverlay_InGameRenderingCompleted_{pid}"
               || plain == $"GameOverlay_SerializedWorkQueued_{pid}"
               || plain == $"GameOverlay_GameExitingEvent_{pid}";
    }
}
