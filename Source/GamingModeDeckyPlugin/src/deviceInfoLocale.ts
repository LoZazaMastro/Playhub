const rows = {
  en: ["Device name", "Model", "Processor", "Graphics", "Memory", "Total local storage", "Windows edition", "Windows version", "Windows Update", "Loading device information...", "Unavailable", "Could not open Windows Update.", "Could not load device information."],
  it: ["Nome dispositivo", "Modello", "Processore", "GPU", "Memoria", "Archiviazione locale totale", "Edizione Windows", "Versione Windows", "Windows Update", "Caricamento informazioni dispositivo...", "Non disponibile", "Impossibile aprire Windows Update.", "Impossibile caricare le informazioni del dispositivo."],
  de: ["Gerätename", "Modell", "Prozessor", "Grafik", "Arbeitsspeicher", "Lokaler Gesamtspeicher", "Windows-Edition", "Windows-Version", "Windows Update", "Geräteinformationen werden geladen...", "Nicht verfügbar", "Windows Update konnte nicht geöffnet werden.", "Geräteinformationen konnten nicht geladen werden."],
  es: ["Nombre del dispositivo", "Modelo", "Procesador", "GPU", "Memoria", "Almacenamiento local total", "Edición de Windows", "Versión de Windows", "Windows Update", "Cargando información del dispositivo...", "No disponible", "No se pudo abrir Windows Update.", "No se pudo cargar la información del dispositivo."],
  fr: ["Nom de l'appareil", "Modèle", "Processeur", "GPU", "Mémoire", "Stockage local total", "Édition de Windows", "Version de Windows", "Windows Update", "Chargement des informations de l'appareil...", "Indisponible", "Impossible d'ouvrir Windows Update.", "Impossible de charger les informations de l'appareil."],
  pt: ["Nome do dispositivo", "Modelo", "Processador", "GPU", "Memória", "Armazenamento local total", "Edição do Windows", "Versão do Windows", "Windows Update", "Carregando informações do dispositivo...", "Indisponível", "Não foi possível abrir o Windows Update.", "Não foi possível carregar as informações do dispositivo."],
  ru: ["Имя устройства", "Модель", "Процессор", "Видеокарта", "Память", "Общий локальный объём", "Выпуск Windows", "Версия Windows", "Windows Update", "Загрузка сведений об устройстве...", "Недоступно", "Не удалось открыть Windows Update.", "Не удалось загрузить сведения об устройстве."],
  uk: ["Ім'я пристрою", "Модель", "Процесор", "Відеокарта", "Пам'ять", "Загальний локальний обсяг", "Випуск Windows", "Версія Windows", "Windows Update", "Завантаження відомостей про пристрій...", "Недоступно", "Не вдалося відкрити Windows Update.", "Не вдалося завантажити відомості про пристрій."],
  ja: ["デバイス名", "モデル", "プロセッサー", "GPU", "メモリ", "ローカルストレージ合計", "Windows エディション", "Windows バージョン", "Windows Update", "デバイス情報を読み込み中...", "利用不可", "Windows Update を開けませんでした。", "デバイス情報を読み込めませんでした。"],
  ko: ["장치 이름", "모델", "프로세서", "GPU", "메모리", "전체 로컬 저장 공간", "Windows 에디션", "Windows 버전", "Windows Update", "장치 정보 불러오는 중...", "사용 불가", "Windows Update를 열 수 없습니다.", "장치 정보를 불러올 수 없습니다."],
  zh: ["设备名称", "型号", "处理器", "GPU", "内存", "本地存储总容量", "Windows 版本类别", "Windows 版本号", "Windows Update", "正在加载设备信息...", "不可用", "无法打开 Windows Update。", "无法加载设备信息。"],
  hi: ["डिवाइस का नाम", "मॉडल", "प्रोसेसर", "GPU", "मेमोरी", "कुल स्थानीय स्टोरेज", "Windows संस्करण", "Windows संस्करण संख्या", "Windows Update", "डिवाइस की जानकारी लोड हो रही है...", "उपलब्ध नहीं", "Windows Update नहीं खुल सका।", "डिवाइस की जानकारी लोड नहीं हो सकी।"],
} as const;
const profiles = {
  en: ["Device profile", "Automatic", "Windows PC", "Other handheld", "Experimental", "The profile changes how Playhub presents your device. Windows supplies the available controls.", "On battery", "Plugged in", "Primary display", "Video controls", "Power controls", "Hardware details", "Could not save the device profile.", "Try again"],
  it: ["Profilo dispositivo", "Automatico", "PC Windows", "Altro portatile da gioco", "Sperimentale", "Il profilo adatta Playhub al dispositivo. I controlli disponibili dipendono da Windows.", "A batteria", "Alimentatore collegato", "Schermo principale", "Controlli video", "Controlli di alimentazione", "Dettagli hardware", "Impossibile salvare il profilo dispositivo.", "Riprova"],
  de: ["Geräteprofil", "Automatisch", "Windows-PC", "Anderes Handheld", "Experimentell", "Das Profil passt Playhub an dein Gerät an. Windows bestimmt die verfügbaren Einstellungen.", "Akkubetrieb", "Netzbetrieb", "Hauptbildschirm", "Videoeinstellungen", "Energieeinstellungen", "Hardwaredetails", "Das Geräteprofil konnte nicht gespeichert werden.", "Erneut versuchen"],
  es: ["Perfil del dispositivo", "Automático", "PC con Windows", "Otra consola portátil", "Experimental", "El perfil adapta Playhub a tu dispositivo. Windows determina los controles disponibles.", "Con batería", "Conectado a la corriente", "Pantalla principal", "Controles de vídeo", "Controles de energía", "Detalles del hardware", "No se pudo guardar el perfil del dispositivo.", "Reintentar"],
  fr: ["Profil de l’appareil", "Automatique", "PC Windows", "Autre console portable", "Expérimental", "Le profil adapte Playhub à votre appareil. Windows détermine les commandes disponibles.", "Sur batterie", "Branché au secteur", "Écran principal", "Réglages vidéo", "Réglages d’alimentation", "Détails du matériel", "Impossible d’enregistrer le profil de l’appareil.", "Réessayer"],
  pt: ["Perfil do dispositivo", "Automático", "PC com Windows", "Outro portátil para jogos", "Experimental", "O perfil adapta o Playhub ao seu dispositivo. O Windows define os controles disponíveis.", "Na bateria", "Ligado à tomada", "Tela principal", "Controles de vídeo", "Controles de energia", "Detalhes do hardware", "Não foi possível salvar o perfil do dispositivo.", "Tentar novamente"],
  ru: ["Профиль устройства", "Автоматически", "ПК с Windows", "Другая портативная консоль", "Экспериментально", "Профиль адаптирует Playhub к вашему устройству. Доступные настройки определяет Windows.", "От батареи", "От сети", "Основной экран", "Настройки видео", "Настройки питания", "Сведения об оборудовании", "Не удалось сохранить профиль устройства.", "Повторить"],
  uk: ["Профіль пристрою", "Автоматично", "ПК з Windows", "Інша портативна консоль", "Експериментально", "Профіль адаптує Playhub до вашого пристрою. Доступні налаштування визначає Windows.", "Від батареї", "Від мережі", "Основний екран", "Налаштування відео", "Налаштування живлення", "Відомості про обладнання", "Не вдалося зберегти профіль пристрою.", "Повторити"],
  ja: ["デバイスプロファイル", "自動", "Windows PC", "その他の携帯ゲーム機", "実験的", "プロファイルに応じて Playhub の表示が変わります。利用できる操作は Windows が判定します。", "バッテリー駆動", "電源に接続中", "メインディスプレイ", "映像設定", "電源設定", "ハードウェアの詳細", "デバイスプロファイルを保存できませんでした。", "再試行"],
  ko: ["장치 프로필", "자동", "Windows PC", "기타 휴대용 게임기", "실험적", "프로필에 따라 Playhub의 표시가 달라집니다. 사용할 수 있는 제어 기능은 Windows가 결정합니다.", "배터리 사용 중", "전원 연결됨", "기본 디스플레이", "화면 설정", "전원 설정", "하드웨어 정보", "장치 프로필을 저장할 수 없습니다.", "다시 시도"],
  zh: ["设备配置", "自动", "Windows PC", "其他游戏掌机", "实验性", "Playhub 会根据配置调整界面。可用的控制选项由 Windows 决定。", "使用电池", "已接通电源", "主显示器", "显示设置", "电源设置", "硬件详情", "无法保存设备配置。", "重试"],
  hi: ["डिवाइस प्रोफ़ाइल", "स्वचालित", "Windows PC", "अन्य हैंडहेल्ड", "प्रयोगात्मक", "प्रोफ़ाइल के अनुसार Playhub का इंटरफ़ेस बदलता है। उपलब्ध नियंत्रण Windows तय करता है।", "बैटरी पर", "बिजली से कनेक्ट है", "मुख्य डिस्प्ले", "वीडियो नियंत्रण", "पावर नियंत्रण", "हार्डवेयर की जानकारी", "डिवाइस प्रोफ़ाइल सेव नहीं हो सकी।", "फिर कोशिश करें"],
} as const;
const batteryHealth = {
  en: "Battery health", it: "Salute batteria", de: "Akkuzustand", es: "Salud de la batería",
  fr: "État de santé de la batterie", pt: "Saúde da bateria", ru: "Состояние аккумулятора",
  uk: "Стан акумулятора", ja: "バッテリーの状態", ko: "배터리 상태", zh: "电池健康度", hi: "बैटरी की सेहत",
} as const;
const batteryRemaining = {
  en: "Estimated time remaining", it: "Autonomia stimata", de: "Geschätzte Restlaufzeit", es: "Autonomía estimada",
  fr: "Autonomie estimée", pt: "Autonomia estimada", ru: "Примерное оставшееся время", uk: "Приблизний час роботи",
  ja: "推定残り時間", ko: "예상 남은 시간", zh: "预计剩余时间", hi: "अनुमानित शेष समय",
} as const;
export function deviceLanguage(locale: string): keyof typeof rows {
  const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };
  const key = locale.toLowerCase();
  const candidate = aliases[key] ?? key.split(/[-_]/)[0];
  return Object.prototype.hasOwnProperty.call(rows, candidate) ? candidate as keyof typeof rows : "en";
}
export function deviceInfoLocale(locale: string) {
  const language = deviceLanguage(locale);
  const row = rows[language] ?? rows.en;
  const profile = profiles[language] ?? profiles.en;
  return { language, batteryRemaining: batteryRemaining[language] ?? batteryRemaining.en, name: row[0], model: row[1], cpu: row[2], gpu: row[3], ram: row[4], storage: row[5], edition: row[6], version: row[7], update: row[8], loading: row[9], unavailable: row[10], openError: row[11], loadError: row[12],
    profile: profile[0], auto: profile[1], pc: profile[2], handheld: profile[3], experimental: profile[4], profileHint: profile[5], battery: profile[6], ac: profile[7], primaryDisplay: profile[8], video: profile[9], power: profile[10], details: profile[11], saveError: profile[12], retry: profile[13], batteryHealth: batteryHealth[language] ?? batteryHealth.en };
}
