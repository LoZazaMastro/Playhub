// Independent passive RPC contract. Never import controlBackend: it may reload.
export const diagnosticMethod = "get_controller_sdl_diagnostics";
export const blockedFeatures = ["liveInput", "physicalRumble", "liveMapping", "virtualOutput", "gyro", "trackpads", "exclusiveMode", "hardwareValidated"] as const;
const statuses = ["not_configured", "invalid_path", "file_missing", "access_denied", "read_failed", "file_too_large", "file_changed", "file_observed"] as const;
type Status = typeof statuses[number];
export interface ControllerDiagnostic {
  schemaVersion: 1;
  scope: "sdl_file_only";
  observedAtMs: number;
  status: Status;
  sha256: string | null;
  sizeBytes: number | null;
  nativeLoaded: false;
  deviceProbed: false;
  capabilities: Record<typeof blockedFeatures[number], false>;
}
export type DiagnosticResult = { kind: "observed"; value: ControllerDiagnostic } | { kind: "unavailable" | "invalid" | "timeout" };
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === "object" && !Array.isArray(value);
export function parseControllerDiagnostic(value: unknown, now = Date.now()): ControllerDiagnostic | null {
  const caps = record(value) && record(value.capabilities) ? value.capabilities : null;
  if (!record(value) || value.schemaVersion !== 1 || value.scope !== "sdl_file_only" ||
      value.nativeLoaded !== false || value.deviceProbed !== false ||
      !statuses.includes(value.status as Status) || !caps ||
      Object.keys(caps).length !== blockedFeatures.length ||
      !blockedFeatures.every(key => caps[key] === false)) return null;
  if (typeof value.observedAtMs !== "number" || !Number.isSafeInteger(value.observedAtMs) ||
      value.observedAtMs <= 0 || !Number.isFinite(now) ||
      now - value.observedAtMs > 60_000 || value.observedAtMs - now > 5_000) return null;
  if (value.status === "file_observed") {
    if (typeof value.sha256 !== "string" || !/^[a-f0-9]{64}$/.test(value.sha256) ||
        typeof value.sizeBytes !== "number" || !Number.isSafeInteger(value.sizeBytes) ||
        value.sizeBytes < 0 || value.sizeBytes > 32 * 1024 * 1024) return null;
  } else if (value.sha256 !== null || value.sizeBytes !== null) return null;
  return {
    schemaVersion: 1, scope: "sdl_file_only", observedAtMs: value.observedAtMs,
    status: value.status as Status, sha256: value.sha256 as string | null,
    sizeBytes: value.sizeBytes as number | null, nativeLoaded: false, deviceProbed: false,
    capabilities: Object.fromEntries(blockedFeatures.map(key => [key, false])) as ControllerDiagnostic["capabilities"],
  };
}
export async function requestControllerDiagnostic(call: (method: string) => Promise<unknown>): Promise<DiagnosticResult> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  try {
    // No retry, reload, controller argument, path argument or cached readiness.
    const request = Promise.resolve().then(() => call(diagnosticMethod)).then((raw): DiagnosticResult => {
      const value = parseControllerDiagnostic(raw);
      return value ? { kind: "observed", value } : { kind: "invalid" };
    }).catch((): DiagnosticResult => ({ kind: "unavailable" }));
    return await Promise.race([request, new Promise<DiagnosticResult>(resolve => {
      timer = setTimeout(() => resolve({ kind: "timeout" }), 3000);
    })]);
  } finally { if (timer !== undefined) clearTimeout(timer); }
}
export function controllerDiagnosticCopy(locale: string) {
  const it = /^(it|italian)(-|$)/i.test(locale);
  return it ? {
    title: "Diagnostica runtime controller", check: "Verifica file SDL", pending: "Verifica in corso",
    idle: "File SDL non verificato", unavailable: "Diagnostica backend non disponibile",
    invalid: "Risposta diagnostica non valida o scaduta", timeout: "Diagnostica scaduta",
    not_configured: "Runtime SDL non configurato", invalid_path: "Percorso SDL non valido",
    file_missing: "File SDL assente", access_denied: "Accesso al file SDL negato",
    read_failed: "Lettura del file SDL fallita", file_too_large: "File SDL oltre il limite diagnostico",
    file_changed: "File SDL cambiato durante la verifica", file_observed: "File SDL presente; compatibilita e hardware non verificati",
  } : {
    title: "Controller runtime diagnostics", check: "Check SDL file", pending: "Checking",
    idle: "SDL file not checked", unavailable: "Backend diagnostics unavailable",
    invalid: "Invalid or stale diagnostic response", timeout: "Diagnostics timed out",
    not_configured: "SDL runtime not configured", invalid_path: "Invalid SDL path",
    file_missing: "SDL file missing", access_denied: "SDL file access denied",
    read_failed: "SDL file read failed", file_too_large: "SDL file exceeds diagnostic limit",
    file_changed: "SDL file changed during check", file_observed: "SDL file present; compatibility and hardware unverified",
  };
}
export function controllerDiagnosticText(result: DiagnosticResult | null, locale: string): string {
  const copy = controllerDiagnosticCopy(locale);
  if (!result) return copy.idle;
  if (result.kind !== "observed") return copy[result.kind];
  // Revalidate when rendered later; a retained result must not become readiness.
  const value = parseControllerDiagnostic(result.value);
  return value ? copy[value.status] : copy.invalid;
}
