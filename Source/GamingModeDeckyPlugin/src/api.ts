// PONTE VERSO L'AGENTE.
//
// L'interfaccia di Steam non puo' leggere il disco, enumerare finestre o
// interrogare i contatori di sistema. L'agente si', ed espone tutto su
// 127.0.0.1. Qui c'e' un solo posto dove passano le chiamate, tipizzate, con i
// tempi di attesa e la gestione degli errori.
//
// Nota: il plugin Gaming Mode gia' parlava con l'agente in questo modo. Non si
// introduce niente di nuovo, si allarga quello che c'era.

export const API_BASE = "http://127.0.0.1:47991";

// L'helper che salva il primo piano quando l'overlay di Steam si apre sopra un
// gioco. Vive fuori da Steam perche' togliere e rimettere il "sempre in primo
// piano" a una finestra non e' cosa che si possa fare da qui dentro.
export const FOCUS_HELPER_BASE = "http://127.0.0.1:47992";

async function focusHelper(path: "steam" | "game" | "release"): Promise<boolean> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), 900);
  try {
    const response = await fetch(`${FOCUS_HELPER_BASE}/focus/${path}`, {
      method: "POST",
      signal: controller.signal,
    });
    return response.ok;
  } catch {
    return false;
  } finally {
    window.clearTimeout(timer);
  }
}

export function requestDashboardSteamFocus() { return focusHelper("steam"); }
export function restoreDashboardSourceFocus() { return focusHelper("game"); }
export function releaseDashboardFocus() { return focusHelper("release"); }

// UNA RIGA NEL LOG DELL'AGENTE.
//
// Meta' di questa storia vive dentro Steam, dove la console non la legge
// nessuno. Scrivendo di qua, tutto finisce nello stesso file in ordine di
// tempo, e un guasto si legge invece di supporlo.
export function logToAgent(message: string) {
  try {
    fetch(`${API_BASE}/dash/log`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message }),
    }).catch(() => {});
  } catch {
    // Il log non deve mai essere il motivo per cui qualcosa non funziona.
  }
}

export interface WindowEntry {
  handle: string;
  processId: number;
  title: string;
  processName: string;
  minimized: boolean;
  foreground: boolean;
  primary: boolean;
  bannerPath: string;
  heroPath: string;
  iconBase64: string;
}

export interface DashboardEnvironment {
  language: string;
  logoPath: string;
  quickSettingsInstalled: boolean;
  enabled: boolean;
  mode: string;
}

export interface QuickSettingsSnapshot {
  available: boolean;
  volume: number;
  muted: boolean;
  brightnessAvailable: boolean;
  brightness: number;
  bluetoothAvailable: boolean;
  bluetoothEnabled: boolean;
  wifiAvailable: boolean;
  wifiEnabled: boolean;
}

export interface BluetoothDevice {
  id: string;
  name: string;
  paired: boolean;
  connected: boolean;
  canPair: boolean;
  signalStrength?: number | null;
}

export interface ShortcutEntry {
  id: string;
  name: string;
  kind: string;
  target: string;
  iconBase64: string;
}

export interface DiskEntry {
  name: string;
  bytesPerSecond: number;
  // "ssd", "hdd" oppure "" quando il dispositivo non risponde.
  kind: string;
}

export interface UsageReport {
  cpuPercent: number;
  gpuPercent: number;
  gpuAvailable: boolean;
  memoryPercent: number;
  memoryUsedMb: number;
  memoryTotalMb: number;
  networkBytesPerSecond: number;
  disks: DiskEntry[];
}

export interface DashboardSettings {
  keyboardShortcutEnabled: boolean;
  hotkey: string;
  defaultMode: string;
}

export interface ProcessEntry {
  id: number;
  name: string;
  cpuPercent: number;
  memoryMb: number;
  diskBytesPerSecond: number;
}

export interface ProgramEntry {
  name: string;
  target: string;
  kind: string;
  iconBase64: string;
}

export interface LearnState {
  // idle | waiting | done | cancelled | timeout | failed
  state: string;
  combo: string;
}

// Ogni chiamata ha un tetto di attesa: se l'agente non c'e' o e' occupato, la
// pagina non deve restare appesa. Meglio un elenco vuoto che una schermata
// bloccata - e' esattamente l'errore che ha bloccato la vecchia Dashboard per
// quattordici secondi.
async function request<T>(path: string, init?: RequestInit, timeoutMs = 4000): Promise<T | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(`${API_BASE}${path}`, { ...init, signal: controller.signal });
    if (!response.ok) return null;
    return (await response.json()) as T;
  } catch (error) {
    console.warn(`Playhub Dashboard: ${path} non ha risposto`, error);
    return null;
  } finally {
    clearTimeout(timer);
  }
}

