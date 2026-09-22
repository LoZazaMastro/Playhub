const copy = {
  en: ["Session", "Audio", "Display", "Performance", "Graphics", "Controller", "System", "Settings", "Customize your panel", "Show tabs", "Move up", "Move down", "Restore defaults", "Back", "Hide a tab without changing its settings or disabling its features.", "Expand", "Collapse", "Switch tab", "Settings could not be saved. Try again."],
  it: ["Sessione", "Audio", "Schermo", "Prestazioni", "Grafica", "Controller", "Sistema", "Impostazioni", "Personalizza il pannello", "Mostra le schede", "Sposta su", "Sposta giù", "Ripristina predefiniti", "Indietro", "Nascondere una scheda non modifica le impostazioni e non disattiva le sue funzioni.", "Espandi", "Comprimi", "Cambia scheda", "Impossibile salvare le impostazioni. Riprova."],
  de: ["Sitzung", "Audio", "Bildschirm", "Leistung", "Grafik", "Controller", "System", "Einstellungen", "Panel anpassen", "Tabs anzeigen", "Nach oben", "Nach unten", "Standard wiederherstellen", "Zurück", "Das Ausblenden eines Tabs ändert keine Einstellungen und deaktiviert keine Funktionen.", "Ausklappen", "Einklappen", "Tab wechseln", "Einstellungen konnten nicht gespeichert werden. Erneut versuchen."],
  es: ["Sesión", "Audio", "Pantalla", "Rendimiento", "Gráficos", "Mando", "Sistema", "Ajustes", "Personaliza el panel", "Mostrar pestañas", "Subir", "Bajar", "Restablecer valores", "Volver", "Ocultar una pestaña no cambia sus ajustes ni desactiva sus funciones.", "Expandir", "Contraer", "Cambiar pestaña", "No se han podido guardar los ajustes. Inténtalo de nuevo."],
  fr: ["Session", "Audio", "Affichage", "Performances", "Graphismes", "Manette", "Système", "Paramètres", "Personnaliser le panneau", "Afficher les onglets", "Monter", "Descendre", "Rétablir les valeurs par défaut", "Retour", "Masquer un onglet ne modifie pas ses paramètres et ne désactive pas ses fonctions.", "Développer", "Réduire", "Changer d’onglet", "Impossible d’enregistrer les paramètres. Réessayez."],
  pt: ["Sessão", "Áudio", "Tela", "Desempenho", "Gráficos", "Controle", "Sistema", "Configurações", "Personalize o painel", "Mostrar abas", "Mover para cima", "Mover para baixo", "Restaurar padrões", "Voltar", "Ocultar uma aba não altera suas configurações nem desativa suas funções.", "Expandir", "Recolher", "Trocar aba", "Não foi possível salvar as configurações. Tente novamente."],
  ru: ["Сеанс", "Звук", "Экран", "Производительность", "Графика", "Контроллер", "Система", "Настройки", "Настройте панель", "Показывать вкладки", "Вверх", "Вниз", "Сбросить настройки", "Назад", "Скрытие вкладки не меняет её настройки и не отключает функции.", "Развернуть", "Свернуть", "Сменить вкладку", "Не удалось сохранить настройки. Повторите попытку."],
  uk: ["Сеанс", "Звук", "Екран", "Продуктивність", "Графіка", "Контролер", "Система", "Налаштування", "Налаштуйте панель", "Показувати вкладки", "Угору", "Униз", "Відновити типові", "Назад", "Приховування вкладки не змінює її налаштувань і не вимикає функцій.", "Розгорнути", "Згорнути", "Змінити вкладку", "Не вдалося зберегти налаштування. Спробуйте ще раз."],
  ja: ["セッション", "音声", "画面", "パフォーマンス", "グラフィック", "コントローラー", "システム", "設定", "パネルをカスタマイズ", "タブを表示", "上へ移動", "下へ移動", "初期設定に戻す", "戻る", "タブを非表示にしても、設定や機能は変更されません。", "展開", "折りたたむ", "タブを切り替え", "設定を保存できませんでした。もう一度お試しください。"],
  ko: ["세션", "오디오", "화면", "성능", "그래픽", "컨트롤러", "시스템", "설정", "패널 맞춤 설정", "탭 표시", "위로 이동", "아래로 이동", "기본값 복원", "뒤로", "탭을 숨겨도 설정이 변경되거나 기능이 꺼지지 않습니다.", "펼치기", "접기", "탭 전환", "설정을 저장하지 못했습니다. 다시 시도해 주세요."],
  zh: ["会话", "音频", "显示", "性能", "图形", "控制器", "系统", "设置", "自定义面板", "显示选项卡", "上移", "下移", "恢复默认设置", "返回", "隐藏选项卡不会更改设置或停用其功能。", "展开", "收起", "切换选项卡", "无法保存设置，请重试。"],
  hi: ["सत्र", "ऑडियो", "डिस्प्ले", "प्रदर्शन", "ग्राफ़िक्स", "कंट्रोलर", "सिस्टम", "सेटिंग्स", "पैनल को अपने हिसाब से बदलें", "टैब दिखाएँ", "ऊपर ले जाएँ", "नीचे ले जाएँ", "डिफ़ॉल्ट बहाल करें", "वापस", "टैब छिपाने से उसकी सेटिंग्स नहीं बदलतीं और उसके फ़ीचर बंद नहीं होते।", "खोलें", "समेटें", "टैब बदलें", "सेटिंग्स सेव नहीं हो सकीं। फिर से कोशिश करें।"],
} as const;
export function controlLocale(locale: string) {
  const aliases: Record<string, keyof typeof copy> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };
  const key = aliases[locale] ?? locale.split(/[-_]/)[0];
  const row = copy[key as keyof typeof copy] ?? copy.en;
  const tabs = tabCopy[key] ?? tabCopy.en;
  const video: Record<string, string> = { en: "Video", it: "Video", de: "Video", es: "Vídeo", fr: "Vidéo", pt: "Vídeo", ru: "Видео", uk: "Відео", ja: "映像", ko: "비디오", zh: "视频", hi: "वीडियो" };
  return { home: row[0], audio: video[key] ?? video.en, display: row[2], performance: row[1], graphics: row[4], controller: row[5], tools: row[6], store: 'Plugin Store', decky: 'Decky', settings: row[7], customize: tabs[1], show: tabs[4], up: tabs[2], down: tabs[3], reset: row[12], back: row[13], hint: row[14], expand: row[15], collapse: row[16], switchTab: row[17], saveError: row[18], device: tabs[5], unavailable: tabs[6], loading: tabs[7], retry: tabs[8] };
}

