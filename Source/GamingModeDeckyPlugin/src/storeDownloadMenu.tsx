// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import { DFL, SP_REACT as React, toaster } from "./decky";
import {storeDigitalDisplayTitle} from "./storeDigitalPresentation";
import { readStoreDownloads, subscribeStoreLibrary, storeJobAction, type StoreJob } from "./storeLibraryRuntime";
import { storeLibraryLocale } from "./storeLibraryLocale";
const { PanelSection, PanelSectionRow, DropdownItem, ConfirmModal, showModal } = DFL as any;
const danger = `.ph-store-download-cancel:is(:hover,:focus-visible,:focus-within,.gpfocus,.gpfocuswithin,[aria-selected=true]) {
background:#c42b1c!important;color:#fff!important}
.ph-store-download-cancel:is(:hover,:focus-visible,:focus-within,.gpfocus,.gpfocuswithin,[aria-selected=true]) * {color:inherit!important}`;
const pendingStates = new Set(["queued", "blockedSteam", "starting", "downloading", "pausing", "pausedByUser", "pausedBySteam", "installing", "verifying", "canceling", "interrupted", "externalHandoff"]);
type JobAction = "pause" | "resume" | "cancel" | "downloadNow";
const finiteCount = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value) && value >= 0;
function transferBytes(value: number, locale: string) {
  const units = ["B", "KB", "MB", "GB", "TB"], unit = Math.min(4, Math.floor(Math.log2(Math.max(1, value)) / 10));
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: unit > 1 ? 1 : 0 }).format(value / 1024 ** unit)} ${units[unit]}`;
}
export function StoreDownloadProgress({ job, locale }: { job: StoreJob; locale: string }) {
  const total = finiteCount(job.totalBytes) && job.totalBytes > 0 ? job.totalBytes : undefined;
  const downloaded = finiteCount(job.downloadedBytes) ? job.downloadedBytes : undefined;
  const waiting = ["queued", "blockedSteam"].includes(job.state);
  const measured = job.state === "completed" ? 100 : finiteCount(job.progressPercent) ? job.progressPercent
    : total !== undefined && downloaded !== undefined ? downloaded / total * 100 : undefined;
  const percent = waiting ? undefined : measured === undefined ? undefined : Math.max(0, Math.min(100, measured));
  const c = storeLibraryLocale(locale) as Record<string, string>;
  const pending = c[`StoreLibrary.Job.${job.state[0].toUpperCase()}${job.state.slice(1)}`] ?? c["StoreLibrary.Verifying"];
  const amount = downloaded !== undefined && (!waiting || downloaded > 0 || total !== undefined)
    ? `${transferBytes(downloaded, locale)}${total !== undefined ? ` / ${transferBytes(total, locale)}` : ""}` : pending;
  const speed = !waiting && finiteCount(job.bytesPerSecond) ? `${transferBytes(job.bytesPerSecond, locale)}/s` : undefined;
  return <div className="ph-store-download-progress" style={{ width: "100%", minWidth: 0, display: "grid", gap: 7, paddingBottom: 6 }}>
    <style>{`.ph-store-download-progress-track{height:6px;overflow:hidden;border-radius:999px;background:rgba(255,255,255,.14)}
.ph-store-download-progress-fill{height:100%;border-radius:inherit;background:#1a9fff;transition:width .25s ease}
.ph-store-download-progress-pending{width:32%;height:100%;border-radius:inherit;background:linear-gradient(90deg,transparent,rgba(102,192,244,.7),transparent);animation:ph-store-download-pending 1.8s ease-in-out infinite}
@keyframes ph-store-download-pending{from{transform:translateX(-100%)}to{transform:translateX(420%)}}
@media(prefers-reduced-motion:reduce){.ph-store-download-progress-pending{animation:none;transform:translateX(106%)}.ph-store-download-progress-fill{transition:none}}`}</style>
    <div role="progressbar" aria-label={storeDigitalDisplayTitle(job)} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}
      aria-valuetext={percent === undefined ? pending : `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(percent)}%`}
      className="ph-store-download-progress-track">
      {percent === undefined ? <div className="ph-store-download-progress-pending" /> : <div className="ph-store-download-progress-fill" style={{ width: `${percent}%` }} />}
    </div>
    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", gap: 8, fontSize: 12, lineHeight: "18px", color: "#b8bcbf", fontVariantNumeric: "tabular-nums" }}>
      <span style={{ minWidth: 0, overflowWrap: "anywhere" }}>{amount}</span>
      <span style={{ flexShrink: 0 }}>{speed ?? (percent === undefined ? "" : `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(percent)}%`)}</span>
    </div>
  </div>;
}
export function confirmStoreDownloadCancel(job: StoreJob, locale: string, action: () => void) {
  const c = storeLibraryLocale(locale) as Record<string, string>;
  showModal(<ConfirmModal bDestructiveWarning focusButton="secondary" strTitle={c["StoreLibrary.ConfirmCancelDownload"]}
    strDescription={storeDigitalDisplayTitle(job)} strOKButtonText={c["StoreLibrary.Yes"]} strCancelButtonText={c["StoreLibrary.No"]}
    onOK={action} onCancel={() => {}} />, undefined, { bNeverPopOut: true });
}
export function StoreDownloadMenu({ job, locale }: { job: StoreJob; locale: string }) {
  const c = storeLibraryLocale(locale) as Record<string, string>, [busy, setBusy] = React.useState(false);
  const run = async (action: JobAction) => {
    setBusy(true);
    try { if (!(await storeJobAction(job, action)).accepted) throw new Error("store_command_rejected"); }
    catch { toaster.toast({ title: "Playhub", body: c["StoreLibrary.CommandFailed"] }); }
    finally { setBusy(false); }
  };
  const options = [
    ...(job.canDownloadNow ? [{ data: "downloadNow", label: c["StoreLibrary.DownloadNow"] }] : []),
    ...(job.canPause ? [{ data: "pause", label: c["StoreLibrary.Pause"] }] : []),
    ...(job.canResume ? [{ data: "resume", label: c["StoreLibrary.Resume"] }] : []),
    ...(job.canRemove===true || job.canCancel ? [{ data: "cancel", strOptionClass: "ph-store-download-cancel", label: <span><style>{danger}</style>{c["StoreLibrary.Cancel"]}</span> }] : []),
  ];
  return <DropdownItem label={storeDigitalDisplayTitle(job)} description={c[`StoreLibrary.Job.${job.state[0].toUpperCase()}${job.state.slice(1)}`] ?? ""}
    layout="below" bottomSeparator="none" selectedOption={null} strDefaultLabel={c["StoreLibrary.Actions"]}
    disabled={busy || !options.length} rgOptions={options} onChange={(option: { data: JobAction }) => {
      if (!options.some(available => available.data === option.data)) return;
      if (option.data === "cancel") confirmStoreDownloadCancel(job, locale, () => void run("cancel"));
      else void run(option.data);
    }} />;
}
export function StoreDownloadsQam({ locale }: { locale: string }) {
  const jobs = React.useSyncExternalStore(subscribeStoreLibrary, readStoreDownloads).filter(job => pendingStates.has(job.state));
  const c = storeLibraryLocale(locale);
  if (!jobs.length) return null;
  return <PanelSection title={c["StoreLibrary.Downloads"]}>
    {jobs.map(job => <PanelSectionRow key={job.jobId}><div style={{ width: "100%", minWidth: 0, paddingBottom: 10 }}><StoreDownloadMenu job={job} locale={locale} /><StoreDownloadProgress job={job} locale={locale} /></div></PanelSectionRow>)}
  </PanelSection>;
}
