# Changelog

## 2.6.0

Playhub 2.6.0 brings more of your PC within reach of the controller, with new monitoring tools, device-aware settings and more reliable game launching.

**Update Launch Curtain to 2.5.9 and TrailerHero to 1.7.3 as soon as possible.** These updates fix launch coverage, game focus handoff and trailer playback with the current Steam client.

### What's new

- A live performance display with FPS, frame-time graphs and system statistics. Choose Steam or Playhub as the source, adjust the detail level and screen position, and use a transparent background or your preferred text color.
- Experimental handheld profiles with model-specific controls for power, cooling, charging, lighting and vibration. Available controls depend on the device and its Windows support.
- An audio mixer for separate system and game volume, with Quick Settings that remember which sections you left open.
- Discord joins the Plugin Store as a separate Windows plugin, with its own packaged client for voice, channels and conversations. Its first release appears in What's New and the featured selection.

### Improvements

- Improved Epic Games and GOG discovery, including newer Epic installation records and GOG launch settings.
- Improved Xbox game tracking, game focus handoff and the return to Big Picture.
- Improved Decky startup and recovery when Steam closes or reopens.
- Stabilized the top-bar date during Steam redraws and stopped keyboard navigation from triggering controller vibration.

## 2.5.5

- Fixed Decky installation, updates, and repair on Windows by using verified releases from Decky Loader.
- Fixed Plugin Store flicker during background refreshes and refreshed the featured plugin selection.
- Restored artwork on plugin detail pages when the catalog has no dedicated cover.
- Simplified the Gaming Mode setup, moved startup mode controls to the top of the page, and placed the recovery steps with the controller shortcuts.
- Matched the Import Games cover format switcher to the Plugin Store controls.
- Updated the Decky tutorials to show the Steam interface directly.
- Removed the old Quick Settings and Shortcuts notice from Setup.
- Restored the Decky panel in Playhub and kept the native tab available if the integrated panel fails to load.
- Reduced background work in the System dashboard and made Steam display recovery respect active game sessions.
- Added on-demand FPS and frame-time measurement for the current game. Gaming Mode repair now restores the bundled frame-time capture tool if it is missing.

## 2.5.0

Playhub 2.5.0 brings more plugins to discover, a lighter download, and a simpler way to prepare your games for Steam.

### What's new

- **More to discover.** The Plugin Store welcomes 47 additional plugins from independent developers. The new What's New collection brings together the 100 most recently published plugins, before the Playhub collection.
- **A catalog that keeps growing.** Playhub now reads its plugin catalog from the public Playhub repository and refreshes Decky Store entries independently. New listings and catalog corrections no longer need an app update.
- **Gaming history, delivered as you read.** On this day now loads stories and images from Playhub's public repository. Images download when needed instead of being packed into every installer.

### Improvements

- The installer is now around 562 MB, down from more than 1 GB in 2.1.0.
- Plugin publication dates now reflect the original Decky Store listing or first GitHub release. Last Updated uses the latest upstream release, and both date views run from newest to oldest.
- External plugin descriptions keep the developers' original wording and language.
- Plugin pages stay in place while the catalog refreshes in the background, without repeatedly clearing and rebuilding their content.
- Import Games now focuses on importing games and choosing artwork, including Perfect Hero and Perfect Banner. Configure Metadata, ThemeDeck, TrailerHero, and Launch Curtain directly in Steam Big Picture.
- Removed the unfinished Emulation and Recomp sections. LumaDeck has also been removed from the plugin catalog.

### Fixes

- Fixed shadows missing from Perfect Hero and Perfect Banner previews.
- Improved Xbox and Microsoft Store game discovery when app details are missing or cannot be read.
- Improved Decky's default release selection and recovery after interrupted startup.
- Improved Decky process cleanup during Windows shutdown, including child processes that could keep temporary files open.
- Improved recovery to Desktop Mode when Steam exits or Gaming Mode startup is interrupted.
- Fixed English setup showing Italian text on the game-library introduction page.

### Install or update

Download `Playhub-Setup.exe` from this release and run it. Existing installations are updated while keeping your settings.

## 2.1.0

- Reduced Gaming Mode background work and improved optional installation and removal.
- Fixed Xbox game launching, controller Game Bar access and return to Big Picture.
- Improved game focus and session tracking for Epic, GOG and local games.
- Fixed dashboard connection recovery and closing with Back or Escape.
- Improved Decky installation, repair guidance and plugin recovery.
- Improved game artwork previews, title matching and integration editors.
- Updated bundled plugins and corrected interface translations.

Emulation is reserved for a future release and is not included in this installer.