function post<T>(path: string, body?: Record<string, unknown>): Promise<T | null> {
  return request<T>(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  });
}

// ---------- DWM mirror lifecycle ----------

export function prepareOverlay(why: string) {
  return post<{ ok: boolean }>("/dash/overlay/prepare", { why });
}

export function showOverlay() {
  return post<{ ok: boolean }>("/dash/overlay/show");
}

export function hideOverlay() {
  return post<{ ok: boolean }>("/dash/overlay/hide");
}

export function heartbeatOverlay() {
  return post<{ open: boolean }>("/dash/overlay/heartbeat");
}

export interface OverlayPreviewPlacement {
  handle: string;
  x: number;
  y: number;
  width: number;
  height: number;
  radius: number;
}

export function setOverlayPreviews(viewportWidth: number, viewportHeight: number, items: OverlayPreviewPlacement[]) {
  return post<{ ok: boolean; count: number }>("/dash/overlay/previews", { viewportWidth, viewportHeight, items });
}

export async function switchOverlayWindow(handle: string) {
  await releaseDashboardFocus();
  let result: { ok: boolean; open: boolean } | null = null;
  // NavigateBack briefly gives Steam the foreground again. Retry after that
  // transition instead of leaving the user on a motionless Steam frame.
  for (const delay of [90, 220, 420]) {
    await new Promise((resolve) => window.setTimeout(resolve, delay));
    result = await post<{ ok: boolean; open: boolean }>("/dash/overlay/switch", { handle });
    if (result?.ok) break;
  }
  return result;
}

export async function launchOverlayShortcut(id: string) {
  await releaseDashboardFocus();
  return post<{ ok: boolean; open: boolean }>("/dash/overlay/launch", { id });
}

// ---------- finestre ----------

export async function listWindows(): Promise<WindowEntry[]> {
  return (await request<WindowEntry[]>("/dash/windows")) ?? [];
}

export function activateWindow(handle: string) {
  return post("/dash/windows/activate", { handle });
}

export function closeWindow(handle: string) {
  return post("/dash/windows/close", { handle });
}

export async function loadWindowPreview(handle: string, width = 720, height = 405): Promise<string> {
  if (!handle) return "";
  const result = await request<{ data: string }>(
    `/dash/windows/preview?handle=${encodeURIComponent(handle)}&width=${width}&height=${height}`,
    undefined,
    2600,
  );
  return result?.data ? `data:image/jpeg;base64,${result.data}` : "";
}

// ---------- preferite ----------

export async function listShortcuts(): Promise<ShortcutEntry[]> {
  return (await request<ShortcutEntry[]>("/dash/shortcuts")) ?? [];
}

export function launchShortcut(id: string) {
  return post("/dash/shortcuts/launch", { id });
}

export function renameShortcut(id: string, name: string) {
  return post("/dash/shortcuts/rename", { id, name });
}

export function removeShortcut(id: string) {
  return post("/dash/shortcuts/remove", { id });
}

export function addShortcut(target: string, name: string, kind: string) {
  return post("/dash/shortcuts/add", { target, name, kind });
}

// ---------- imparare la combinazione ----------

// La cattura la fa l'agente: dentro l'interfaccia di Steam i tasti vengono
// consumati prima e non arrivano mai a un ascoltatore nostro.
export function beginLearnHotkey() {
  return post("/dash/hotkey/learn");
}

export async function readLearnHotkey(): Promise<LearnState> {
  return (await request<LearnState>("/dash/hotkey/learn", undefined, 2000)) ?? { state: "idle", combo: "" };
}

// L'elenco dei programmi si legge dal menu Start: puo' metterci qualche
// secondo la prima volta, quindi ha un tetto d'attesa piu' alto degli altri.
export interface ProgramList {
  items: ProgramEntry[];
  // Vuoto quando e' andato tutto bene. Altrimenti spiega cosa non ha
  // funzionato: molto piu' utile che indovinare una causa a caso.
  note: string;
  pending: boolean;
}

export async function listPrograms(): Promise<ProgramList> {
  // L'agente si ferma a 15 secondi: qui si aspetta un po' di piu', altrimenti
  // rinunceremmo proprio mentre sta per rispondere.
  const result = await request<ProgramList>("/dash/programs", undefined, 20000);
  if (!result) {
    return { items: [], note: "L'agente di Playhub non ha risposto. Controlla che Playhub sia in esecuzione.", pending: false };
  }
  return { items: result.items ?? [], note: result.note ?? "", pending: result.pending === true };
}

