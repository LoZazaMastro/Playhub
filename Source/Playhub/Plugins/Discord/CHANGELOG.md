# Changelog

## 1.0.1

- Fix the Windows connection when the system reserves the previous local port.
- Add a bounded connection screen with Retry and clearer phone sign-in guidance.
- Preserve saved accounts and reconnect the existing private client.

## 1.0.0 — Playhub Windows fork

- Add Discord to Playhub with a private, bundled Windows client and QR sign-in.
- Organize voice, conversations and settings around a persistent call strip.
- Keep the runtime isolated per Steam account and preserve saved sessions.
- Verify the client process, local debugging endpoint and Discord page before
  connecting; authenticate the private loopback channel.
- Include pinned runtime assets, original licenses and corresponding sources.
- Keep Steam status and game activity sharing opt-in; manage updates in Playhub.

Based on Steamcord 1.45.1 by Necrosiak. The original release history is retained
in `docs/UPSTREAM-CHANGELOG.md`.
