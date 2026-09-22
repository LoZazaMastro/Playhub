export type StoreLocale = "en" | "it" | "es" | "fr" | "de" | "pt" | "uk" | "zh" | "ja" | "ko" | "hi" | "ru";

export interface PluginStoreCopy {
  store: string;
  discover: string;
  manage: string;
  search: string;
  featured: string;
  newCategory: string;
  allPlugins: string;
  installedPlugins: string;
  installedOnly: string;
  allSources: string;
  sortName: string;
  sortNewest: string;
  sortUpdated: string;
  sortBy: string;
  previous: string;
  next: string;
  searchPlaceholder: string;
  noPlugins: string;
  loading: string;
  retry: string;
  install: string;
  update: string;
  updateAll: string;
  uninstall: string;
  pluginInstalled: string;
  pluginUpdated: string;
  pluginsUpdated: string;
  pluginsPartiallyUpdated: string;
  dontAskAgain: string;
  preferenceSaveFailed: string;
  restartDeckyQuestion: string;
  restartDeckyFailed: string;
  ok: string;
  later: string;
  installedVersion: string;
  latestVersion: string;
  description: string;
  releaseNotes: string;
  media: string;
  repository: string;
  select: string;
  back: string;
  switchTabs: string;
  scroll: string;
  stopReading: string;
  openCategory: string;
  preparing: string;
  releaseUnavailable: string;
  releaseNotesUnavailable: string;
  catalogUnavailable: string;
  uninstallTitle: string;
  installQuestion: string;
  updateQuestion: string;
  chooseVersion: string;
  uninstallDescription: string;
  listView: string;
  gridView: string;
}

const LOCALES: StoreLocale[] = ["en", "it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru"];
type TranslationRow = readonly [string, string, string, string, string, string, string, string, string, string, string, string];

