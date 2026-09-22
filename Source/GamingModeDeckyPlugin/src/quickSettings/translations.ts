// Localised UI strings. English is the fallback; Italian is also provided.
import { quickCoreLocale } from "./coreLocale";

export interface Strings {
  title: string;
  audio: string;
  volume: string;
  microphoneVolume: string;
  audioOutput: string;
  microphoneInput: string;
  audioDevicesUnavailable: string;
  audioChangeFailed: string;
  display: string;
  brightness: string;
  hdr: string;
  hdrUnavailable: string;
  displayChangeFailed: string;
  powerModeFailed: string;
  hdrConfirmTitle: string;
  hdrConfirmBody: string;
  resolution: string;
  refreshRate: string;
  adaptingUi: string;
  performance: string;
  powerMode: string;
  powerEfficiency: string;
  powerBalanced: string;
  powerBetter: string;
  powerBest: string;
  tdp: string;
  tdpLimit: string;
  lossless: string;
  losslessLaunch: string;
  losslessScaling: string;
  losslessFrameGen: string;
  losslessMultiplier: string;
  losslessHotkeyHint: string;
  losslessApplyNote: string;
  losslessManagedHint: string;
  losslessGameOnlyHint: string;
  automaticScaling: string;
  automaticScalingDesc: string;
  autoScaleGame: string;
  autoScaleGameDesc: string;
  activationDelay: string;
  activationDelayDesc: string;
  scalingAlgorithm: string;
  scalingAlgorithmDesc: string;
  scalingMode: string;
  scalingModeDesc: string;
  fitMode: string;
  scaleFactor: string;
  resizeBeforeScaling: string;
  resizeBeforeScalingDesc: string;
  windowedMode: string;
  windowedModeDesc: string;
  fsrVariant: string;
  ls1Variant: string;
  anime4kSize: string;
  sharpness: string;
  ls1Sharpness: string;
  variableRateShading: string;
  variableRateShadingDesc: string;
  frameGenMode: string;
  frameGenerationDesc: string;
  frameGenModeDesc: string;
  targetFps: string;
  targetFpsDesc: string;
  flowScale: string;
  performanceProfile: string;
  rendering: string;
  renderingDesc: string;
  syncMode: string;
  syncModeDesc: string;
  maxFrameLatency: string;
  maxFrameLatencyDesc: string;
  gsyncSupport: string;
  hdrPassthrough: string;
  hdrPassthroughDesc: string;
  drawFps: string;
  drawFpsDesc: string;
  captureApi: string;
  captureApiDesc: string;
  queueTarget: string;
  cursor: string;
  cursorDesc: string;
  clipCursor: string;
  adjustCursorSpeed: string;
  hideCursor: string;
  scaleCursor: string;
  multiDisplayMode: string;
  multiDisplayModeDesc: string;
  cropInput: string;
  cropInputDesc: string;
  cropLeft: string;
  cropTop: string;
  cropRight: string;
  cropBottom: string;
  gpuAndDisplay: string;
  gpuAndDisplayDesc: string;
  preferredGpu: string;
  preferredGpuDesc: string;
  outputDisplay: string;
  outputDisplayDesc: string;
  saved: string;
  saving: string;
  profileLoadError: string;
  back: string;
  off: string;
  amd: string;
  amdGlobalHint: string;
  amdGameProfile: string;
  amdGameProfileDesc: string;
  amdGameProfileToggle: string;
  amdGameProfileToggleDesc: string;
  amdGameProfileUnavailable: string;
  sdl3GameProfile: string;
  sdl3GameProfileDesc: string;
  sdl3GameProfileToggle: string;
  sdl3GameProfileToggleDesc: string;
  amdFrameAndLatency: string;
  amdScalingAndImage: string;
  amdBuildNeeded: string;
  amdRsr: string;
  amdRsrHint: string;
  amdRsrSharpness: string;
  amdAfmf: string;
  amdAfmfHint: string;
  amdAntilag: string;
  amdAntilagHint: string;
  amdChill: string;
  amdChillHint: string;
  amdChillMin: string;
  amdChillMax: string;
  amdSharpening: string;
  amdSharpeningHint: string;
  amdSharpeningValue: string;
  amdBoost: string;
  amdBoostHint: string;
  amdBoostResolution: string;
  amdEnhancedSync: string;
  amdEnhancedSyncHint: string;
  amdDisplayColor: string;
  amdDisplayBrightness: string;
  amdDisplayContrast: string;
  amdDisplaySaturation: string;
  amdDisplayTemperature: string;
  amdDriverRejected: string;
  advanced: string;
  agentLabel: string;
  agentRunning: string;
  agentStopped: string;
  startAgent: string;
  stopAgent: string;
  agentHint: string;
  diagnostics: string;
  generateDiagnostics: string;
  diagnosticsReady: string;
  ok: string;
  cancel: string;
  notConnected: string;
}

