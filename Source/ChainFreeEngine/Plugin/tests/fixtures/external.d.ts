// Solo per il controllo dell'estratto: non sostituisce i tipi delle dipendenze
// nel repository completo e non deve entrare nella build di produzione.
declare module '@decky/ui';
declare module '*renderer-ownership.mjs' {
  export function mountPlayhubRenderer(options: any): { ready: Promise<void>; dispose(): Promise<void> };
}
