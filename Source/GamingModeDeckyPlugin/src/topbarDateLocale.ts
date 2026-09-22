// Testi della sezione "Barra superiore" nelle impostazioni del plugin.
// Ordine: titolo, data accanto all'ora, descrizione, sposta a sinistra,
// descrizione, formato della data, automatico.
const copy: Record<string, string[]> = {
  en: ["Top bar", "Date next to the time", "Shows the date in Steam's top bar, before the weather and the current track.", "Move time and date to the left", "Moves the clock group, together with weather and music when shown.", "Date format", "Automatic"],
  it: ["Barra superiore", "Data accanto all'ora", "Mostra la data nella barra superiore di Steam, prima di meteo e brano corrente.", "Sposta ora e data a sinistra", "Sposta il gruppo dell’orologio, insieme a meteo e musica se presenti.", "Formato della data", "Automatico"],
  de: ["Obere Leiste", "Datum neben der Uhrzeit", "Zeigt das Datum in der oberen Leiste von Steam, vor Wetter und aktuellem Titel.", "Uhrzeit und Datum nach links", "Verschiebt die Uhrgruppe, zusammen mit Wetter und Musik, falls vorhanden.", "Datumsformat", "Automatisch"],
  es: ["Barra superior", "Fecha junto a la hora", "Muestra la fecha en la barra superior de Steam, antes del tiempo y la canción actual.", "Mover hora y fecha a la izquierda", "Mueve el grupo del reloj, junto con el tiempo y la música si aparecen.", "Formato de fecha", "Automático"],
  fr: ["Barre supérieure", "Date à côté de l’heure", "Affiche la date dans la barre supérieure de Steam, avant la météo et le titre en cours.", "Déplacer l’heure et la date à gauche", "Déplace le groupe de l’horloge, avec la météo et la musique si elles sont affichées.", "Format de date", "Automatique"],
  pt: ["Barra superior", "Data ao lado da hora", "Mostra a data na barra superior do Steam, antes do clima e da música atual.", "Mover hora e data para a esquerda", "Move o grupo do relógio, junto com clima e música, se exibidos.", "Formato da data", "Automático"],
  ru: ["Верхняя панель", "Дата рядом со временем", "Показывает дату в верхней панели Steam перед погодой и текущим треком.", "Переместить время и дату влево", "Перемещает блок часов вместе с погодой и музыкой, если они показаны.", "Формат даты", "Автоматически"],
  uk: ["Верхня панель", "Дата поруч із часом", "Показує дату у верхній панелі Steam перед погодою та поточним треком.", "Перемістити час і дату ліворуч", "Переміщує блок годинника разом із погодою та музикою, якщо вони показані.", "Формат дати", "Автоматично"],
  ja: ["トップバー", "時刻の横に日付", "Steam のトップバーで、天気と再生中の曲の前に日付を表示します。", "時刻と日付を左に移動", "天気や音楽が表示されている場合は、それらと一緒に時計を移動します。", "日付の形式", "自動"],
  ko: ["상단 바", "시간 옆에 날짜", "Steam 상단 바에서 날씨와 현재 곡 앞에 날짜를 표시합니다.", "시간과 날짜를 왼쪽으로 이동", "날씨와 음악이 표시되면 함께 시계 그룹을 이동합니다.", "날짜 형식", "자동"],
  zh: ["顶部栏", "时间旁显示日期", "在 Steam 顶部栏中，于天气和当前曲目之前显示日期。", "将时间和日期移到左侧", "移动时钟区域，若显示天气和音乐则一同移动。", "日期格式", "自动"],
  hi: ["ऊपरी बार", "समय के पास तारीख", "Steam की ऊपरी बार में मौसम और मौजूदा गाने से पहले तारीख दिखाता है।", "समय और तारीख बाईं ओर ले जाएँ", "घड़ी वाले हिस्से को, दिखने पर मौसम और संगीत के साथ, बाईं ओर ले जाता है।", "तारीख का फ़ॉर्मैट", "स्वचालित"],
};

const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };
const tags: Record<string, string> = { en: "en-US", it: "it-IT", de: "de-DE", es: "es-ES", fr: "fr-FR", pt: "pt-BR", ru: "ru-RU", uk: "uk-UA", ja: "ja-JP", ko: "ko-KR", zh: "zh-CN", hi: "hi-IN" };

function key(locale: string) {
  const lower = String(locale || "").toLowerCase();
  const k = aliases[lower] ?? lower.split(/[-_]/)[0];
  return copy[k] ? k : "en";
}

export function topbarDateCopy(locale: string) {
  const [title, dateLabel, dateDescription, leftLabel, leftDescription, formatLabel, automatic] = copy[key(locale)];
  return { title, dateLabel, dateDescription, leftLabel, leftDescription, formatLabel, automatic };
}

// Esempi calcolati come li scrive la barra superiore: stesse opzioni Intl e
// iniziale maiuscola per giorno e mese.
const FORMATS: Array<[string, Intl.DateTimeFormatOptions | null]> = [
  ["auto", { weekday: "short", day: "numeric", month: "short" }],
  ["dd_mm_yyyy", { day: "2-digit", month: "2-digit", year: "numeric" }],
  ["dd_mm_yy", { day: "2-digit", month: "2-digit", year: "2-digit" }],
  ["yyyy_mm_dd", { year: "numeric", month: "2-digit", day: "2-digit" }],
  ["dd_month_yyyy", { day: "numeric", month: "long", year: "numeric" }],
  ["weekday_dd_month", { weekday: "long", day: "numeric", month: "long" }],
  ["weekday_short_dd_month", { weekday: "short", day: "numeric", month: "short" }],
  ["month_dd_yyyy", { month: "long", day: "numeric", year: "numeric" }],
  ["month_short_dd_yyyy", { month: "short", day: "numeric", year: "numeric" }],
  ["iso", null],
];

export function topbarDateOptions(locale: string, sample = new Date(2026, 8, 19)) {
  const k = key(locale);
  const tag = tags[k];
  return FORMATS.map(([data, options]) => {
    let label = "2026-09-19";
    if (options) {
      try {
        label = new Intl.DateTimeFormat(tag, options).formatToParts(sample)
          .map(part => part.type === "weekday" || part.type === "month" ? part.value.charAt(0).toLocaleUpperCase(tag) + part.value.slice(1) : part.value)
          .join("");
      } catch { label = sample.toLocaleDateString(); }
    }
    if (data === "auto") label = `${copy[k][6]} (${label})`;
    return { data, label };
  });
}