const amdEn = {
  amd: "AMD Radeon",
  amdGlobalHint: "Driver-level global Radeon settings. 3D features become visible in supported games; desktop sharpening also affects Steam.",
  amdGameProfile: "AMD Radeon game profile",
  amdGameProfileDesc: "When enabled, these settings temporarily replace the global QAM settings while this game is running. Your global settings return when the game closes.",
  amdGameProfileToggle: "Use an AMD profile for this game",
  amdGameProfileToggleDesc: "Leave this off to use the global AMD settings selected in Quick Settings.",
  amdGameProfileUnavailable: "AMD Radeon controls are not available on this system.",
  sdl3GameProfile: "Native SDL3 controller",
  sdl3GameProfileDesc: "Choose whether this game should start with native SDL3 controller recognition, bypassing Steam Input.",
  sdl3GameProfileToggle: "Enable SDL3 for this game",
  sdl3GameProfileToggleDesc: "Useful for emulators and games that need direct Steam Controller access. The global controller setting remains unchanged.",
  amdFrameAndLatency: "Frames and latency",
  amdScalingAndImage: "Scaling and image",
  amdBuildNeeded: "Radeon helper missing. If it does not appear, reinstall the plugin (it ships pre-built).",
  amdRsr: "Radeon Super Resolution",
  amdRsrHint: "Runs the game at a lower resolution and upscales it sharply — more FPS in almost any game.",
  amdRsrSharpness: "RSR sharpness",
  amdAfmf: "Fluid Motion Frames (AFMF)",
  amdAfmfHint: "Globally enables driver frame generation for compatible games. Its effect is visible in a running game, not in the Steam interface.",
  amdAntilag: "Anti-Lag",
  amdAntilagHint: "Shortens the delay between your input and the screen — controls feel snappier.",
  amdChill: "Radeon Chill",
  amdChillHint: "Lowers FPS when you are idle to save power and heat, raising them when the action resumes.",
  amdChillMin: "Chill min FPS",
  amdChillMax: "Chill max FPS",
  amdSharpening: "Image Sharpening",
  amdSharpeningHint: "Sharpens supported 3D apps and the Windows desktop immediately, including the Steam interface.",
  amdSharpeningValue: "Sharpening amount",
  amdBoost: "Radeon Boost",
  amdBoostHint: "Dynamically lowers resolution during fast movement to improve frame rate.",
  amdBoostResolution: "Minimum dynamic resolution",
  amdEnhancedSync: "Enhanced Sync",
  amdEnhancedSyncHint: "Reduces tearing without the full latency cost of traditional V-Sync.",
  amdDisplayColor: "Display color",
  amdDisplayBrightness: "Color brightness",
  amdDisplayContrast: "Contrast",
  amdDisplaySaturation: "Saturation",
  amdDisplayTemperature: "Color temperature",
  amdDriverRejected: "The AMD driver did not confirm this change. The previous value has been restored.",
};

