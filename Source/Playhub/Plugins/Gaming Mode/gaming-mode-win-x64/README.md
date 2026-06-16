# Playhub Gaming Mode

Playhub Gaming Mode is a console mode for a Windows living-room
PC. It keeps the current Windows user account and switches between two shells:

- Desktop Mode: your normal Windows PC installation with no changes.
- Gaming Mode: a controller-first Steam Big Picture session with Sunshine and
  Decky Loader prepared before Steam starts (so you can also turn on your PC and stream it wherever you want with Moonlight).

The goal is a cleaner couch gaming experience while keeping the Windows services
needed for gaming, streaming, audio, display, and input.

## Features

- a GUI installer and a Companion app.
- Steam Big Picture startup with Decky Loader launched first to avoid loading issues.
- Sunshine, Apollo, and Vibepollo startup and service preparation for streaming.
- Optional custom background apps for tools such as MSI Afterburner, AutoActions,
  HDR utilities, fan tools, overlays, or controller helpers.
- Desktop-like Decky plugin helper environment for bundled tools such as
  `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, `curl.exe`, `wget.exe`, and
  Launch Curtain PowerShell overlays.
- Input compatibility preparation for HID, Bluetooth, GameInput, Xbox GIP,
  Steam Input, DirectInput, and `dinput8.dll` wrappers.
- Idle mouse cursor hiding in Gaming Mode.
- Borderless fullscreen handling for visible game/app windows in Gaming Mode.
- Launch Curtain priority handling: while its black cover or overlay is visible,
  Gaming Mode keeps it above launcher/splash windows and pauses its own
  borderless window handling.
- Black shell splash screen with a centered logo while Gaming Mode starts after
  Windows sign-in. The splash fades only after Steam Big Picture is detected as
  fullscreen.
- Automatic cleanup of orphaned Decky Loader PyInstaller child processes.
- A Decky companion plugin to do everything with your gamepad.
- Hold the Shift key during sign-in to force Desktop Mode.

## Requirements

- Windows 11.
- Steam.
- Sunshine, Apollo, or Vibepollo, if Moonlight streaming is used.
- Decky Loader for Windows, if the Decky plugin is used.
- PowerShell 5 or newer.

## Install

Download `gaming-mode-win-x64.zip` from the latest release, extract it, then run Setup.exe

The installer configures the companion app, local agent, shortcuts, startup modes, and required service preparation.

If you use the Decky companion plugin, install `gaming-mode-decky.zip` manually
through Decky Loader before using the plugin controls. 

## How to install the plugin in DeckyLoader: 
- Open Steam Big Picture 
- Press CTRL + 2 / Home + B / PS + Circle
- Select the electric plug icon on the bottom
- Select the Setting icon on the top right of the menu
- In General, scroll down and enable "Developer Mode"
- Now in "Developer" select "Install from a ZIP file"
- Search for the file `gaming-mode-decky.zip` in your PC and click Install 

## Usage

Open `Gaming Mode` from the desktop shortcut or Start Menu.

Here you can:
- Switch to Gaming Mode.
- Switch to Desktop Mode.
- Choose the default startup mode.

Switching modes saves the destination shell and signs out. On the next sign-in,
Windows will start the selected shell, that's it.

Gaming Mode startup order:

1. Sunshine/Apollo/Vibepollo compatibility services.
2. Input compatibility services.
3. Decky plugin helper compatibility.
4. Custom background apps.
5. Sunshine/Apollo/Vibepollo.
6. Decky Loader from `%USERPROFILE%\homebrew\services\PluginLoader_noconsole.exe`.
7. Steam Big Picture.

## Safety

Gaming Mode is designed to be reversible:

- Hold `Shift` during sign-in to force Desktop Mode.
- Desktop Mode restores Explorer as the normal shell before sign-out.
- Explorer is restored automatically if Steam cannot start.
- A watchdog restores Desktop Mode if Steam disappears while Explorer is hidden.

Core Windows display, audio, networking, GPU, input, anti-cheat, and driver
stacks are not disabled for obvious reason.

## Configuration

Configuration file:

```text
%APPDATA%\GamingMode\config.json
```

Useful fields:

```json
{
  "defaultMode": "Desktop",
  "nextBootMode": null,
  "gaming": {
    "steamPath": null,
    "steamArguments": "-gamepadui",
    "deckyPath": null,
    "sunshinePath": null,
    "deckyRequired": true,
    "sunshineRequired": true,
    "delaySteamAfterDeckyMs": 1500,
    "closeExplorerInGamingMode": true,
    "allowExplorerCloseInGamingMode": true,
    "restoreExplorerOnDesktop": true,
    "restoreStartupAppsOnDesktop": false,
    "openSteamDesktopOnInteractiveDesktopMode": false,
    "ensureInputCompatibilityInGamingMode": true,
    "ensureSunshineCompatibilityInGamingMode": true,
    "autoHideMouseCursorInGamingMode": true,
    "autoHideMouseCursorAfterMs": 2200,
    "borderlessFullscreenWindowsInGamingMode": true,
    "customStartupApps": [
      {
        "name": "MSI Afterburner",
        "path": "C:\\Program Files (x86)\\MSI Afterburner\\MSIAfterburner.exe",
        "arguments": "",
        "workingDirectory": null,
        "processName": "MSIAfterburner",
        "enabled": true,
        "startMinimized": true,
        "delayAfterStartMs": 0
      }
    ],
    "splash": {
      "enabled": true,
      "logoPath": null,
      "minVisibleMs": 1200,
      "maxVisibleMs": 120000
    },
    "manageAudio": false
  },
  "safety": {
    "apiPort": 47991,
    "allowRemoteApi": false,
    "restartWithoutPrompt": true
  }
}
```

Leave paths as `null` to use automatic discovery. Set explicit paths only when
Steam, Decky Loader, or Sunshine are installed in custom locations.

`customStartupApps` is optional. Add one entry for each app that must run in
Gaming Mode before Steam starts. `processName` prevents duplicate launches.

The splash logo defaults to the bundled Playhub logo. Use the companion app
startup logo selector to choose Playhub, ASUS, Lenovo, MSI, PlayStation, ROG,
Steam Deck, SteamOS, or Xbox.

## Decky Plugin

The Decky plugin talks to the local Windows agent:

```text
http://127.0.0.1:47991
```

Release asset:

```text
gaming-mode-decky.zip
```

Manual development install helper:

```powershell
.\scripts\install-decky-plugin.ps1 -DeckyPluginsDir "C:\path\to\decky\plugins"
```

## Build From Source

```powershell
.\scripts\build.ps1
```

Generated packages:

```text
artifacts\gaming-mode-win-x64.zip
artifacts\gaming-mode-decky.zip
```

Local install from source:

```powershell
.\scripts\install.ps1
```

## Uninstall

Open Setup.exe, select the language, skip the warning and select Uninstall (if it gives you an error just select Uninstall again and it will work)

Run:

```text
Uninstall Gaming Mode.cmd
```

Or:

```powershell
.\scripts\uninstall.ps1
```

Uninstall restores the standard Explorer shell, removes shortcuts, and removes
installed files under `%LOCALAPPDATA%\GamingMode`.
