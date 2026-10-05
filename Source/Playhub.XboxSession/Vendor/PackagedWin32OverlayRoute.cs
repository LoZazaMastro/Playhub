using System;

namespace WSGM.PackagedLaunch;

/// <summary>What a route managed to do, and what to tell the user if it could not.</summary>
/// <param name="Succeeded">Whether the route did what it set out to do.</param>
/// <param name="Detail">One sentence for the log, naming the deciding step.</param>
/// <param name="Degraded">Whether it worked only in part, so the session must not report a clean result.</param>
public sealed record RouteOutcome(bool Succeeded, string Detail, bool Degraded = false);

/// <summary>
///     The overlay route for a full-trust packaged title: set Steam up in the launch helper that
///     activation returns, and let Steam's own child-process handoff carry it into the game.
/// </summary>
/// <remarks>
///     <para>
///         This is the shape that worked for PowerWash Simulator 2 on 2026-09-14, and the controls
///         recorded beside it are why it is built this way:
///     </para>
///     <list type="bullet">
///         <item>
///             Activation alone kept Steam's running state but never brought the renderer into the
///             game, so the shortcut ran with no overlay and the desktop input profile.
///         </item>
///         <item>
///             Launching the game executable directly made it exit and replace itself through Gaming
///             Services, outside Steam's tracking entirely.
///         </item>
///         <item>
///             Launching the helper directly got Steam's renderer into that helper, and then
///             <c>dllhost</c> started a replacement helper without it.
///         </item>
///     </list>
///     <para>
///         What worked was the one remaining order: let activation create the helper, set Steam's
///         session and components up in that helper immediately, and let Steam follow the handoff
///         itself. In the successful run the real game already had the renderer at the supervisor's
///         first observation, before anything was done to the game process.
///     </para>
///     <para>
///         So this route deliberately does nothing to the game. The spike also performed delayed
///         environment writes and loads in the game and could not say whether they mattered; leaving
///         them out is both the simpler design and less to justify to an anti-cheat.
///     </para>
/// </remarks>
internal sealed class PackagedWin32OverlayRoute(GameInjector injector)
{
    /// <summary>Sets Steam up in the launch helper activation returned.</summary>
    /// <param name="helperProcessId">The process activation returned.</param>
    /// <returns>What happened.</returns>
    internal RouteOutcome Prepare(int helperProcessId)
    {
        if (helperProcessId <= 0)
        {
            return new RouteOutcome(false, "Activation returned no process to set Steam up in.");
        }

        // Every payload this route loads - Steam's client and renderer, the bridge, the environment
        // stub - is x64. A 32-bit game cannot load any of it, so the route is refused rather than
        // written into a process it cannot work in.
        if (ProcessInspector.IsNativeX64(helperProcessId) is not true)
        {
            return new RouteOutcome(false,
                "The game is not a native 64-bit process, and the overlay components are 64-bit only, "
                + "so nothing was loaded into it.");
        }

        if (!SteamInstallation.ComponentsPresent(out var missing))
        {
            return new RouteOutcome(false,
                $"Steam's own components are not where Steam says they are: {missing} is missing.");
        }

        var session = SteamInstallation.SessionVariables();
        if (session.Count == 0)
        {
            // Every variable the renderer needs comes from the process Steam launched. Without them
            // it would load and attach to nothing, which looks like a working overlay that is not
            // there.
            return new RouteOutcome(false,
                "This wrapper carries no Steam session variables, so it was not started by Steam. "
                + "The overlay route needs the session Steam launches the shortcut with.");
        }

        // Environment first, always. The renderer reads the session identity when it loads, so a
        // renderer loaded before the variables are in place attaches to nothing.
        if (!injector.SetEnvironment(helperProcessId, session))
        {
            return new RouteOutcome(false,
                "Steam's session could not be carried into the launch helper, so the renderer would "
                + "have attached to nothing.");
        }

        if (!injector.LoadAll(helperProcessId, SteamInstallation.ClientStack))
        {
            return new RouteOutcome(false, "Steam's client could not be loaded into the launch helper.");
        }

        if (!injector.Load(helperProcessId, SteamInstallation.OverlayRenderer))
        {
            return new RouteOutcome(false, "Steam's overlay renderer could not be loaded into the launch helper.");
        }

        return new RouteOutcome(true,
            $"Steam's session and components are set up in process {helperProcessId}; Steam's own "
            + "handoff carries them into the game.");
    }

    /// <summary>Reports whether Steam's handoff actually reached a game process.</summary>
    /// <param name="facts">A game process the supervisor has just seen.</param>
    /// <returns>True once the renderer is observed in the game itself.</returns>
    /// <remarks>
    ///     Observation only: nothing is written to the game, and a game without the renderer is
    ///     reported rather than repaired. A loaded module elsewhere is not evidence that the process
    ///     owning the swap chain has one, which is the whole point of checking here.
    /// </remarks>
    internal static bool RendererReached(ProcessFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return GameInjector.HasModule(facts.Id, "GameOverlayRenderer64.dll");
    }
}
