// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import { SP_REACT as React, DFL, routerHook } from "./decky";
import { createOutputPatch } from "./reactOutputPatch";
import { readStoreLibrary, readStoreDownloads, subscribeStoreLibrary, storeRequest, observeOwnedShortcut, ownedShortcutIdentity, type StoreOwnedGame, type StoreJob } from "./storeLibraryRuntime";
import { storeLibraryLocale } from "./storeLibraryLocale";
import { StoreDownloadMenu, StoreDownloadProgress } from "./storeDownloadMenu";
import { openStoreDigitalGame } from "./StoreDigitalNavigation";
const { DialogButton } = DFL as any;
const labelStore = (store: string) => ({ gog: "GOG", epic: "Epic Games", xbox: "Xbox", amazon: "Amazon Games" } as Record<string, string>)[store] ?? "";
const terminal = (job?: StoreJob) => !job || ["completed", "canceled", "failed"].includes(job.state) || job.state === "externalHandoff" && job.reasonCode !== "windows_store_managed";
let locale = () => navigator.language;
const strings = () => storeLibraryLocale(locale()) as Record<string, string>;
const text = () => {
  const c = strings(), t = (key: string) => c[`StoreLibrary.${key}`];
  return { install: t("Install"), next: t("Review"), cancel: t("Cancel"), drive: t("Drive"), folder: t("Folder"),
    available: t("Free"), required: t("Required"), confirm: t("Review"), waiting: t("Verifying"),
    error: t("PrepareFailed"), downloads: t("Downloads"), pause: t("Pause"), resume: t("Resume"), retry: t("Refresh") };
};
function JobControls({ job }: { job: StoreJob }) {
  return <div style={{ minWidth: 0, paddingBlock: 10, display: "grid", gap: 10 }}>
    <StoreDownloadProgress job={job} locale={locale()} />
    <StoreDownloadMenu job={job} locale={locale()} />
  </div>;
}
function GameAction({ appId, native }: { appId: number; native: any }) {
  const items = React.useSyncExternalStore(subscribeStoreLibrary, readStoreLibrary), row = items.get(appId);
  const identity = ownedShortcutIdentity(row);
  const apps = (globalThis as any).SteamClient?.Apps;
  const [proof, setProof] = React.useState<{ identity?: string; source?: unknown; matches: boolean }>({ matches: false });
  React.useEffect(() => {
    setProof({ identity, source: apps, matches: false });
    return row && identity ? observeOwnedShortcut(row, matches => setProof({ identity, source: apps, matches })) : undefined;
  }, [identity, apps]);
  const ui = (globalThis as any).SteamUIStore;
  const running = Number(ui?.MainRunningAppID) === appId || (Array.isArray(ui?.RunningApps) && ui.RunningApps.some((app: any) => app?.appid === appId));
  if (!row?.accountCurrent || !row.game || !identity || proof.identity !== identity || proof.source !== apps || !proof.matches || running || row.game.isInstalled && row.game.isLaunchable) return native;
  return <div style={{ minWidth: 240, maxWidth: 560, width: "100%" }}>
    {terminal(row.job) ? <DialogButton style={{ whiteSpace: "normal" }} onClick={() => openStoreDigitalGame(row.key, "install")}>{text().install} · {labelStore(row.key.store)}</DialogButton> : <JobControls job={row.job!} />}
  </div>;
}
function DownloadSection() {
  const rows = React.useSyncExternalStore(subscribeStoreLibrary, readStoreDownloads);
  const active = rows.filter(job => !terminal(job));
  if (!active.length) return null;
  return <section style={{ padding: "24px 36px", background: "rgba(255,255,255,.045)", borderRadius: 8, marginBlock: 18 }}>
    <h2 style={{ marginBlock: "0 16px" }}>{text().downloads}</h2>
    {active.map(job => <div key={job.jobId} style={{ borderTop: "1px solid rgba(255,255,255,.1)", paddingBlock: 14 }}><JobControls job={job} /></div>)}
  </section>;
}
export function installStoreLibrarySteamUi(getLocale: () => string) {
  locale = getLocale;
  let alive = true;
  const buttons = createOutputPatch([() => true, node => typeof node?.props?.bShowStreamingSelector === "boolean" && node?.props?.overview?.appid > 0],
    (native, sourceProps) => alive && sourceProps?.overview?.appid ? <GameAction appId={sourceProps.overview.appid} native={native} /> : native,
    () => {});
  const gameRoute = routerHook.addPatch("/library/app/:appid", (props: any) => ({ ...props, children: buttons.apply(props.children) }));
  const downloads = createOutputPatch([() => true], native => alive ? <><DownloadSection />{native}</> : native, () => {});
  const downloadRoute = routerHook.addPatch("/downloads", (props: any) => ({ ...props, children: downloads.apply(props.children) }));
  return () => {
    alive = false; buttons.stop(); downloads.stop();
    routerHook.removePatch("/library/app/:appid", gameRoute); routerHook.removePatch("/downloads", downloadRoute);
  };
}
