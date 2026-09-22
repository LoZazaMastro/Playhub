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
export function deviceInfoLocale(locale: string) {
  const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };
  const key = locale.toLowerCase();
  const row = rows[(aliases[key] ?? key.split(/[-_]/)[0]) as keyof typeof rows] ?? rows.en;
  return { name: row[0], model: row[1], cpu: row[2], gpu: row[3], ram: row[4], storage: row[5], edition: row[6], version: row[7], update: row[8], loading: row[9], unavailable: row[10], openError: row[11], loadError: row[12] };
}
