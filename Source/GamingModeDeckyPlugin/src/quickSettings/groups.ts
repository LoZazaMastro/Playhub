const rows = {
  en: ["Power", "Game", "Shaders", "Mixer", "Devices", "Measurement"],
  it: ["Alimentazione", "Gioco", "Shader", "Mixer", "Dispositivi", "Misurazione"],
  de: ["Energie", "Spiel", "Shader", "Mixer", "Geräte", "Messung"],
  es: ["Energía", "Juego", "Shaders", "Mezclador", "Dispositivos", "Medición"],
  fr: ["Alimentation", "Jeu", "Shaders", "Mixeur", "Périphériques", "Mesure"],
  pt: ["Energia", "Jogo", "Shaders", "Mixer", "Dispositivos", "Medição"],
  ru: ["Питание", "Игра", "Шейдеры", "Микшер", "Устройства", "Измерение"],
  uk: ["Живлення", "Гра", "Шейдери", "Мікшер", "Пристрої", "Вимірювання"],
  ja: ["電源", "ゲーム", "シェーダー", "ミキサー", "デバイス", "計測"],
  ko: ["전원", "게임", "셰이더", "믹서", "장치", "측정"],
  zh: ["电源", "游戏", "着色器", "混音器", "设备", "测量"],
  hi: ["पावर", "गेम", "शेडर", "मिक्सर", "डिवाइस", "मापन"],
} as const;

export function quickSettingsGroups(locale: string) {
  const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es",
    french: "fr", portuguese: "pt", brazilian: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };
  const key = aliases[locale] ?? locale.toLowerCase().split(/[-_]/)[0];
  const row = rows[key as keyof typeof rows] ?? rows.en;
  return { power: row[0], game: row[1], shaders: row[2], mixer: row[3], devices: row[4], measurement: row[5] };
}