// La finestra "Apri" di Windows, quella vera. Resta aperta finche' l'utente non
// sceglie, quindi non ha un tetto d'attesa breve.
export async function pickProgramFile(): Promise<string> {
  const result = await request<{ path: string }>("/dash/programs/pick", { method: "POST" }, 600000);
  return result?.path ?? "";
}

// ---------- attivita' in corso ----------

// Il consumo di processore si misura fra due chiamate: la prima torna con gli
// zeri ed e' normale. Dalla seconda in poi i numeri sono veri.
export async function listProcesses(): Promise<ProcessEntry[]> {
  return (await request<ProcessEntry[]>("/dash/processes", undefined, 8000)) ?? [];
}

export function closeProcess(id: number) {
  return post("/dash/processes/close", { id });
}

export function killProcess(id: number) {
  return post("/dash/processes/kill", { id });
}

// ---------- sistema ----------

export function readUsage(): Promise<UsageReport | null> {
  return request<UsageReport>("/dash/usage");
}

export function readEnvironment(): Promise<DashboardEnvironment | null> {
  return request<DashboardEnvironment>("/dash/environment", undefined, 2200);
}

export function readQuickSettings(): Promise<QuickSettingsSnapshot | null> {
  return request<QuickSettingsSnapshot>("/dash/quick", undefined, 2200);
}

export function setQuickVolume(level: number) {
  return post<{ ok: boolean }>("/dash/quick/volume", { level: Math.max(0, Math.min(100, Math.round(level))) });
}

export function setQuickMute(muted: boolean) {
  return post<{ ok: boolean }>("/dash/quick/mute", { muted });
}

export function setQuickBrightness(level: number) {
  return post<{ ok: boolean }>("/dash/quick/brightness", { level: Math.max(0, Math.min(100, Math.round(level))) });
}

export function setQuickBluetooth(enabled: boolean) {
  return post<{ ok: boolean }>("/dash/quick/bluetooth", { enabled });
}

export function setQuickWifi(enabled: boolean) {
  return post<{ ok: boolean }>("/dash/quick/wifi", { enabled });
}

export async function listBluetoothDevices(): Promise<BluetoothDevice[]> {
  return (await request<BluetoothDevice[]>("/dash/bluetooth", undefined, 6200)) ?? [];
}

export function setBluetoothRadio(enabled: boolean) {
  return post<{ ok: boolean }>("/dash/bluetooth/radio", { enabled });
}

export function pairBluetoothDevice(id: string) {
  return post<{ ok: boolean }>("/dash/bluetooth/pair", { id });
}

export function unpairBluetoothDevice(id: string) {
  return post<{ ok: boolean }>("/dash/bluetooth/unpair", { id });
}

export function readSettings(): Promise<DashboardSettings | null> {
  return request<DashboardSettings>("/dash/settings");
}

export function writeSettings(changes: Partial<DashboardSettings>): Promise<DashboardSettings | null> {
  return post<DashboardSettings>("/dash/settings", changes as Record<string, unknown>);
}

export function setDefaultMode(mode: "gaming" | "desktop") {
  return post(`/default/${mode}`);
}

export function restartSteam() {
  return post("/restart/steam");
}

export function restartDecky() {
  return post("/restart/decky");
}

export function switchMode(mode: "gaming" | "desktop") {
  return post(`/mode/${mode}/switch`);
}

// ---------- immagini ----------

// Un'immagine dal disco, gia' pronta per un tag <img>. Le richieste vengono
// ricordate: gli stessi banner tornano a ogni apertura e non ha senso
// rileggerli dal disco ogni volta.
const imageCache = new Map<string, string>();

export async function loadImage(path: string): Promise<string> {
  if (!path) return "";
  const cached = imageCache.get(path);
  if (cached !== undefined) return cached;
  const result = await request<{ data: string }>(`/dash/image?path=${encodeURIComponent(path)}`, undefined, 6000);
  const data = result?.data ? `data:image/png;base64,${result.data}` : "";
  imageCache.set(path, data);
  return data;
}

export function iconSource(base64: string): string {
  return base64 ? `data:image/png;base64,${base64}` : "";
}

// ---------- apertura richiesta dall'esterno ----------

// L'agente non apre niente: alza una bandierina quando viene premuta una
// scorciatoia. Qui la si raccoglie. La risposta si consuma alla lettura, quindi
// due schede in ascolto non aprono la pagina due volte.
export async function consumeOpenRequest(): Promise<boolean> {
  const result = await request<{ open: boolean }>("/dash/open-requested", undefined, 1500);
  return result?.open === true;
}