const amdIt = {
  amd: "AMD Radeon",
  amdGlobalHint: "Impostazioni Radeon globali a livello driver. Le funzioni 3D diventano visibili nei giochi supportati; la nitidezza desktop agisce anche su Steam.",
  amdGameProfile: "Profilo gioco AMD Radeon",
  amdGameProfileDesc: "Quando è attivo, queste impostazioni sostituiscono temporaneamente quelle globali del QAM mentre il gioco è in esecuzione. Alla chiusura vengono ripristinate le impostazioni globali.",
  amdGameProfileToggle: "Usa un profilo AMD per questo gioco",
  amdGameProfileToggleDesc: "Lascialo disattivato per usare le impostazioni AMD globali scelte in Quick Settings.",
  amdGameProfileUnavailable: "I controlli AMD Radeon non sono disponibili su questo sistema.",
  sdl3GameProfile: "Controller nativo SDL3",
  sdl3GameProfileDesc: "Scegli se avviare questo gioco con il riconoscimento nativo del controller SDL3, bypassando Steam Input.",
  sdl3GameProfileToggle: "Abilita SDL3 per questo gioco",
  sdl3GameProfileToggleDesc: "Utile per emulatori e giochi che richiedono l'accesso diretto allo Steam Controller. L'impostazione globale del controller resta invariata.",
  amdFrameAndLatency: "Fotogrammi e latenza",
  amdScalingAndImage: "Scaling e immagine",
  amdBuildNeeded: "Helper Radeon mancante. Se non compare, reinstalla il plugin (viene fornito gi\u00e0 compilato).",
  amdRsr: "Radeon Super Resolution",
  amdRsrHint: "Esegue il gioco a risoluzione più bassa e lo ingrandisce restando nitido — più FPS quasi ovunque.",
  amdRsrSharpness: "Nitidezza RSR",
  amdAfmf: "Fluid Motion Frames (AFMF)",
  amdAfmfHint: "Attiva globalmente la generazione di fotogrammi nei giochi compatibili. L'effetto si vede nel gioco, non nell'interfaccia di Steam.",
  amdAntilag: "Anti-Lag",
  amdAntilagHint: "Accorcia il ritardo tra il tuo comando e lo schermo — i controlli sembrano più immediati.",
  amdChill: "Radeon Chill",
  amdChillHint: "Abbassa gli FPS quando sei fermo per consumare e scaldare meno, e li rialza quando riprende l'azione.",
  amdChillMin: "Chill FPS min",
  amdChillMax: "Chill FPS max",
  amdSharpening: "Nitidezza immagine",
  amdSharpeningHint: "Rende subito più definiti le app 3D supportate e il desktop Windows, inclusa l'interfaccia di Steam.",
  amdSharpeningValue: "Intensità nitidezza",
  amdBoost: "Radeon Boost",
  amdBoostHint: "Riduce dinamicamente la risoluzione durante i movimenti rapidi per aumentare il frame rate.",
  amdBoostResolution: "Risoluzione dinamica minima",
  amdEnhancedSync: "Enhanced Sync",
  amdEnhancedSyncHint: "Riduce il tearing senza aggiungere tutta la latenza del V-Sync tradizionale.",
  amdDisplayColor: "Colore schermo",
  amdDisplayBrightness: "Luminosità colore",
  amdDisplayContrast: "Contrasto",
  amdDisplaySaturation: "Saturazione",
  amdDisplayTemperature: "Temperatura colore",
    amdDriverRejected: "Il driver AMD non ha confermato questa modifica. Il valore precedente è stato ripristinato.",
};