const rows: Record<keyof PluginStoreCopy, TranslationRow> = {
  installQuestion: ["Install this plugin?", "Vuoi installare questo plugin?", "¿Quieres instalar este plugin?", "Voulez-vous installer ce plugin ?", "Dieses Plugin installieren?", "Deseja instalar este plugin?", "Встановити цей плагін?", "要安装此插件吗？", "このプラグインをインストールしますか？", "이 플러그인을 설치할까요?", "यह प्लगइन इंस्टॉल करें?", "Установить этот плагин?"],
  updateQuestion: ["Update this plugin?", "Vuoi aggiornare questo plugin?", "¿Quieres actualizar este plugin?", "Voulez-vous mettre à jour ce plugin ?", "Dieses Plugin aktualisieren?", "Deseja atualizar este plugin?", "Оновити цей плагін?", "要更新此插件吗？", "このプラグインを更新しますか？", "이 플러그인을 업데이트할까요?", "यह प्लगइन अपडेट करें?", "Обновить этот плагин?"],
  chooseVersion: ["Install a specific version", "Installa una versione specifica", "Instalar una versión específica", "Installer une version précise", "Bestimmte Version installieren", "Instalar uma versão específica", "Встановити певну версію", "安装指定版本", "バージョンを指定してインストール", "특정 버전 설치", "कोई खास संस्करण इंस्टॉल करें", "Установить определённую версию"],
  store: ["Plugin Store", "Plugin Store", "Tienda de plugins", "Boutique de plugins", "Plugin Store", "Loja de plugins", "Магазин плагінів", "插件商店", "プラグインストア", "플러그인 스토어", "प्लगइन स्टोर", "Магазин плагинов"],
  discover: ["Discover", "Scopri", "Descubrir", "Découvrir", "Entdecken", "Descobrir", "Огляд", "发现", "見つける", "둘러보기", "खोजें", "Обзор"],
  manage: ["Manage", "Gestisci", "Gestionar", "Gérer", "Verwalten", "Gerenciar", "Керування", "管理", "管理", "관리", "प्रबंधित करें", "Управление"],
  search: ["Search", "Cerca", "Buscar", "Rechercher", "Suchen", "Buscar", "Пошук", "搜索", "検索", "검색", "खोजें", "Поиск"],
  featured: ["Featured", "Vetrina", "Destacados", "À la une", "Im Fokus", "Em destaque", "Рекомендоване", "精选", "注目", "추천", "चुनिंदा", "Рекомендуем"],
  newCategory: ["New", "Novità", "Novedades", "Nouveautés", "Neu", "Novidades", "Новинки", "新品", "新着", "새 소식", "नया", "Новинки"],
  allPlugins: ["All plugins", "Tutti i plugin", "Todos los plugins", "Tous les plugins", "Alle Plugins", "Todos os plugins", "Усі плагіни", "所有插件", "すべてのプラグイン", "모든 플러그인", "सभी प्लगइन", "Все плагины"],
  installedPlugins: ["Installed plugins", "Plugin installati", "Plugins instalados", "Plugins installés", "Installierte Plugins", "Plugins instalados", "Встановлені плагіни", "已安装的插件", "インストール済みプラグイン", "설치된 플러그인", "इंस्टॉल किए गए प्लगइन", "Установленные плагины"],
  installedOnly: ["Installed only", "Solo installati", "Solo instalados", "Installés uniquement", "Nur installiert", "Somente instalados", "Лише встановлені", "仅已安装", "インストール済みのみ", "설치 항목만", "केवल इंस्टॉल किए गए", "Только установленные"],
  allSources: ["All sources", "Tutte le fonti", "Todas las fuentes", "Toutes les sources", "Alle Quellen", "Todas as fontes", "Усі джерела", "所有来源", "すべての配布元", "모든 출처", "सभी स्रोत", "Все источники"],
  sortName: ["Name", "Nome", "Nombre", "Nom", "Name", "Nome", "Назва", "名称", "名前", "이름", "नाम", "Название"],
  sortNewest: ["Date added", "Data di aggiunta", "Fecha de incorporación", "Date d’ajout", "Hinzugefügt am", "Data de adição", "Дата додавання", "添加日期", "追加日", "추가 날짜", "जोड़ने की तारीख", "Дата добавления"],
  sortUpdated: ["Date updated", "Data di aggiornamento", "Fecha de actualización", "Date de mise à jour", "Aktualisierungsdatum", "Data de atualização", "Дата оновлення", "更新日期", "更新日", "업데이트 날짜", "अपडेट की तारीख", "Дата обновления"],
  sortBy: ["Sort by", "Ordina per", "Ordenar por", "Trier par", "Sortieren nach", "Ordenar por", "Сортувати за", "排序方式", "並べ替え", "정렬 기준", "इसके अनुसार क्रमबद्ध करें", "Сортировать по"],
  previous: ["Previous", "Precedente", "Anterior", "Précédent", "Zurück", "Anterior", "Попередній", "上一个", "前へ", "이전", "पिछला", "Назад"],
  next: ["Next", "Successivo", "Siguiente", "Suivant", "Weiter", "Próximo", "Наступний", "下一个", "次へ", "다음", "अगला", "Далее"],
  searchPlaceholder: ["Search plugins and features", "Cerca plugin e funzioni", "Buscar plugins y funciones", "Rechercher des plugins et des fonctionnalités", "Plugins und Funktionen suchen", "Buscar plugins e recursos", "Пошук плагінів і функцій", "搜索插件和功能", "プラグインや機能を検索", "플러그인 및 기능 검색", "प्लगइन और सुविधाएँ खोजें", "Найти плагины и функции"],
  noPlugins: ["No plugins found.", "Nessun plugin trovato.", "No se encontraron plugins.", "Aucun plugin trouvé.", "Keine Plugins gefunden.", "Nenhum plugin encontrado.", "Плагінів не знайдено.", "未找到插件。", "プラグインが見つかりません。", "플러그인을 찾을 수 없습니다.", "कोई प्लगइन नहीं मिला।", "Плагины не найдены."],
  loading: ["Loading…", "Caricamento…", "Cargando…", "Chargement…", "Wird geladen…", "Carregando…", "Завантаження…", "正在加载…", "読み込み中…", "불러오는 중…", "लोड हो रहा है…", "Загрузка…"],
  retry: ["Try again", "Riprova", "Reintentar", "Réessayer", "Erneut versuchen", "Tentar novamente", "Спробувати ще раз", "重试", "再試行", "다시 시도", "फिर कोशिश करें", "Повторить"],
  install: ["Install", "Installa", "Instalar", "Installer", "Installieren", "Instalar", "Встановити", "安装", "インストール", "설치", "इंस्टॉल करें", "Установить"],
  update: ["Update", "Aggiorna", "Actualizar", "Mettre à jour", "Aktualisieren", "Atualizar", "Оновити", "更新", "更新", "업데이트", "अपडेट करें", "Обновить"],
  updateAll: ["Update all", "Aggiorna tutto", "Actualizar todo", "Tout mettre à jour", "Alle aktualisieren", "Atualizar tudo", "Оновити все", "全部更新", "すべて更新", "모두 업데이트", "सभी अपडेट करें", "Обновить все"],
  uninstall: ["Uninstall", "Disinstalla", "Desinstalar", "Désinstaller", "Deinstallieren", "Desinstalar", "Видалити", "卸载", "アンインストール", "제거", "अनइंस्टॉल करें", "Удалить"],
  pluginInstalled: ["Plugin installed.", "Il plugin è stato installato.", "Plugin instalado.", "Plugin installé.", "Plugin installiert.", "Plugin instalado.", "Плагін установлено.", "插件已安装。", "プラグインをインストールしました。", "플러그인이 설치되었습니다.", "प्लगइन इंस्टॉल हो गया है।", "Плагин установлен."],
  pluginUpdated: ["Plugin updated.", "Il plugin è stato aggiornato.", "Plugin actualizado.", "Plugin mis à jour.", "Plugin aktualisiert.", "Plugin atualizado.", "Плагін оновлено.", "插件已更新。", "プラグインを更新しました。", "플러그인이 업데이트되었습니다.", "प्लगइन अपडेट हो गया है।", "Плагин обновлён."],
  pluginsUpdated: ["Plugins updated.", "I plugin sono stati aggiornati.", "Plugins actualizados.", "Plugins mis à jour.", "Plugins aktualisiert.", "Plugins atualizados.", "Плагіни оновлено.", "插件已更新。", "プラグインを更新しました。", "플러그인이 업데이트되었습니다.", "प्लगइन अपडेट हो गए हैं।", "Плагины обновлены."],
  restartDeckyQuestion: ["Do you want to restart Decky now?", "Vuoi riavviare Decky ora?", "¿Quieres reiniciar Decky ahora?", "Voulez-vous redémarrer Decky maintenant ?", "Möchtest du Decky jetzt neu starten?", "Quer reiniciar o Decky agora?", "Перезапустити Decky зараз?", "要立即重启 Decky 吗？", "Decky を今すぐ再起動しますか？", "지금 Decky를 다시 시작할까요?", "क्या आप अभी Decky को रीस्टार्ट करना चाहते हैं?", "Перезапустить Decky сейчас?"],
  restartDeckyFailed: ["Decky could not be restarted.", "Non è stato possibile riavviare Decky.", "No se pudo reiniciar Decky.", "Impossible de redémarrer Decky.", "Decky konnte nicht neu gestartet werden.", "Não foi possível reiniciar o Decky.", "Не вдалося перезапустити Decky.", "无法重启 Decky。", "Decky を再起動できませんでした。", "Decky를 다시 시작하지 못했습니다.", "Decky को रीस्टार्ट नहीं किया जा सका।", "Не удалось перезапустить Decky."],
  ok: ["OK", "OK", "Aceptar", "OK", "OK", "OK", "OK", "确定", "OK", "확인", "ठीक है", "OK"],
  pluginsPartiallyUpdated: ["Some plugins could not be updated.", "Alcuni plugin non sono stati aggiornati.", "Algunos plugins no se pudieron actualizar.", "Certains plugins n’ont pas pu être mis à jour.", "Einige Plugins konnten nicht aktualisiert werden.", "Alguns plugins não puderam ser atualizados.", "Деякі плагіни не вдалося оновити.", "部分插件未能更新。", "一部のプラグインを更新できませんでした。", "일부 플러그인을 업데이트하지 못했습니다.", "कुछ प्लगइन अपडेट नहीं हो सके।", "Некоторые плагины не удалось обновить."],
  dontAskAgain: ["Don't ask me again", "Non chiedermelo più", "No volver a preguntar", "Ne plus me le demander", "Nicht mehr fragen", "Não perguntar novamente", "Більше не запитувати", "不再询问", "今後は確認しない", "다시 묻지 않기", "दोबारा न पूछें", "Больше не спрашивать"],
  preferenceSaveFailed: ["The preference could not be saved.", "Non è stato possibile salvare la preferenza.", "No se pudo guardar la preferencia.", "Impossible d’enregistrer la préférence.", "Die Einstellung konnte nicht gespeichert werden.", "Não foi possível salvar a preferência.", "Не вдалося зберегти налаштування.", "无法保存偏好设置。", "設定を保存できませんでした。", "설정을 저장하지 못했습니다.", "पसंद सेव नहीं हो सकी।", "Не удалось сохранить настройку."],
  later: ["Later", "Più tardi", "Más tarde", "Plus tard", "Später", "Mais tarde", "Пізніше", "稍后", "後で", "나중에", "बाद में", "Позже"],
  installedVersion: ["Installed version", "Versione installata", "Versión instalada", "Version installée", "Installierte Version", "Versão instalada", "Встановлена версія", "已安装版本", "インストール済みバージョン", "설치된 버전", "इंस्टॉल किया गया संस्करण", "Установленная версия"],
  latestVersion: ["Latest version", "Ultima versione", "Última versión", "Dernière version", "Neueste Version", "Versão mais recente", "Остання версія", "最新版本", "最新バージョン", "최신 버전", "नवीनतम संस्करण", "Последняя версия"],
  description: ["Description", "Descrizione", "Descripción", "Description", "Beschreibung", "Descrição", "Опис", "说明", "説明", "설명", "विवरण", "Описание"],
  releaseNotes: ["Release notes", "Note di rilascio", "Notas de la versión", "Notes de version", "Versionshinweise", "Notas da versão", "Примітки до випуску", "发行说明", "リリースノート", "릴리스 노트", "रिलीज़ नोट्स", "Примечания к выпуску"],
  media: ["Screenshots and media", "Screenshot e media", "Capturas y contenido multimedia", "Captures et médias", "Screenshots und Medien", "Capturas e mídia", "Знімки екрана й медіа", "截图和媒体", "スクリーンショットとメディア", "스크린샷 및 미디어", "स्क्रीनशॉट और मीडिया", "Снимки экрана и медиа"],
  repository: ["Repository", "Repository", "Repositorio", "Dépôt", "Repository", "Repositório", "Репозиторій", "代码仓库", "リポジトリ", "저장소", "रिपॉज़िटरी", "Репозиторий"],
  select: ["Select", "Seleziona", "Seleccionar", "Sélectionner", "Auswählen", "Selecionar", "Вибрати", "选择", "選択", "선택", "चुनें", "Выбрать"],
  back: ["Back", "Indietro", "Atrás", "Retour", "Zurück", "Voltar", "Назад", "返回", "戻る", "뒤로", "वापस", "Назад"],
  switchTabs: ["Switch tab", "Cambia scheda", "Cambiar pestaña", "Changer d’onglet", "Tab wechseln", "Mudar de aba", "Змінити вкладку", "切换标签页", "タブを切り替える", "탭 전환", "टैब बदलें", "Сменить вкладку"],
  scroll: ["Read and scroll", "Leggi e scorri", "Leer y desplazar", "Lire et faire défiler", "Lesen und scrollen", "Ler e rolar", "Читати й прокручувати", "阅读并滚动", "読んでスクロール", "읽고 스크롤", "पढ़ें और स्क्रॉल करें", "Читать и прокручивать"],
  stopReading: ["Stop scrolling", "Esci dallo scorrimento", "Salir del desplazamiento", "Quitter le défilement", "Scrollen beenden", "Sair da rolagem", "Завершити прокручування", "退出滚动", "スクロールを終了", "스크롤 종료", "स्क्रॉल बंद करें", "Завершить прокрутку"],
  openCategory: ["Open category", "Apri categoria", "Abrir categoría", "Ouvrir la catégorie", "Kategorie öffnen", "Abrir categoria", "Відкрити категорію", "打开类别", "カテゴリーを開く", "카테고리 열기", "श्रेणी खोलें", "Открыть категорию"],
  preparing: ["Preparing…", "Preparazione…", "Preparando…", "Préparation…", "Wird vorbereitet…", "Preparando…", "Підготовка…", "正在准备…", "準備中…", "준비 중…", "तैयार हो रहा है…", "Подготовка…"],
  releaseUnavailable: ["No installable release was found.", "Non è stata trovata una release installabile.", "No se encontró una versión instalable.", "Aucune version installable n’a été trouvée.", "Keine installierbare Version gefunden.", "Nenhuma versão instalável foi encontrada.", "Не знайдено версії для встановлення.", "未找到可安装的版本。", "インストール可能なリリースが見つかりません。", "설치 가능한 릴리스를 찾을 수 없습니다.", "इंस्टॉल करने योग्य रिलीज़ नहीं मिली।", "Устанавливаемый выпуск не найден."],
  releaseNotesUnavailable: ["Release notes are not available.", "Le note di rilascio non sono disponibili.", "Las notas de la versión no están disponibles.", "Les notes de version ne sont pas disponibles.", "Versionshinweise sind nicht verfügbar.", "As notas da versão não estão disponíveis.", "Примітки до випуску недоступні.", "暂无发行说明。", "リリースノートはありません。", "릴리스 노트를 사용할 수 없습니다.", "रिलीज़ नोट्स उपलब्ध नहीं हैं।", "Примечания к выпуску недоступны."],
  catalogUnavailable: ["The Plugin Store is unavailable. Check your connection and try again.", "Il Plugin Store non è disponibile. Controlla la connessione e riprova.", "La tienda de plugins no está disponible. Comprueba la conexión e inténtalo de nuevo.", "La boutique de plugins est indisponible. Vérifiez votre connexion et réessayez.", "Der Plugin Store ist nicht verfügbar. Prüfe die Verbindung und versuche es erneut.", "A Loja de plugins não está disponível. Verifique a conexão e tente novamente.", "Магазин плагінів недоступний. Перевірте з’єднання та спробуйте ще раз.", "插件商店不可用。请检查网络连接后重试。", "プラグインストアを利用できません。接続を確認して再試行してください。", "플러그인 스토어를 사용할 수 없습니다. 연결을 확인한 후 다시 시도하세요.", "प्लगइन स्टोर उपलब्ध नहीं है। कनेक्शन जाँचकर फिर कोशिश करें।", "Магазин плагинов недоступен. Проверьте подключение и повторите попытку."],
  uninstallTitle: ["Uninstall this plugin?", "Vuoi disinstallare questo plugin?", "¿Quieres desinstalar este plugin?", "Voulez-vous désinstaller ce plugin ?", "Dieses Plugin deinstallieren?", "Deseja desinstalar este plugin?", "Видалити цей плагін?", "要卸载此插件吗？", "このプラグインをアンインストールしますか？", "이 플러그인을 제거할까요?", "यह प्लगइन अनइंस्टॉल करें?", "Удалить этот плагин?"],
  uninstallDescription: ["The plugin will be removed from Decky.", "Il plugin verrà rimosso da Decky.", "El plugin se eliminará de Decky.", "Le plugin sera supprimé de Decky.", "Das Plugin wird aus Decky entfernt.", "O plugin será removido do Decky.", "Плагін буде видалено з Decky.", "该插件将从 Decky 中移除。", "プラグインを Decky から削除します。", "Decky에서 플러그인이 제거됩니다.", "प्लगइन Decky से हटा दिया जाएगा।", "Плагин будет удалён из Decky."],
  listView: ["List view", "Vista elenco", "Vista de lista", "Vue en liste", "Listenansicht", "Exibição em lista", "Список", "列表视图", "リスト表示", "목록 보기", "सूची दृश्य", "Список"],
  gridView: ["Grid view", "Vista griglia", "Vista de cuadrícula", "Vue en grille", "Rasteransicht", "Exibição em grade", "Сітка", "网格视图", "グリッド表示", "격자 보기", "ग्रिड दृश्य", "Сетка"],
};

