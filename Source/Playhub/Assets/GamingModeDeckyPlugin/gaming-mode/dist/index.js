const manifest = {"name":"Gaming Mode"};
const API_VERSION = 2;
const internalAPIConnection = window.__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit;
if (!internalAPIConnection) {
    throw new Error('[@decky/api]: Failed to connect to the loader as as the loader API was not initialized. This is likely a bug in Decky Loader.');
}
let api;
try {
    api = internalAPIConnection.connect(API_VERSION, manifest.name);
}
catch {
    api = internalAPIConnection.connect(1, manifest.name);
    console.warn(`[@decky/api] Requested API version ${API_VERSION} but the running loader only supports version 1. Some features may not work.`);
}
if (api._version != API_VERSION) {
    console.warn(`[@decky/api] Requested API version ${API_VERSION} but the running loader only supports version ${api._version}. Some features may not work.`);
}
const routerHook = api.routerHook;
const toaster = api.toaster;
const definePlugin = (fn) => {
    return (...args) => {
        return fn(...args);
    };
};

// Ponte verso le librerie che Decky mette a disposizione a tempo di
// esecuzione. Stessa forma usata da Launch Curtain: si importa da qui, non
// direttamente dai pacchetti, cosi' il punto di aggancio e' uno solo.



const _global_SP_REACT = SP_REACT;
const _global_DFL = DFL;

var DefaultContext = {
  color: undefined,
  size: undefined,
  className: undefined,
  style: undefined,
  attr: undefined
};
var IconContext = SP_REACT.createContext && /*#__PURE__*/SP_REACT.createContext(DefaultContext);

var _excluded = ["attr", "size", "title"];
function _objectWithoutProperties(e, t) { if (null == e) return {}; var o, r, i = _objectWithoutPropertiesLoose(e, t); if (Object.getOwnPropertySymbols) { var n = Object.getOwnPropertySymbols(e); for (r = 0; r < n.length; r++) o = n[r], -1 === t.indexOf(o) && {}.propertyIsEnumerable.call(e, o) && (i[o] = e[o]); } return i; }
function _objectWithoutPropertiesLoose(r, e) { if (null == r) return {}; var t = {}; for (var n in r) if ({}.hasOwnProperty.call(r, n)) { if (-1 !== e.indexOf(n)) continue; t[n] = r[n]; } return t; }
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function ownKeys(e, r) { var t = Object.keys(e); if (Object.getOwnPropertySymbols) { var o = Object.getOwnPropertySymbols(e); r && (o = o.filter(function (r) { return Object.getOwnPropertyDescriptor(e, r).enumerable; })), t.push.apply(t, o); } return t; }
function _objectSpread(e) { for (var r = 1; r < arguments.length; r++) { var t = null != arguments[r] ? arguments[r] : {}; r % 2 ? ownKeys(Object(t), true).forEach(function (r) { _defineProperty(e, r, t[r]); }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(t)) : ownKeys(Object(t)).forEach(function (r) { Object.defineProperty(e, r, Object.getOwnPropertyDescriptor(t, r)); }); } return e; }
function _defineProperty(e, r, t) { return (r = _toPropertyKey(r)) in e ? Object.defineProperty(e, r, { value: t, enumerable: true, configurable: true, writable: true }) : e[r] = t, e; }
function _toPropertyKey(t) { var i = _toPrimitive(t, "string"); return "symbol" == typeof i ? i : i + ""; }
function _toPrimitive(t, r) { if ("object" != typeof t || !t) return t; var e = t[Symbol.toPrimitive]; if (void 0 !== e) { var i = e.call(t, r); if ("object" != typeof i) return i; throw new TypeError("@@toPrimitive must return a primitive value."); } return ("string" === r ? String : Number)(t); }
function Tree2Element(tree) {
  return tree && tree.map((node, i) => /*#__PURE__*/SP_REACT.createElement(node.tag, _objectSpread({
    key: i
  }, node.attr), Tree2Element(node.child)));
}
function GenIcon(data) {
  return props => /*#__PURE__*/SP_REACT.createElement(IconBase, _extends({
    attr: _objectSpread({}, data.attr)
  }, props), Tree2Element(data.child));
}
function IconBase(props) {
  var elem = conf => {
    var attr = props.attr,
      size = props.size,
      title = props.title,
      svgProps = _objectWithoutProperties(props, _excluded);
    var computedSize = size || conf.size || "1em";
    var className;
    if (conf.className) className = conf.className;
    if (props.className) className = (className ? className + " " : "") + props.className;
    return /*#__PURE__*/SP_REACT.createElement("svg", _extends({
      stroke: "currentColor",
      fill: "currentColor",
      strokeWidth: "0"
    }, conf.attr, attr, svgProps, {
      className: className,
      style: _objectSpread(_objectSpread({
        color: props.color || conf.color
      }, conf.style), props.style),
      height: computedSize,
      width: computedSize,
      xmlns: "http://www.w3.org/2000/svg"
    }), title && /*#__PURE__*/SP_REACT.createElement("title", null, title), props.children);
  };
  return IconContext !== undefined ? /*#__PURE__*/SP_REACT.createElement(IconContext.Consumer, null, conf => elem(conf)) : elem(DefaultContext);
}

// THIS FILE IS AUTO GENERATED
function FaGamepad (props) {
  return GenIcon({"attr":{"viewBox":"0 0 640 512"},"child":[{"tag":"path","attr":{"d":"M480.07 96H160a160 160 0 1 0 114.24 272h91.52A160 160 0 1 0 480.07 96zM248 268a12 12 0 0 1-12 12h-52v52a12 12 0 0 1-12 12h-24a12 12 0 0 1-12-12v-52H84a12 12 0 0 1-12-12v-24a12 12 0 0 1 12-12h52v-52a12 12 0 0 1 12-12h24a12 12 0 0 1 12 12v52h52a12 12 0 0 1 12 12zm216 76a40 40 0 1 1 40-40 40 40 0 0 1-40 40zm64-96a40 40 0 1 1 40-40 40 40 0 0 1-40 40z"},"child":[]}]})(props);
}

// PONTE VERSO L'AGENTE.
//
// L'interfaccia di Steam non puo' leggere il disco, enumerare finestre o
// interrogare i contatori di sistema. L'agente si', ed espone tutto su
// 127.0.0.1. Qui c'e' un solo posto dove passano le chiamate, tipizzate, con i
// tempi di attesa e la gestione degli errori.
//
// Nota: il plugin Gaming Mode gia' parlava con l'agente in questo modo. Non si
// introduce niente di nuovo, si allarga quello che c'era.
const API_BASE = "http://127.0.0.1:47991";
// L'helper che salva il primo piano quando l'overlay di Steam si apre sopra un
// gioco. Vive fuori da Steam perche' togliere e rimettere il "sempre in primo
// piano" a una finestra non e' cosa che si possa fare da qui dentro.
const FOCUS_HELPER_BASE = "http://127.0.0.1:47992";
async function focusHelper(path) {
    const controller = new AbortController();
    const timer = window.setTimeout(() => controller.abort(), 900);
    try {
        const response = await fetch(`${FOCUS_HELPER_BASE}/focus/${path}`, {
            method: "POST",
            signal: controller.signal,
        });
        return response.ok;
    }
    catch {
        return false;
    }
    finally {
        window.clearTimeout(timer);
    }
}
function requestDashboardSteamFocus() { return focusHelper("steam"); }
function captureDashboardSourceFocus() { return focusHelper("capture"); }
function restoreDashboardSourceFocus() { return focusHelper("game"); }
function releaseDashboardFocus() { return focusHelper("release"); }
// UNA RIGA NEL LOG DELL'AGENTE.
//
// Meta' di questa storia vive dentro Steam, dove la console non la legge
// nessuno. Scrivendo di qua, tutto finisce nello stesso file in ordine di
// tempo, e un guasto si legge invece di supporlo.
function logToAgent(message) {
    try {
        fetch(`${API_BASE}/dash/log`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ message }),
        }).catch(() => { });
    }
    catch {
        // Il log non deve mai essere il motivo per cui qualcosa non funziona.
    }
}
// Ogni chiamata ha un tetto di attesa: se l'agente non c'e' o e' occupato, la
// pagina non deve restare appesa. Meglio un elenco vuoto che una schermata
// bloccata - e' esattamente l'errore che ha bloccato la vecchia Dashboard per
// quattordici secondi.
async function request(path, init, timeoutMs = 4000) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const response = await fetch(`${API_BASE}${path}`, { ...init, signal: controller.signal });
        if (!response.ok)
            return null;
        return (await response.json());
    }
    catch (error) {
        console.warn(`Playhub Dashboard: ${path} non ha risposto`, error);
        return null;
    }
    finally {
        clearTimeout(timer);
    }
}
function post$1(path, body) {
    return request(path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: body ? JSON.stringify(body) : undefined,
    });
}
async function switchOverlayWindow(handle) {
    await releaseDashboardFocus();
    let result = null;
    // NavigateBack briefly gives Steam the foreground again. Retry after that
    // transition instead of leaving the user on a motionless Steam frame.
    for (const delay of [90, 220, 420]) {
        await new Promise((resolve) => window.setTimeout(resolve, delay));
        result = await post$1("/dash/overlay/switch", { handle });
        if (result?.ok)
            break;
    }
    return result;
}
async function launchOverlayShortcut(id) {
    await releaseDashboardFocus();
    return post$1("/dash/overlay/launch", { id });
}
// ---------- finestre ----------
async function listWindows() {
    return (await request("/dash/windows")) ?? [];
}
function closeWindow(handle) {
    return post$1("/dash/windows/close", { handle });
}
// ---------- preferite ----------
async function listShortcuts() {
    return (await request("/dash/shortcuts")) ?? [];
}
function renameShortcut(id, name) {
    return post$1("/dash/shortcuts/rename", { id, name });
}
function removeShortcut(id) {
    return post$1("/dash/shortcuts/remove", { id });
}
function addShortcut(target, name, kind) {
    return post$1("/dash/shortcuts/add", { target, name, kind });
}
async function listPrograms() {
    // L'agente si ferma a 15 secondi: qui si aspetta un po' di piu', altrimenti
    // rinunceremmo proprio mentre sta per rispondere.
    const result = await request("/dash/programs", undefined, 20000);
    if (!result) {
        return { items: [], note: "L'agente di Playhub non ha risposto. Controlla che Playhub sia in esecuzione.", pending: false };
    }
    return { items: result.items ?? [], note: result.note ?? "", pending: result.pending === true };
}
// ---------- attivita' in corso ----------
// Il consumo di processore si misura fra due chiamate: la prima torna con gli
// zeri ed e' normale. Dalla seconda in poi i numeri sono veri.
async function listProcesses() {
    return (await request("/dash/processes", undefined, 8000)) ?? [];
}
function closeProcess(id) {
    return post$1("/dash/processes/close", { id });
}
function killProcess(id) {
    return post$1("/dash/processes/kill", { id });
}
// ---------- sistema ----------
function readUsage() {
    return request("/dash/usage");
}
function readEnvironment() {
    return request("/dash/environment", undefined, 2200);
}
function readSettings() {
    return request("/dash/settings");
}
function writeSettings(changes) {
    return post$1("/dash/settings", changes);
}
function restartDecky() {
    return post$1("/restart/decky");
}
// ---------- immagini ----------
// Un'immagine dal disco, gia' pronta per un tag <img>. Le richieste vengono
// ricordate: gli stessi banner tornano a ogni apertura e non ha senso
// rileggerli dal disco ogni volta.
const imageCache = new Map();
async function loadImage(path) {
    if (!path)
        return "";
    const cached = imageCache.get(path);
    if (cached !== undefined)
        return cached;
    const result = await request(`/dash/image?path=${encodeURIComponent(path)}`, undefined, 6000);
    const data = result?.data ? `data:image/png;base64,${result.data}` : "";
    imageCache.set(path, data);
    return data;
}
function iconSource(base64) {
    return base64 ? `data:image/png;base64,${base64}` : "";
}
async function consumeOpenRequest() {
    const result = await request("/dash/open-requested", undefined, 1500);
    return {
        open: result?.open === true,
        focusRecovery: Number(result?.focusRecovery) || 0,
        steamForeground: result?.steamForeground === true,
    };
}

// THIS FILE IS AUTO GENERATED
function FiSearch (props) {
  return GenIcon({"attr":{"viewBox":"0 0 24 24","fill":"none","stroke":"currentColor","strokeWidth":"2","strokeLinecap":"round","strokeLinejoin":"round"},"child":[{"tag":"circle","attr":{"cx":"11","cy":"11","r":"8"},"child":[]},{"tag":"line","attr":{"x1":"21","y1":"21","x2":"16.65","y2":"16.65"},"child":[]}]})(props);
}function FiRefreshCw (props) {
  return GenIcon({"attr":{"viewBox":"0 0 24 24","fill":"none","stroke":"currentColor","strokeWidth":"2","strokeLinecap":"round","strokeLinejoin":"round"},"child":[{"tag":"polyline","attr":{"points":"23 4 23 10 17 10"},"child":[]},{"tag":"polyline","attr":{"points":"1 20 1 14 7 14"},"child":[]},{"tag":"path","attr":{"d":"M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"},"child":[]}]})(props);
}function FiPlus (props) {
  return GenIcon({"attr":{"viewBox":"0 0 24 24","fill":"none","stroke":"currentColor","strokeWidth":"2","strokeLinecap":"round","strokeLinejoin":"round"},"child":[{"tag":"line","attr":{"x1":"12","y1":"5","x2":"12","y2":"19"},"child":[]},{"tag":"line","attr":{"x1":"5","y1":"12","x2":"19","y2":"12"},"child":[]}]})(props);
}function FiMonitor (props) {
  return GenIcon({"attr":{"viewBox":"0 0 24 24","fill":"none","stroke":"currentColor","strokeWidth":"2","strokeLinecap":"round","strokeLinejoin":"round"},"child":[{"tag":"rect","attr":{"x":"2","y":"3","width":"20","height":"14","rx":"2","ry":"2"},"child":[]},{"tag":"line","attr":{"x1":"8","y1":"21","x2":"16","y2":"21"},"child":[]},{"tag":"line","attr":{"x1":"12","y1":"17","x2":"12","y2":"21"},"child":[]}]})(props);
}function FiGrid (props) {
  return GenIcon({"attr":{"viewBox":"0 0 24 24","fill":"none","stroke":"currentColor","strokeWidth":"2","strokeLinecap":"round","strokeLinejoin":"round"},"child":[{"tag":"rect","attr":{"x":"3","y":"3","width":"7","height":"7"},"child":[]},{"tag":"rect","attr":{"x":"14","y":"3","width":"7","height":"7"},"child":[]},{"tag":"rect","attr":{"x":"14","y":"14","width":"7","height":"7"},"child":[]},{"tag":"rect","attr":{"x":"3","y":"14","width":"7","height":"7"},"child":[]}]})(props);
}function FiCpu (props) {
  return GenIcon({"attr":{"viewBox":"0 0 24 24","fill":"none","stroke":"currentColor","strokeWidth":"2","strokeLinecap":"round","strokeLinejoin":"round"},"child":[{"tag":"rect","attr":{"x":"4","y":"4","width":"16","height":"16","rx":"2","ry":"2"},"child":[]},{"tag":"rect","attr":{"x":"9","y":"9","width":"6","height":"6"},"child":[]},{"tag":"line","attr":{"x1":"9","y1":"1","x2":"9","y2":"4"},"child":[]},{"tag":"line","attr":{"x1":"15","y1":"1","x2":"15","y2":"4"},"child":[]},{"tag":"line","attr":{"x1":"9","y1":"20","x2":"9","y2":"23"},"child":[]},{"tag":"line","attr":{"x1":"15","y1":"20","x2":"15","y2":"23"},"child":[]},{"tag":"line","attr":{"x1":"20","y1":"9","x2":"23","y2":"9"},"child":[]},{"tag":"line","attr":{"x1":"20","y1":"14","x2":"23","y2":"14"},"child":[]},{"tag":"line","attr":{"x1":"1","y1":"9","x2":"4","y2":"9"},"child":[]},{"tag":"line","attr":{"x1":"1","y1":"14","x2":"4","y2":"14"},"child":[]}]})(props);
}

// THIS FILE IS AUTO GENERATED
function SiSteam (props) {
  return GenIcon({"attr":{"role":"img","viewBox":"0 0 24 24"},"child":[{"tag":"path","attr":{"d":"M11.979 0C5.678 0 .511 4.86.022 11.037l6.432 2.658c.545-.371 1.203-.59 1.912-.59.063 0 .125.004.188.006l2.861-4.142V8.91c0-2.495 2.028-4.524 4.524-4.524 2.494 0 4.524 2.031 4.524 4.527s-2.03 4.525-4.524 4.525h-.105l-4.076 2.911c0 .052.004.105.004.159 0 1.875-1.515 3.396-3.39 3.396-1.635 0-3.016-1.173-3.331-2.727L.436 15.27C1.862 20.307 6.486 24 11.979 24c6.627 0 11.999-5.373 11.999-12S18.605 0 11.979 0zM7.54 18.21l-1.473-.61c.262.543.714.999 1.314 1.25 1.297.539 2.793-.076 3.332-1.375.263-.63.264-1.319.005-1.949s-.75-1.121-1.377-1.383c-.624-.26-1.29-.249-1.878-.03l1.523.63c.956.4 1.409 1.5 1.009 2.455-.397.957-1.497 1.41-2.454 1.012H7.54zm11.415-9.303c0-1.662-1.353-3.015-3.015-3.015-1.665 0-3.015 1.353-3.015 3.015 0 1.665 1.35 3.015 3.015 3.015 1.663 0 3.015-1.35 3.015-3.015zm-5.273-.005c0-1.252 1.013-2.266 2.265-2.266 1.249 0 2.266 1.014 2.266 2.266 0 1.251-1.017 2.265-2.266 2.265-1.253 0-2.265-1.014-2.265-2.265z"},"child":[]}]})(props);
}

