// Match MainWindow.PluginImagePath's preferred desktop artwork, not
// Assets/PluginCovers, which still contains generated text placeholders.
import launchCurtain from "../../Playhub/Assets/PluginImages/Launch Curtain.jpg";
import news from "../../Playhub/Assets/PluginImages/News.jpg";
import nowPlaying from "../../Playhub/Assets/PluginImages/Now Playing.jpg";
import playhubArtworks from "../../Playhub/Assets/PluginImages/Playhub Artworks.jpg";
import playhubMetadata from "../../Playhub/Assets/PluginImages/Playhub Metadata.jpg";
import playhubNotifications from "../../Playhub/Assets/PluginImages/Playhub Notifications.jpg";
import playhubSurround from "../../Playhub/Assets/PluginImages/Playhub Surround.jpg";
import protonVpn from "../../Playhub/Assets/PluginImages/Proton VPN.jpg";
import quickSettings from "../../Playhub/Assets/PluginImages/Quick Settings.jpg";
import shortcuts from "../assets/plugin-covers/shortcuts.jpg";
import themeDeck from "../../Playhub/Assets/PluginImages/ThemeDeck.jpg";
import trailerHero from "../../Playhub/Assets/PluginImages/TrailerHero.jpg";
import weather from "../../Playhub/Assets/PluginImages/Weather.jpg";
import { isSafeHttpsUrl, isSafeRepository } from "./pluginStoreCatalog";
import deckyStoreBadge from "../assets/source-badges/decky-store.png";
import playhubBadge from "../assets/source-badges/playhub.png";

const PLAYHUB_COVERS: Record<string, string> = {
  "lozazamastro/launch-curtain": launchCurtain,
  "lozazamastro/news": news,
  "lozazamastro/now-playing": nowPlaying,
  "lozazamastro/playhub-artworks": playhubArtworks,
  "lozazamastro/playhub-metadata": playhubMetadata,
  "lozazamastro/playhub-notifications": playhubNotifications,
  "lozazamastro/playhub-surround": playhubSurround,
  "lozazamastro/proton-vpn": protonVpn,
  "lozazamastro/quick-settings": quickSettings,
  "lozazamastro/shortcuts": shortcuts,
  "lozazamastro/themedeck-windows": themeDeck,
  "lozazamastro/trailerhero": trailerHero,
  "lozazamastro/weather": weather,
};

export function bundledPluginCover(repository: string): string {
  const key = String(repository ?? "").trim().toLocaleLowerCase("en-US");
  return Object.prototype.hasOwnProperty.call(PLAYHUB_COVERS, key) ? PLAYHUB_COVERS[key] : "";
}

/** Try in order on image error. Bundled artwork works offline; README images
 * precede the generated GitHub preview. Never use this for installed-only rows. */
export function pluginCoverSources(repository: string, coverUrl = "", readmeImages: readonly string[] = []): string[] {
  const slug = String(repository ?? "").trim();
  if (!isSafeRepository(slug) || slug.split("/").some((part) => part === "." || part === "..")) return [];
  return Array.from(new Set([
    bundledPluginCover(slug),
    ...[coverUrl, ...readmeImages].filter((url) => isSafeHttpsUrl(url)),
    `https://opengraph.githubassets.com/playhub-store/${slug}`,
  ].filter(Boolean)));
}

export const SOURCE_BADGES = {
  playhub: playhubBadge,
  deckyStore: deckyStoreBadge,
} as const;