const en: Strings = {
  title: "Quick Settings",
  audio: "Audio",
  volume: "Device volume",
  microphoneVolume: "Microphone volume",
  audioOutput: "Audio output",
  microphoneInput: "Audio input",
  audioDevicesUnavailable: "Audio devices unavailable",
  audioChangeFailed: "Could not change the audio device",
  display: "Display",
  brightness: "Brightness",
  hdr: "HDR",
  hdrUnavailable: "Could not toggle HDR",
  displayChangeFailed: "Could not apply the display mode",
  powerModeFailed: "Windows did not apply the power mode",
  hdrConfirmTitle: "Keep HDR change?",
  hdrConfirmBody:
    "If the image looks correct, press OK. Otherwise the HDR toggle will be reverted automatically in",
  resolution: "Resolution",
  refreshRate: "Refresh rate",
  adaptingUi: "Adapting the interface to the new resolution…",
  performance: "Performance",
  powerMode: "Windows power mode",
  powerEfficiency: "Best power efficiency",
  powerBalanced: "Balanced",
  powerBetter: "Better performance",
  powerBest: "Best performance",
  tdp: "TDP (AMD)",
  tdpLimit: "Power limit",
  lossless: "Lossless Scaling",
  losslessLaunch: "Launch Lossless Scaling",
  losslessScaling: "Scaling",
  losslessFrameGen: "Frame generation",
  losslessMultiplier: "Frame gen multiplier",
  losslessHotkeyHint: "Scaling hotkey:",
  losslessApplyNote: "Changing frame generation restarts Lossless Scaling.",
  losslessManagedHint: "Uses the installed Lossless Scaling engine directly and releases it when scaling is disabled.",
  losslessGameOnlyHint: "Scaling can be enabled only while a game is running. Configure automatic activation from that game's Quick Settings page.",
  automaticScaling: "Automatic scaling",
  automaticScalingDesc: "Choose whether this game should start Lossless Scaling automatically.",
  autoScaleGame: "Automatically enable Lossless Scaling when the game starts",
  autoScaleGameDesc: "Activates the installed Lossless Scaling engine after Steam reports this game as running.",
  activationDelay: "Activation delay",
  activationDelayDesc: "Waits for the game window to finish opening before the scaling hotkey is sent.",
  scalingAlgorithm: "Scaling algorithm",
  scalingAlgorithmDesc: "Controls how a lower-resolution game image is enlarged to the output display.",
  scalingMode: "Scaling mode",
  scalingModeDesc: "Auto detects the game area; Custom uses the scale factor and fit options below.",
  fitMode: "Fit mode",
  scaleFactor: "Scale factor",
  resizeBeforeScaling: "Resize before scaling",
  resizeBeforeScalingDesc: "Resizes the captured frame before applying the selected scaling algorithm.",
  windowedMode: "Windowed mode",
  windowedModeDesc: "Use this when a game cannot be captured correctly in borderless or fullscreen mode.",
  fsrVariant: "FSR variant",
  ls1Variant: "LS1 quality",
  anime4kSize: "Anime4K size",
  sharpness: "Sharpness",
  ls1Sharpness: "LS1 sharpness",
  variableRateShading: "Variable rate shading",
  variableRateShadingDesc: "Reduces shading work in less detailed areas. It may improve performance but soften fine detail.",
  frameGenMode: "Frame generation mode",
  frameGenerationDesc: "Creates intermediate frames for smoother motion. Higher multipliers need stable base FPS.",
  frameGenModeDesc: "Selects the Lossless Scaling frame-generation engine used for this game.",
  targetFps: "Target FPS",
  targetFpsDesc: "Adaptive LSFG3 uses this value to decide how many frames to generate.",
  flowScale: "Optical flow scale",
  performanceProfile: "Performance profile",
  rendering: "Rendering",
  renderingDesc: "Capture, synchronization and latency options for the final scaled output.",
  syncMode: "Sync mode",
  syncModeDesc: "Controls presentation timing. Default is safest; other modes trade tearing for latency.",
  maxFrameLatency: "Maximum frame latency",
  maxFrameLatencyDesc: "Limits queued frames. Lower values reduce latency but can make frame pacing uneven.",
  gsyncSupport: "G-Sync / VRR support",
  hdrPassthrough: "HDR support",
  hdrPassthroughDesc: "Keeps HDR metadata and luminance when both the game and display use HDR.",
  drawFps: "Draw FPS counter",
  drawFpsDesc: "Applied the next time Lossless Scaling starts.",
  captureApi: "Capture API",
  captureApiDesc: "DXGI is usually fastest; WGC improves compatibility; GDI is a last-resort fallback.",
  queueTarget: "Queue target",
  cursor: "Cursor and capture",
  cursorDesc: "Controls pointer confinement, scaling and optional cropping of the captured game.",
  clipCursor: "Clip cursor",
  adjustCursorSpeed: "Adjust cursor speed",
  hideCursor: "Hide cursor",
  scaleCursor: "Scale cursor",
  multiDisplayMode: "Multi-display mode",
  multiDisplayModeDesc: "Use when the game window and scaled output are on different displays.",
  cropInput: "Crop input",
  cropInputDesc: "Removes unwanted borders from the captured image before scaling.",
  cropLeft: "Crop left",
  cropTop: "Crop top",
  cropRight: "Crop right",
  cropBottom: "Crop bottom",
  gpuAndDisplay: "GPU and display",
  gpuAndDisplayDesc: "Override these only when automatic device selection chooses incorrectly.",
  preferredGpu: "Preferred GPU",
  preferredGpuDesc: "Lossless Scaling GPU index. Leave at 0 for automatic selection.",
  outputDisplay: "Output display",
  outputDisplayDesc: "Display index used for the scaled image. Leave at 0 for the current display.",
  saved: "Saved",
  saving: "Saving…",
  profileLoadError: "Could not load this game profile",
  back: "Back",
  off: "Off",
  ...amdEn,
  advanced: "Advanced",
  agentLabel: "Quick Settings agent",
  agentRunning: "Running",
  agentStopped: "Stopped",
  startAgent: "Start agent",
  stopAgent: "Stop agent",
  agentHint: "Stop the agent before uninstalling or updating the plugin. It only runs in Big Picture.",
  diagnostics: "Diagnostics",
  generateDiagnostics: "Generate diagnostic log",
  diagnosticsReady: "Diagnostic log saved to",
  ok: "OK",
  cancel: "Cancel",
  notConnected: "Quick Settings agent is not connected",
};

