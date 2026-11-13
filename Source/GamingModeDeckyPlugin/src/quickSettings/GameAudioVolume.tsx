import { SliderField } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { createSettledWriter } from "../panelIntegration/settledWriter";

const labels: Record<string, string> = {
  en: "Game volume", it: "Volume del gioco", de: "Spiellautstärke", es: "Volumen del juego",
  fr: "Volume du jeu", pt: "Volume do jogo", ru: "Громкость игры", uk: "Гучність гри",
  ja: "ゲーム音量", ko: "게임 볼륨", zh: "遊戲音量", hi: "गेम की आवाज़",
};

type Volume = { level: number; token: string };
type Reply = { status?: string; level?: number; token?: string };

function confirmedVolume(result: Reply | undefined): Volume | undefined {
  if (result?.status !== "Ready" || typeof result.level !== "number" || !Number.isInteger(result.level) ||
      result.level < 0 || result.level > 100 || typeof result.token !== "string" || !/^[0-9a-f]{64}$/.test(result.token)) return;
  return { level: result.level, token: result.token };
}

export function GameAudioVolume({ appId, locale }: { appId: number; locale: string }) {
  const [volume, setVolume] = useState<Volume>();
  const [available, setAvailable] = useState(false);
  const [failed, setFailed] = useState(false);
  const confirmed = useRef<Volume | undefined>(undefined);
  const writer = useRef<ReturnType<typeof createSettledWriter<Volume, Reply>> | undefined>(undefined);
  const revision = useRef(0);
  const pending = useRef(false);

  useEffect(() => {
    if (!appId) return;
    let alive = true;
    let reading = false;
    confirmed.current = undefined;
    pending.current = false;
    setVolume(undefined);
    setAvailable(false);
    setFailed(false);

    const settled = (reply?: Reply) => {
      pending.current = false;
      const saved = confirmedVolume(reply);
      if (saved) confirmed.current = saved;
      setVolume(confirmed.current);
      setAvailable(!!saved);
      setFailed(!saved);
    };
    const currentWriter = createSettledWriter<Volume, Reply>({
      delayMs: 350,
      write: ({ level, token }) => call<[number, number, string], Reply>("game_audio_volume", appId, level, token),
      onSettled: settled,
      onError: () => settled(),
    });
    writer.current = currentWriter;

    const refresh = async () => {
      if (!alive || reading || pending.current || document.visibilityState === "hidden") return;
      reading = true;
      const startedAt = revision.current;
      try {
        const reply = await call<[number], Reply>("game_audio_volume", appId);
        if (!alive || pending.current || revision.current !== startedAt) return;
        const current = confirmedVolume(reply);
        if (current) {
          confirmed.current = current;
          setVolume(current);
          setFailed(false);
        }
        setAvailable(!!current);
      } catch {
        if (alive && revision.current === startedAt && !pending.current) setAvailable(false);
      }
      finally { reading = false; }
    };
    void refresh();
    const timer = window.setInterval(refresh, 3_000);
    return () => {
      alive = false;
      currentWriter.dispose();
      if (writer.current === currentWriter) writer.current = undefined;
      window.clearInterval(timer);
    };
  }, [appId]);

  if (!volume) return null;
  const lang = locale.toLowerCase().split(/[-_]/)[0];
  return <div>
    <SliderField bottomSeparator="none" label={labels[lang] ?? labels.en}
      description={new Intl.NumberFormat(labels[lang] ? lang : "en", { style: "percent" }).format(volume.level / 100)}
      disabled={!available} min={0} max={100} step={1} minimumDpadGranularity={2} value={volume.level} onChange={value => {
        const level = Math.round(value);
        if (!available || !writer.current || !Number.isFinite(level) || level < 0 || level > 100 || level === volume.level) return;
        const next = { token: volume.token, level };
        revision.current++;
        pending.current = true;
        setFailed(false);
        setVolume(next);
        writer.current.enqueue(next);
      }} />
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}