const tabCopy: Record<string, string[]> = {
  en: ['Audio & Video', 'Customize horizontal tabs', 'Move left', 'Move right', 'Show this tab', 'Device information', 'Unavailable on this device', 'Loading controls...', 'Retry'],
  it: ['Audio e Video', 'Personalizza le tab orizzontali', 'Sposta a sinistra', 'Sposta a destra', 'Mostra questa tab', 'Informazioni sul dispositivo', 'Non disponibile su questo dispositivo', 'Caricamento dei controlli...', 'Riprova'],
  de: ['Audio & Video', 'Horizontale Tabs anpassen', 'Nach links', 'Nach rechts', 'Diesen Tab anzeigen', 'Geräteinformationen', 'Auf diesem Gerät nicht verfügbar', 'Steuerelemente werden geladen...', 'Erneut versuchen'],
  es: ['Audio y vídeo', 'Personalizar pestañas horizontales', 'Mover a la izquierda', 'Mover a la derecha', 'Mostrar esta pestaña', 'Información del dispositivo', 'No disponible en este dispositivo', 'Cargando controles...', 'Reintentar'],
  fr: ['Audio et vidéo', 'Personnaliser les onglets horizontaux', 'Déplacer à gauche', 'Déplacer à droite', 'Afficher cet onglet', 'Informations sur l’appareil', 'Indisponible sur cet appareil', 'Chargement des réglages...', 'Réessayer'],
  pt: ['Áudio e vídeo', 'Personalizar abas horizontais', 'Mover à esquerda', 'Mover à direita', 'Mostrar esta aba', 'Informações do dispositivo', 'Indisponível neste dispositivo', 'Carregando controles...', 'Tentar novamente'],
  ru: ['Звук и видео', 'Настроить горизонтальные вкладки', 'Сдвинуть влево', 'Сдвинуть вправо', 'Показывать эту вкладку', 'Информация об устройстве', 'Недоступно на этом устройстве', 'Загрузка настроек...', 'Повторить'],
  uk: ['Звук і відео', 'Налаштувати горизонтальні вкладки', 'Перемістити ліворуч', 'Перемістити праворуч', 'Показувати цю вкладку', 'Інформація про пристрій', 'Недоступно на цьому пристрої', 'Завантаження налаштувань...', 'Повторити'],
  ja: ['音声と映像', '横並びのタブをカスタマイズ', '左へ移動', '右へ移動', 'このタブを表示', 'デバイス情報', 'このデバイスでは利用できません', '設定を読み込み中...', '再試行'],
  ko: ['오디오 및 비디오', '가로 탭 맞춤 설정', '왼쪽으로 이동', '오른쪽으로 이동', '이 탭 표시', '기기 정보', '이 기기에서는 사용할 수 없음', '설정 불러오는 중...', '다시 시도'],
  zh: ['音频和视频', '自定义横向选项卡', '向左移动', '向右移动', '显示此选项卡', '设备信息', '此设备不支持', '正在加载控件...', '重试'],
  hi: ['ऑडियो और वीडियो', 'क्षैतिज टैब अपने हिसाब से बदलें', 'बाएँ ले जाएँ', 'दाएँ ले जाएँ', 'यह टैब दिखाएँ', 'डिवाइस की जानकारी', 'इस डिवाइस पर उपलब्ध नहीं', 'नियंत्रण लोड हो रहे हैं...', 'फिर कोशिश करें'],
};

