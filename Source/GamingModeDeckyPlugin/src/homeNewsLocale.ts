const words: Record<string, string[]> = {
 en: ["Gaming news", "Show gaming news in Home", "Country", "Follow Playhub language", "News unavailable. Try again later.", "Could not save news settings.", "Last available news"],
 it: ["Notizie dal mondo dei videogiochi", "Mostra le news nella Home", "Paese delle notizie", "Segui la lingua di Playhub", "Notizie non disponibili. Riprova più tardi.", "Impossibile salvare le impostazioni News.", "Ultime notizie disponibili"],
 de: ["Gaming-Nachrichten", "Gaming-Nachrichten auf der Startseite", "Land", "Playhub-Sprache verwenden", "Nachrichten derzeit nicht verfügbar.", "Nachrichteneinstellungen konnten nicht gespeichert werden.", "Zuletzt verfügbare Nachrichten"],
 es: ["Noticias de videojuegos", "Mostrar noticias en Inicio", "País", "Seguir el idioma de Playhub", "Noticias no disponibles. Inténtalo más tarde.", "No se pudo guardar la configuración.", "Últimas noticias disponibles"],
 fr: ["Actualités des jeux vidéo", "Afficher les actualités sur l’accueil", "Pays", "Suivre la langue de Playhub", "Actualités indisponibles. Réessayez plus tard.", "Impossible d’enregistrer les paramètres.", "Dernières actualités disponibles"],
 pt: ["Notícias de videogames", "Mostrar notícias na página inicial", "País", "Usar o idioma do Playhub", "Notícias indisponíveis. Tente mais tarde.", "Não foi possível salvar as configurações.", "Últimas notícias disponíveis"],
 ru: ["Новости видеоигр", "Показывать новости на главной", "Страна", "Использовать язык Playhub", "Новости пока недоступны.", "Не удалось сохранить настройки.", "Последние доступные новости"],
 uk: ["Новини відеоігор", "Показувати новини на головній", "Країна", "Використовувати мову Playhub", "Новини поки недоступні.", "Не вдалося зберегти налаштування.", "Останні доступні новини"],
 ja: ["ゲームニュース", "ホームにゲームニュースを表示", "国", "Playhubの言語に合わせる", "ニュースを取得できません。後でもう一度お試しください。", "設定を保存できませんでした。", "前回取得したニュース"],
 ko: ["게임 소식", "홈에 게임 소식 표시", "국가", "Playhub 언어 따르기", "뉴스를 불러올 수 없습니다.", "설정을 저장하지 못했습니다.", "마지막으로 받은 소식"],
 zh: ["游戏新闻", "在主页显示游戏新闻", "国家或地区", "跟随 Playhub 语言", "暂时无法获取新闻，请稍后重试。", "无法保存设置。", "最近获取的新闻"],
 hi: ["वीडियो गेम समाचार", "होम में गेम समाचार दिखाएँ", "देश", "Playhub की भाषा का उपयोग करें", "समाचार उपलब्ध नहीं हैं। बाद में फिर कोशिश करें।", "सेटिंग सहेजी नहीं जा सकी।", "पिछले उपलब्ध समाचार"],
};
export const NEWS_COUNTRIES = ["US", "IT", "DE", "ES", "FR", "BR", "RU", "UA", "JP", "KR", "TW", "IN"];
export function newsCopy(locale: string) { return words[locale] ?? words.en; }