const { useCallback, useEffect: useEffect$1, useLayoutEffect, useMemo: useMemo$1, useRef, useState: useState$1 } = _global_SP_REACT;
const { Focusable, GamepadButton, Navigation: Navigation$1, TextField } = _global_DFL;
const DASHBOARD_ROUTE = "/playhub-dashboard";
const STEAM_LOCALE_ALIASES$1 = {
    english: "en", en: "en",
    italian: "it", it: "it",
    spanish: "es", latam: "es", es: "es",
    french: "fr", fr: "fr",
    german: "de", de: "de",
    brazilian: "pt", portuguese: "pt", pt: "pt",
    ukrainian: "uk", uk: "uk",
    schinese: "zh", tchinese: "zh", zh: "zh",
    japanese: "ja", ja: "ja",
    koreana: "ko", korean: "ko", ko: "ko",
    hindi: "hi", hi: "hi",
    russian: "ru", ru: "ru",
};
const STEAM_LOCALE_TAGS = {
    en: "en-US", it: "it-IT", es: "es-ES", fr: "fr-FR", de: "de-DE", pt: "pt-BR",
    uk: "uk-UA", zh: "zh-CN", ja: "ja-JP", ko: "ko-KR", hi: "hi-IN", ru: "ru-RU",
};
function normalizeSteamLocale$1(value) {
    const candidate = String(value ?? "").trim().toLowerCase().replace(/_/g, "-");
    if (!candidate)
        return null;
    return STEAM_LOCALE_ALIASES$1[candidate] ?? STEAM_LOCALE_ALIASES$1[candidate.split("-")[0]] ?? null;
}
function detectSteamLocale$1() {
    const win = window;
    const candidates = [];
    [win.LocalizationManager, win.g_LocalizationManager, win.SteamLocalizationManager]
        .filter(Boolean)
        .forEach((manager) => {
        candidates.push(manager?.m_strLanguage, manager?.m_language, manager?.m_strCurrentLanguage, manager?.language);
        ["GetLanguage", "GetCurrentLanguage"].forEach((method) => {
            try {
                const value = manager?.[method]?.();
                if (typeof value === "string")
                    candidates.push(value);
            }
            catch { }
        });
    });
    candidates.push(win.g_strLanguage, win.g_rgAppContextData?.language, document.documentElement.lang);
    candidates.push(...Array.from(navigator.languages ?? []), navigator.language);
    for (const candidate of candidates) {
        const locale = normalizeSteamLocale$1(candidate);
        if (locale)
            return locale;
    }
    return "en";
}
async function readSteamLocale$1() {
    try {
        const value = await window.SteamClient?.Settings?.GetCurrentLanguage?.();
        const locale = normalizeSteamLocale$1(value);
        if (locale)
            return locale;
    }
    catch { }
    return detectSteamLocale$1();
}
let steamSettingsRoot = null;
function steamUses24HourClock(localeTag) {
    try {
        if (!steamSettingsRoot) {
            steamSettingsRoot = _global_DFL.findModuleExport?.((value) => typeof value?.SettingsStore?.FriendsSettings?.b24HourClock === "boolean") ?? null;
        }
        const preference = steamSettingsRoot?.SettingsStore?.FriendsSettings?.b24HourClock;
        if (typeof preference === "boolean")
            return preference;
    }
    catch { }
    return new Intl.DateTimeFormat(localeTag, { hour: "numeric" }).resolvedOptions().hour12 === false;
}
const DASHBOARD_ACTIVE_CLASS = "phDashboardActive";
const DASHBOARD_CHROME_STYLE_ID = "ph-dashboard-chrome-style";
const DASHBOARD_CHROME_SELECTORS = [
    "#header",
    '[class*="GamepadHeader"]',
    '[class*="HeaderStatus"]',
    '[class*="StatusIcons"]',
    '[class*="TopBar"]',
];
function dashboardDocuments() {
    const documents = [];
    const addDocument = (candidate) => {
        try {
            if (candidate?.documentElement && !documents.includes(candidate))
                documents.push(candidate);
        }
        catch { }
    };
    const addWindowDocument = (candidate) => {
        if (!candidate)
            return;
        try {
            addDocument(candidate.document);
        }
        catch { }
        try {
            addDocument(candidate.window?.document);
        }
        catch { }
        try {
            addDocument(candidate.m_Window?.document);
        }
        catch { }
        try {
            addDocument(candidate.m_popup?.document);
        }
        catch { }
        try {
            addDocument(candidate.m_BrowserWindow?.document);
        }
        catch { }
        try {
            addDocument(candidate.BrowserWindow?.document);
        }
        catch { }
        try {
            addDocument(candidate.GetWindow?.()?.document);
        }
        catch { }
    };
    addDocument(document);
    try {
        addDocument(window.top?.document);
    }
    catch { }
    try {
        addDocument(window.parent?.document);
    }
    catch { }
    try {
        addDocument(window.opener?.document);
    }
    catch { }
    const store = _global_DFL?.Router?.WindowStore;
    addWindowDocument(store?.GamepadUIMainWindowInstance);
    if (Array.isArray(store?.SteamUIWindows))
        store.SteamUIWindows.forEach(addWindowDocument);
    if (Array.isArray(store?.OverlayWindows))
        store.OverlayWindows.forEach(addWindowDocument);
    return documents;
}
let dashboardOverlayGameId = "";
function overlayDocument() {
    const store = _global_DFL?.Router?.WindowStore;
    const candidates = [
        ...(Array.isArray(store?.OverlayWindows) ? store.OverlayWindows : []),
        ...(Array.isArray(store?.SteamUIWindows) ? store.SteamUIWindows : []),
    ];
    for (const candidate of candidates) {
        try {
            const browserWindow = candidate?.m_BrowserWindow ?? candidate?.BrowserWindow;
            const targetDocument = browserWindow?.document;
            if (!targetDocument?.body || targetDocument === document)
                continue;
            const title = `${targetDocument.title ?? ""}`;
            const location = `${targetDocument.defaultView?.location?.href ?? ""}`;
            if (/SP Overlay|GamepadUIOverlay|overlay/i.test(`${title} ${location}`))
                return targetDocument;
        }
        catch { }
    }
    return null;
}
async function prepareDashboardOverlay() {
    try {
        const infos = await window.SteamClient?.Overlay?.GetOverlayBrowserInfo?.();
        const current = Array.isArray(infos)
            ? infos.find((info) => Number(info?.appID ?? 0) > 0 && Number(info?.unPID ?? 0) > 0 && `${info?.gameID ?? ""}`)
            : null;
        dashboardOverlayGameId = current ? `${current.gameID}` : "";
        if (!dashboardOverlayGameId)
            return false;
        await window.SteamClient?.Overlay?.SetOverlayState?.(dashboardOverlayGameId, 2);
        const deadline = performance.now() + 1600;
        while (performance.now() < deadline) {
            if (overlayDocument()?.body)
                return true;
            await new Promise((resolve) => window.setTimeout(resolve, 40));
        }
        try {
            await window.SteamClient?.Overlay?.SetOverlayState?.(dashboardOverlayGameId, 0);
        }
        catch { }
        dashboardOverlayGameId = "";
        return false;
    }
    catch {
        dashboardOverlayGameId = "";
        return false;
    }
}
function closeDashboardOverlay() {
    const gameId = dashboardOverlayGameId;
    dashboardOverlayGameId = "";
    if (!gameId)
        return;
    try {
        window.SteamClient?.Overlay?.SetOverlayState?.(gameId, 0);
    }
    catch { }
}
function markDashboardChrome() {
    dashboardDocuments().forEach((targetDocument) => {
        try {
            targetDocument.documentElement.classList.add(DASHBOARD_ACTIVE_CLASS);
            targetDocument.body?.classList.add(DASHBOARD_ACTIVE_CLASS);
            let style = targetDocument.getElementById(DASHBOARD_CHROME_STYLE_ID);
            if (!style) {
                style = targetDocument.createElement("style");
                style.id = DASHBOARD_CHROME_STYLE_ID;
                const selectors = DASHBOARD_CHROME_SELECTORS.flatMap((selector) => [
                    `html.${DASHBOARD_ACTIVE_CLASS} ${selector}`,
                    `body.${DASHBOARD_ACTIVE_CLASS} ${selector}`,
                ]);
                style.textContent = `${selectors.join(",")}{display:none!important;opacity:0!important;visibility:hidden!important;pointer-events:none!important;transition:none!important;animation:none!important}html.${DASHBOARD_ACTIVE_CLASS},body.${DASHBOARD_ACTIVE_CLASS}{overflow:hidden!important}`;
                targetDocument.head?.appendChild(style);
            }
        }
        catch { }
    });
}
function clearDashboardChrome() {
    dashboardDocuments().forEach((targetDocument) => {
        try {
            targetDocument.documentElement.classList.remove(DASHBOARD_ACTIVE_CLASS);
            targetDocument.body?.classList.remove(DASHBOARD_ACTIVE_CLASS);
        }
        catch { }
    });
}
function dashboardRoot() {
    for (const targetDocument of dashboardDocuments()) {
        try {
            const root = targetDocument.querySelector(".ph-dashboard");
            if (root)
                return root;
        }
        catch { }
    }
    return null;
}
function focusDashboard(selector) {
    const target = dashboardRoot()?.querySelector(selector);
    if (!target)
        return false;
    try {
        target.focus({ preventScroll: true });
        return target.ownerDocument.activeElement === target;
    }
    catch {
        return false;
    }
}
function activateDashboardSteamContext() {
    const store = _global_DFL?.Router?.WindowStore;
    const candidates = [
        ...(Array.isArray(store?.OverlayWindows) ? store.OverlayWindows : []),
        ...(Array.isArray(store?.SteamUIWindows) ? store.SteamUIWindows : []),
        store?.GamepadUIMainWindowInstance,
    ];
    const visited = new Set();
    for (const steamWindow of candidates) {
        if (!steamWindow || visited.has(steamWindow))
            continue;
        visited.add(steamWindow);
        try {
            const browserWindow = steamWindow.m_BrowserWindow ?? steamWindow.BrowserWindow;
            if (!browserWindow?.document?.querySelector?.(".ph-dashboard"))
                continue;
            const context = steamWindow.m_FocusNavContext;
            try {
                browserWindow.SteamClient?.Window?.MarkLastFocused?.();
            }
            catch { }
            try {
                browserWindow.SteamClient?.Window?.SetKeyFocus?.(true);
            }
            catch { }
            try {
                browserWindow.focus?.();
            }
            catch { }
            if (!context?.BIsActive?.())
                context?.OnActivate?.(browserWindow);
            steamWindow.FocusApplicationRoot?.();
            return true;
        }
        catch { }
    }
    return false;
}
function ensureDashboardFocus() {
    activateDashboardSteamContext();
    const dashboard = dashboardRoot();
    if (!dashboard)
        return false;
    if (dashboard.contains(dashboard.ownerDocument.activeElement))
        return true;
    return focusDashboard(".ph-window-card[data-window-primary='true']")
        || focusDashboard(".ph-page [data-ph-focusable='true']")
        || focusDashboard(".ph-tab.ph-active")
        || focusDashboard(".ph-tab");
}
function focusDashboardSurface() {
    return ensureDashboardFocus();
}
function currentDashboardFocus() {
    const root = dashboardRoot();
    const active = root?.ownerDocument.activeElement;
    if (!root || !active || !root.contains(active))
        return null;
    return active.closest("[data-ph-focusable='true']");
}
const COPY = {
    en: {
        switcher: "Dashboard", apps: "Apps", quick: "Quick Settings", bluetooth: "Bluetooth", system: "System",
        noWindows: "Nothing else is open", noWindowsBody: "Your games and apps will appear here as soon as they open.",
        open: "Open", close: "Close", cancel: "Cancel", remove: "Remove", addApp: "Add app", appLibrary: "Choose an app",
        loadingApps: "Loading your apps...", noApps: "No apps found", volume: "Volume", brightness: "Brightness", wifi: "Wi-Fi",
        muted: "Muted", connected: "Connected", disconnected: "Not connected", paired: "Paired", available: "Available",
        scan: "Scan again", scanning: "Looking for devices...", pair: "Pair", forget: "Forget", bluetoothOff: "Bluetooth is off",
        bluetoothOffBody: "Turn it on to find controllers, headsets and nearby devices.", cpu: "CPU", gpu: "GPU", memory: "Memory",
        processes: "Running processes", restartDecky: "Restart Decky", desktopMode: "Desktop Mode", forceClose: "Force close",
        confirmClose: "Close this window?", confirmRemove: "Remove this shortcut?", confirmAction: "Continue with this action?",
        agentUnavailable: "Quick Settings is installed, but its agent is not responding.", keyboardHint: "Ctrl + Alt + P",
        minimized: "Minimized", devices: "devices", continue: "Continue",
    },
    it: {
        switcher: "Dashboard", apps: "App", quick: "Quick Settings", bluetooth: "Bluetooth", system: "Sistema",
        noWindows: "Non ci sono altre finestre", noWindowsBody: "Giochi e applicazioni compariranno qui appena vengono aperti.",
        open: "Apri", close: "Chiudi", cancel: "Annulla", remove: "Rimuovi", addApp: "Aggiungi app", appLibrary: "Scegli un'app",
        loadingApps: "Caricamento delle app...", noApps: "Nessuna app trovata", volume: "Volume", brightness: "Luminosita", wifi: "Wi-Fi",
        muted: "Silenzioso", connected: "Connesso", disconnected: "Non connesso", paired: "Associato", available: "Disponibile",
        scan: "Cerca di nuovo", scanning: "Ricerca dei dispositivi...", pair: "Associa", forget: "Dimentica", bluetoothOff: "Bluetooth disattivato",
        bluetoothOffBody: "Attivalo per trovare controller, cuffie e dispositivi nelle vicinanze.", cpu: "CPU", gpu: "GPU", memory: "Memoria",
        processes: "Processi attivi", restartDecky: "Riavvia Decky", desktopMode: "Modalita Desktop", forceClose: "Termina",
        confirmClose: "Chiudere questa finestra?", confirmRemove: "Rimuovere questa scorciatoia?", confirmAction: "Continuare con questa azione?",
        agentUnavailable: "Quick Settings e installato, ma il suo agent non risponde.", keyboardHint: "Ctrl + Alt + P",
        minimized: "Ridotta a icona", devices: "dispositivi", continue: "Continua",
    },
    es: {
        switcher: "Panel", apps: "Apps", quick: "Ajustes rapidos", bluetooth: "Bluetooth", system: "Sistema",
        noWindows: "No hay otras ventanas", noWindowsBody: "Tus juegos y aplicaciones apareceran aqui cuando se abran.", open: "Abrir",
        close: "Cerrar", cancel: "Cancelar", remove: "Eliminar", addApp: "Anadir app", appLibrary: "Elegir una app",
        loadingApps: "Cargando aplicaciones...", noApps: "No se encontraron aplicaciones", volume: "Volumen", brightness: "Brillo", wifi: "Wi-Fi",
        muted: "Silenciado", connected: "Conectado", disconnected: "Sin conexion", paired: "Emparejado", available: "Disponible", scan: "Buscar de nuevo",
        scanning: "Buscando dispositivos...", pair: "Emparejar", forget: "Olvidar", bluetoothOff: "Bluetooth desactivado",
        bluetoothOffBody: "Activalo para encontrar mandos, auriculares y dispositivos cercanos.", cpu: "CPU", gpu: "GPU", memory: "Memoria",
        processes: "Procesos activos", restartDecky: "Reiniciar Decky", desktopMode: "Modo Escritorio", forceClose: "Forzar cierre",
        confirmClose: "Cerrar esta ventana?", confirmRemove: "Eliminar este acceso directo?", confirmAction: "Continuar con esta accion?",
        agentUnavailable: "Quick Settings esta instalado, pero su agente no responde.", keyboardHint: "Ctrl + Alt + P",
        minimized: "Minimizada", devices: "dispositivos", continue: "Continuar",
    },
    fr: {
        switcher: "Tableau de bord", apps: "Apps", quick: "Reglages rapides", bluetooth: "Bluetooth", system: "Systeme",
        noWindows: "Aucune autre fenetre", noWindowsBody: "Vos jeux et apps apparaitront ici des leur ouverture.", open: "Ouvrir", close: "Fermer",
        cancel: "Annuler", remove: "Retirer", addApp: "Ajouter une app", appLibrary: "Choisir une app", loadingApps: "Chargement des apps...",
        noApps: "Aucune app trouvee", volume: "Volume", brightness: "Luminosite", wifi: "Wi-Fi", muted: "Silencieux", connected: "Connecte",
        disconnected: "Non connecte", paired: "Associe", available: "Disponible", scan: "Rechercher", scanning: "Recherche des appareils...",
        pair: "Associer", forget: "Oublier", bluetoothOff: "Bluetooth desactive", bluetoothOffBody: "Activez-le pour trouver les appareils proches.",
        cpu: "CPU", gpu: "GPU", memory: "Memoire", processes: "Processus actifs", restartDecky: "Redemarrer Decky", desktopMode: "Mode Bureau",
        forceClose: "Forcer l'arret", confirmClose: "Fermer cette fenetre ?", confirmRemove: "Retirer ce raccourci ?",
        confirmAction: "Continuer cette action ?", agentUnavailable: "Quick Settings est installe, mais son agent ne repond pas.", keyboardHint: "Ctrl + Alt + P",
        minimized: "Reduite", devices: "appareils", continue: "Continuer",
    },
    de: {
        switcher: "Dashboard", apps: "Apps", quick: "Schnelleinstellungen", bluetooth: "Bluetooth", system: "System",
        noWindows: "Keine weiteren Fenster", noWindowsBody: "Geoffnete Spiele und Apps erscheinen hier.", open: "Offnen", close: "Schliessen",
        cancel: "Abbrechen", remove: "Entfernen", addApp: "App hinzufugen", appLibrary: "App auswahlen", loadingApps: "Apps werden geladen...",
        noApps: "Keine Apps gefunden", volume: "Lautstarke", brightness: "Helligkeit", wifi: "WLAN", muted: "Stumm", connected: "Verbunden",
        disconnected: "Nicht verbunden", paired: "Gekoppelt", available: "Verfugbar", scan: "Erneut suchen", scanning: "Gerate werden gesucht...",
        pair: "Koppeln", forget: "Entfernen", bluetoothOff: "Bluetooth ist aus", bluetoothOffBody: "Einschalten, um Gerate in der Nahe zu finden.",
        cpu: "CPU", gpu: "GPU", memory: "Arbeitsspeicher", processes: "Aktive Prozesse", restartDecky: "Decky neu starten", desktopMode: "Desktop-Modus",
        forceClose: "Beenden erzwingen", confirmClose: "Dieses Fenster schliessen?", confirmRemove: "Diese Verknupfung entfernen?",
        confirmAction: "Mit dieser Aktion fortfahren?", agentUnavailable: "Quick Settings ist installiert, aber der Agent antwortet nicht.", keyboardHint: "Ctrl + Alt + P",
        minimized: "Minimiert", devices: "Gerate", continue: "Fortfahren",
    },
    pt: {
        switcher: "Painel", apps: "Apps", quick: "Definicoes rapidas", bluetooth: "Bluetooth", system: "Sistema",
        noWindows: "Nao ha outras janelas", noWindowsBody: "Os seus jogos e apps aparecem aqui quando forem abertos.", open: "Abrir", close: "Fechar",
        cancel: "Cancelar", remove: "Remover", addApp: "Adicionar app", appLibrary: "Escolher uma app", loadingApps: "A carregar apps...",
        noApps: "Nenhuma app encontrada", volume: "Volume", brightness: "Brilho", wifi: "Wi-Fi", muted: "Sem som", connected: "Ligado",
        disconnected: "Desligado", paired: "Emparelhado", available: "Disponivel", scan: "Procurar novamente", scanning: "A procurar dispositivos...",
        pair: "Emparelhar", forget: "Esquecer", bluetoothOff: "Bluetooth desligado", bluetoothOffBody: "Ligue-o para encontrar dispositivos proximos.",
        cpu: "CPU", gpu: "GPU", memory: "Memoria", processes: "Processos ativos", restartDecky: "Reiniciar Decky", desktopMode: "Modo Desktop",
        forceClose: "Forcar encerramento", confirmClose: "Fechar esta janela?", confirmRemove: "Remover este atalho?", confirmAction: "Continuar com esta acao?",
        agentUnavailable: "Quick Settings esta instalado, mas o agente nao responde.", keyboardHint: "Ctrl + Alt + P",
        minimized: "Minimizada", devices: "dispositivos", continue: "Continuar",
    },
    uk: {
        switcher: "Панель", apps: "Програми", quick: "Швидкі налаштування", bluetooth: "Bluetooth", system: "Система",
        noWindows: "Інших вікон немає", noWindowsBody: "Ігри та програми з'являться тут після запуску.", open: "Відкрити", close: "Закрити",
        cancel: "Скасувати", remove: "Видалити", addApp: "Додати програму", appLibrary: "Вибрати програму", loadingApps: "Завантаження програм...",
        noApps: "Програм не знайдено", volume: "Гучність", brightness: "Яскравість", wifi: "Wi-Fi", muted: "Без звуку", connected: "Підключено",
        disconnected: "Не підключено", paired: "Сполучені", available: "Доступні", scan: "Шукати знову", scanning: "Пошук пристроїв...",
        pair: "Сполучити", forget: "Забути", bluetoothOff: "Bluetooth вимкнено", bluetoothOffBody: "Увімкніть його, щоб знайти контролери, навушники та інші пристрої.",
        cpu: "ЦП", gpu: "ГП", memory: "Пам'ять", processes: "Активні процеси", restartDecky: "Перезапустити Decky", desktopMode: "Режим робочого столу",
        forceClose: "Примусово закрити", confirmClose: "Закрити це вікно?", confirmRemove: "Видалити цей ярлик?", confirmAction: "Продовжити цю дію?",
        agentUnavailable: "Quick Settings встановлено, але агент не відповідає.", keyboardHint: "Ctrl + Alt + P", minimized: "Згорнуто", devices: "пристроїв", continue: "Продовжити",
    },
    zh: {
        switcher: "仪表板", apps: "应用", quick: "快速设置", bluetooth: "蓝牙", system: "系统",
        noWindows: "没有其他窗口", noWindowsBody: "游戏和应用打开后会显示在这里。", open: "打开", close: "关闭", cancel: "取消", remove: "移除",
        addApp: "添加应用", appLibrary: "选择应用", loadingApps: "正在加载应用...", noApps: "未找到应用", volume: "音量", brightness: "亮度", wifi: "Wi-Fi",
        muted: "静音", connected: "已连接", disconnected: "未连接", paired: "已配对", available: "可用设备", scan: "再次扫描", scanning: "正在查找设备...",
        pair: "配对", forget: "忽略", bluetoothOff: "蓝牙已关闭", bluetoothOffBody: "开启蓝牙以查找控制器、耳机和附近设备。", cpu: "CPU", gpu: "GPU", memory: "内存",
        processes: "运行中的进程", restartDecky: "重启 Decky", desktopMode: "桌面模式", forceClose: "强制关闭", confirmClose: "关闭此窗口？",
        confirmRemove: "移除此快捷方式？", confirmAction: "继续执行此操作？", agentUnavailable: "Quick Settings 已安装，但代理没有响应。", keyboardHint: "Ctrl + Alt + P",
        minimized: "已最小化", devices: "台设备", continue: "继续",
    },
    ja: {
        switcher: "ダッシュボード", apps: "アプリ", quick: "クイック設定", bluetooth: "Bluetooth", system: "システム",
        noWindows: "ほかのウィンドウはありません", noWindowsBody: "ゲームやアプリを開くと、ここに表示されます。", open: "開く", close: "閉じる", cancel: "キャンセル", remove: "削除",
        addApp: "アプリを追加", appLibrary: "アプリを選択", loadingApps: "アプリを読み込み中...", noApps: "アプリが見つかりません", volume: "音量", brightness: "明るさ", wifi: "Wi-Fi",
        muted: "ミュート", connected: "接続済み", disconnected: "未接続", paired: "ペアリング済み", available: "利用可能", scan: "再スキャン", scanning: "デバイスを検索中...",
        pair: "ペアリング", forget: "登録解除", bluetoothOff: "Bluetooth はオフです", bluetoothOffBody: "オンにすると、コントローラーやヘッドセットなどを検索できます。",
        cpu: "CPU", gpu: "GPU", memory: "メモリ", processes: "実行中のプロセス", restartDecky: "Decky を再起動", desktopMode: "デスクトップモード", forceClose: "強制終了",
        confirmClose: "このウィンドウを閉じますか？", confirmRemove: "このショートカットを削除しますか？", confirmAction: "この操作を続けますか？",
        agentUnavailable: "Quick Settings はインストール済みですが、エージェントが応答していません。", keyboardHint: "Ctrl + Alt + P", minimized: "最小化", devices: "台", continue: "続ける",
    },
    ko: {
        switcher: "대시보드", apps: "앱", quick: "빠른 설정", bluetooth: "Bluetooth", system: "시스템",
        noWindows: "다른 창이 없습니다", noWindowsBody: "게임이나 앱을 열면 여기에 표시됩니다.", open: "열기", close: "닫기", cancel: "취소", remove: "제거",
        addApp: "앱 추가", appLibrary: "앱 선택", loadingApps: "앱 불러오는 중...", noApps: "앱을 찾을 수 없습니다", volume: "음량", brightness: "밝기", wifi: "Wi-Fi",
        muted: "음소거", connected: "연결됨", disconnected: "연결 안 됨", paired: "페어링됨", available: "사용 가능", scan: "다시 검색", scanning: "기기 검색 중...",
        pair: "페어링", forget: "등록 해제", bluetoothOff: "Bluetooth가 꺼져 있습니다", bluetoothOffBody: "컨트롤러, 헤드셋 및 주변 기기를 찾으려면 켜세요.",
        cpu: "CPU", gpu: "GPU", memory: "메모리", processes: "실행 중인 프로세스", restartDecky: "Decky 다시 시작", desktopMode: "데스크톱 모드", forceClose: "강제 종료",
        confirmClose: "이 창을 닫을까요?", confirmRemove: "이 바로가기를 제거할까요?", confirmAction: "이 작업을 계속할까요?",
        agentUnavailable: "Quick Settings가 설치되어 있지만 에이전트가 응답하지 않습니다.", keyboardHint: "Ctrl + Alt + P", minimized: "최소화됨", devices: "개 기기", continue: "계속",
    },
    hi: {
        switcher: "डैशबोर्ड", apps: "ऐप", quick: "त्वरित सेटिंग्स", bluetooth: "Bluetooth", system: "सिस्टम",
        noWindows: "कोई दूसरी विंडो खुली नहीं है", noWindowsBody: "गेम और ऐप खुलने पर यहां दिखाई देंगे।", open: "खोलें", close: "बंद करें", cancel: "रद्द करें", remove: "हटाएं",
        addApp: "ऐप जोड़ें", appLibrary: "ऐप चुनें", loadingApps: "ऐप लोड हो रहे हैं...", noApps: "कोई ऐप नहीं मिला", volume: "वॉल्यूम", brightness: "ब्राइटनेस", wifi: "Wi-Fi",
        muted: "म्यूट", connected: "कनेक्टेड", disconnected: "कनेक्टेड नहीं", paired: "पेयर किए गए", available: "उपलब्ध", scan: "फिर खोजें", scanning: "डिवाइस खोजे जा रहे हैं...",
        pair: "पेयर करें", forget: "हटाएं", bluetoothOff: "Bluetooth बंद है", bluetoothOffBody: "कंट्रोलर, हेडसेट और आस-पास के डिवाइस खोजने के लिए इसे चालू करें।",
        cpu: "CPU", gpu: "GPU", memory: "मेमोरी", processes: "चल रही प्रक्रियाएं", restartDecky: "Decky रीस्टार्ट करें", desktopMode: "डेस्कटॉप मोड", forceClose: "जबरन बंद करें",
        confirmClose: "यह विंडो बंद करें?", confirmRemove: "यह शॉर्टकट हटाएं?", confirmAction: "यह कार्रवाई जारी रखें?",
        agentUnavailable: "Quick Settings इंस्टॉल है, लेकिन उसका एजेंट जवाब नहीं दे रहा।", keyboardHint: "Ctrl + Alt + P", minimized: "मिनिमाइज्ड", devices: "डिवाइस", continue: "जारी रखें",
    },
    ru: {
        switcher: "Панель", apps: "Приложения", quick: "Быстрые настройки", bluetooth: "Bluetooth", system: "Система",
        noWindows: "Других окон нет", noWindowsBody: "Игры и приложения появятся здесь после запуска.", open: "Открыть", close: "Закрыть", cancel: "Отмена", remove: "Удалить",
        addApp: "Добавить приложение", appLibrary: "Выбрать приложение", loadingApps: "Загрузка приложений...", noApps: "Приложения не найдены", volume: "Громкость", brightness: "Яркость", wifi: "Wi-Fi",
        muted: "Без звука", connected: "Подключено", disconnected: "Не подключено", paired: "Сопряженные", available: "Доступные", scan: "Искать снова", scanning: "Поиск устройств...",
        pair: "Подключить", forget: "Забыть", bluetoothOff: "Bluetooth выключен", bluetoothOffBody: "Включите его, чтобы найти контроллеры, гарнитуры и другие устройства.",
        cpu: "ЦП", gpu: "ГП", memory: "Память", processes: "Запущенные процессы", restartDecky: "Перезапустить Decky", desktopMode: "Режим рабочего стола", forceClose: "Завершить принудительно",
        confirmClose: "Закрыть это окно?", confirmRemove: "Удалить этот ярлык?", confirmAction: "Продолжить это действие?",
        agentUnavailable: "Quick Settings установлен, но агент не отвечает.", keyboardHint: "Ctrl + Alt + P", minimized: "Свернуто", devices: "устройств", continue: "Продолжить",
    },
};
const EXTRA_COPY = {
    en: { rename: "Rename", save: "Save", options: "App options", network: "Network", disk: "Disk", pid: "PID", threads: "threads", protected: "Protected" },
    it: { rename: "Rinomina", save: "Salva", options: "Opzioni app", network: "Rete", disk: "Disco", pid: "PID", threads: "thread", protected: "Protetto" },
    es: { rename: "Renombrar", save: "Guardar", options: "Opciones de la app", network: "Red", disk: "Disco", pid: "PID", threads: "hilos", protected: "Protegido" },
    fr: { rename: "Renommer", save: "Enregistrer", options: "Options de l'app", network: "Reseau", disk: "Disque", pid: "PID", threads: "threads", protected: "Protege" },
    de: { rename: "Umbenennen", save: "Speichern", options: "App-Optionen", network: "Netzwerk", disk: "Datentrager", pid: "PID", threads: "Threads", protected: "Geschutzt" },
    pt: { rename: "Mudar nome", save: "Guardar", options: "Opcoes da app", network: "Rede", disk: "Disco", pid: "PID", threads: "threads", protected: "Protegido" },
    uk: { rename: "Перейменувати", save: "Зберегти", options: "Параметри програми", network: "Мережа", disk: "Диск", pid: "PID", threads: "потоків", protected: "Захищено" },
    zh: { rename: "重命名", save: "保存", options: "应用选项", network: "网络", disk: "磁盘", pid: "PID", threads: "线程", protected: "受保护" },
    ja: { rename: "名前を変更", save: "保存", options: "アプリのオプション", network: "ネットワーク", disk: "ディスク", pid: "PID", threads: "スレッド", protected: "保護対象" },
    ko: { rename: "이름 바꾸기", save: "저장", options: "앱 옵션", network: "네트워크", disk: "디스크", pid: "PID", threads: "스레드", protected: "보호됨" },
    hi: { rename: "नाम बदलें", save: "सहेजें", options: "ऐप विकल्प", network: "नेटवर्क", disk: "डिस्क", pid: "PID", threads: "थ्रेड", protected: "सुरक्षित" },
    ru: { rename: "Переименовать", save: "Сохранить", options: "Параметры приложения", network: "Сеть", disk: "Диск", pid: "PID", threads: "потоков", protected: "Защищено" },
};
const STYLE = `
  .ph-dashboard, .ph-dashboard * { box-sizing: border-box; letter-spacing: 0; }
  .ph-dashboard {
    position: fixed; inset: 0; z-index: 6500; overflow: hidden;
    color: #fff; font-family: "Motiva Sans", "Segoe UI", sans-serif;
    background: #24262b;
    isolation: isolate;
  }
  @keyframes phPageIn { from { opacity: 0; transform: translate3d(0,10px,0) scale(.992); } to { opacity: 1; transform: none; } }
  @keyframes phReveal { from { opacity: 0; transform: translate3d(20px,0,0) scale(.985); } to { opacity: 1; transform: none; } }
  @keyframes phSpin { to { transform: rotate(360deg); } }
  .ph-header { height: 108px; padding: 30px 54px 18px; display: grid; grid-template-columns: 200px minmax(680px, 900px) minmax(170px, 1fr); align-items: center; gap: 16px; }
  .ph-brand { width: 190px; height: 54px; display: flex; align-items: center; }
  .ph-brand img { display: block; max-width: 184px; max-height: 44px; object-fit: contain; object-position: left center; filter: brightness(0) invert(1) drop-shadow(0 2px 9px rgba(0,0,0,.28)); }
  .ph-brand-fallback { font-size: 37px; line-height: 1; font-weight: 780; text-transform: lowercase; text-shadow: 0 2px 10px rgba(0,0,0,.34); }
  .ph-tabs { justify-self: center; width: 100%; height: 58px; padding: 6px; display: flex; align-items: stretch; gap: 11px; border-radius: 29px; background: rgba(12,13,18,.48); border: 1px solid rgba(255,255,255,.12); box-shadow: 0 16px 34px rgba(0,0,0,.18); }
  .ph-tab { min-width: 0; flex: 1 1 0; border-radius: 23px; color: rgba(255,255,255,.74); display: flex; align-items: center; justify-content: center; gap: 9px; font-size: 17px; font-weight: 570; white-space: nowrap; transition: color 150ms ease, background 170ms ease, transform 170ms ease, box-shadow 170ms ease; }
  .ph-tab svg { width: 19px; height: 19px; flex: 0 0 auto; }
  .ph-tab.ph-active { color: #12151a; background: rgba(248,250,255,.93); box-shadow: 0 0 28px rgba(214,231,255,.36), inset 0 1px rgba(255,255,255,.88); }
  .ph-tab.ph-focus, .ph-tab:focus { transform: scale(1.035); box-shadow: 0 0 0 3px rgba(255,255,255,.82), 0 0 28px rgba(224,235,255,.28); }
  .ph-tab.ph-active.ph-focus, .ph-tab.ph-active:focus { box-shadow: 0 0 0 3px rgba(255,255,255,.72), 0 0 34px rgba(222,236,255,.48); }
  .ph-clock { justify-self: end; display: flex; align-items: baseline; justify-content: flex-end; gap: 12px; color: rgba(255,255,255,.82); white-space: nowrap; }
  .ph-clock-date { font-size: 16px; font-weight: 520; opacity: .72; }
  .ph-clock-time { font-size: 21px; font-weight: 650; }
  .ph-main { height: calc(100% - 108px); padding: 10px 54px 82px; overflow: hidden; }
  .ph-system-focus-bridge { position: absolute; z-index: -1; top: 106px; left: 34%; right: 34%; height: 3px; opacity: .001; overflow: hidden; }
  .ph-page { height: 100%; animation: phPageIn 260ms cubic-bezier(.2,.8,.2,1); }
  .ph-page-scroll { height: 100%; overflow-y: auto; overflow-x: hidden; padding: 12px 7px 112px; scroll-padding-block: 12px 112px; scrollbar-width: none; }
  .ph-page-scroll::-webkit-scrollbar, .ph-window-rail::-webkit-scrollbar { display: none; }
  .ph-section-title { display: flex; align-items: center; gap: 13px; margin: 4px 0 15px 8px; font-size: 27px; font-weight: 720; }
  .ph-section-title svg { width: 26px; height: 26px; opacity: .9; }
  .ph-switcher-page { height: 100%; }
  .ph-window-rail { height: 100%; min-width: 0; display: flex; align-items: center; gap: 24px; overflow-x: auto; overflow-y: hidden; padding: 24px 8px 42px; scrollbar-width: none; scroll-padding-inline: 8px; contain: layout paint; }
  .ph-window-rail.ph-single { justify-content: center; }
  .ph-window-card { position: relative; flex: 0 0 clamp(250px, 20vw, 380px); min-width: 0; display: flex; flex-direction: column; color: #fff; border-radius: 30px; transition: flex-basis 260ms cubic-bezier(.2,.82,.2,1), transform 220ms cubic-bezier(.2,.82,.2,1), opacity 170ms ease; animation: phReveal 300ms both; outline: none; transform-origin: center center; }
  .ph-window-card.ph-edge-clipped:not(.ph-focus):not(:focus) { opacity: 0 !important; pointer-events: none; }
  .ph-window-card.ph-focus, .ph-window-card:focus { flex-basis: clamp(560px, 43vw, 820px); transform: translate3d(0,-7px,0); z-index: 2; }
  .ph-window-title { height: 56px; display: flex; align-items: center; gap: 13px; padding: 0 10px 10px; font-size: 23px; font-weight: 650; text-shadow: 0 2px 12px rgba(0,0,0,.45); }
  .ph-window-title span { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ph-app-icon { width: 34px; height: 34px; flex: 0 0 auto; padding: 0; object-fit: contain; background: transparent; box-shadow: none; }
  .ph-window-frame { position: relative; width: 100%; aspect-ratio: 920 / 430; overflow: hidden; border-radius: 29px; clip-path: inset(0 round 29px); background: rgba(10,12,17,.72); border: 1px solid rgba(255,255,255,.2); box-shadow: inset 0 1px rgba(255,255,255,.09), 0 18px 36px rgba(0,0,0,.24); transition: border-color 170ms ease, box-shadow 170ms ease; }
  .ph-window-card.ph-focus .ph-window-frame, .ph-window-card:focus .ph-window-frame { border-color: rgba(255,255,255,.96); box-shadow: 0 0 0 4px rgba(255,255,255,.9), 0 26px 56px rgba(0,0,0,.32), 0 0 38px rgba(230,238,255,.22); }
  .ph-window-frame img { width: 100%; height: 100%; display: block; object-fit: cover; border-radius: inherit; }
  .ph-window-placeholder { width: 100%; height: 100%; display: grid; place-items: center; background: linear-gradient(145deg, rgba(255,255,255,.12), rgba(5,8,14,.5)); }
  .ph-window-placeholder img { width: 104px; height: 104px; object-fit: contain; }
  .ph-window-placeholder svg { width: 80px; height: 80px; opacity: .68; }
  .ph-window-meta { position: absolute; right: 17px; bottom: 15px; padding: 7px 11px; border-radius: 14px; color: rgba(255,255,255,.8); font-size: 14px; background: rgba(5,7,11,.62); }
  .ph-live-dot { width: 8px; height: 8px; border-radius: 50%; background: #71e2a5; box-shadow: 0 0 12px rgba(113,226,165,.75); }
  .ph-empty { height: 70%; display: grid; place-items: center; text-align: center; }
  .ph-empty-inner { max-width: 530px; }
  .ph-empty svg { width: 60px; height: 60px; opacity: .7; margin-bottom: 15px; }
  .ph-empty-title { font-size: 30px; font-weight: 720; margin-bottom: 8px; }
  .ph-muted { color: rgba(255,255,255,.62); line-height: 1.42; }
  .ph-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(154px, 1fr)); gap: 18px; padding: 9px 8px 42px; }
  .ph-app-tile { height: 176px; padding: 20px 14px 14px; border-radius: 25px; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 13px; color: #fff; background: rgba(12,15,21,.45); border: 1px solid rgba(255,255,255,.12); box-shadow: 0 12px 26px rgba(0,0,0,.14); transition: transform 170ms ease, background 170ms ease, box-shadow 170ms ease; }
  .ph-app-tile.ph-focus, .ph-app-tile:focus { transform: translate3d(0,-5px,0) scale(1.035); background: rgba(245,248,255,.93); color: #12151a; box-shadow: 0 0 0 4px rgba(255,255,255,.9), 0 24px 42px rgba(0,0,0,.26); }
  .ph-app-tile img { width: 72px; height: 72px; object-fit: contain; background: transparent; }
  .ph-app-tile svg { width: 52px; height: 52px; }
  .ph-app-name { width: 100%; min-height: 25px; padding: 1px 2px 3px; text-align: center; font-size: 17px; line-height: 1.24; font-weight: 620; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ph-toolbar { display: flex; align-items: center; justify-content: space-between; margin: 0 8px 18px; padding: 7px 5px 5px; }
  .ph-back { display: flex; align-items: center; gap: 10px; font-size: 22px; font-weight: 680; }
  .ph-tiles { display: grid; grid-template-columns: repeat(12, 1fr); gap: 18px; padding: 6px 8px 28px; align-items: stretch; }
  .ph-tile { min-height: 148px; padding: 24px; border-radius: 27px; color: #fff; background: rgba(13,16,22,.44); border: 1px solid rgba(255,255,255,.12); box-shadow: 0 14px 30px rgba(0,0,0,.14); transition: transform 170ms ease, background 170ms ease, box-shadow 170ms ease, color 170ms ease; overflow: hidden; }
  .ph-tile.ph-focus, .ph-tile:focus { transform: translate3d(0,-4px,0) scale(1.018); color: #101318; background: rgba(246,249,255,.94); box-shadow: 0 0 0 4px rgba(255,255,255,.88), 0 23px 43px rgba(0,0,0,.24); }
  .ph-span-3 { grid-column: span 3; } .ph-span-4 { grid-column: span 4; } .ph-span-5 { grid-column: span 5; } .ph-span-6 { grid-column: span 6; } .ph-span-8 { grid-column: span 8; } .ph-span-12 { grid-column: span 12; }
  .ph-tile-head { display: flex; align-items: center; gap: 14px; }
  .ph-tile-icon { width: 54px; height: 54px; flex: 0 0 auto; display: grid; place-items: center; border-radius: 18px; background: rgba(255,255,255,.13); }
  .ph-tile.ph-focus .ph-tile-icon, .ph-tile:focus .ph-tile-icon { background: rgba(15,20,28,.09); }
  .ph-tile-icon svg { width: 29px; height: 29px; }
  .ph-tile-title { font-size: 22px; line-height: 1.1; font-weight: 680; }
  .ph-tile-subtitle { margin-top: 5px; font-size: 15px; opacity: .62; }
  .ph-switch { margin-left: auto; width: 54px; height: 31px; padding: 4px; border-radius: 16px; background: rgba(255,255,255,.18); transition: background 160ms ease; }
  .ph-switch span { display: block; width: 23px; height: 23px; border-radius: 50%; background: #fff; box-shadow: 0 2px 7px rgba(0,0,0,.24); transition: transform 180ms cubic-bezier(.2,.8,.2,1); }
  .ph-switch.ph-on { background: #42d483; } .ph-switch.ph-on span { transform: translateX(23px); }
  .ph-slider { margin-top: 24px; display: grid; grid-template-columns: 1fr auto; align-items: center; gap: 14px; }
  .ph-slider-track { position: relative; height: 8px; border-radius: 4px; background: rgba(255,255,255,.2); overflow: hidden; }
  .ph-tile.ph-focus .ph-slider-track, .ph-tile:focus .ph-slider-track { background: rgba(15,20,28,.15); }
  .ph-slider-fill { height: 100%; border-radius: inherit; background: currentColor; opacity: .92; }
  .ph-slider-value { min-width: 48px; text-align: right; font-size: 18px; font-weight: 650; }
  .ph-native-range { position: absolute; inset: -12px 0; width: 100%; opacity: 0; cursor: pointer; }
  .ph-device-list { grid-column: span 7; padding: 0; min-height: 360px; }
  .ph-device-list.ph-span-5 { grid-column: span 5; }
  .ph-device-list-head { padding: 22px 24px 12px; font-size: 21px; font-weight: 680; }
  .ph-device-row { min-height: 76px; margin: 0 12px 8px; padding: 12px 15px; border-radius: 19px; display: flex; align-items: center; gap: 15px; color: #fff; background: rgba(255,255,255,.055); transition: background 160ms ease, transform 160ms ease, color 160ms ease; }
  .ph-device-row.ph-focus, .ph-device-row:focus { color: #12151a; background: rgba(248,250,255,.94); transform: translateX(4px); box-shadow: 0 0 0 3px rgba(255,255,255,.8); }
  .ph-device-row svg { width: 25px; height: 25px; flex: 0 0 auto; }
  .ph-device-copy { min-width: 0; flex: 1; } .ph-device-name { font-size: 18px; font-weight: 650; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ph-device-state { margin-top: 3px; font-size: 14px; opacity: .58; }
  .ph-state-dot { width: 9px; height: 9px; border-radius: 50%; background: rgba(255,255,255,.32); }
  .ph-state-dot.ph-online { background: #4dde8d; box-shadow: 0 0 12px rgba(77,222,141,.65); }
  .ph-metric { min-height: 128px; }
  .ph-metric-value { margin-top: 19px; font-size: 33px; font-weight: 710; }
  .ph-system-stack { height: 100%; display: flex; flex-direction: column; gap: 14px; }
  .ph-metrics-rail { flex: 0 0 158px; display: flex; gap: 13px; padding: 7px 7px 11px; overflow-x: auto; overflow-y: hidden; scrollbar-width: none; scroll-snap-type: x mandatory; scroll-padding-inline: 7px; contain: paint; }
  .ph-metrics-rail::-webkit-scrollbar { display: none; }
  .ph-history-card { flex: 0 0 calc((100% - 52px) / 5); height: 140px; padding: 16px 17px 12px; border-radius: 23px; color: #fff; background: rgba(13,16,22,.44); border: 1px solid rgba(255,255,255,.12); box-shadow: 0 12px 28px rgba(0,0,0,.14); scroll-snap-align: start; scroll-snap-stop: always; transition: transform 170ms ease, background 170ms ease, box-shadow 170ms ease; }
  .ph-history-card:hover, .ph-history-card.ph-focus, .ph-history-card:focus { transform: translateY(-3px); background: rgba(30,34,43,.76); box-shadow: 0 0 0 3px rgba(255,255,255,.72), 0 18px 34px rgba(0,0,0,.22); }
  .ph-history-label { color: rgba(255,255,255,.58); font-size: 12px; font-weight: 680; text-transform: uppercase; }
  .ph-history-value { margin-top: 2px; font-size: 26px; line-height: 1.08; font-weight: 720; }
  .ph-history-chart { display: block; width: 100%; height: 42px; margin-top: 5px; overflow: visible; }
  .ph-history-chart polyline { fill: none; stroke: #8ec5ff; stroke-width: 2.2; stroke-linecap: round; stroke-linejoin: round; filter: drop-shadow(0 0 5px rgba(142,197,255,.34)); }
  .ph-history-detail { margin-top: 3px; color: rgba(255,255,255,.49); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ph-system-lower { min-height: 0; flex: 1 1 auto; }
  .ph-process-panel { min-height: 0; padding: 18px; overflow: visible; }
  .ph-process-heading { display: flex; align-items: center; justify-content: space-between; gap: 20px; padding: 0 4px 10px; }
  .ph-process-list { max-height: 292px; padding: 4px; overflow-y: auto; overflow-x: visible; scrollbar-width: none; }
  .ph-process-list::-webkit-scrollbar { display: none; }
  .ph-process-row { min-height: 57px; margin: 0 1px 3px; padding: 9px 13px; border-radius: 16px; display: grid; grid-template-columns: minmax(160px,1fr) 90px 90px; align-items: center; gap: 12px; color: #fff; }
  .ph-process-row.ph-focus, .ph-process-row:focus { color: #12151a; background: rgba(248,250,255,.94); box-shadow: 0 0 0 3px rgba(255,255,255,.78); }
  .ph-process-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 620; }
  .ph-process-stat { text-align: right; opacity: .68; }
  .ph-restart-decky { min-height: 44px; padding: 0 16px; border-radius: 17px; display: inline-flex; align-items: center; gap: 9px; color: rgba(255,255,255,.86); background: rgba(255,255,255,.09); font-size: 15px; font-weight: 650; white-space: nowrap; }
  .ph-restart-decky svg { width: 19px; height: 19px; }
  .ph-restart-decky.ph-busy { opacity: .82; }
  .ph-restart-decky.ph-busy svg { animation: phSpin .72s linear infinite; }
  .ph-restart-decky.ph-focus, .ph-restart-decky:focus { color: #12151a; background: rgba(248,250,255,.95); box-shadow: 0 0 0 3px rgba(255,255,255,.68); }
  .ph-spinner { width: 32px; height: 32px; margin: 30px auto; border-radius: 50%; border: 3px solid rgba(255,255,255,.2); border-top-color: #fff; animation: phSpin .8s linear infinite; }
  .ph-confirm-backdrop { position: absolute; inset: 0; z-index: 20; display: grid; place-items: center; background: rgba(4,6,10,.58); animation: phPageIn 150ms ease; }
  .ph-confirm { width: min(590px, calc(100vw - 90px)); padding: 28px; border-radius: 28px; background: rgba(26,29,37,.96); border: 1px solid rgba(255,255,255,.17); box-shadow: 0 34px 80px rgba(0,0,0,.45); }
  .ph-confirm-title { font-size: 26px; font-weight: 720; margin-bottom: 8px; }
  .ph-confirm-name { color: rgba(255,255,255,.62); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ph-confirm-actions { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; margin-top: 24px; }
  .ph-confirm-actions.ph-three { grid-template-columns: 1fr 1fr 1fr; }
  .ph-options-field { margin-top: 22px; }
  .ph-options-field input { min-height: 54px; font-size: 18px; }
  .ph-confirm-button { min-height: 58px; border-radius: 20px; display: grid; place-items: center; color: #fff; background: rgba(255,255,255,.1); font-size: 17px; font-weight: 650; }
  .ph-confirm-button.ph-danger { background: rgba(222,68,72,.38); }
  .ph-confirm-button.ph-focus, .ph-confirm-button:focus { color: #11151a; background: #fff; box-shadow: 0 0 0 4px rgba(255,255,255,.68), 0 15px 30px rgba(0,0,0,.22); }
  @media (max-width: 1180px) {
    .ph-header { grid-template-columns: 170px minmax(560px,1fr) 150px; padding-left: 34px; padding-right: 34px; }
    .ph-main { padding-left: 34px; padding-right: 34px; }
    .ph-tab { font-size: 15px; gap: 6px; } .ph-window-card { flex-basis: clamp(230px, 19vw, 320px); } .ph-window-card.ph-focus, .ph-window-card:focus { flex-basis: clamp(520px, 44vw, 690px); }
  }
  @media (max-height: 760px) {
    .ph-header { height: 92px; padding-top: 22px; } .ph-main { height: calc(100% - 92px); padding-top: 2px; padding-bottom: 82px; }
    .ph-window-card { flex-basis: clamp(220px, 19vw, 310px); } .ph-window-card.ph-focus, .ph-window-card:focus { flex-basis: clamp(480px, 42vw, 660px); }
    .ph-tile { min-height: 122px; padding: 19px; } .ph-grid { gap: 14px; } .ph-app-tile { height: 150px; }
  }
  @media (prefers-reduced-motion: reduce) { .ph-dashboard *, .ph-dashboard::before { animation: none !important; transition-duration: 1ms !important; } }
`;
let audioContext;
function sound(kind) {
    try {
        const Context = window.AudioContext || window.webkitAudioContext;
        if (!Context)
            return;
        const context = audioContext ?? new Context();
        audioContext = context;
        if (context.state === "suspended")
            void context.resume();
        const now = context.currentTime;
        const gain = context.createGain();
        const first = context.createOscillator();
        const second = context.createOscillator();
        first.type = "sine";
        second.type = "sine";
        first.frequency.setValueAtTime(kind === "open" ? 330 : 420, now);
        second.frequency.setValueAtTime(kind === "open" ? 495 : 630, now);
        gain.gain.setValueAtTime(0.0001, now);
        gain.gain.exponentialRampToValueAtTime(kind === "open" ? 0.028 : 0.012, now + 0.012);
        gain.gain.exponentialRampToValueAtTime(0.0001, now + (kind === "open" ? 0.18 : 0.09));
        first.connect(gain);
        second.connect(gain);
        gain.connect(context.destination);
        first.start(now);
        second.start(now + 0.008);
        first.stop(now + 0.2);
        second.stop(now + 0.2);
    }
    catch { }
}
function stopEvent(event, prevent = false) {
    try {
        if (prevent)
            event?.preventDefault?.();
        event?.stopPropagation?.();
    }
    catch { }
}
function FocusItem({ className = "", children, onPress, onFocus, ...props }) {
    const lastActivation = useRef(0);
    const activate = (event) => {
        stopEvent(event, true);
        const now = performance.now();
        if (now - lastActivation.current < 160)
            return;
        lastActivation.current = now;
        sound("open");
        onPress?.();
    };
    return (SP_JSX.jsx(Focusable, { ...props, tabIndex: 0, "data-ph-focusable": "true", noFocusRing: true, focusClassName: "ph-focus", className: className, onActivate: activate, onClick: (event) => { if (event?.detail > 0)
            activate(event); }, onFocus: (event) => { sound("move"); onFocus?.(event); }, children: children }));
}
function gridDirectionFromKey(key) {
    if (key === "ArrowLeft" || key === "Left")
        return "left";
    if (key === "ArrowRight" || key === "Right")
        return "right";
    if (key === "ArrowUp" || key === "Up")
        return "up";
    if (key === "ArrowDown" || key === "Down")
        return "down";
    return null;
}
function gridDirectionFromGamepad(button) {
    if (button === GamepadButton?.DIR_LEFT)
        return "left";
    if (button === GamepadButton?.DIR_RIGHT)
        return "right";
    if (button === GamepadButton?.DIR_UP)
        return "up";
    if (button === GamepadButton?.DIR_DOWN)
        return "down";
    return null;
}
const gridFocusMoveState = new WeakMap();
function stopDirectionalEvent(event) {
    event?.preventDefault?.();
    event?.stopPropagation?.();
    event?.stopImmediatePropagation?.();
    event?.nativeEvent?.stopImmediatePropagation?.();
}
function moveGridFocus(event, direction) {
    if (!direction || typeof document === "undefined")
        return false;
    const eventTarget = event?.target;
    const activeTarget = (dashboardRoot()?.ownerDocument.activeElement ?? document.activeElement);
    const current = eventTarget?.closest?.("[data-ph-grid-index]")
        ?? activeTarget?.closest?.("[data-ph-grid-index]");
    const grid = current?.closest?.("[data-ph-focus-grid]");
    if (!current || !grid)
        return false;
    const currentRect = current.getBoundingClientRect();
    const currentX = currentRect.left + currentRect.width / 2;
    const currentY = currentRect.top + currentRect.height / 2;
    let best = null;
    for (const element of Array.from(grid.querySelectorAll("[data-ph-grid-index]"))) {
        if (element === current)
            continue;
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2;
        const y = rect.top + rect.height / 2;
        const dx = x - currentX;
        const dy = y - currentY;
        const sameRow = Math.abs(dy) <= Math.max(currentRect.height, rect.height) * 0.55;
        const sameColumn = Math.abs(dx) <= Math.max(currentRect.width, rect.width) * 0.6;
        let score = Number.POSITIVE_INFINITY;
        if (direction === "left" && dx < -2 && sameRow)
            score = Math.abs(dx) + Math.abs(dy) * 5;
        if (direction === "right" && dx > 2 && sameRow)
            score = Math.abs(dx) + Math.abs(dy) * 5;
        if (direction === "up" && dy < -2 && sameColumn)
            score = Math.abs(dy) + Math.abs(dx) * 5;
        if (direction === "down" && dy > 2 && sameColumn)
            score = Math.abs(dy) + Math.abs(dx) * 5;
        if (score < (best?.score ?? Number.POSITIVE_INFINITY))
            best = { element, score };
    }
    if (!best)
        return false;
    const now = typeof performance !== "undefined" ? performance.now() : Date.now();
    const previousMove = gridFocusMoveState.get(grid);
    if (previousMove && previousMove.direction === direction && now - previousMove.at < 180) {
        stopDirectionalEvent(event);
        return true;
    }
    gridFocusMoveState.set(grid, { at: now, direction });
    stopDirectionalEvent(event);
    best.element.focus?.();
    return true;
}
function focusSystemMetric(position = 0) {
    const metrics = Array.from(dashboardRoot()?.querySelectorAll(".ph-history-card") ?? []);
    if (!metrics.length)
        return false;
    const target = metrics[Math.max(0, Math.min(metrics.length - 1, position))];
    target.focus({ preventScroll: true });
    target.scrollIntoView({ behavior: "smooth", block: "nearest", inline: "nearest" });
    return true;
}
function FocusGrid({ className = "", children, ...props }) {
    return (SP_JSX.jsx(Focusable, { ...props, className: className, "flow-children": "grid", "data-ph-focus-grid": "true", children: children }));
}
function useClock() {
    const [now, setNow] = useState$1(() => new Date());
    useEffect$1(() => {
        const timer = window.setInterval(() => setNow(new Date()), 15000);
        return () => window.clearInterval(timer);
    }, []);
    return now;
}
function Header({ tab, setTab, copy, locale, logo }) {
    const now = useClock();
    const localeTag = STEAM_LOCALE_TAGS[locale] ?? STEAM_LOCALE_TAGS.en;
    const use24HourClock = steamUses24HourClock(localeTag);
    const tabs = useMemo$1(() => {
        return [
            { id: "switcher", label: copy.switcher, icon: FiMonitor },
            { id: "apps", label: copy.apps, icon: FiGrid },
            { id: "system", label: copy.system, icon: FiCpu },
        ];
    }, [copy]);
    return (SP_JSX.jsxs("header", { className: "ph-header", children: [SP_JSX.jsx("div", { className: "ph-brand", children: logo ? SP_JSX.jsx("img", { src: logo, alt: "Playhub" }) : SP_JSX.jsx("div", { className: "ph-brand-fallback", children: "playhub" }) }), SP_JSX.jsx(Focusable, { className: "ph-tabs", "flow-children": "horizontal", children: tabs.map((item) => {
                    const Icon = item.icon;
                    return (SP_JSX.jsxs(FocusItem, { className: `ph-tab ${tab === item.id ? "ph-active" : ""}`, onPress: () => setTab(item.id), onButtonDown: (event) => {
                            if (item.id !== "system" || tab !== "system" || Number(event?.detail?.button) !== GamepadButton?.DIR_DOWN)
                                return;
                            stopDirectionalEvent(event);
                            focusSystemMetric(0);
                        }, onKeyDown: (event) => {
                            if (item.id !== "system" || tab !== "system" || gridDirectionFromKey(event.key) !== "down")
                                return;
                            stopDirectionalEvent(event);
                            focusSystemMetric(0);
                        }, onOKActionDescription: item.label, children: [SP_JSX.jsx(Icon, {}), SP_JSX.jsx("span", { children: item.label })] }, item.id));
                }) }), SP_JSX.jsxs("div", { className: "ph-clock", children: [SP_JSX.jsx("span", { className: "ph-clock-date", children: now.toLocaleDateString(localeTag, { weekday: "short", day: "numeric", month: "short" }) }), SP_JSX.jsx("span", { className: "ph-clock-time", children: now.toLocaleTimeString(localeTag, { hour: "2-digit", minute: "2-digit", hour12: !use24HourClock }) })] })] }));
}
function updateWindowEdgeVisibility(container) {
    const bounds = container.getBoundingClientRect();
    const inset = 7;
    container.querySelectorAll(".ph-window-card").forEach((card) => {
        const rect = card.getBoundingClientRect();
        const clipped = rect.left < bounds.left + inset || rect.right > bounds.right - inset;
        card.classList.toggle("ph-edge-clipped", clipped && card !== container.ownerDocument.activeElement);
    });
}
function WindowCard({ entry, artwork, index, copy, onSelect, onAskClose }) {
    const icon = iconSource(entry.iconBase64);
    const isSteam = /^(steam|steamwebhelper)$/i.test(entry.processName);
    return (SP_JSX.jsxs(FocusItem, { className: "ph-window-card", "data-ph-grid-index": index, "data-window-card-handle": entry.handle, "data-window-primary": entry.primary ? "true" : "false", style: { animationDelay: `${Math.min(index, 6) * 36}ms` }, onPress: onSelect, onButtonDown: (event) => moveGridFocus(event, gridDirectionFromGamepad(event?.detail?.button)), onSecondaryButton: (event) => { stopEvent(event, true); onAskClose(); }, onOKActionDescription: copy.open, onSecondaryActionDescription: copy.close, onFocus: (event) => {
            const item = event?.currentTarget;
            const container = item?.closest?.(".ph-window-rail");
            if (!item || !container)
                return;
            item.classList.remove("ph-edge-clipped");
            const alignFocusedCard = (behavior) => {
                if (!item.isConnected || !container.isConnected)
                    return;
                const itemRect = item.getBoundingClientRect();
                const containerRect = container.getBoundingClientRect();
                const safeInset = 12;
                if (itemRect.left < containerRect.left + safeInset) {
                    container.scrollBy({ left: itemRect.left - containerRect.left - safeInset, behavior });
                }
                else if (itemRect.right > containerRect.right - safeInset) {
                    container.scrollBy({ left: itemRect.right - containerRect.right + safeInset, behavior });
                }
                window.requestAnimationFrame(() => updateWindowEdgeVisibility(container));
            };
            window.requestAnimationFrame(() => alignFocusedCard("smooth"));
            window.setTimeout(() => alignFocusedCard("auto"), 285);
        }, children: [SP_JSX.jsxs("div", { className: "ph-window-title", children: [icon ? SP_JSX.jsx("img", { className: "ph-app-icon", src: icon, alt: "" }) : isSteam ? SP_JSX.jsx(SiSteam, {}) : SP_JSX.jsx(FiMonitor, {}), SP_JSX.jsx("span", { children: entry.title })] }), SP_JSX.jsxs("div", { className: "ph-window-frame", "data-window-handle": entry.handle, children: [artwork ? SP_JSX.jsx("img", { src: artwork, alt: "" }) : (SP_JSX.jsx("div", { className: "ph-window-placeholder", children: icon ? SP_JSX.jsx("img", { src: icon, alt: "" }) : SP_JSX.jsx(FiMonitor, {}) })), entry.minimized ? SP_JSX.jsx("div", { className: "ph-window-meta", children: copy.minimized }) : null] })] }));
}
async function loadFallback(entry) {
    // Steam library banners use the native 920x430 card format. Keep the hero
    // only as a fallback for games that do not have a horizontal banner cached.
    for (const path of [entry.bannerPath, entry.heroPath]) {
        if (!path)
            continue;
        const image = await loadImage(path);
        if (image)
            return image;
    }
    return "";
}
let cachedSwitcherWindows = [];
let cachedSwitcherArtwork = {};
let switcherWindowsRequest = null;
function sortSwitcherWindows(windows) {
    return windows.slice().sort((left, right) => Number(right.primary) - Number(left.primary) || Number(right.foreground) - Number(left.foreground));
}
function preloadDashboardWindows(force = false) {
    if (!force && cachedSwitcherWindows.length > 0)
        return Promise.resolve(cachedSwitcherWindows);
    if (switcherWindowsRequest)
        return switcherWindowsRequest;
    switcherWindowsRequest = listWindows()
        .then((windows) => {
        const latest = sortSwitcherWindows(windows);
        cachedSwitcherWindows = latest;
        return latest;
    })
        .finally(() => {
        switcherWindowsRequest = null;
    });
    return switcherWindowsRequest;
}
function TaskSwitcher({ copy, onReady, onSelectWindow }) {
    const [windows, setWindows] = useState$1(cachedSwitcherWindows);
    const [artwork, setArtwork] = useState$1(cachedSwitcherArtwork);
    const rail = useRef(null);
    const readySent = useRef(false);
    const focusedPrimaryHandle = useRef("");
    const orderedWindows = useMemo$1(() => sortSwitcherWindows(windows), [windows]);
    const refresh = useCallback(async () => {
        const latest = await preloadDashboardWindows(true);
        setWindows((current) => {
            const unchanged = current.length === latest.length && current.every((entry, index) => {
                const next = latest[index];
                return next?.handle === entry.handle
                    && next.title === entry.title
                    && next.minimized === entry.minimized
                    && next.primary === entry.primary
                    && next.foreground === entry.foreground
                    && next.heroPath === entry.heroPath
                    && next.bannerPath === entry.bannerPath
                    && next.iconBase64 === entry.iconBase64;
            });
            return unchanged ? current : latest;
        });
    }, []);
    useEffect$1(() => { void refresh(); const timer = window.setInterval(() => void refresh(), 3500); return () => window.clearInterval(timer); }, [refresh]);
    useEffect$1(() => {
        const container = document.querySelector(".ph-window-rail");
        if (!container)
            return;
        let frame = 0;
        const update = () => {
            window.cancelAnimationFrame(frame);
            frame = window.requestAnimationFrame(() => updateWindowEdgeVisibility(container));
        };
        container.addEventListener("scroll", update, { passive: true });
        window.addEventListener("resize", update);
        update();
        const settled = window.setTimeout(update, 340);
        return () => {
            window.clearTimeout(settled);
            window.cancelAnimationFrame(frame);
            container.removeEventListener("scroll", update);
            window.removeEventListener("resize", update);
        };
    }, [orderedWindows.map((entry) => entry.handle).join("|")]);
    useLayoutEffect(() => {
        if (readySent.current || windows.length === 0)
            return;
        readySent.current = true;
        const frame = window.requestAnimationFrame(onReady);
        return () => window.cancelAnimationFrame(frame);
    }, [windows.length, onReady]);
    const primaryHandle = orderedWindows.find((entry) => entry.primary)?.handle ?? "";
    useLayoutEffect(() => {
        if (!primaryHandle || focusedPrimaryHandle.current === primaryHandle)
            return;
        focusedPrimaryHandle.current = primaryHandle;
        const frame = window.requestAnimationFrame(() => {
            activateDashboardSteamContext();
            focusDashboard(`[data-window-card-handle="${primaryHandle}"]`);
        });
        return () => window.cancelAnimationFrame(frame);
    }, [primaryHandle]);
    useEffect$1(() => {
        let alive = true;
        const candidates = windows.slice(0, 12);
        const run = async () => {
            for (let index = 0; index < candidates.length && alive; index += 2) {
                const pair = candidates.slice(index, index + 2);
                await Promise.all(pair.map(async (entry) => {
                    const fallback = await loadFallback(entry);
                    if (!alive)
                        return;
                    if (fallback)
                        setArtwork((current) => {
                            if (current[entry.handle] === fallback)
                                return current;
                            const next = { ...current, [entry.handle]: fallback };
                            cachedSwitcherArtwork = next;
                            return next;
                        });
                }));
            }
        };
        void run();
        return () => { alive = false; };
    }, [windows.map((entry) => `${entry.handle}:${entry.heroPath}:${entry.bannerPath}`).join("|")]);
    useEffect$1(() => {
        const liveHandles = new Set(windows.map((entry) => entry.handle));
        setArtwork((current) => {
            const next = Object.fromEntries(Object.entries(current).filter(([handle]) => liveHandles.has(handle)));
            if (Object.keys(next).length === Object.keys(current).length)
                return current;
            cachedSwitcherArtwork = next;
            return next;
        });
    }, [windows.map((entry) => entry.handle).join("|")]);
    const closeEntry = useCallback(async (entry, index) => {
        const ordered = sortSwitcherWindows(windows);
        const fallbackHandle = ordered[index + 1]?.handle ?? ordered[index - 1]?.handle ?? "";
        await closeWindow(entry.handle);
        window.setTimeout(async () => {
            await refresh();
            window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
                activateDashboardSteamContext();
                const selector = fallbackHandle ? `[data-window-card-handle="${fallbackHandle}"]` : "";
                if ((selector && focusDashboard(selector)) || ensureDashboardFocus())
                    return;
                onReady();
            }));
        }, 220);
    }, [windows, refresh, onReady]);
    return (SP_JSX.jsx("div", { className: "ph-page ph-switcher-page", children: windows.length === 0 ? (SP_JSX.jsx("div", { className: "ph-empty", children: SP_JSX.jsxs("div", { className: "ph-empty-inner", children: [SP_JSX.jsx(FiMonitor, {}), SP_JSX.jsx("div", { className: "ph-empty-title", children: copy.noWindows }), SP_JSX.jsx("div", { className: "ph-muted", children: copy.noWindowsBody })] }) })) : (SP_JSX.jsx(Focusable, { ref: rail, className: `ph-window-rail ${orderedWindows.length === 1 ? "ph-single" : ""}`, "flow-children": "horizontal", "data-ph-focus-grid": "true", children: orderedWindows.map((entry, index) => (SP_JSX.jsx(WindowCard, { entry: entry, artwork: artwork[entry.handle] ?? "", index: index, copy: copy, onSelect: () => onSelectWindow(entry), onAskClose: () => void closeEntry(entry, index) }, entry.handle))) })) }));
}
function AppTile({ title, icon, fallback, onPress, onOptions, copy, extra, index = 0 }) {
    const Fallback = fallback ?? FiGrid;
    return (SP_JSX.jsxs(FocusItem, { className: "ph-app-tile", "data-ph-grid-index": index, style: { animation: "phReveal 260ms both", animationDelay: `${Math.min(index, 14) * 24}ms` }, onPress: onPress, onButtonDown: (event) => moveGridFocus(event, gridDirectionFromGamepad(event?.detail?.button)), onSecondaryButton: onOptions ? (event) => { stopEvent(event, true); onOptions(); } : undefined, onOKActionDescription: copy.open, onSecondaryActionDescription: onOptions ? extra?.options : undefined, onFocus: (event) => event?.currentTarget?.scrollIntoView?.({ behavior: "auto", block: "nearest" }), children: [icon ? SP_JSX.jsx("img", { src: icon, alt: "" }) : SP_JSX.jsx(Fallback, {}), SP_JSX.jsx("div", { className: "ph-app-name", children: title })] }));
}
function AppsTab({ copy, extra, onOptions, onLaunch, library, setLibrary }) {
    const [shortcuts, setShortcuts] = useState$1([]);
    const [programs, setPrograms] = useState$1([]);
    const [loading, setLoading] = useState$1(false);
    const libraryRequest = useRef(0);
    const refresh = useCallback(async () => setShortcuts(await listShortcuts()), []);
    useEffect$1(() => { void refresh(); }, [refresh]);
    useEffect$1(() => {
        if (!library) {
            libraryRequest.current++;
            setLoading(false);
        }
    }, [library]);
    const openLibrary = async () => {
        setLibrary(true);
        if (programs.length > 0)
            return;
        const request = ++libraryRequest.current;
        setLoading(true);
        const deadline = Date.now() + 22000;
        let result = await listPrograms();
        while (result.pending && result.items.length === 0 && Date.now() < deadline && request === libraryRequest.current) {
            await new Promise(resolve => setTimeout(resolve, 650));
            result = await listPrograms();
        }
        if (request !== libraryRequest.current)
            return;
        setPrograms(result.items.slice(0, 180));
        setLoading(false);
    };
    const closeLibrary = () => {
        libraryRequest.current++;
        setLoading(false);
        setLibrary(false);
    };
    if (library) {
        return (SP_JSX.jsxs("div", { className: "ph-page ph-page-scroll", children: [SP_JSX.jsxs("div", { className: "ph-toolbar", children: [SP_JSX.jsxs("div", { className: "ph-back", children: [SP_JSX.jsx(FiGrid, {}), SP_JSX.jsx("span", { children: copy.appLibrary })] }), SP_JSX.jsx(FocusItem, { className: "ph-confirm-button", style: { width: "150px" }, onPress: closeLibrary, children: copy.cancel })] }), loading ? SP_JSX.jsx("div", { className: "ph-empty", children: SP_JSX.jsxs("div", { children: [SP_JSX.jsx("div", { className: "ph-spinner" }), SP_JSX.jsx("div", { children: copy.loadingApps })] }) }) : programs.length === 0 ? (SP_JSX.jsx("div", { className: "ph-empty", children: SP_JSX.jsxs("div", { className: "ph-empty-inner", children: [SP_JSX.jsx(FiSearch, {}), SP_JSX.jsx("div", { className: "ph-empty-title", children: copy.noApps })] }) })) : (SP_JSX.jsx(FocusGrid, { className: "ph-grid", children: programs.map((program, index) => (SP_JSX.jsx(AppTile, { title: program.name, icon: iconSource(program.iconBase64), copy: copy, index: index, onPress: () => void addShortcut(program.target, program.name, program.kind).then(() => { void refresh(); setLibrary(false); }) }, `${program.kind}-${program.target}`))) }))] }));
    }
    return (SP_JSX.jsx("div", { className: "ph-page ph-page-scroll", children: SP_JSX.jsxs(FocusGrid, { className: "ph-grid", children: [SP_JSX.jsx(AppTile, { title: copy.addApp, fallback: FiPlus, copy: copy, extra: extra, onPress: () => void openLibrary() }), shortcuts.map((shortcut, index) => (SP_JSX.jsx(AppTile, { title: shortcut.name, icon: iconSource(shortcut.iconBase64), copy: copy, extra: extra, index: index + 1, onPress: () => onLaunch(shortcut), onOptions: () => onOptions(shortcut, refresh) }, shortcut.id)))] }) }));
}
function formatRate(value) {
    if (value >= 1024 ** 3)
        return `${(value / 1024 ** 3).toFixed(1)} GB/s`;
    if (value >= 1024 ** 2)
        return `${(value / 1024 ** 2).toFixed(1)} MB/s`;
    if (value >= 1024)
        return `${Math.round(value / 1024)} KB/s`;
    return `${Math.round(value)} B/s`;
}
function HistoryMetric({ title, value, detail, series, fixedPeak, index }) {
    const lineRef = useRef(null);
    const samplesRef = useRef([]);
    const targetRef = useRef(0);
    const displayedRef = useRef(0);
    const hasSampleRef = useRef(false);
    useEffect$1(() => {
        if (!series.length)
            return;
        const next = Number(series[series.length - 1]) || 0;
        if (!hasSampleRef.current) {
            hasSampleRef.current = true;
            targetRef.current = next;
            displayedRef.current = next;
            return;
        }
        samplesRef.current.push({ value: displayedRef.current, at: performance.now() });
        targetRef.current = next;
        if (samplesRef.current.length > 32)
            samplesRef.current.splice(0, samplesRef.current.length - 32);
    }, [series]);
    useEffect$1(() => {
        const windowMs = 65000;
        let frame = 0;
        let previousFrame = performance.now();
        const paint = (now) => {
            const elapsed = Math.min(50, now - previousFrame);
            previousFrame = now;
            const smoothing = 1 - Math.exp(-elapsed / 420);
            displayedRef.current += (targetRef.current - displayedRef.current) * smoothing;
            samplesRef.current = samplesRef.current.filter((sample) => now - sample.at <= windowMs);
            const samples = samplesRef.current;
            const peak = Math.max(1, fixedPeak ?? Math.max(displayedRef.current, ...samples.map((sample) => sample.value), 1));
            const historyPoints = samples.map((sample) => {
                const x = 182 - Math.min(1, (now - sample.at) / windowMs) * 182;
                const y = 38 - Math.min(1, sample.value / peak) * 34;
                return `${x.toFixed(2)},${y.toFixed(2)}`;
            });
            const liveY = 38 - Math.min(1, displayedRef.current / peak) * 34;
            const points = historyPoints.length ? [...historyPoints, `182.00,${liveY.toFixed(2)}`].join(" ") : "";
            lineRef.current?.setAttribute("points", points);
            frame = window.requestAnimationFrame(paint);
        };
        frame = window.requestAnimationFrame(paint);
        return () => window.cancelAnimationFrame(frame);
    }, [fixedPeak]);
    return (SP_JSX.jsxs(FocusItem, { className: "ph-history-card", "data-ph-grid-index": index, onPress: () => { }, onButtonDown: (event) => {
            const direction = gridDirectionFromGamepad(event?.detail?.button);
            if (direction === "down") {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (direction === "up") {
                stopDirectionalEvent(event);
                focusDashboard(".ph-tab.ph-active");
                return;
            }
            moveGridFocus(event, direction);
        }, onKeyDown: (event) => {
            const direction = gridDirectionFromKey(event.key);
            if (direction === "down") {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (direction === "up") {
                stopDirectionalEvent(event);
                focusDashboard(".ph-tab.ph-active");
                return;
            }
            moveGridFocus(event, direction);
        }, onFocus: (event) => {
            sound("move");
            const card = event?.currentTarget;
            card?.scrollIntoView?.({ behavior: "smooth", block: "nearest", inline: "nearest" });
        }, children: [SP_JSX.jsx("div", { className: "ph-history-label", children: title }), SP_JSX.jsx("div", { className: "ph-history-value", children: value }), SP_JSX.jsx("svg", { className: "ph-history-chart", viewBox: "0 0 184 40", preserveAspectRatio: "none", "aria-hidden": "true", children: SP_JSX.jsx("polyline", { ref: lineRef, points: "" }) }), SP_JSX.jsx("div", { className: "ph-history-detail", children: detail })] }));
}
function SystemTab({ copy, extra, onConfirm }) {
    const [usage, setUsage] = useState$1(null);
    const [processes, setProcesses] = useState$1([]);
    const [selectedProcess, setSelectedProcess] = useState$1(null);
    const [history, setHistory] = useState$1({});
    const [deckyRestarting, setDeckyRestarting] = useState$1(false);
    const deckyRestartingRef = useRef(false);
    const refreshing = useRef(false);
    const refresh = useCallback(async () => {
        if (refreshing.current)
            return;
        refreshing.current = true;
        try {
            const [nextUsage, nextProcesses] = await Promise.all([readUsage(), listProcesses()]);
            setUsage(nextUsage);
            if (nextUsage) {
                const samples = {
                    cpu: nextUsage.cpuPercent,
                    gpu: nextUsage.gpuAvailable ? nextUsage.gpuPercent : 0,
                    memory: nextUsage.memoryPercent,
                    network: nextUsage.networkBytesPerSecond,
                };
                nextUsage.disks.forEach((disk) => { samples[`disk:${disk.name}`] = disk.bytesPerSecond; });
                setHistory((current) => {
                    const next = { ...current };
                    Object.entries(samples).forEach(([key, value]) => { next[key] = [...(current[key] ?? []), value].slice(-28); });
                    return next;
                });
            }
            setProcesses((current) => {
                if (current.length === 0)
                    return nextProcesses;
                const incoming = new Map(nextProcesses.map((entry) => [entry.id, entry]));
                const known = new Set(current.map((entry) => entry.id));
                return [
                    ...current.filter((entry) => incoming.has(entry.id)).map((entry) => incoming.get(entry.id)),
                    ...nextProcesses.filter((entry) => !known.has(entry.id)),
                ];
            });
        }
        finally {
            refreshing.current = false;
        }
    }, []);
    useEffect$1(() => { void refresh(); const timer = window.setInterval(() => void refresh(), 2500); return () => window.clearInterval(timer); }, [refresh]);
    const requestDeckyRestart = useCallback(() => {
        if (deckyRestartingRef.current)
            return;
        deckyRestartingRef.current = true;
        setDeckyRestarting(true);
        // Decky ricarica o chiude questa pagina durante il riavvio. Lo spinner
        // resta quindi attivo per tutta la vita residua della pagina, anche se la
        // richiesta HTTP termina prima del riavvio effettivo.
        void restartDecky();
    }, []);
    return (SP_JSX.jsxs("div", { className: "ph-page ph-page-scroll", children: [SP_JSX.jsxs("div", { className: "ph-system-stack", children: [SP_JSX.jsxs(FocusGrid, { className: "ph-metrics-rail", onWheel: (event) => { if (Math.abs(event.deltaY) > Math.abs(event.deltaX))
                            event.currentTarget.scrollLeft += event.deltaY; }, children: [SP_JSX.jsx(HistoryMetric, { index: 0, title: copy.cpu, value: `${Math.round(usage?.cpuPercent ?? 0)}%`, detail: `${navigator.hardwareConcurrency || "--"} core`, series: history.cpu ?? [], fixedPeak: 100 }), SP_JSX.jsx(HistoryMetric, { index: 1, title: copy.gpu, value: usage?.gpuAvailable ? `${Math.round(usage.gpuPercent)}%` : "--", detail: usage?.gpuAvailable ? "GPU" : "--", series: history.gpu ?? [], fixedPeak: 100 }), SP_JSX.jsx(HistoryMetric, { index: 2, title: copy.memory, value: `${Math.round(usage?.memoryPercent ?? 0)}%`, detail: `${((usage?.memoryUsedMb ?? 0) / 1024).toFixed(1)} / ${((usage?.memoryTotalMb ?? 0) / 1024).toFixed(1)} GB`, series: history.memory ?? [], fixedPeak: 100 }), SP_JSX.jsx(HistoryMetric, { index: 3, title: extra.network, value: formatRate(usage?.networkBytesPerSecond ?? 0), detail: extra.network, series: history.network ?? [] }), (usage?.disks ?? []).map((disk, diskIndex) => SP_JSX.jsx(HistoryMetric, { index: 4 + diskIndex, title: `${extra.disk} ${disk.name}`, value: formatRate(disk.bytesPerSecond), detail: disk.kind ? disk.kind.toUpperCase() : extra.disk, series: history[`disk:${disk.name}`] ?? [] }, disk.name))] }), SP_JSX.jsx("div", { className: "ph-system-lower", children: SP_JSX.jsxs("div", { className: "ph-tile ph-process-panel", children: [SP_JSX.jsxs("div", { className: "ph-process-heading", children: [SP_JSX.jsx("div", { className: "ph-device-list-head", style: { padding: 0 }, children: copy.processes }), SP_JSX.jsxs(FocusItem, { className: `ph-restart-decky${deckyRestarting ? " ph-busy" : ""}`, "aria-busy": deckyRestarting ? "true" : "false", "aria-disabled": deckyRestarting ? "true" : "false", onPress: () => {
                                                if (deckyRestartingRef.current)
                                                    return;
                                                onConfirm(copy.confirmAction, copy.restartDecky, requestDeckyRestart);
                                            }, onButtonDown: (event) => {
                                                const direction = gridDirectionFromGamepad(event?.detail?.button);
                                                if (direction === "up") {
                                                    stopDirectionalEvent(event);
                                                    focusSystemMetric(0);
                                                }
                                                if (direction === "down") {
                                                    stopDirectionalEvent(event);
                                                    focusDashboard(".ph-process-row");
                                                }
                                            }, onKeyDown: (event) => {
                                                const direction = gridDirectionFromKey(event.key);
                                                if (direction === "up") {
                                                    stopDirectionalEvent(event);
                                                    focusSystemMetric(0);
                                                }
                                                if (direction === "down") {
                                                    stopDirectionalEvent(event);
                                                    focusDashboard(".ph-process-row");
                                                }
                                            }, children: [SP_JSX.jsx(FiRefreshCw, {}), SP_JSX.jsx("span", { children: copy.restartDecky })] })] }), SP_JSX.jsx(Focusable, { className: "ph-process-list", "flow-children": "vertical", children: processes.slice(0, 18).map((process) => (SP_JSX.jsxs(FocusItem, { className: "ph-process-row", onPress: () => setSelectedProcess(process), onOKActionDescription: copy.open, onButtonDown: (event) => {
                                            const direction = gridDirectionFromGamepad(event?.detail?.button);
                                            if (direction === "up" && process === processes[0]) {
                                                stopDirectionalEvent(event);
                                                focusDashboard(".ph-restart-decky");
                                            }
                                            if (direction === "right") {
                                                stopDirectionalEvent(event);
                                                focusDashboard(".ph-restart-decky");
                                            }
                                        }, onKeyDown: (event) => {
                                            const direction = gridDirectionFromKey(event.key);
                                            if (direction === "up" && process === processes[0]) {
                                                stopDirectionalEvent(event);
                                                focusDashboard(".ph-restart-decky");
                                            }
                                            if (direction === "right") {
                                                stopDirectionalEvent(event);
                                                focusDashboard(".ph-restart-decky");
                                            }
                                        }, onFocus: (event) => {
                                            const item = event?.currentTarget;
                                            const container = item?.closest?.(".ph-process-list");
                                            if (!item || !container)
                                                return;
                                            const itemTop = item.getBoundingClientRect().top - container.getBoundingClientRect().top + container.scrollTop;
                                            const top = itemTop - Math.max(0, (container.clientHeight - item.offsetHeight) / 2);
                                            container.scrollTo({ top: Math.max(0, top), behavior: "auto" });
                                        }, children: [SP_JSX.jsx("div", { className: "ph-process-name", children: process.name }), SP_JSX.jsxs("div", { className: "ph-process-stat", children: [process.cpuPercent, "%"] }), SP_JSX.jsxs("div", { className: "ph-process-stat", children: [Math.round(process.memoryMb), " MB"] })] }, process.id))) })] }) })] }), selectedProcess ? (SP_JSX.jsx(ProcessOptionsSheet, { process: selectedProcess, copy: copy, onCancel: () => setSelectedProcess(null), onClose: () => {
                    const current = selectedProcess;
                    setSelectedProcess(null);
                    void closeProcess(current.id).then(() => window.setTimeout(refresh, 450));
                }, onKill: () => {
                    const current = selectedProcess;
                    setSelectedProcess(null);
                    void killProcess(current.id).then(() => window.setTimeout(refresh, 450));
                } })) : null] }));
}
function ProcessOptionsSheet({ process, copy, onCancel, onClose, onKill }) {
    useEffect$1(() => {
        const timer = window.setTimeout(() => focusDashboard(".ph-process-options .ph-confirm-button"), 40);
        return () => window.clearTimeout(timer);
    }, []);
    return (SP_JSX.jsx(Focusable, { className: "ph-confirm-backdrop ph-process-options", onCancel: (event) => { stopEvent(event, true); onCancel(); }, onCancelButton: (event) => { stopEvent(event, true); onCancel(); }, children: SP_JSX.jsxs("div", { className: "ph-confirm", children: [SP_JSX.jsx("div", { className: "ph-confirm-title", children: process.name }), SP_JSX.jsxs("div", { className: "ph-confirm-name", children: ["PID ", process.id] }), SP_JSX.jsxs(Focusable, { className: "ph-confirm-actions ph-three", "flow-children": "horizontal", children: [SP_JSX.jsx(FocusItem, { preferredFocus: true, className: "ph-confirm-button", onPress: onCancel, children: copy.cancel }), SP_JSX.jsx(FocusItem, { className: "ph-confirm-button", onPress: onClose, children: copy.close }), SP_JSX.jsx(FocusItem, { className: "ph-confirm-button ph-danger", onPress: onKill, children: copy.forceClose })] })] }) }));
}
function ConfirmSheet({ title, name, copy, onCancel, onConfirm }) {
    useEffect$1(() => {
        const timer = window.setTimeout(() => focusDashboard(".ph-confirm-button"), 40);
        return () => window.clearTimeout(timer);
    }, []);
    return (SP_JSX.jsx(Focusable, { className: "ph-confirm-backdrop", onCancel: (event) => { stopEvent(event, true); onCancel(); }, onCancelButton: (event) => { stopEvent(event, true); onCancel(); }, children: SP_JSX.jsxs("div", { className: "ph-confirm", children: [SP_JSX.jsx("div", { className: "ph-confirm-title", children: title }), SP_JSX.jsx("div", { className: "ph-confirm-name", children: name }), SP_JSX.jsxs(Focusable, { className: "ph-confirm-actions", "flow-children": "horizontal", children: [SP_JSX.jsx(FocusItem, { preferredFocus: true, className: "ph-confirm-button", onPress: onCancel, children: copy.cancel }), SP_JSX.jsx(FocusItem, { className: "ph-confirm-button ph-danger", onPress: onConfirm, children: copy.continue })] })] }) }));
}
function AppOptionsSheet({ shortcut, copy, extra, onCancel, onRemove, onSave }) {
    const [name, setName] = useState$1(shortcut.name);
    useEffect$1(() => {
        const timer = window.setTimeout(() => focusDashboard(".ph-options-field input"), 50);
        return () => window.clearTimeout(timer);
    }, []);
    return (SP_JSX.jsx(Focusable, { className: "ph-confirm-backdrop", onCancel: (event) => { stopEvent(event, true); onCancel(); }, onCancelButton: (event) => { stopEvent(event, true); onCancel(); }, children: SP_JSX.jsxs("div", { className: "ph-confirm", children: [SP_JSX.jsx("div", { className: "ph-confirm-title", children: extra.options }), SP_JSX.jsx("div", { className: "ph-confirm-name", children: shortcut.name }), SP_JSX.jsx("div", { className: "ph-options-field", children: SP_JSX.jsx(TextField, { value: name, onChange: (event) => setName(event.target.value), style: { width: "100%", minWidth: 0 } }) }), SP_JSX.jsxs(Focusable, { className: "ph-confirm-actions ph-three", "flow-children": "horizontal", children: [SP_JSX.jsx(FocusItem, { className: "ph-confirm-button", onPress: onCancel, children: copy.cancel }), SP_JSX.jsx(FocusItem, { className: "ph-confirm-button ph-danger", onPress: onRemove, children: copy.remove }), SP_JSX.jsx(FocusItem, { className: "ph-confirm-button", onPress: () => { if (name.trim())
                                onSave(name.trim()); }, children: extra.save })] })] }) }));
}
function DashboardSurface() {
    const [environment, setEnvironment] = useState$1(null);
    const [logo, setLogo] = useState$1("");
    const [tab, setTab] = useState$1("switcher");
    const [appsLibrary, setAppsLibrary] = useState$1(false);
    const [confirm, setConfirm] = useState$1(null);
    const [appOptions, setAppOptions] = useState$1(null);
    const root = useRef(null);
    const lastCancelAt = useRef(0);
    const modalReturnFocus = useRef(null);
    const explicitExit = useRef(false);
    const [steamLocale, setSteamLocale] = useState$1(detectSteamLocale$1);
    const copy = COPY[steamLocale] ?? COPY.en;
    const extra = EXTRA_COPY[steamLocale] ?? EXTRA_COPY.en;
    useEffect$1(() => {
        let alive = true;
        void readSteamLocale$1().then((locale) => { if (alive)
            setSteamLocale(locale); });
        return () => { alive = false; };
    }, []);
    useEffect$1(() => {
        let alive = true;
        void readEnvironment().then(async (value) => {
            if (!alive)
                return;
            setEnvironment(value);
            if (value?.logoPath)
                setLogo(await loadImage(value.logoPath));
        });
        return () => { alive = false; };
    }, []);
    useEffect$1(() => {
        logToAgent("pagina Dashboard aperta");
        return () => {
            logToAgent("pagina Dashboard chiusa");
            // A route replacement, a plugin reload or an unexpected React unmount
            // must never leave Steam temporarily topmost above the source window.
            if (!explicitExit.current)
                void restoreDashboardSourceFocus();
        };
    }, []);
    useLayoutEffect(() => {
        markDashboardChrome();
        const followUps = [30, 90, 220, 500, 1000].map((delay) => window.setTimeout(markDashboardChrome, delay));
        return () => {
            followUps.forEach((timer) => window.clearTimeout(timer));
            clearDashboardChrome();
        };
    }, []);
    const tabs = useMemo$1(() => ["switcher", "apps", "system"], []);
    const changeTab = (next) => {
        setConfirm(null);
        setAppOptions(null);
        setAppsLibrary(false);
        setTab(next);
    };
    useEffect$1(() => {
        if (tab === "switcher")
            return;
        const frame = window.requestAnimationFrame(() => {
            activateDashboardSteamContext();
            focusDashboard(".ph-page [data-ph-focusable='true']") || focusDashboard(".ph-tab.ph-active");
        });
        return () => window.cancelAnimationFrame(frame);
    }, [tab, appsLibrary]);
    const focusInitialSwitcher = useCallback(() => {
        activateDashboardSteamContext();
        focusDashboard(".ph-window-card[data-window-primary='true']")
            || focusDashboard(".ph-window-card")
            || ensureDashboardFocus();
    }, []);
    const rememberModalFocus = useCallback(() => {
        const active = currentDashboardFocus();
        if (active && !active.closest(".ph-confirm-backdrop"))
            modalReturnFocus.current = active;
    }, []);
    const restoreModalFocus = useCallback(() => {
        const target = modalReturnFocus.current;
        window.requestAnimationFrame(() => window.requestAnimationFrame(() => {
            activateDashboardSteamContext();
            if (target?.isConnected) {
                try {
                    target.focus({ preventScroll: true });
                    return;
                }
                catch { }
            }
            ensureDashboardFocus();
        }));
    }, []);
    const dismissConfirm = useCallback(() => {
        setConfirm(null);
        restoreModalFocus();
    }, [restoreModalFocus]);
    const dismissAppOptions = useCallback(() => {
        setAppOptions(null);
        restoreModalFocus();
    }, [restoreModalFocus]);
    const ask = (title, name, action) => {
        rememberModalFocus();
        setConfirm({ title, name, action });
    };
    const leaveDashboard = useCallback((focus, value = "") => {
        explicitExit.current = true;
        closeDashboardOverlay();
        clearDashboardChrome();
        Navigation$1?.NavigateBack?.();
        // Steam can leave its empty side-menu backdrop mounted when a custom
        // route returns directly to the library. Remove that native layer after
        // the route transition so the library is never left blurred.
        window.setTimeout(() => Navigation$1?.CloseSideMenus?.(), 60);
        if (focus === "source") {
            void restoreDashboardSourceFocus();
        }
        else if (focus === "steam") {
            void releaseDashboardFocus();
        }
        else if (focus === "window") {
            window.setTimeout(() => { void switchOverlayWindow(value); }, 40);
        }
        else {
            window.setTimeout(() => { void launchOverlayShortcut(value); }, 40);
        }
    }, []);
    const closeDashboard = useCallback(() => leaveDashboard("source"), [leaveDashboard]);
    const selectWindow = useCallback((entry) => {
        const isSteam = /^(steam|steamwebhelper)$/i.test(entry.processName);
        leaveDashboard(isSteam ? "steam" : "window", entry.handle);
    }, [leaveDashboard]);
    const launchShortcut = useCallback((shortcut) => {
        leaveDashboard("app", shortcut.id);
    }, [leaveDashboard]);
    const cancelDashboard = useCallback((event) => {
        const now = performance.now();
        // Steam can dispatch the same physical B press through both onCancel and
        // onCancelButton. Keep one deliberate action per press so nested views do
        // not collapse all the way out of the Dashboard.
        if (now - lastCancelAt.current < 420)
            return true;
        lastCancelAt.current = now;
        stopEvent(event, true);
        if (appOptions) {
            dismissAppOptions();
            return true;
        }
        if (confirm) {
            dismissConfirm();
            return true;
        }
        if (tab === "apps" && appsLibrary) {
            setAppsLibrary(false);
            return true;
        }
        if (tab !== "switcher") {
            changeTab("switcher");
            return true;
        }
        closeDashboard();
        return true;
    }, [appOptions, confirm, tab, appsLibrary, closeDashboard, dismissAppOptions, dismissConfirm]);
    useEffect$1(() => {
        const onKeyDown = (event) => {
            // Ctrl+Alt+P is owned by the agent's global RegisterHotKey. Handling it
            // here as well made the opening key press reach the freshly mounted page
            // and immediately close it again when Steam was not initially focused.
            const direction = gridDirectionFromKey(event.key);
            const active = currentDashboardFocus();
            if (direction === "up" && active?.classList.contains("ph-tab")) {
                stopDirectionalEvent(event);
                return;
            }
            if (tab === "system" && direction === "down" && active?.classList.contains("ph-tab")) {
                stopDirectionalEvent(event);
                focusSystemMetric(0);
                return;
            }
            if (tab === "system" && direction === "up" && active?.classList.contains("ph-history-card")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-tab.ph-active");
                return;
            }
            if (tab === "system" && direction === "down" && active?.classList.contains("ph-history-card")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (tab === "system" && direction === "up" && active?.classList.contains("ph-restart-decky")) {
                stopDirectionalEvent(event);
                focusSystemMetric(0);
                return;
            }
            if (tab === "system" && direction === "down" && active?.classList.contains("ph-restart-decky")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-process-row");
                return;
            }
            if (tab === "system" && direction === "up" && active?.classList.contains("ph-process-row") && active === dashboardRoot()?.querySelector(".ph-process-row")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (direction === "right" && active?.classList.contains("ph-process-row")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (direction && moveGridFocus(event, direction))
                return;
            if (event.key === "Escape") {
                event.preventDefault();
                event.stopPropagation();
                cancelDashboard(event);
            }
        };
        const targets = dashboardDocuments().map((targetDocument) => targetDocument.defaultView).filter(Boolean);
        targets.forEach((targetWindow) => targetWindow.addEventListener("keydown", onKeyDown, true));
        return () => targets.forEach((targetWindow) => targetWindow.removeEventListener("keydown", onKeyDown, true));
    }, [cancelDashboard, tab]);
    return (SP_JSX.jsxs(Focusable, { ref: root, className: "ph-dashboard", noFocusRing: true, onButtonDown: (event) => {
            const button = Number(event?.detail?.button);
            const active = currentDashboardFocus();
            if (button === GamepadButton?.DIR_UP && active?.classList.contains("ph-tab")) {
                stopDirectionalEvent(event);
                return;
            }
            if (tab === "system" && button === GamepadButton?.DIR_DOWN && active?.classList.contains("ph-tab")) {
                stopDirectionalEvent(event);
                focusSystemMetric(0);
                return;
            }
            if (tab === "system" && button === GamepadButton?.DIR_UP && active?.classList.contains("ph-history-card")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-tab.ph-active");
                return;
            }
            if (tab === "system" && button === GamepadButton?.DIR_DOWN && active?.classList.contains("ph-history-card")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (tab === "system" && button === GamepadButton?.DIR_UP && active?.classList.contains("ph-restart-decky")) {
                stopDirectionalEvent(event);
                focusSystemMetric(0);
                return;
            }
            if (tab === "system" && button === GamepadButton?.DIR_DOWN && active?.classList.contains("ph-restart-decky")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-process-row");
                return;
            }
            if (tab === "system" && button === GamepadButton?.DIR_UP && active?.classList.contains("ph-process-row") && active === dashboardRoot()?.querySelector(".ph-process-row")) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (tab === "system" && active?.classList.contains("ph-history-card")) {
                const direction = gridDirectionFromGamepad(button);
                if (direction === "left" || direction === "right") {
                    moveGridFocus(event, direction);
                    return;
                }
            }
            const processRow = event?.target?.closest?.(".ph-process-row");
            if (processRow && button === GamepadButton?.DIR_RIGHT) {
                stopDirectionalEvent(event);
                focusDashboard(".ph-restart-decky");
                return;
            }
            if (button !== 5 && button !== 6)
                return;
            stopEvent(event, true);
            if (confirm || appOptions)
                return;
            const current = Math.max(0, tabs.indexOf(tab));
            changeTab(tabs[(current + (button === 5 ? -1 : 1) + tabs.length) % tabs.length]);
        }, onButtonUp: (event) => {
            const button = Number(event?.detail?.button);
            if (button === 5 || button === 6)
                stopEvent(event, true);
        }, onCancel: cancelDashboard, onCancelButton: cancelDashboard, onCancelActionDescription: copy.close, children: [SP_JSX.jsx("style", { children: STYLE }), SP_JSX.jsx(Header, { tab: tab, setTab: changeTab, copy: copy, locale: steamLocale, logo: logo }), tab === "system" ? SP_JSX.jsx(FocusItem, { className: "ph-system-focus-bridge", onPress: () => { }, onFocus: () => window.requestAnimationFrame(() => focusSystemMetric(0)) }) : null, SP_JSX.jsxs("main", { className: "ph-main", children: [tab === "switcher" ? SP_JSX.jsx(TaskSwitcher, { copy: copy, onReady: focusInitialSwitcher, onSelectWindow: selectWindow }) : null, tab === "apps" ? SP_JSX.jsx(AppsTab, { copy: copy, extra: extra, onOptions: (shortcut, refresh) => { rememberModalFocus(); setAppOptions({ shortcut, refresh }); }, onLaunch: launchShortcut, library: appsLibrary, setLibrary: setAppsLibrary }) : null, tab === "system" ? SP_JSX.jsx(SystemTab, { copy: copy, extra: extra, onConfirm: ask }) : null] }, tab), confirm ? (SP_JSX.jsx(ConfirmSheet, { title: confirm.title, name: confirm.name, copy: copy, onCancel: dismissConfirm, onConfirm: () => { const action = confirm.action; dismissConfirm(); action(); window.setTimeout(ensureDashboardFocus, 520); } })) : null, appOptions ? (SP_JSX.jsx(AppOptionsSheet, { shortcut: appOptions.shortcut, copy: copy, extra: extra, onCancel: dismissAppOptions, onRemove: () => {
                    const current = appOptions;
                    setAppOptions(null);
                    ask(copy.confirmRemove, current.shortcut.name, () => void removeShortcut(current.shortcut.id).then(current.refresh));
                }, onSave: (name) => {
                    const current = appOptions;
                    void renameShortcut(current.shortcut.id, name).then(current.refresh).finally(dismissAppOptions);
                } })) : null] }));
}
function DashboardPage() {
    const inOverlay = Boolean(dashboardOverlayGameId);
    const [portalTarget, setPortalTarget] = useState$1(null);
    useEffect$1(() => {
        if (!inOverlay)
            return;
        let alive = true;
        const startedAt = performance.now();
        const timer = window.setInterval(() => {
            if (!alive)
                return;
            const targetDocument = overlayDocument();
            if (targetDocument?.body) {
                let target = targetDocument.getElementById("ph-dashboard-overlay-root");
                if (!target) {
                    target = targetDocument.createElement("div");
                    target.id = "ph-dashboard-overlay-root";
                    targetDocument.body.appendChild(target);
                }
                setPortalTarget(target);
                window.clearInterval(timer);
                return;
            }
            if (performance.now() - startedAt >= 1100) {
                window.clearInterval(timer);
                closeDashboardOverlay();
                Navigation$1?.NavigateBack?.();
                void restoreDashboardSourceFocus();
                logToAgent("overlay Dashboard annullato: browser Steam non disponibile");
            }
        }, 40);
        return () => { alive = false; window.clearInterval(timer); };
    }, [inOverlay]);
    useEffect$1(() => {
        if (!inOverlay || !portalTarget)
            return;
        let leaving = false;
        const returnToSource = () => {
            if (leaving)
                return;
            leaving = true;
            closeDashboardOverlay();
            Navigation$1?.NavigateBack?.();
            void restoreDashboardSourceFocus();
        };
        const frame = window.requestAnimationFrame(() => {
            const mounted = Boolean(portalTarget.querySelector(".ph-dashboard"));
            if (!mounted || !dashboardOverlayGameId) {
                returnToSource();
                return;
            }
            try {
                window.SteamClient?.Overlay?.SetOverlayState?.(dashboardOverlayGameId, 2);
            }
            catch { }
        });
        const monitor = window.setInterval(() => {
            if (portalTarget.isConnected && dashboardOverlayGameId)
                return;
            returnToSource();
        }, 250);
        return () => {
            window.cancelAnimationFrame(frame);
            window.clearInterval(monitor);
        };
    }, [inOverlay, portalTarget]);
    useEffect$1(() => () => {
        if (inOverlay)
            closeDashboardOverlay();
        try {
            portalTarget?.remove();
        }
        catch { }
    }, [inOverlay, portalTarget]);
    if (!inOverlay)
        return SP_JSX.jsx(DashboardSurface, {});
    return portalTarget ? SP_REACTDOM.createPortal(SP_JSX.jsx(DashboardSurface, {}), portalTarget) : null;
}

const MODERN_HAPTIC_CONTROLLERS = new Set([10, 48]);
const pending = new Set();
let config = { enabled: false, intensity: 55 };
let lastControllerIndex = 0;
const soundRequests = new Map();
const soundPlayerPatches = [];
const uiAudioManagers = new Set();
const patchedManagerPrototypes = new Set();
const patchedPlaybackPrototypes = new Set();
const patchedAudioSourcePrototypes = new Set();
const soundBuffersByUrl = new Map();
const soundBuffersByAction = new Map();
const waveformTimers = new Set();
let waveformGeneration = 0;
let lastNativeRequestId = Math.floor(Date.now() * 1000);
const gamepadTimestamps = new Map();
function clampIntensity(value) {
    return Math.max(5, Math.min(100, Math.round(value)));
}
function steamRoots() {
    const roots = [window];
    try {
        const steam = _global_DFL?.findSP?.();
        if (steam && !roots.includes(steam))
            roots.push(steam);
        if (steam?.window && !roots.includes(steam.window))
            roots.push(steam.window);
    }
    catch { }
    try {
        const trees = _global_DFL?.getGamepadNavigationTrees?.() ?? [];
        for (const tree of trees) {
            const treeWindow = tree?.Root?.Element?.ownerDocument?.defaultView;
            if (treeWindow && !roots.includes(treeWindow))
                roots.push(treeWindow);
        }
    }
    catch { }
    for (const root of [...roots]) {
        const popups = root?.g_PopupManager?.m_mapPopups;
        if (!popups?.values)
            continue;
        for (const popup of Array.from(popups.values())) {
            const popupWindow = popup?.m_popup ?? popup?.window;
            if (popupWindow && !roots.includes(popupWindow))
                roots.push(popupWindow);
        }
    }
    return roots;
}
function steamIsForeground() {
    for (const root of steamRoots()) {
        const doc = root?.document;
        if (doc?.visibilityState === "visible" && doc?.hasFocus?.())
            return true;
        if (root?.m_bFocused)
            return true;
    }
    return false;
}
function focusedControl() {
    try {
        const controller = _global_DFL?.getFocusNavController?.();
        const context = controller?.m_ActiveContext ?? controller?.m_LastActiveContext;
        const tree = context?.m_LastActiveFocusNavTree ?? context?.m_LastActiveNavTree;
        const element = tree?.m_lastFocusNode?.Element
            ?? tree?.m_lastFocusNode?.m_element
            ?? tree?.GetLastFocusedNode?.()?.Element
            ?? tree?.GetLastFocusedNode?.()?.m_element;
        if (element?.isConnected)
            return element;
    }
    catch { }
    for (const root of steamRoots()) {
        const doc = root?.document;
        if (!doc)
            continue;
        const gamepadFocus = doc.querySelector(".gpfocus, [data-gp-focus='true'], [data-gpfocus='true']");
        if (gamepadFocus)
            return gamepadFocus;
        const active = doc.activeElement;
        if (active && active !== doc.body && (doc.hasFocus?.() || active.matches?.(":focus")))
            return active;
    }
    return null;
}
function controlKind(element) {
    if (!element)
        return null;
    const selector = '[role="slider"], input[type="range"], [aria-haspopup], [role="combobox"]';
    const control = (element.closest?.(selector) ?? element.querySelector?.(selector));
    if (!control)
        return null;
    if (control.matches('[role="slider"], input[type="range"]'))
        return "slider";
    const popup = control.getAttribute("aria-haspopup");
    return popup && popup !== "false" ? "dropdown" : control.getAttribute("role") === "combobox" ? "dropdown" : null;
}
function toggleAction(element) {
    if (!element)
        return null;
    const selector = '[role="switch"], [role="checkbox"], [aria-checked]';
    const toggle = (element.closest?.(selector) ?? element.querySelector?.(selector));
    if (!toggle)
        return null;
    return toggle.getAttribute("aria-checked") === "true" ? "toggleOff" : "toggleOn";
}
function sliderValue(element) {
    if (!element)
        return null;
    const selector = '[role="slider"], input[type="range"]';
    const slider = (element.closest?.(selector) ?? element.querySelector?.(selector));
    const value = Number(slider?.getAttribute("aria-valuenow") ?? slider?.value);
    return Number.isFinite(value) ? value : null;
}
function controllerFor(index) {
    const store = window.ControllerStore;
    const controllers = Array.from(store?.GetControllers?.() ?? []);
    return store?.GetController?.(index)
        ?? controllers.find((controller) => Number(controller?.nControllerIndex) === index)
        ?? controllers.find((controller) => Number(controller?.nXInputIndex) === index && index !== 0xffffffff)
        ?? (controllers.length === 1 ? controllers[0] : undefined);
}
function usesModernHaptics(controller) {
    const vendor = Number(controller?.unVendorID);
    const product = Number(controller?.unProductID);
    const dualSense = vendor === 0x054c && (product === 0x0ce6 || product === 0x0df2);
    return !dualSense && MODERN_HAPTIC_CONTROLLERS.has(Number(controller?.eControllerType));
}
function activeController(preferredIndex) {
    const controller = controllerFor(preferredIndex);
    if (!controller)
        return undefined;
    const index = Number(controller?.nControllerIndex);
    return Number.isFinite(index) ? controller : undefined;
}
function controllerFromActiveSlot(value) {
    const slot = Number(value?.nActiveController ?? value);
    if (!Number.isInteger(slot) || slot < 0)
        return undefined;
    const store = window.ControllerStore;
    const controllers = Array.from(store?.GetControllers?.() ?? []);
    const controller = controllers[slot] ?? store?.GetController?.(slot);
    return Number.isFinite(Number(controller?.nControllerIndex)) ? controller : undefined;
}
function gamepadHardwareId(id) {
    const match = id.match(/Vendor:\s*([0-9a-f]{4})\s+Product:\s*([0-9a-f]{4})/i);
    if (!match)
        return null;
    return { vendor: Number.parseInt(match[1], 16), product: Number.parseInt(match[2], 16) };
}
function primeGamepadTimestamps() {
    gamepadTimestamps.clear();
    for (const gamepad of Array.from(navigator.getGamepads?.() ?? []).filter(Boolean)) {
        gamepadTimestamps.set(gamepad.index, Number(gamepad.timestamp) || 0);
    }
}
function later(callback, delay) {
    const timer = window.setTimeout(() => {
        pending.delete(timer);
        callback();
    }, delay);
    pending.add(timer);
    return timer;
}
function nextNativeRequestId() {
    lastNativeRequestId = Math.max(lastNativeRequestId + 1, Math.floor(Date.now() * 1000));
    return lastNativeRequestId;
}
function actionSide(action) {
    if (action === "moveLeft" || action === "sliderDecrease" || action === "tabPrevious")
        return 0;
    if (action === "moveRight" || action === "sliderIncrease" || action === "tabNext")
        return 1;
    return 2;
}
function discardSoundRequest(request) {
    window.clearTimeout(request.timer);
    pending.delete(request.timer);
    soundRequests.delete(request.id);
}
function actionPattern(action) {
    const side = actionSide(action);
    switch (action) {
        case "moveLeft":
        case "moveRight":
        case "moveUp":
        case "moveDown":
            return [{ delay: 0, scale: 1.18, kind: 2, side, duration: 19 }];
        case "tabPrevious":
        case "tabNext":
            return [
                { delay: 0, scale: 1.2, kind: 2, side, duration: 22 },
                { delay: 38, scale: 0.68, kind: 1, side, duration: 17 },
            ];
        case "sliderDecrease":
        case "sliderIncrease":
            return [
                { delay: 0, scale: 0.95, kind: 1, side, duration: 16 },
                { delay: 18, scale: 0.52, kind: 2, side, duration: 14 },
            ];
        case "toggleOn":
            return [
                { delay: 0, scale: 0.58, kind: 1, side: 0, duration: 18 },
                { delay: 34, scale: 1.25, kind: 2, side: 1, duration: 26 },
            ];
        case "toggleOff":
            return [
                { delay: 0, scale: 0.58, kind: 1, side: 1, duration: 18 },
                { delay: 34, scale: 1.08, kind: 2, side: 0, duration: 24 },
            ];
        case "confirm":
            return [
                { delay: 0, scale: 1.25, kind: 2, side: 2, duration: 25 },
                { delay: 46, scale: 0.62, kind: 1, side: 2, duration: 18 },
            ];
        case "back":
            return [
                { delay: 0, scale: 1.08, kind: 2, side: 0, duration: 22 },
                { delay: 32, scale: 0.48, kind: 1, side: 0, duration: 16 },
            ];
        case "dropdown":
            return [
                { delay: 0, scale: 0.68, kind: 1, side: 2, duration: 18 },
                { delay: 28, scale: 1.12, kind: 2, side: 2, duration: 24 },
            ];
        case "options":
        case "menu":
            return [
                { delay: 0, scale: 0.78, kind: 1, side: 2, duration: 18 },
                { delay: 30, scale: 1.16, kind: 2, side: 2, duration: 23 },
                { delay: 65, scale: 0.52, kind: 1, side: 2, duration: 16 },
            ];
        case "letter":
            return [{ delay: 0, scale: 1.28, kind: 2, side: 2, duration: 27 }];
    }
}
function playActionPattern(controllerIndex, action) {
    waveformGeneration += 1;
    const generation = waveformGeneration;
    waveformTimers.forEach((timer) => {
        window.clearTimeout(timer);
        pending.delete(timer);
    });
    waveformTimers.clear();
    const controller = activeController(controllerIndex);
    if (!controller)
        return;
    const index = Number(controller.nControllerIndex);
    const modern = usesModernHaptics(controller);
    if (!modern) {
        const vendor = Number(controller.unVendorID);
        if (vendor === 0x045e) {
            playLegacyPattern(index, action, generation);
            return;
        }
        void nativePattern(index, action).then((handled) => {
            if (!handled && generation === waveformGeneration && config.enabled && steamIsForeground()) {
                playLegacyPattern(index, action, generation);
            }
        });
        return;
    }
    for (const step of actionPattern(action)) {
        const emit = () => {
            if (generation !== waveformGeneration || !config.enabled || !steamIsForeground())
                return;
            const side = step.side ?? actionSide(action);
            modernPulse(index, side, step.kind, step.scale);
        };
        if (step.delay === 0)
            emit();
        else {
            let timer = 0;
            timer = later(() => {
                waveformTimers.delete(timer);
                emit();
            }, step.delay);
            waveformTimers.add(timer);
        }
    }
}
function playLegacyPattern(index, action, generation) {
    for (const step of actionPattern(action)) {
        const emit = () => {
            if (generation !== waveformGeneration || !config.enabled || !steamIsForeground())
                return;
            rumblePulse(index, step.side ?? actionSide(action), step.scale, step.duration ?? 20);
        };
        if (step.delay === 0)
            emit();
        else {
            let timer = 0;
            timer = later(() => {
                waveformTimers.delete(timer);
                emit();
            }, step.delay);
            waveformTimers.add(timer);
        }
    }
}
function modernPulse(index, side, kind, scale) {
    const input = window.SteamClient?.Input;
    const normalizedShape = Math.min(1, Math.max(0.03, scale));
    const soundShape = 0.18 + normalizedShape * 0.82;
    const requestedAmplitude = Math.max(0.04, (config.intensity / 12.5) * soundShape);
    const gain = Math.max(-12, Math.min(16, Math.round(1 + 20 * Math.log10(requestedAmplitude))));
    input?.TriggerSimpleHapticEvent?.(index, side, kind, 2, gain);
}
function browserRumble(index, side, scale, duration = 38) {
    const pads = Array.from(navigator.getGamepads?.() ?? []);
    const controller = controllerFor(index);
    const vendor = Number(controller?.unVendorID);
    const product = Number(controller?.unProductID);
    const connectedPads = pads.filter(Boolean);
    const hardwareMatches = connectedPads.filter((candidate) => {
        const hardware = gamepadHardwareId(String(candidate.id ?? ""));
        return hardware && hardware.vendor === vendor && hardware.product === product
            && (candidate.vibrationActuator ?? candidate.hapticActuators?.[0]);
    });
    const pad = (hardwareMatches.length === 1 ? hardwareMatches[0] : undefined)
        ?? (connectedPads.length === 1 ? connectedPads[0] : undefined);
    const actuator = pad?.vibrationActuator ?? pad?.hapticActuators?.[0];
    if (!actuator?.playEffect)
        return false;
    const strength = Math.pow(config.intensity / 100, 1.28);
    const normalized = Math.min(1, Math.max(0.04, strength * (0.18 + Math.min(1, scale) * 0.82) * 2));
    const both = side === 2;
    void actuator.playEffect("dual-rumble", {
        duration: Math.round(duration * (0.72 + config.intensity / 72)),
        startDelay: 0,
        strongMagnitude: both || side === 0 ? normalized : normalized * 0.1,
        weakMagnitude: both || side === 1 ? normalized : normalized * 0.1,
    }).catch(() => undefined);
    return true;
}
async function nativePattern(index, action) {
    const controller = controllerFor(index);
    const vendorId = Number(controller?.unVendorID);
    const productId = Number(controller?.unProductID);
    if (!Number.isInteger(vendorId) || vendorId <= 0 || !Number.isInteger(productId) || productId <= 0)
        return false;
    const magnitude = Math.min(1, Math.max(0.05, Math.pow(config.intensity / 100, 1.35) * 0.95));
    const controllerSignal = new AbortController();
    const timer = window.setTimeout(() => controllerSignal.abort(), 240);
    try {
        const response = await fetch(`${API_BASE}/dash/haptic`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                controllerIndex: index,
                vendorId,
                productId,
                side: actionSide(action),
                magnitude,
                pattern: action,
                requestId: nextNativeRequestId(),
            }),
            signal: controllerSignal.signal,
        });
        if (!response.ok)
            return false;
        const result = await response.json();
        return result.handled === true;
    }
    catch {
        return false;
    }
    finally {
        window.clearTimeout(timer);
    }
}
function rumblePulse(index, side, scale, duration = 38) {
    if (browserRumble(index, side, scale, duration))
        return;
    const input = window.SteamClient?.Input;
    const requestedAmplitude = Math.max(0.03, (config.intensity / 12.5) * scale);
    const gain = Math.max(-12, Math.min(16, Math.round(1 + 20 * Math.log10(requestedAmplitude))));
    if (input?.ForceSimpleHapticEvent)
        input.ForceSimpleHapticEvent(index, side, 2, 2, gain);
    else if (input?.TriggerSimpleHapticEvent)
        input.TriggerSimpleHapticEvent(index, side, 2, 2, gain);
    else
        input?.TriggerHapticPulse?.(index, side, Math.round(duration * 10), 0);
}
function stopBrowserRumble() {
    for (const pad of Array.from(navigator.getGamepads?.() ?? []).filter(Boolean)) {
        const actuator = pad.vibrationActuator ?? pad.hapticActuators?.[0];
        try {
            if (actuator?.reset)
                void Promise.resolve(actuator.reset()).catch(() => undefined);
            else if (actuator?.playEffect) {
                void actuator.playEffect("dual-rumble", {
                    duration: 1,
                    startDelay: 0,
                    strongMagnitude: 0,
                    weakMagnitude: 0,
                }).catch(() => undefined);
            }
        }
        catch { }
    }
}
function stopNativeHaptics() {
    void fetch(`${API_BASE}/dash/haptic`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ stop: true, requestId: nextNativeRequestId() }),
        keepalive: true,
    }).catch(() => undefined);
}
function stopActiveHaptics() {
    waveformGeneration += 1;
    waveformTimers.forEach((timer) => {
        window.clearTimeout(timer);
        pending.delete(timer);
    });
    waveformTimers.clear();
    stopBrowserRumble();
    stopNativeHaptics();
}
function playNavigationHaptic(action, controllerIndex) {
    if (!config.enabled || !steamIsForeground())
        return;
    const controller = activeController(controllerIndex ?? lastControllerIndex);
    if (!controller)
        return;
    const index = Number(controller.nControllerIndex);
    lastControllerIndex = index;
    playActionPattern(index, action);
}
function configureNavigationHaptics(next) {
    config = {
        enabled: next.enabled ?? config.enabled,
        intensity: clampIntensity(next.intensity ?? config.intensity),
    };
    if (!config.enabled) {
        soundRequests.forEach(discardSoundRequest);
        stopActiveHaptics();
    }
}
function actionForButton(button) {
    const kind = controlKind(focusedControl());
    if (button === 30)
        return "tabPrevious";
    if (button === 31)
        return "tabNext";
    if (button === 0)
        return toggleAction(focusedControl()) ?? (kind === "dropdown" ? "dropdown" : "confirm");
    if (button === 1)
        return "back";
    if (button === 2 || button === 3)
        return "options";
    if (button === 8 || button === 9 || button === 34 || button === 35 || button === 36)
        return "menu";
    if (button === 4 || button === 10 || button === 20)
        return "moveUp";
    if (button === 6 || button === 11 || button === 21)
        return "moveDown";
    if (button === 7 || button === 12 || button === 22)
        return kind === "slider" ? null : "moveLeft";
    if (button === 5 || button === 13 || button === 23)
        return kind === "slider" ? null : "moveRight";
    return null;
}
function installNavigationHaptics() {
    let registration;
    let activeControllerRegistration;
    let lastElement = null;
    let lastIdentity = "";
    let lastBox = null;
    let lastSliderValue = null;
    let lastToggleState = null;
    let lastButtonAt = 0;
    let lastFocusAction = null;
    let lastFocusActionAt = 0;
    const identities = new WeakMap();
    let nextIdentity = 1;
    let lastLetter = "";
    let lastLetterAt = 0;
    const letterObservers = new Map();
    const focusDocuments = new Map();
    let focusFrame;
    let focusContext;
    let focusRegistration;
    const playFocusAction = (action) => {
        const now = performance.now();
        if (now - lastButtonAt < 180)
            return;
        if (lastFocusAction === action && now - lastFocusActionAt < 140)
            return;
        lastFocusAction = action;
        lastFocusActionAt = now;
        playNavigationHaptic(action);
    };
    const playLetter = (value) => {
        const now = performance.now();
        if (value === lastLetter && now - lastLetterAt < 80)
            return;
        lastLetter = value;
        lastLetterAt = now;
        playNavigationHaptic("letter");
    };
    const inspectLetter = (node) => {
        let candidate = (node.nodeType === Node.TEXT_NODE ? node.parentElement : node);
        for (let depth = 0; candidate && depth < 5; depth += 1) {
            if (candidate.childElementCount > 1)
                return;
            const value = candidate.textContent?.trim() ?? "";
            if (!/^[A-Z#]$/i.test(value))
                return;
            if (candidate.childElementCount === 0) {
                const view = candidate.ownerDocument.defaultView;
                const box = candidate.getBoundingClientRect();
                const fontSize = Number.parseFloat(view?.getComputedStyle(candidate).fontSize ?? "0");
                if (box.width > 0 && box.height > 0 && fontSize >= 24)
                    playLetter(value.toUpperCase());
                return;
            }
            candidate = candidate.firstElementChild;
        }
    };
    const bindLetterObservers = () => {
        for (const root of steamRoots()) {
            const doc = root?.document;
            if (!doc?.documentElement || letterObservers.has(doc))
                continue;
            const Observer = root.MutationObserver;
            if (!Observer)
                continue;
            const observer = new Observer((mutations) => {
                if (!config.enabled || !steamIsForeground())
                    return;
                for (const mutation of mutations) {
                    inspectLetter(mutation.target);
                    mutation.addedNodes.forEach(inspectLetter);
                }
            });
            observer.observe(doc.documentElement, { childList: true, subtree: true, characterData: true });
            letterObservers.set(doc, observer);
        }
    };
    primeGamepadTimestamps();
    bindLetterObservers();
    const inspectFocus = () => {
        if (!config.enabled || !steamIsForeground())
            return;
        const element = focusedControl();
        if (!element)
            return;
        let identity = identities.get(element);
        if (!identity) {
            identity = nextIdentity++;
            identities.set(element, identity);
        }
        const activeDescendant = element.getAttribute("aria-activedescendant") ?? "";
        const label = element.getAttribute("aria-label") ?? element.getAttribute("title") ?? element.textContent?.trim() ?? "";
        const selected = element.getAttribute("aria-selected") ?? "";
        const checked = element.getAttribute("aria-checked");
        const value = sliderValue(element);
        const signature = `${identity}:${activeDescendant}:${selected}:${checked ?? ""}:${value ?? ""}:${label.slice(0, 80)}`;
        if (signature === lastIdentity)
            return;
        const box = element.getBoundingClientRect();
        const previous = lastBox;
        const wasElement = lastElement;
        const previousSliderValue = lastElement === element ? lastSliderValue : null;
        const previousToggleState = lastElement === element ? lastToggleState : null;
        lastElement = element;
        lastIdentity = signature;
        lastBox = box;
        lastSliderValue = value;
        lastToggleState = checked;
        if (!wasElement)
            return;
        if (value !== null && previousSliderValue !== null && value !== previousSliderValue) {
            playFocusAction(value > previousSliderValue ? "sliderIncrease" : "sliderDecrease");
            return;
        }
        if (checked !== null && previousToggleState !== null && checked !== previousToggleState) {
            playFocusAction(checked === "true" ? "toggleOn" : "toggleOff");
            return;
        }
        if (/^[A-ZÀ-ÖØ-Þ0-9]$/i.test(label)) {
            playLetter(label.toUpperCase());
            return;
        }
        if (element.closest('[role="tab"]')) {
            const next = previous ? box.left >= previous.left : true;
            playFocusAction(next ? "tabNext" : "tabPrevious");
            return;
        }
        if (wasElement === element)
            return;
        const dx = previous ? box.left + box.width / 2 - (previous.left + previous.width / 2) : 0;
        const dy = previous ? box.top + box.height / 2 - (previous.top + previous.height / 2) : 0;
        if (Math.abs(dx) >= Math.abs(dy))
            playFocusAction(dx < 0 ? "moveLeft" : "moveRight");
        else
            playFocusAction(dy < 0 ? "moveUp" : "moveDown");
    };
    const scheduleFocusInspection = () => {
        if (focusFrame !== undefined)
            return;
        focusFrame = window.requestAnimationFrame(() => {
            focusFrame = undefined;
            inspectFocus();
        });
    };
    const bindFocusContext = () => {
        const controller = _global_DFL?.getFocusNavController?.();
        const context = controller?.m_ActiveContext ?? controller?.m_LastActiveContext;
        if (!context || context === focusContext)
            return;
        try {
            focusRegistration?.Unregister?.();
        }
        catch { }
        focusContext = context;
        focusRegistration = context.m_FocusChangedCallbacks?.Register?.(scheduleFocusInspection);
        scheduleFocusInspection();
    };
    const bindFocusDocuments = () => {
        for (const root of steamRoots()) {
            const doc = root?.document;
            const Observer = root?.MutationObserver;
            if (!doc?.documentElement || !Observer || focusDocuments.has(doc))
                continue;
            const onValue = scheduleFocusInspection;
            doc.addEventListener("focusin", onValue, true);
            doc.addEventListener("input", onValue, true);
            doc.addEventListener("change", onValue, true);
            const observer = new Observer(scheduleFocusInspection);
            observer.observe(doc.documentElement, {
                attributes: true,
                subtree: true,
                attributeFilter: ["aria-valuenow", "aria-checked", "aria-selected", "aria-activedescendant"],
            });
            focusDocuments.set(doc, { observer, onValue });
        }
    };
    bindFocusContext();
    bindFocusDocuments();
    const observerTimer = window.setInterval(() => {
        bindLetterObservers();
        bindFocusContext();
        bindFocusDocuments();
    }, 500);
    try {
        activeControllerRegistration = window.SteamClient?.Input?.RegisterForActiveControllerChanges?.((message) => {
            const controller = controllerFromActiveSlot(message);
            if (controller)
                lastControllerIndex = Number(controller.nControllerIndex);
        });
        registration = window.SteamClient?.Input?.RegisterForControllerInputMessages?.((...args) => {
            const message = args[0] && typeof args[0] === "object" ? args[0] : undefined;
            const slotController = controllerFromActiveSlot(message);
            const controllerIndex = Number(slotController?.nControllerIndex
                ?? message?.nControllerIndex
                ?? message?.unControllerIndex
                ?? args[0]);
            const button = Number(message?.eButton ?? message?.nButton ?? message?.button ?? args[1]);
            const pressed = Boolean(message?.bPressed ?? message?.bDown ?? message?.pressed ?? args[2]);
            if (!pressed)
                return;
            if (Number.isInteger(controllerIndex) && activeController(controllerIndex))
                lastControllerIndex = controllerIndex;
            const action = actionForButton(button);
            if (action) {
                lastButtonAt = performance.now();
                playNavigationHaptic(action, controllerIndex);
            }
        });
    }
    catch { }
    return () => {
        stopActiveHaptics();
        window.clearInterval(observerTimer);
        if (focusFrame !== undefined)
            window.cancelAnimationFrame(focusFrame);
        focusFrame = undefined;
        try {
            focusRegistration?.Unregister?.();
        }
        catch { }
        focusRegistration = undefined;
        focusContext = undefined;
        focusDocuments.forEach(({ observer, onValue }, doc) => {
            observer.disconnect();
            doc.removeEventListener("focusin", onValue, true);
            doc.removeEventListener("input", onValue, true);
            doc.removeEventListener("change", onValue, true);
        });
        focusDocuments.clear();
        letterObservers.forEach((observer) => observer.disconnect());
        letterObservers.clear();
        try {
            (registration?.Unregister ?? registration?.unregister)?.call(registration);
        }
        catch { }
        try {
            (activeControllerRegistration?.Unregister ?? activeControllerRegistration?.unregister)?.call(activeControllerRegistration);
        }
        catch { }
        pending.forEach((timer) => window.clearTimeout(timer));
        pending.clear();
        waveformTimers.clear();
        soundRequests.clear();
        soundPlayerPatches.splice(0).forEach((patch) => {
            try {
                patch?.unpatch?.();
            }
            catch { }
        });
        uiAudioManagers.clear();
        patchedManagerPrototypes.clear();
        patchedPlaybackPrototypes.clear();
        patchedAudioSourcePrototypes.clear();
        soundBuffersByUrl.clear();
        soundBuffersByAction.clear();
        gamepadTimestamps.clear();
    };
}

const PATCH_MARKER = "__playhubPowerMenuPatch";
const RESTART_TOKENS = new Set(["#Quit_Restart", "#RestartDevice", "#Restart"]);
function steamWindows() {
    const roots = [window];
    try {
        const steam = _global_DFL?.findSP?.();
        if (steam && !roots.includes(steam))
            roots.push(steam);
        const trees = _global_DFL?.getGamepadNavigationTrees?.() ?? [];
        for (const tree of trees) {
            const treeWindow = tree?.Root?.Element?.ownerDocument?.defaultView;
            if (treeWindow && !roots.includes(treeWindow))
                roots.push(treeWindow);
        }
    }
    catch { }
    return roots.filter((root) => root?.document);
}
function fiberOf(node) {
    const key = Object.keys(node).find((name) => name.startsWith("__reactFiber$"));
    return key ? node[key] : null;
}
function powerMenuInstance(row) {
    let fiber = fiberOf(row);
    for (let step = 0; fiber && step < 24; step += 1, fiber = fiber.return) {
        const instance = fiber.stateNode;
        if (typeof instance?.forceUpdate === "function"
            && typeof instance.render === "function"
            && containsRestart(instance.props?.children))
            return instance;
    }
    return null;
}
function containsRestart(node) {
    if (Array.isArray(node))
        return node.some(containsRestart);
    if (!node || typeof node !== "object")
        return false;
    return RESTART_TOKENS.has(node.props?.strDisplayNameLocToken)
        || containsRestart(node.props?.children);
}
function addRestartActions(node, labels, restart) {
    if (Array.isArray(node)) {
        if (node.some((child) => child?.key === "playhub-restart-gaming"))
            return node;
        const output = [];
        for (const child of node) {
            output.push(addRestartActions(child, labels, restart));
            if (!RESTART_TOKENS.has(child?.props?.strDisplayNameLocToken))
                continue;
            const MenuItem = _global_DFL.MenuItem;
            const MenuSeparator = _global_DFL.MenuSeparator;
            if (MenuSeparator)
                output.push(_global_SP_REACT.createElement(MenuSeparator, { key: "playhub-restart-separator" }));
            output.push(_global_SP_REACT.createElement(MenuItem, {
                key: "playhub-restart-gaming",
                tone: "destructive",
                onSelected: () => restart("gaming"),
                children: labels.gaming,
            }), _global_SP_REACT.createElement(MenuItem, {
                key: "playhub-restart-desktop",
                tone: "destructive",
                onSelected: () => restart("desktop"),
                children: labels.desktop,
            }));
        }
        return output;
    }
    if (!_global_SP_REACT.isValidElement(node))
        return node;
    const element = node;
    if (!element.props?.children)
        return node;
    const children = element.props.children;
    return _global_SP_REACT.cloneElement(element, undefined, addRestartActions(RESTART_TOKENS.has(children?.props?.strDisplayNameLocToken) ? [children] : children, labels, restart));
}
function installPowerMenuPatch(labels, restart) {
    const observers = new Map();
    const patches = new Set();
    const refreshedInstances = new WeakSet();
    const pendingRefreshes = new Set();
    let disposed = false;
    const refreshInstance = (instance, row) => {
        if (refreshedInstances.has(instance))
            return;
        refreshedInstances.add(instance);
        const view = row.ownerDocument.defaultView ?? window;
        let frame;
        const refresh = () => {
            if (disposed)
                return;
            const mounted = row.isConnected || Array.from(row.ownerDocument.querySelectorAll("[role='menuitem']")).some((candidate) => powerMenuInstance(candidate) === instance);
            if (mounted) {
                try {
                    instance.forceUpdate();
                }
                catch { }
            }
        };
        const cancel = () => {
            view.clearTimeout(timeout);
            if (frame !== undefined)
                view.cancelAnimationFrame(frame);
        };
        const timeout = view.setTimeout(() => {
            refresh();
            frame = view.requestAnimationFrame(() => {
                refresh();
                pendingRefreshes.delete(cancel);
            });
        }, 0);
        pendingRefreshes.add(cancel);
    };
    const patchDocument = (doc) => {
        for (const row of Array.from(doc.querySelectorAll("[role='menuitem']"))) {
            const instance = powerMenuInstance(row);
            if (!instance)
                continue;
            // Some Steam components bind render on the instance. Patching only their
            // prototype cannot update the menu that is already mounted.
            const target = Object.prototype.hasOwnProperty.call(instance, "render")
                ? instance : Object.getPrototypeOf(instance);
            if (!target)
                continue;
            if (!Object.prototype.hasOwnProperty.call(target, PATCH_MARKER)) {
                const patch = _global_DFL.afterPatch(target, "render", function (_args, result) {
                    return !disposed && containsRestart(this?.props?.children)
                        ? addRestartActions(result, labels(), restart)
                        : result;
                });
                Object.defineProperty(target, PATCH_MARKER, { value: patch, configurable: true });
                patches.add(patch);
            }
            // A new instance can mount after prototype discovery but before the first
            // refresh. It still needs its own refresh even when the prototype is patched.
            refreshInstance(instance, row);
        }
    };
    const bind = () => {
        for (const win of steamWindows()) {
            const doc = win.document;
            if (!doc.documentElement)
                continue;
            patchDocument(doc);
            if (observers.has(doc))
                continue;
            const Observer = win.MutationObserver;
            if (!Observer)
                continue;
            const observer = new Observer(() => patchDocument(doc));
            observer.observe(doc.documentElement, { childList: true, subtree: true });
            observers.set(doc, observer);
        }
    };
    bind();
    const timer = window.setInterval(bind, 500);
    return () => {
        disposed = true;
        window.clearInterval(timer);
        pendingRefreshes.forEach((cancel) => cancel());
        pendingRefreshes.clear();
        observers.forEach((observer) => observer.disconnect());
        observers.clear();
        patches.forEach((patch) => {
            try {
                delete patch.object?.[PATCH_MARKER];
                patch.unpatch?.();
            }
            catch { }
        });
        patches.clear();
    };
}

const { useState, useEffect, useMemo } = _global_SP_REACT;
const { PanelSection, PanelSectionRow, ButtonItem, DropdownItem, ToggleField, SliderField, Navigation, Router, staticClasses, ConfirmModal, showModal } = _global_DFL;
const strings = {
    en: {
        mode: "Mode",
        switchGaming: "Switch to Gaming Mode",
        switchDesktop: "Switch to Desktop Mode",
        defaultStartup: "Default startup",
        desktopMode: "Desktop Mode",
        gamingMode: "Gaming Mode",
        notConnected: "Agent not connected",
        agentReturned: "Agent returned",
        dashboard: "Playhub Dashboard",
        openDashboard: "Open Dashboard",
        dashboardShortcut: "Open it with CTRL + ALT + P",
        dashboardSteamInput: "In Steam Input, assign this shortcut to Guide + any button for instant controller access.",
        dashboardWindowSwitch: "You can also map ALT + TAB in Steam Input to switch windows immediately.",
        haptics: "Controller feedback",
        hapticsDescription: "Adds tactile feedback while navigating Steam.",
        hapticsIntensity: "Feedback intensity",
        restartGaming: "Restart in Gaming Mode", restartDesktop: "Restart in Desktop Mode",
        restartConfirm: "The system will restart now. Continue?", restartNow: "Restart", cancel: "Cancel",
    },
    it: {
        mode: "Modalità",
        switchGaming: "Passa alla modalità Gaming",
        switchDesktop: "Passa alla modalità Desktop",
        defaultStartup: "Avvio predefinito",
        desktopMode: "Modalità Desktop",
        gamingMode: "Modalità Gaming",
        notConnected: "Agent non collegato",
        agentReturned: "Agent ha risposto",
        dashboard: "Playhub Dashboard",
        openDashboard: "Apri Dashboard",
        dashboardShortcut: "Aprila con CTRL + ALT + P",
        dashboardSteamInput: "In Steam Input, assegna questa scorciatoia a Guida + un pulsante per aprirla subito dal controller.",
        dashboardWindowSwitch: "Puoi anche associare tramite Steam Input la combinazione ALT + TAB per cambiare immediatamente finestra.",
        haptics: "Feedback del controller",
        hapticsDescription: "Aggiunge una risposta tattile mentre navighi in Steam.",
        hapticsIntensity: "Intensità del feedback",
        restartGaming: "Riavvia in Gaming Mode", restartDesktop: "Riavvia in Desktop Mode",
        restartConfirm: "Il sistema verrà riavviato ora. Vuoi continuare?", restartNow: "Riavvia", cancel: "Annulla",
    },
    es: {
        mode: "Modo",
        switchGaming: "Cambiar al modo Gaming",
        switchDesktop: "Cambiar al modo Escritorio",
        defaultStartup: "Inicio predeterminado",
        desktopMode: "Modo Escritorio",
        gamingMode: "Modo Gaming",
        notConnected: "Agente no conectado",
        agentReturned: "El agente devolvió",
        dashboard: "Playhub Dashboard",
        openDashboard: "Abrir Dashboard",
        dashboardShortcut: "Ábrelo con CTRL + ALT + P",
        dashboardSteamInput: "En Steam Input, asigna este atajo a Guía + un botón para abrirlo al instante con el mando.",
        dashboardWindowSwitch: "También puedes asignar ALT + TAB en Steam Input para cambiar de ventana al instante.",
        haptics: "Respuesta del mando", hapticsDescription: "Añade respuesta táctil al navegar por Steam.", hapticsIntensity: "Intensidad",
        restartGaming: "Reiniciar en modo Gaming", restartDesktop: "Reiniciar en modo Escritorio",
        restartConfirm: "El sistema se reiniciará ahora. ¿Quieres continuar?", restartNow: "Reiniciar", cancel: "Cancelar",
    },
    fr: {
        mode: "Mode",
        switchGaming: "Passer en mode Gaming",
        switchDesktop: "Passer en mode Bureau",
        defaultStartup: "Démarrage par défaut",
        desktopMode: "Mode Bureau",
        gamingMode: "Mode Gaming",
        notConnected: "Agent non connecté",
        agentReturned: "Agent a renvoyé",
        dashboard: "Playhub Dashboard",
        openDashboard: "Ouvrir le Dashboard",
        dashboardShortcut: "Ouvrez-le avec CTRL + ALT + P",
        dashboardSteamInput: "Dans Steam Input, associez ce raccourci à Guide + un bouton pour l'ouvrir instantanément avec la manette.",
        dashboardWindowSwitch: "Vous pouvez aussi associer ALT + TAB dans Steam Input pour changer immédiatement de fenêtre.",
        haptics: "Retour de la manette", hapticsDescription: "Ajoute un retour tactile lors de la navigation dans Steam.", hapticsIntensity: "Intensité",
        restartGaming: "Redémarrer en mode Gaming", restartDesktop: "Redémarrer en mode Bureau",
        restartConfirm: "Le système va redémarrer maintenant. Continuer ?", restartNow: "Redémarrer", cancel: "Annuler",
    },
    de: {
        mode: "Modus",
        switchGaming: "In den Gaming-Modus wechseln",
        switchDesktop: "In den Desktop-Modus wechseln",
        defaultStartup: "Standardstart",
        desktopMode: "Desktop-Modus",
        gamingMode: "Gaming-Modus",
        notConnected: "Agent nicht verbunden",
        agentReturned: "Agent meldete",
        dashboard: "Playhub Dashboard",
        openDashboard: "Dashboard öffnen",
        dashboardShortcut: "Mit CTRL + ALT + P öffnen",
        dashboardSteamInput: "Weise diese Tastenkombination in Steam Input Guide + einer Taste zu, um das Dashboard direkt per Controller zu öffnen.",
        dashboardWindowSwitch: "Du kannst in Steam Input auch ALT + TAB zuweisen, um sofort zwischen Fenstern zu wechseln.",
        haptics: "Controller-Feedback", hapticsDescription: "Fügt beim Navigieren in Steam taktiles Feedback hinzu.", hapticsIntensity: "Intensität",
        restartGaming: "Im Gaming-Modus neu starten", restartDesktop: "Im Desktop-Modus neu starten",
        restartConfirm: "Das System wird jetzt neu gestartet. Fortfahren?", restartNow: "Neu starten", cancel: "Abbrechen",
    },
    pt: {
        mode: "Modo",
        switchGaming: "Mudar para modo Gaming",
        switchDesktop: "Mudar para modo Desktop",
        defaultStartup: "Arranque predefinido",
        desktopMode: "Modo Desktop",
        gamingMode: "Modo Gaming",
        notConnected: "Agente não ligado",
        agentReturned: "Agente devolveu",
        dashboard: "Playhub Dashboard",
        openDashboard: "Abrir Dashboard",
        dashboardShortcut: "Abra com CTRL + ALT + P",
        dashboardSteamInput: "No Steam Input, atribua este atalho a Guia + um botão para abrir imediatamente pelo comando.",
        dashboardWindowSwitch: "Também pode atribuir ALT + TAB no Steam Input para mudar imediatamente de janela.",
        haptics: "Resposta do controle", hapticsDescription: "Adiciona resposta tátil ao navegar pelo Steam.", hapticsIntensity: "Intensidade",
        restartGaming: "Reiniciar no modo Gaming", restartDesktop: "Reiniciar no modo Desktop",
        restartConfirm: "O sistema será reiniciado agora. Continuar?", restartNow: "Reiniciar", cancel: "Cancelar",
    },
    uk: {
        mode: "Режим", switchGaming: "Перейти в ігровий режим", switchDesktop: "Перейти в режим робочого столу",
        defaultStartup: "Типовий запуск", desktopMode: "Режим робочого столу", gamingMode: "Ігровий режим",
        notConnected: "Агент не підключено", agentReturned: "Агент повернув",
        dashboard: "Панель Playhub", openDashboard: "Відкрити панель",
        dashboardShortcut: "Відкрийте за допомогою CTRL + ALT + P",
        dashboardSteamInput: "У Steam Input призначте це сполучення на Guide + будь-яку кнопку для миттєвого доступу з контролера.",
        dashboardWindowSwitch: "Також можна призначити ALT + TAB у Steam Input для миттєвого перемикання вікон.",
        haptics: "Відгук контролера", hapticsDescription: "Додає тактильний відгук під час навігації Steam.", hapticsIntensity: "Інтенсивність",
        restartGaming: "Перезапустити в ігровому режимі", restartDesktop: "Перезапустити в режимі робочого столу",
        restartConfirm: "Систему буде перезапущено зараз. Продовжити?", restartNow: "Перезапустити", cancel: "Скасувати",
    },
    zh: {
        mode: "模式", switchGaming: "切换到游戏模式", switchDesktop: "切换到桌面模式",
        defaultStartup: "默认启动模式", desktopMode: "桌面模式", gamingMode: "游戏模式",
        notConnected: "代理未连接", agentReturned: "代理返回",
        dashboard: "Playhub 控制面板", openDashboard: "打开控制面板",
        dashboardShortcut: "按 CTRL + ALT + P 打开",
        dashboardSteamInput: "在 Steam Input 中，将此快捷键绑定到 Guide + 任意按钮，即可通过控制器快速打开。",
        dashboardWindowSwitch: "也可以在 Steam Input 中绑定 ALT + TAB，以便立即切换窗口。",
        haptics: "手柄反馈", hapticsDescription: "在 Steam 中导航时提供触觉反馈。", hapticsIntensity: "反馈强度",
        restartGaming: "重启到游戏模式", restartDesktop: "重启到桌面模式",
        restartConfirm: "系统将立即重启。是否继续？", restartNow: "重启", cancel: "取消",
    },
    ja: {
        mode: "モード", switchGaming: "ゲーミングモードに切り替え", switchDesktop: "デスクトップモードに切り替え",
        defaultStartup: "既定の起動", desktopMode: "デスクトップモード", gamingMode: "ゲーミングモード",
        notConnected: "エージェントに接続されていません", agentReturned: "エージェントの応答",
        dashboard: "Playhub ダッシュボード", openDashboard: "ダッシュボードを開く",
        dashboardShortcut: "CTRL + ALT + P で開きます",
        dashboardSteamInput: "Steam Input でこのショートカットを Guide + 任意のボタンに割り当てると、コントローラーからすぐに開けます。",
        dashboardWindowSwitch: "Steam Input に ALT + TAB を割り当てて、ウィンドウをすぐに切り替えることもできます。",
        haptics: "コントローラーのフィードバック", hapticsDescription: "Steam の操作に触覚フィードバックを加えます。", hapticsIntensity: "フィードバックの強さ",
        restartGaming: "ゲーミングモードで再起動", restartDesktop: "デスクトップモードで再起動",
        restartConfirm: "システムを今すぐ再起動します。続行しますか？", restartNow: "再起動", cancel: "キャンセル",
    },
    ko: {
        mode: "모드", switchGaming: "게이밍 모드로 전환", switchDesktop: "데스크톱 모드로 전환",
        defaultStartup: "기본 시작", desktopMode: "데스크톱 모드", gamingMode: "게이밍 모드",
        notConnected: "에이전트가 연결되지 않음", agentReturned: "에이전트 응답",
        dashboard: "Playhub 대시보드", openDashboard: "대시보드 열기",
        dashboardShortcut: "CTRL + ALT + P로 열기",
        dashboardSteamInput: "Steam Input에서 이 단축키를 Guide + 원하는 버튼에 지정하면 컨트롤러로 즉시 열 수 있습니다.",
        dashboardWindowSwitch: "Steam Input에 ALT + TAB을 지정하여 창을 즉시 전환할 수도 있습니다.",
        haptics: "컨트롤러 피드백", hapticsDescription: "Steam을 탐색할 때 촉각 피드백을 추가합니다.", hapticsIntensity: "피드백 강도",
        restartGaming: "게이밍 모드로 다시 시작", restartDesktop: "데스크톱 모드로 다시 시작",
        restartConfirm: "시스템을 지금 다시 시작합니다. 계속하시겠습니까?", restartNow: "다시 시작", cancel: "취소",
    },
    hi: {
        mode: "मोड", switchGaming: "गेमिंग मोड पर जाएं", switchDesktop: "डेस्कटॉप मोड पर जाएं",
        defaultStartup: "डिफ़ॉल्ट स्टार्टअप", desktopMode: "डेस्कटॉप मोड", gamingMode: "गेमिंग मोड",
        notConnected: "एजेंट कनेक्ट नहीं है", agentReturned: "एजेंट ने लौटाया",
        dashboard: "Playhub डैशबोर्ड", openDashboard: "डैशबोर्ड खोलें",
        dashboardShortcut: "CTRL + ALT + P से खोलें",
        dashboardSteamInput: "कंट्रोलर से तुरंत खोलने के लिए Steam Input में इस शॉर्टकट को Guide + किसी बटन से जोड़ें।",
        dashboardWindowSwitch: "विंडो तुरंत बदलने के लिए Steam Input में ALT + TAB भी जोड़ सकते हैं।",
        haptics: "कंट्रोलर फ़ीडबैक", hapticsDescription: "Steam में नेविगेट करते समय स्पर्श प्रतिक्रिया जोड़ता है।", hapticsIntensity: "फ़ीडबैक की तीव्रता",
        restartGaming: "गेमिंग मोड में रीस्टार्ट करें", restartDesktop: "डेस्कटॉप मोड में रीस्टार्ट करें",
        restartConfirm: "सिस्टम अब रीस्टार्ट होगा। जारी रखें?", restartNow: "रीस्टार्ट", cancel: "रद्द करें",
    },
    ru: {
        mode: "Режим", switchGaming: "Перейти в игровой режим", switchDesktop: "Перейти в режим рабочего стола",
        defaultStartup: "Запуск по умолчанию", desktopMode: "Режим рабочего стола", gamingMode: "Игровой режим",
        notConnected: "Агент не подключен", agentReturned: "Агент вернул",
        dashboard: "Панель Playhub", openDashboard: "Открыть панель",
        dashboardShortcut: "Откройте с помощью CTRL + ALT + P",
        dashboardSteamInput: "В Steam Input назначьте это сочетание на Guide + любую кнопку для мгновенного доступа с контроллера.",
        dashboardWindowSwitch: "Также можно назначить ALT + TAB в Steam Input для мгновенного переключения окон.",
        haptics: "Отклик контроллера", hapticsDescription: "Добавляет тактильный отклик при навигации в Steam.", hapticsIntensity: "Интенсивность",
        restartGaming: "Перезапустить в игровом режиме", restartDesktop: "Перезапустить в режиме рабочего стола",
        restartConfirm: "Система будет перезапущена сейчас. Продолжить?", restartNow: "Перезапустить", cancel: "Отмена",
    },
};
const STEAM_LOCALE_ALIASES = {
    english: "en", en: "en", italian: "it", it: "it", spanish: "es", latam: "es", es: "es",
    french: "fr", fr: "fr", german: "de", de: "de", brazilian: "pt", portuguese: "pt", pt: "pt",
    ukrainian: "uk", uk: "uk", schinese: "zh", tchinese: "zh", zh: "zh", japanese: "ja", ja: "ja",
    koreana: "ko", korean: "ko", ko: "ko", hindi: "hi", hi: "hi", russian: "ru", ru: "ru",
};
function normalizeSteamLocale(value) {
    const candidate = String(value ?? "").trim().toLowerCase().replace(/_/g, "-");
    if (!candidate)
        return null;
    return STEAM_LOCALE_ALIASES[candidate] ?? STEAM_LOCALE_ALIASES[candidate.split("-")[0]] ?? null;
}
function detectSteamLocale() {
    const win = window;
    const candidates = [];
    [win.LocalizationManager, win.g_LocalizationManager, win.SteamLocalizationManager].filter(Boolean).forEach((manager) => {
        candidates.push(manager?.m_strLanguage, manager?.m_language, manager?.m_strCurrentLanguage, manager?.language);
        ["GetLanguage", "GetCurrentLanguage"].forEach((method) => {
            try {
                const value = manager?.[method]?.();
                if (typeof value === "string")
                    candidates.push(value);
            }
            catch { }
        });
    });
    candidates.push(win.g_strLanguage, win.g_rgAppContextData?.language, document.documentElement.lang);
    candidates.push(...Array.from(navigator.languages ?? []), navigator.language);
    for (const candidate of candidates) {
        const locale = normalizeSteamLocale(candidate);
        if (locale)
            return locale;
    }
    return "en";
}
async function readSteamLocale() {
    try {
        const value = await window.SteamClient?.Settings?.GetCurrentLanguage?.();
        const locale = normalizeSteamLocale(value);
        if (locale)
            return locale;
    }
    catch { }
    return detectSteamLocale();
}
let currentLocale = detectSteamLocale();
function t() {
    return strings[currentLocale] ?? strings.en;
}
function confirmRestart(mode, action) {
    const local = t();
    showModal(SP_JSX.jsx(ConfirmModal, { strTitle: mode === "gaming" ? local.restartGaming : local.restartDesktop, strDescription: local.restartConfirm, strOKButtonText: local.restartNow, strCancelButtonText: local.cancel, onOK: action }));
}
async function getStatus() {
    const response = await fetch(`${API_BASE}/status`);
    if (!response.ok) {
        throw new Error(`${t().agentReturned} ${response.status}`);
    }
    return await response.json();
}
async function post(path) {
    const response = await fetch(`${API_BASE}${path}`, { method: "POST" });
    if (!response.ok) {
        throw new Error(`${t().agentReturned} ${response.status}`);
    }
    return await response.json();
}
// ---------------------------------------------------------------------------
// Apertura della Dashboard richiesta da fuori
// ---------------------------------------------------------------------------
// Mentre giochi, l'interfaccia di Steam non riceve input: nessun codice che
// vive qui dentro potrebbe accorgersi della scorciatoia da tastiera. Se ne
// occupa l'agente, che pero' non apre niente per conto suo: alza una
// bandierina, e qui sotto c'e' chi la raccoglie. La bandierina si consuma alla
// lettura, quindi due schede in ascolto non aprono la pagina due volte.
//
// QUESTO NON E' UN COMPONENTE, ED E' IL PUNTO.
//
// Prima l'attesa viveva dentro il pannello del Quick Access Menu. Decky monta
// il pannello di UN plugin per volta - quello selezionato - quindi la
// scorciatoia funzionava solo quando Gaming Mode era la voce aperta, e
// "rinveniva" appena si tornava sul suo pannello. Il sintomo era esattamente
// quello: premi la combinazione e non succede niente, apri il QAM e di colpo
// riparte. Qui l'attesa e' agganciata al plugin, non alla sua interfaccia:
// resta accesa finche' Decky e' caricato, QAM aperto o chiuso, pannello
// selezionato o no.
// APRIRE LA DASHBOARD.
//
// UNA SOLA INTERFACCIA, SEMPRE LA STESSA.
//
// Per un po' ce ne sono state due: una schermata disegnata da Steam e una
// finestra nativa. Era uno spreco e una confusione. Chi usa Playhub deve vedere
// la stessa cosa che apra dal Quick Access Menu o dalla scorciatoia, dentro o
// fuori da un gioco, e non deve chiedersi perche' a volte e' diversa.
//
// Il plugin Decky disegna la Dashboard e possiede navigazione e input. Non
// vengono create finestre native o mirror DWM: se Steam non puo' mostrare la
// route, la richiesta termina senza sovrapporre superfici al gioco.
async function openDashboard(reason = "richiesta") {
    logToAgent("apertura richiesta");
    const environmentRequest = readEnvironment();
    const dashboardWindowsReady = preloadDashboardWindows();
    const environment = await environmentRequest;
    if (!environment?.enabled)
        return;
    void captureDashboardSourceFocus();
    const [useSteamOverlay] = await Promise.all([prepareDashboardOverlay(), dashboardWindowsReady]);
    if (useSteamOverlay) {
        try {
            Navigation?.CloseSideMenus?.();
            Navigation?.Navigate?.(DASHBOARD_ROUTE);
        }
        catch (error) {
            logToAgent(`navigazione overlay NON riuscita: ${error}`);
        }
        [80, 180, 360, 700].forEach((delay) => window.setTimeout(focusDashboardSurface, delay));
        return;
    }
    await requestDashboardSteamFocus();
    markDashboardChrome();
    try {
        try {
            window.SteamClient?.Window?.BringToFront?.(EWindowBringToFront_AndForceOS);
        }
        catch { }
        Navigation?.CloseSideMenus?.();
        window.setTimeout(() => Navigation?.Navigate?.(DASHBOARD_ROUTE), 40);
        // One delayed hand-off is enough for the newly mounted route. Repeating
        // it after the user has already started moving can steal focus back to a
        // tab or another fallback target.
        window.setTimeout(focusDashboardSurface, 140);
    }
    catch (error) {
        clearDashboardChrome();
        void restoreDashboardSourceFocus();
        logToAgent(`navigazione NON riuscita: ${error}`);
    }
}
function restoreSteamNavigationFocus(reason) {
    const store = _global_DFL?.Router?.WindowStore;
    const main = store?.GamepadUIMainWindowInstance;
    const browserWindow = main?.m_BrowserWindow ?? main?.BrowserWindow;
    const context = main?.m_FocusNavContext;
    if (!main || !browserWindow || !context)
        return false;
    if (browserWindow.document?.querySelector?.(".ph-dashboard")) {
        return focusDashboardSurface();
    }
    const controller = _global_DFL?.getFocusNavController?.() ?? window.FocusNavController;
    const hasRealFocus = () => {
        const activeContext = controller?.GetActiveContext?.() ?? controller?.m_ActiveContext;
        const active = activeContext === context || context.BIsActive?.() === true;
        const documentFocused = browserWindow.document?.hasFocus?.() === true;
        const navigationFocused = !!browserWindow.document?.querySelector?.(".gpfocus");
        return active && documentFocused && navigationFocused;
    };
    try {
        browserWindow.SteamClient?.Window?.BringToFront?.(1);
    }
    catch { }
    try {
        browserWindow.SteamClient?.Window?.MarkLastFocused?.();
    }
    catch { }
    if (hasRealFocus())
        return true;
    try {
        browserWindow.focus?.();
    }
    catch { }
    try {
        if (!context.BIsActive?.())
            context.OnActivate?.(browserWindow);
        main.FocusApplicationRoot?.();
        browserWindow.SteamClient?.Window?.SetKeyFocus?.(true);
    }
    catch {
        return false;
    }
    const focused = hasRealFocus();
    if (focused)
        logToAgent(`focus Steam ripristinato (${reason})`);
    return focused;
}
function installSteamNavigationFocusRecovery() {
    const timers = new Set();
    const clearTimers = () => {
        timers.forEach((timer) => window.clearTimeout(timer));
        timers.clear();
    };
    const schedule = (reason) => {
        clearTimers();
        [0, 100, 280, 650].forEach((delay) => {
            const timer = window.setTimeout(() => {
                timers.delete(timer);
                if (restoreSteamNavigationFocus(reason))
                    clearTimers();
            }, delay);
            timers.add(timer);
        });
    };
    return {
        schedule,
        uninstall: clearTimers,
    };
}
function startOpenRequestWatcher(onFocusRecovery) {
    let alive = true;
    let inFlight = false;
    let focusRecoveryVersion;
    let focusRecoveryPending = false;
    const timer = window.setInterval(async () => {
        if (!alive || inFlight)
            return;
        inFlight = true;
        try {
            const signal = await consumeOpenRequest();
            if (signal.open) {
                openDashboard();
            }
            if (focusRecoveryVersion === undefined) {
                focusRecoveryVersion = signal.focusRecovery;
            }
            else if (signal.focusRecovery !== focusRecoveryVersion) {
                focusRecoveryVersion = signal.focusRecovery;
                focusRecoveryPending = true;
            }
            if (focusRecoveryPending && signal.steamForeground) {
                focusRecoveryPending = false;
                onFocusRecovery();
            }
        }
        catch (error) {
            console.warn("Playhub Dashboard: attesa dell'apertura non riuscita", error);
        }
        finally {
            inFlight = false;
        }
    }, 500);
    return () => {
        alive = false;
        window.clearInterval(timer);
    };
}
// ---------------------------------------------------------------------------
// Pannello del Quick Access Menu
// ---------------------------------------------------------------------------
function Content() {
    const [locale, setLocale] = useState(currentLocale);
    const local = strings[locale] ?? strings.en;
    const [status, setStatus] = useState();
    const [busy, setBusy] = useState(false);
    const [dashboardEnabled, setDashboardEnabled] = useState(true);
    const [hapticsEnabled, setHapticsEnabled] = useState(false);
    const [hapticsIntensity, setHapticsIntensity] = useState(55);
    const defaultOptions = useMemo(() => [
        { data: "Desktop", label: "Desktop" },
        { data: "Gaming", label: "Gaming" },
    ], []);
    const refresh = async () => {
        try {
            setStatus(await getStatus());
        }
        catch (error) {
            setStatus(undefined);
            toaster.toast({
                title: "Gaming Mode",
                body: error instanceof Error ? error.message : local.notConnected,
            });
        }
    };
    const run = async (path, title) => {
        setBusy(true);
        try {
            const result = await post(path);
            toaster.toast({ title, body: result.message });
            if (result.status) {
                setStatus(result.status);
            }
            else {
                await refresh();
            }
        }
        catch (error) {
            toaster.toast({
                title,
                body: error instanceof Error ? error.message : local.notConnected,
            });
        }
        finally {
            setBusy(false);
        }
    };
    const setDefault = async (option) => {
        await run(option.data === "Gaming" ? "/default/gaming" : "/default/desktop", local.defaultStartup);
    };
    useEffect(() => {
        let alive = true;
        void readSteamLocale().then((value) => {
            currentLocale = value;
            if (alive)
                setLocale(value);
        });
        refresh();
        void readEnvironment().then((environment) => setDashboardEnabled(environment?.enabled !== false));
        void readSettings().then((settings) => {
            if (!settings)
                return;
            const enabled = settings.navigationHapticsEnabled === true;
            const intensity = Math.max(5, Math.min(100, Number(settings.navigationHapticsIntensity) || 55));
            setHapticsEnabled(enabled);
            setHapticsIntensity(intensity);
            configureNavigationHaptics({ enabled, intensity });
        });
        const timer = window.setInterval(refresh, 5000);
        return () => { alive = false; window.clearInterval(timer); };
    }, []);
    return (SP_JSX.jsxs(SP_JSX.Fragment, { children: [SP_JSX.jsxs(PanelSection, { title: local.dashboard, children: [SP_JSX.jsx(PanelSectionRow, { children: SP_JSX.jsx(ButtonItem, { disabled: !dashboardEnabled, layout: "below", onClick: () => openDashboard(), children: local.openDashboard }) }), SP_JSX.jsxs(PanelSectionRow, { children: [SP_JSX.jsx("style", { children: `
            @keyframes phShortcutPulse { 0%,100% { opacity:.62; transform:scale(.96); } 50% { opacity:1; transform:scale(1); } }
            @keyframes phShortcutTravel { 0% { transform:translateX(-3px); opacity:.35; } 50%,100% { transform:translateX(3px); opacity:1; } }
            .ph-dashboard-shortcut { box-sizing:border-box; width:100%; padding:9px 16px 14px; color:rgba(255,255,255,.88); }
            .ph-dashboard-shortcut-figure { width:min(100%,292px); min-height:42px; margin:0 auto; display:flex; align-items:center; justify-content:center; gap:7px; color:#fff; }
            .ph-dashboard-keys { display:flex; align-items:center; gap:4px; }
            .ph-dashboard-key { min-width:28px; height:24px; padding:0 6px; display:grid; place-items:center; border:1.5px solid rgba(255,255,255,.88); border-radius:6px; font-size:9px; line-height:1; font-weight:700; background:transparent; }
            .ph-dashboard-plus { font-size:13px; opacity:.58; }
            .ph-dashboard-flow { width:22px; height:15px; flex:0 0 auto; animation:phShortcutTravel 1.6s ease-in-out infinite; }
            .ph-dashboard-flow path { fill:rgba(255,255,255,.82); }
            .ph-dashboard-pad { width:48px; height:29px; flex:0 0 auto; animation:phShortcutPulse 1.6s ease-in-out infinite; }
            .ph-dashboard-pad .pad-shell, .ph-dashboard-pad .pad-detail { fill:none; stroke:currentColor; stroke-width:1.7; stroke-linecap:round; stroke-linejoin:round; }
            .ph-dashboard-pad .pad-guide { fill:#fff; stroke:#fff; stroke-width:1; filter:drop-shadow(0 0 3px rgba(255,255,255,.7)); }
            .ph-dashboard-pad .pad-button { fill:#fff; opacity:.92; }
            .ph-dashboard-shortcut-title { margin-top:3px; text-align:center; font-size:14px; line-height:1.3; font-weight:650; }
            .ph-dashboard-shortcut-copy { width:calc(100% - 42px); margin:6px auto 0; max-width:278px; text-align:center; color:rgba(255,255,255,.58); font-size:12px; line-height:1.4; }
          ` }), SP_JSX.jsxs("div", { className: "ph-dashboard-shortcut", children: [SP_JSX.jsxs("div", { className: "ph-dashboard-shortcut-figure", "aria-hidden": "true", children: [SP_JSX.jsxs("div", { className: "ph-dashboard-keys", children: [SP_JSX.jsx("span", { className: "ph-dashboard-key", children: "CTRL" }), SP_JSX.jsx("span", { className: "ph-dashboard-plus", children: "+" }), SP_JSX.jsx("span", { className: "ph-dashboard-key", children: "ALT" }), SP_JSX.jsx("span", { className: "ph-dashboard-plus", children: "+" }), SP_JSX.jsx("span", { className: "ph-dashboard-key", children: "P" })] }), SP_JSX.jsx("svg", { className: "ph-dashboard-flow", viewBox: "0 0 28 18", focusable: "false", children: SP_JSX.jsx("path", { d: "M1 7.25h18.4l-4.2-4.2L17.25 1 25 8.75l-7.75 7.75-2.05-2.05 4.2-4.2H1z" }) }), SP_JSX.jsxs("svg", { className: "ph-dashboard-pad", viewBox: "0 0 64 38", focusable: "false", children: [SP_JSX.jsx("path", { className: "pad-shell", d: "M12 7.5h40c3.5 0 5.8 2.1 6.5 5.4l2.1 10.7c.8 4.1-1.8 7-5.8 7H9.2c-4 0-6.6-2.9-5.8-7l2.1-10.7c.7-3.3 3-5.4 6.5-5.4Z" }), SP_JSX.jsx("path", { className: "pad-detail", d: "M17 14v10M12 19h10" }), SP_JSX.jsx("circle", { className: "pad-guide", cx: "32", cy: "19", r: "3" }), SP_JSX.jsx("circle", { className: "pad-button", cx: "47", cy: "14.7", r: "1.7" }), SP_JSX.jsx("circle", { className: "pad-button", cx: "51.3", cy: "19", r: "1.7" }), SP_JSX.jsx("circle", { className: "pad-button", cx: "47", cy: "23.3", r: "1.7" }), SP_JSX.jsx("circle", { className: "pad-button", cx: "42.7", cy: "19", r: "1.7" })] })] }), SP_JSX.jsx("div", { className: "ph-dashboard-shortcut-title", children: local.dashboardShortcut }), SP_JSX.jsx("div", { className: "ph-dashboard-shortcut-copy", children: local.dashboardSteamInput }), SP_JSX.jsx("div", { className: "ph-dashboard-shortcut-copy", children: local.dashboardWindowSwitch })] })] })] }), SP_JSX.jsxs(PanelSection, { title: local.mode, children: [SP_JSX.jsx(PanelSectionRow, { children: SP_JSX.jsx(ButtonItem, { disabled: busy, layout: "below", onClick: () => confirmRestart("gaming", () => { void run("/mode/gaming/switch", local.gamingMode); }), children: local.switchGaming }) }), SP_JSX.jsx(PanelSectionRow, { children: SP_JSX.jsx(ButtonItem, { disabled: busy, layout: "below", onClick: () => confirmRestart("desktop", () => { void run("/mode/desktop/switch", local.desktopMode); }), children: local.switchDesktop }) }), SP_JSX.jsx(PanelSectionRow, { children: SP_JSX.jsx(DropdownItem, { label: local.defaultStartup, disabled: busy, rgOptions: defaultOptions, selectedOption: status?.defaultMode ?? "Desktop", onChange: setDefault }) })] }), SP_JSX.jsxs(PanelSection, { children: [SP_JSX.jsx(PanelSectionRow, { children: SP_JSX.jsx(ToggleField, { label: local.haptics, description: local.hapticsDescription, checked: hapticsEnabled, onChange: (enabled) => {
                                setHapticsEnabled(enabled);
                                configureNavigationHaptics({ enabled });
                                void writeSettings({ navigationHapticsEnabled: enabled });
                            } }) }), hapticsEnabled && (SP_JSX.jsx(PanelSectionRow, { children: SP_JSX.jsx(SliderField, { label: local.hapticsIntensity, value: hapticsIntensity, min: 5, max: 100, step: 5, valueSuffix: "%", showValue: true, onChange: (intensity) => {
                                const value = Math.max(5, Math.min(100, Math.round(intensity)));
                                setHapticsIntensity(value);
                                configureNavigationHaptics({ intensity: value });
                                void writeSettings({ navigationHapticsIntensity: value });
                            } }) }))] })] }));
}
// ---------------------------------------------------------------------------
// Focus rescue
// ---------------------------------------------------------------------------
//
// Su Windows il "foreground lock" impedisce a Steam di portare la finestra Big
// Picture sopra un gioco quando si preme il tasto Steam/QAM (suono si',
// finestra no). Qui, dentro la CEF di Steam, intercettiamo l'attivazione
// dell'overlay e:
//   1. proviamo la via interna: SteamClient.Window.BringToFront(AndForceOS);
//   2. avvisiamo l'helper Win32 di Playhub (porta 47992) che toglie il TOPMOST
//      al gioco e forza Big Picture in primo piano.
// A overlay chiuso l'helper ripristina TOPMOST e focus del gioco.
// Se l'overlay e' correttamente agganciato in-game (utenti senza il problema)
// non facciamo nulla: zero regressioni. Il borderless resta intatto.
const EWindowBringToFront_AndForceOS = 1;
async function overlayHookedInGame(appId) {
    try {
        const infos = await window.SteamClient.Overlay.GetOverlayBrowserInfo();
        return Array.isArray(infos) && infos.some((info) => info && info.appID === appId && (info.unPID ?? 0) > 0);
    }
    catch {
        return false;
    }
}
function installFocusRescue() {
    let overlayWasActive = false;
    let overlayActivationGeneration = 0;
    let focusRetry;
    let registration;
    try {
        registration = window.SteamClient.Overlay.RegisterForOverlayActivated(async (_overlayPid, appId, active) => {
            const activationGeneration = ++overlayActivationGeneration;
            try {
                if (active) {
                    // Steam can report the highlighted library app as appId even when
                    // no game is running. MainRunningApp is the reliable boundary:
                    // without it this is a normal Big Picture panel and forcing focus
                    // would immediately dismiss the QAM or the sidebar.
                    const runningAppId = Number.parseInt(String(Router?.MainRunningApp?.appid ?? "0"), 10);
                    if (!Number.isFinite(runningAppId) || runningAppId <= 0)
                        return;
                    overlayWasActive = true;
                    // Se l'overlay Steam e' gia' agganciato in-game, il menu appare
                    // dentro il gioco: non interferire.
                    const hookedInGame = await overlayHookedInGame(runningAppId || appId);
                    if (hookedInGame || !overlayWasActive || activationGeneration !== overlayActivationGeneration) {
                        return;
                    }
                    try {
                        window.SteamClient.Window.BringToFront(EWindowBringToFront_AndForceOS);
                    }
                    catch { }
                    void requestDashboardSteamFocus();
                    // Retry: se il primo tentativo e' arrivato mentre Windows stava
                    // ancora negando il cambio di primo piano.
                    if (focusRetry !== undefined)
                        window.clearTimeout(focusRetry);
                    focusRetry = window.setTimeout(() => {
                        focusRetry = undefined;
                        if (overlayWasActive)
                            void requestDashboardSteamFocus();
                    }, 450);
                }
                else if (overlayWasActive) {
                    overlayWasActive = false;
                    if (focusRetry !== undefined)
                        window.clearTimeout(focusRetry);
                    focusRetry = undefined;
                    void restoreDashboardSourceFocus();
                }
            }
            catch { }
        });
    }
    catch { }
    return () => {
        overlayActivationGeneration += 1;
        overlayWasActive = false;
        if (focusRetry !== undefined)
            window.clearTimeout(focusRetry);
        focusRetry = undefined;
        try {
            registration?.unregister?.();
        }
        catch { }
    };
}
// ---------------------------------------------------------------------------
var index = definePlugin(() => {
    // Riempie la cache prima della prima apertura: la dashboard monta subito la
    // finestra primaria invece di mostrarla soltanto dopo il primo effect React.
    void preloadDashboardWindows();
    const uninstallFocusRescue = installFocusRescue();
    const steamFocusRecovery = installSteamNavigationFocusRecovery();
    const stopOpenRequestWatcher = startOpenRequestWatcher(() => steamFocusRecovery.schedule("chiusura gioco"));
    const uninstallNavigationHaptics = installNavigationHaptics();
    const uninstallPowerMenuPatch = installPowerMenuPatch(() => ({ gaming: t().restartGaming, desktop: t().restartDesktop }), (mode) => confirmRestart(mode, () => { void post(`/mode/${mode}/restart`); }));
    void readSettings().then((settings) => {
        if (settings)
            configureNavigationHaptics({
                enabled: settings.navigationHapticsEnabled === true,
                intensity: settings.navigationHapticsIntensity,
            });
    });
    try {
        routerHook?.addRoute?.(DASHBOARD_ROUTE, () => SP_JSX.jsx(DashboardPage, {}), { exact: true });
    }
    catch (error) {
        console.warn("Playhub Dashboard: rotta non aggiunta", error);
    }
    return {
        name: "Gaming Mode",
        titleView: SP_JSX.jsx("div", { className: staticClasses?.Title, children: "Gaming Mode" }),
        content: SP_JSX.jsx(Content, {}),
        icon: SP_JSX.jsx(FaGamepad, {}),
        onDismount() {
            clearDashboardChrome();
            uninstallFocusRescue();
            steamFocusRecovery.uninstall();
            stopOpenRequestWatcher();
            uninstallNavigationHaptics();
            uninstallPowerMenuPatch();
            try {
                routerHook?.removeRoute?.(DASHBOARD_ROUTE);
            }
            catch (error) {
                console.warn("Playhub Dashboard: rotta non rimossa", error);
            }
        },
    };
});

export { index as default };
//# sourceMappingURL=index.js.map
