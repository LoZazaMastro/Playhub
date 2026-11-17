export type UpdateNotice = { available?: boolean; version?: string };
type Dependencies = {
  main: () => unknown;
  check: () => Promise<UpdateNotice>;
  read: () => string | null;
  write: (version: string) => void;
  notify: (version: string) => void;
  schedule: (callback: () => void, delay: number) => unknown;
  cancel: (timer: unknown) => void;
};

export function installPlayhubUpdateNotice(deps: Dependencies): () => void {
  let disposed = false, busy = false, previous: unknown, timer: unknown;
  const shown = new Set<string>();
  async function check(main: unknown) {
    if (busy) return;
    busy = true;
    try {
      const reply = await deps.check();
      const version = reply?.version;
      if (disposed || deps.main() !== main || reply?.available !== true || typeof version !== "string"
        || !/^\d+\.\d+\.\d+(?:\.\d+)?$/.test(version) || shown.has(version)) return;
      let saved: string | null = null;
      try { saved = deps.read(); } catch { /* Storage may be unavailable in a Steam window. */ }
      if (saved && /^\d+\.\d+\.\d+(?:\.\d+)?$/.test(saved)) {
        const old = saved.split(".").map(Number), next = version.split(".").map(Number);
        let newer = false;
        for (let i = 0; i < 4; i++) {
          if ((next[i] ?? 0) !== (old[i] ?? 0)) { newer = (next[i] ?? 0) > (old[i] ?? 0); break; }
        }
        if (!newer) return;
      }
      deps.notify(version);
      shown.add(version);
      try { deps.write(version); } catch { /* The current session still prevents duplicate notices. */ }
    } catch { /* Update checks leave Steam usable while offline. */ }
    finally { busy = false; }
  }
  function tick() {
    if (disposed) return;
    let main: unknown;
    try { main = deps.main(); } catch { main = undefined; }
    if (!main) previous = undefined;
    if (!busy) {
      if (main && main !== previous) void check(main);
      previous = main;
    }
    timer = deps.schedule(tick, 2000);
  }
  tick();
  return () => { disposed = true; deps.cancel(timer); };
}

export function updateNoticeBody(locale: string, version: string): string {
  const copy: Record<string, string> = {
    en: "Playhub {version} is available. Open Playhub on your PC to update.",
    it: "È disponibile Playhub {version}. Apri Playhub sul PC per aggiornare.",
    es: "Playhub {version} está disponible. Abre Playhub en tu PC para actualizar.",
    fr: "Playhub {version} est disponible. Ouvrez Playhub sur votre PC pour le mettre à jour.",
    de: "Playhub {version} ist verfügbar. Öffne Playhub auf deinem PC, um es zu aktualisieren.",
    pt: "O Playhub {version} está disponível. Abra o Playhub no PC para atualizar.",
    uk: "Доступна версія Playhub {version}. Відкрийте Playhub на ПК, щоб оновити.",
    zh: "Playhub {version} 已推出。请在电脑上打开 Playhub 进行更新。",
    ja: "Playhub {version} が利用できます。PC で Playhub を開いて更新してください。",
    ko: "Playhub {version}을 사용할 수 있습니다. PC에서 Playhub를 열어 업데이트하세요.",
    hi: "Playhub {version} उपलब्ध है। अपडेट करने के लिए अपने PC पर Playhub खोलें।",
    ru: "Доступна версия Playhub {version}. Откройте Playhub на ПК, чтобы обновить.",
  };
  return (copy[locale] ?? copy.en).replace("{version}", version);
}