const it: Strings = {
  title: "Quick Settings",
  audio: "Audio",
  volume: "Volume dispositivo",
  microphoneVolume: "Volume microfono",
  audioOutput: "Uscita audio",
  microphoneInput: "Ingresso audio",
  audioDevicesUnavailable: "Dispositivi audio non disponibili",
  audioChangeFailed: "Impossibile cambiare il dispositivo audio",
  display: "Schermo",
  brightness: "Luminosità",
  hdr: "HDR",
  hdrUnavailable: "Impossibile attivare/disattivare HDR",
  displayChangeFailed: "Impossibile applicare la modalità schermo",
  powerModeFailed: "Windows non ha applicato la modalità energetica",
  hdrConfirmTitle: "Mantenere la modifica HDR?",
  hdrConfirmBody:
    "Se l'immagine è corretta, premi OK. Altrimenti l'HDR verrà ripristinato automaticamente tra",
  resolution: "Risoluzione",
  refreshRate: "Frequenza di aggiornamento",
  adaptingUi: "Adatto l'interfaccia alla nuova risoluzione…",
  performance: "Prestazioni",
  powerMode: "Modalità energetica di Windows",
  powerEfficiency: "Massima efficienza",
  powerBalanced: "Bilanciata",
  powerBetter: "Prestazioni migliori",
  powerBest: "Prestazioni massime",
  tdp: "TDP (AMD)",
  tdpLimit: "Limite di potenza",
  lossless: "Lossless Scaling",
  losslessLaunch: "Avvia Lossless Scaling",
  losslessScaling: "Scaling",
  losslessFrameGen: "Generazione fotogrammi",
  losslessMultiplier: "Moltiplicatore generazione",
  losslessHotkeyHint: "Scorciatoia scaling:",
  losslessApplyNote: "Cambiare la generazione fotogrammi riavvia Lossless Scaling.",
  losslessManagedHint: "Usa direttamente il motore Lossless Scaling installato e lo libera quando disattivi lo scaling.",
    losslessGameOnlyHint: "Lo scaling può essere attivato solo mentre un gioco è in esecuzione. Configura l'avvio automatico dalla pagina Quick Settings di quel gioco.",
  automaticScaling: "Scaling automatico",
  automaticScalingDesc: "Scegli se avviare automaticamente Lossless Scaling per questo gioco.",
  autoScaleGame: "Attiva automaticamente Lossless Scaling all'avvio del gioco",
  autoScaleGameDesc: "Attiva il motore Lossless Scaling installato quando Steam segnala che il gioco \u00e8 in esecuzione.",
  activationDelay: "Ritardo di attivazione",
  activationDelayDesc: "Attende che la finestra del gioco sia pronta prima di inviare la scorciatoia di scaling.",
  scalingAlgorithm: "Algoritmo di scaling",
  scalingAlgorithmDesc: "Stabilisce come ingrandire l'immagine del gioco dalla risoluzione interna allo schermo.",
  scalingMode: "Modalità di scaling",
  scalingModeDesc: "Auto rileva l'area del gioco; Personalizzata usa fattore di scala e adattamento indicati sotto.",
  fitMode: "Adattamento immagine",
  scaleFactor: "Fattore di scala",
  resizeBeforeScaling: "Ridimensiona prima dello scaling",
  resizeBeforeScalingDesc: "Ridimensiona il fotogramma catturato prima di applicare l'algoritmo scelto.",
  windowedMode: "Modalità finestra",
  windowedModeDesc: "Usala se il gioco non viene catturato correttamente in fullscreen o borderless.",
  fsrVariant: "Variante FSR",
  ls1Variant: "Qualità LS1",
  anime4kSize: "Dimensione Anime4K",
  sharpness: "Nitidezza",
  ls1Sharpness: "Nitidezza LS1",
  variableRateShading: "Variable rate shading",
  variableRateShadingDesc: "Riduce il lavoro grafico nelle aree meno dettagliate. Pu\u00f2 migliorare le prestazioni ma ammorbidire i dettagli fini.",
  frameGenMode: "Modalità generazione fotogrammi",
  frameGenerationDesc: "Crea fotogrammi intermedi per rendere il movimento pi\u00f9 fluido. Moltiplicatori alti richiedono FPS di base stabili.",
  frameGenModeDesc: "Seleziona il motore di generazione fotogrammi usato per questo gioco.",
  targetFps: "FPS obiettivo",
  targetFpsDesc: "LSFG3 adattivo usa questo valore per decidere quanti fotogrammi generare.",
  flowScale: "Scala flusso ottico",
  performanceProfile: "Profilo prestazioni",
  rendering: "Rendering",
  renderingDesc: "Opzioni di cattura, sincronizzazione e latenza dell'immagine finale.",
  syncMode: "Modalità sincronizzazione",
  syncModeDesc: "Controlla la presentazione. Predefinita \u00e8 la scelta pi\u00f9 sicura; le altre bilanciano tearing e latenza.",
  maxFrameLatency: "Latenza massima fotogrammi",
  maxFrameLatencyDesc: "Limita i fotogrammi in coda. Valori bassi riducono la latenza ma possono rendere il frame pacing meno uniforme.",
  gsyncSupport: "Supporto G-Sync / VRR",
  hdrPassthrough: "Supporto HDR",
  hdrPassthroughDesc: "Mantiene metadati e luminosit\u00e0 HDR quando gioco e schermo usano entrambi HDR.",
  drawFps: "Mostra contatore FPS",
  drawFpsDesc: "La modifica viene applicata alla successiva attivazione di Lossless Scaling.",
  captureApi: "API di cattura",
  captureApiDesc: "DXGI \u00e8 solitamente pi\u00f9 veloce; WGC migliora la compatibilit\u00e0; GDI \u00e8 l'ultima alternativa.",
  queueTarget: "Coda obiettivo",
  cursor: "Cursore e cattura",
  cursorDesc: "Gestisce confinamento e dimensione del puntatore, oltre al ritaglio opzionale della cattura.",
  clipCursor: "Limita cursore",
  adjustCursorSpeed: "Regola velocità cursore",
  hideCursor: "Nascondi cursore",
  scaleCursor: "Ridimensiona cursore",
  multiDisplayMode: "Modalità multi-schermo",
  multiDisplayModeDesc: "Usala quando il gioco e l'immagine scalata si trovano su schermi diversi.",
  cropInput: "Ritaglia input",
  cropInputDesc: "Rimuove bordi indesiderati dall'immagine catturata prima dello scaling.",
  cropLeft: "Ritaglio sinistro",
  cropTop: "Ritaglio superiore",
  cropRight: "Ritaglio destro",
  cropBottom: "Ritaglio inferiore",
  gpuAndDisplay: "GPU e schermo",
  gpuAndDisplayDesc: "Forza GPU o schermo solo se la selezione automatica sceglie il dispositivo sbagliato.",
  preferredGpu: "GPU preferita",
  preferredGpuDesc: "Indice GPU di Lossless Scaling. Lascia 0 per la selezione automatica.",
  outputDisplay: "Schermo di uscita",
  outputDisplayDesc: "Indice dello schermo usato per l'immagine scalata. Lascia 0 per quello corrente.",
  saved: "Salvato",
  saving: "Salvataggio…",
  profileLoadError: "Impossibile caricare il profilo del gioco",
  back: "Indietro",
  off: "Off",
  ...amdIt,
  advanced: "Avanzate",
  agentLabel: "Agent Quick Settings",
  agentRunning: "In esecuzione",
  agentStopped: "Fermo",
  startAgent: "Avvia agent",
  stopAgent: "Ferma agent",
  agentHint: "Ferma l'agent prima di disinstallare o aggiornare il plugin. Si avvia solo in Big Picture.",
  diagnostics: "Diagnostica",
  generateDiagnostics: "Genera log diagnostico",
  diagnosticsReady: "Log diagnostico salvato in",
  ok: "OK",
  cancel: "Annulla",
  notConnected: "Agent Quick Settings non collegato",
};

const strings: Record<string, Strings> = { en, it };

export function t(locale = navigator.language): Strings {
  const language = locale.split("-")[0];
  return { ...(strings[language] ?? strings.en), ...quickCoreLocale(language), title: "Playhub" };
}