const statusCopy: Record<string, string[]> = {
  en: ["Connected to power", "On battery", "No supported controls were detected on this device."],
  it: ["Alimentazione collegata", "A batteria", "Non sono stati rilevati controlli supportati su questo dispositivo."],
  de: ["Netzbetrieb", "Akkubetrieb", "Für dieses Gerät wurden keine unterstützten Steuerelemente erkannt."],
  es: ["Conectado a la corriente", "Con batería", "No se han detectado controles compatibles con este dispositivo."],
  fr: ["Branché sur secteur", "Sur batterie", "Aucun réglage compatible n’a été détecté sur cet appareil."],
  pt: ["Conectado à energia", "Na bateria", "Nenhum controle compatível foi detectado neste dispositivo."],
  ru: ["Питание от сети", "Питание от батареи", "На этом устройстве не обнаружены поддерживаемые настройки."],
  uk: ["Живлення від мережі", "Живлення від батареї", "На цьому пристрої не виявлено підтримуваних налаштувань."],
  ja: ["電源に接続中", "バッテリー使用中", "このデバイスで対応している設定は見つかりませんでした。"],
  ko: ["전원 연결됨", "배터리 사용 중", "이 기기에서 지원되는 설정을 찾지 못했습니다."],
  zh: ["已连接电源", "使用电池", "未检测到此设备支持的控件。"],
  hi: ["बिजली से जुड़ा है", "बैटरी पर चल रहा है", "इस डिवाइस पर कोई समर्थित नियंत्रण नहीं मिला।"],
};
export const controlStatus = (locale: string) => statusCopy[locale.split(/[-_]/)[0]] ?? statusCopy.en;
