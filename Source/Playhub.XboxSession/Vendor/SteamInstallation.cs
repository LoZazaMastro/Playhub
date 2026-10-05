using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WSGM.PackagedLaunch;

/// <summary>Where Steam keeps the components this loads into a game, and the session it launched us with.</summary>
/// <remarks>
///     Everything here is read from the running Steam's own installation. Nothing is bundled: the
///     overlay a game gets is exactly the one the user's Steam ships, which is also why a Steam
///     update can invalidate what worked yesterday.
/// </remarks>
internal static class SteamInstallation
{
    /// <summary>Steam's install directory, from the path Steam records for itself.</summary>
    internal static string Root
    {
        get
        {
            try
            {
                var recorded = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null)
                    as string;
                return string.IsNullOrEmpty(recorded)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")
                    : Path.GetFullPath(recorded.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Steam's 64-bit overlay renderer.</summary>
    internal static string OverlayRenderer => Path.Combine(Root, "GameOverlayRenderer64.dll");

    /// <summary>The steamclient stack in load order: its two dependencies, then itself.</summary>
    /// <remarks>
    ///     Order and full paths both matter. <c>LoadLibraryW</c> searches the target process's own
    ///     directory, not the loaded DLL's, so <c>steamclient64</c> cannot find <c>tier0_s64</c> and
    ///     <c>vstdlib_s64</c> beside itself inside a game that lives somewhere else. Mapping the
    ///     dependencies first lets the loader satisfy the imports by name.
    /// </remarks>
    internal static IReadOnlyList<string> ClientStack =>
    [
        Path.Combine(Root, "tier0_s64.dll"),
        Path.Combine(Root, "vstdlib_s64.dll"),
        Path.Combine(Root, "steamclient64.dll")
    ];

    /// <summary>
    ///     The Steam launch variables this wrapper was started with, ready to hand to a game Windows
    ///     started with none of them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Steam's renderer reads the session identity from its environment when it loads, and a
    ///         package activation broker gives the game a clean environment, so the values have to be
    ///         carried across deliberately.
    ///     </para>
    ///     <para>
    ///         <c>SDL_*</c> is deliberately excluded. Steam sets
    ///         <c>SDL_GAMECONTROLLER_IGNORE_DEVICES</c> to hide its own virtual controllers from SDL
    ///         while Steam Input supplies them; handing that to the game hides controllers from the
    ///         game instead. The same exclusion is why every controlled child of <c>WSGM.Launch</c>
    ///         strips it.
    ///     </para>
    /// </remarks>
    internal static IReadOnlyList<string> SessionVariables()
    {
        List<string> carried = [];
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Key is not string name || variable.Value is not string value)
            {
                continue;
            }

            if (name.StartsWith("Steam", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ENABLE_VK_LAYER_VALVE_steam_overlay_1", StringComparison.OrdinalIgnoreCase))
            {
                carried.Add($"{name}={value}");
            }
        }

        carried.Sort(StringComparer.OrdinalIgnoreCase);
        return carried;
    }

    /// <summary>Whether every component this route needs is present.</summary>
    /// <param name="missing">The first component that is absent, when one is.</param>
    internal static bool ComponentsPresent(out string? missing)
    {
        missing = null;
        if (Root.Length == 0)
        {
            missing = "Steam's installation directory";
            return false;
        }

        List<string> required = [.. ClientStack, OverlayRenderer];
        foreach (var component in required)
        {
            if (!File.Exists(component))
            {
                missing = component;
                return false;
            }
        }

        return true;
    }
}