export function normalizeStoreLocale(value: unknown): StoreLocale {
  const candidate = String(value ?? "").trim().toLocaleLowerCase("en-US").replace(/_/g, "-");
  const aliases: Record<string, StoreLocale> = {
    english: "en", italian: "it", spanish: "es", latam: "es", french: "fr", german: "de",
    brazilian: "pt", portuguese: "pt", ukrainian: "uk", schinese: "zh", tchinese: "zh",
    japanese: "ja", koreana: "ko", korean: "ko", hindi: "hi", russian: "ru",
  };
  const simple = candidate.split("-")[0] as StoreLocale;
  return aliases[candidate] ?? (LOCALES.includes(simple) ? simple : "en");
}

export function getPluginStoreCopy(locale: StoreLocale): PluginStoreCopy {
  const index = Math.max(0, LOCALES.indexOf(locale));
  return Object.fromEntries(Object.entries(rows).map(([key, values]) => [key, values[index] || values[0]])) as unknown as PluginStoreCopy;
}

const categoryRows: Record<string, TranslationRow> = {
  "Novità": rows.newCategory,
  Playhub: ["Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub", "Playhub"],
  "Libreria e giochi": ["Library and games", "Libreria e giochi", "Biblioteca y juegos", "Bibliothèque et jeux", "Bibliothek und Spiele", "Biblioteca e jogos", "Бібліотека та ігри", "游戏库与游戏", "ライブラリとゲーム", "라이브러리 및 게임", "लाइब्रेरी और गेम", "Библиотека и игры"],
  "Personalizzazione e media": ["Customization and media", "Personalizzazione e media", "Personalización y multimedia", "Personnalisation et médias", "Personalisierung und Medien", "Personalização e mídia", "Персоналізація та медіа", "个性化与媒体", "カスタマイズとメディア", "개인 설정 및 미디어", "कस्टमाइज़ेशन और मीडिया", "Персонализация и медиа"],
  "Social e community": ["Social and community", "Social e community", "Social y comunidad", "Social et communauté", "Soziales und Community", "Social e comunidade", "Спілкування та спільнота", "社交与社区", "ソーシャルとコミュニティ", "소셜 및 커뮤니티", "सोशल और समुदाय", "Общение и сообщество"],
  "Strumenti e utilità": ["Tools and utilities", "Strumenti e utilità", "Herramientas y utilidades", "Outils et utilitaires", "Werkzeuge und Dienstprogramme", "Ferramentas e utilitários", "Інструменти та утиліти", "工具与实用程序", "ツールとユーティリティ", "도구 및 유틸리티", "टूल और उपयोगिताएँ", "Инструменты и утилиты"],
  "Sistema e hardware": ["System and hardware", "Sistema e hardware", "Sistema y hardware", "Système et matériel", "System und Hardware", "Sistema e hardware", "Система й обладнання", "系统与硬件", "システムとハードウェア", "시스템 및 하드웨어", "सिस्टम और हार्डवेयर", "Система и оборудование"],
  "Rete e strumenti": ["Network and tools", "Rete e strumenti", "Red y herramientas", "Réseau et outils", "Netzwerk und Tools", "Rede e ferramentas", "Мережа й інструменти", "网络与工具", "ネットワークとツール", "네트워크 및 도구", "नेटवर्क और टूल", "Сеть и инструменты"],
};

export function localizePluginCategory(category: string, locale: StoreLocale): string {
  const index = Math.max(0, LOCALES.indexOf(locale));
  return categoryRows[category]?.[index] ?? category;
}
