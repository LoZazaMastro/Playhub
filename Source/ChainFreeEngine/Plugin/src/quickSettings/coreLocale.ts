import { controlLocale } from "../controlCenterLocale";
const keys = ["volume", "microphoneVolume", "audioOutput", "microphoneInput", "brightness", "resolution", "refreshRate", "powerMode", "powerEfficiency", "powerBalanced", "powerBetter", "powerBest", "tdpLimit", "off", "advanced", "diagnostics", "generateDiagnostics", "ok", "cancel", "back"];
const rows: Record<string, string[]> = {
  de: ["Lautstärke", "Mikrofonlautstärke", "Audioausgabe", "Audioeingang", "Helligkeit", "Auflösung", "Bildwiederholrate", "Energiemodus", "Beste Energieeffizienz", "Ausbalanciert", "Bessere Leistung", "Beste Leistung", "Leistungsgrenze", "Aus", "Erweitert", "Diagnose", "Diagnosebericht erstellen", "OK", "Abbrechen", "Zurück"],
  es: ["Volumen", "Volumen del micrófono", "Salida de audio", "Entrada de audio", "Brillo", "Resolución", "Frecuencia de actualización", "Modo de energía", "Máxima eficiencia", "Equilibrado", "Mejor rendimiento", "Máximo rendimiento", "Límite de potencia", "Desactivado", "Avanzado", "Diagnóstico", "Generar informe de diagnóstico", "Aceptar", "Cancelar", "Volver"],
  fr: ["Volume", "Volume du microphone", "Sortie audio", "Entrée audio", "Luminosité", "Résolution", "Fréquence de rafraîchissement", "Mode d’alimentation", "Efficacité énergétique maximale", "Équilibré", "Performances améliorées", "Performances maximales", "Limite de puissance", "Désactivé", "Avancé", "Diagnostic", "Créer un rapport de diagnostic", "OK", "Annuler", "Retour"],
  pt: ["Volume", "Volume do microfone", "Saída de áudio", "Entrada de áudio", "Brilho", "Resolução", "Taxa de atualização", "Modo de energia", "Máxima eficiência", "Equilibrado", "Melhor desempenho", "Desempenho máximo", "Limite de potência", "Desativado", "Avançado", "Diagnóstico", "Gerar relatório de diagnóstico", "OK", "Cancelar", "Voltar"],
  ru: ["Громкость", "Громкость микрофона", "Вывод звука", "Аудиовход", "Яркость", "Разрешение", "Частота обновления", "Режим питания", "Максимальная энергоэффективность", "Сбалансированный", "Повышенная производительность", "Максимальная производительность", "Лимит мощности", "Выкл.", "Дополнительно", "Диагностика", "Создать диагностический отчёт", "ОК", "Отмена", "Назад"],
  uk: ["Гучність", "Гучність мікрофона", "Виведення звуку", "Аудіовхід", "Яскравість", "Роздільна здатність", "Частота оновлення", "Режим живлення", "Максимальна енергоефективність", "Збалансований", "Підвищена продуктивність", "Максимальна продуктивність", "Ліміт потужності", "Вимк.", "Додатково", "Діагностика", "Створити діагностичний звіт", "ОК", "Скасувати", "Назад"],
  ja: ["音量", "マイク音量", "音声出力", "音声入力", "明るさ", "解像度", "リフレッシュレート", "電源モード", "最も高い電力効率", "バランス", "高パフォーマンス", "最高のパフォーマンス", "電力制限", "オフ", "詳細設定", "診断", "診断レポートを作成", "OK", "キャンセル", "戻る"],
  ko: ["음량", "마이크 음량", "오디오 출력", "오디오 입력", "밝기", "해상도", "새로 고침 빈도", "전원 모드", "최고 전력 효율", "균형 조정", "향상된 성능", "최고 성능", "전력 제한", "끄기", "고급", "진단", "진단 보고서 생성", "확인", "취소", "뒤로"],
  zh: ["音量", "麦克风音量", "音频输出", "音频输入", "亮度", "分辨率", "刷新率", "电源模式", "最佳能效", "平衡", "更高性能", "最佳性能", "功率限制", "关闭", "高级", "诊断", "生成诊断报告", "确定", "取消", "返回"],
  hi: ["आवाज़", "माइक्रोफ़ोन की आवाज़", "ऑडियो आउटपुट", "ऑडियो इनपुट", "चमक", "रिज़ॉल्यूशन", "रिफ़्रेश दर", "पावर मोड", "सबसे कम बिजली खपत", "संतुलित", "बेहतर प्रदर्शन", "अधिकतम प्रदर्शन", "पावर सीमा", "बंद", "उन्नत", "जाँच", "जाँच रिपोर्ट बनाएँ", "ठीक है", "रद्द करें", "वापस"],
};
const displayConfirmation: Record<string, [string, string, string]> = {
  en: ['Keep these display settings?', 'Press OK to keep the change. Otherwise, the previous settings will be restored in', 'Could not apply the display settings'],
  it: ['Mantenere queste impostazioni video?', 'Premi OK per confermare. Altrimenti le impostazioni precedenti verranno ripristinate tra', 'Impossibile applicare le impostazioni video'],
  de: ['Diese Anzeigeeinstellungen beibehalten?', 'Mit OK bestätigen. Andernfalls werden die vorherigen Einstellungen wiederhergestellt in', 'Die Anzeigeeinstellungen konnten nicht angewendet werden'],
  es: ['¿Mantener estos ajustes de pantalla?', 'Pulsa Aceptar para confirmar. De lo contrario, se restaurarán los ajustes anteriores en', 'No se pudieron aplicar los ajustes de pantalla'],
  fr: ['Conserver ces réglages d’affichage ?', 'Appuyez sur OK pour confirmer. Sinon, les réglages précédents seront rétablis dans', 'Impossible d’appliquer les réglages d’affichage'],
  pt: ['Manter estas definições de ecrã?', 'Prima OK para confirmar. Caso contrário, as definições anteriores serão restauradas em', 'Não foi possível aplicar as definições de ecrã'],
  ru: ['Сохранить эти настройки экрана?', 'Нажмите ОК для подтверждения. Иначе прежние настройки восстановятся через', 'Не удалось применить настройки экрана'],
  uk: ['Зберегти ці налаштування екрана?', 'Натисніть ОК для підтвердження. Інакше попередні налаштування відновляться через', 'Не вдалося застосувати налаштування екрана'],
  ja: ['この画面設定を維持しますか？', 'OKで確定します。確定しない場合、次の時間が経過すると元の設定に戻ります：', '画面設定を適用できませんでした'],
  ko: ['이 화면 설정을 유지할까요?', '확인 버튼을 누르면 적용됩니다. 누르지 않으면 다음 시간이 지난 후 이전 설정으로 돌아갑니다:', '화면 설정을 적용하지 못했습니다'],
  zh: ['保留这些显示设置？', '按确定保留更改。否则将在以下时间后恢复之前的设置：', '无法应用显示设置'],
  hi: ['ये डिस्प्ले सेटिंग रखें?', 'पुष्टि करने के लिए ठीक है दबाएँ। नहीं तो पिछली सेटिंग इतने समय बाद वापस लागू हो जाएगी:', 'डिस्प्ले सेटिंग लागू नहीं हो सकीं'],
};
const mediaSections: Record<string, [string, string]> = {
  en: ["Audio", "Video"], it: ["Audio", "Video"], de: ["Audio", "Video"],
  es: ["Audio", "Vídeo"], fr: ["Audio", "Vidéo"], pt: ["Áudio", "Vídeo"],
  ru: ["Аудио", "Видео"], uk: ["Аудіо", "Відео"], zh: ["音频", "视频"],
  ja: ["音声", "映像"], ko: ["오디오", "비디오"], hi: ["ऑडियो", "वीडियो"],
};
export function quickCoreLocale(locale: string): Record<string, string> {
  const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", portuguese: "pt", brazilian: "pt", russian: "ru", ukrainian: "uk", schinese: "zh", tchinese: "zh", japanese: "ja", koreana: "ko", hindi: "hi" };
  const key = locale.toLowerCase().split(/[-_]/)[0];
  const language = aliases[key] ?? key;
  const labels = controlLocale(language);
  const row = rows[language];
  const confirmation = displayConfirmation[language] ?? displayConfirmation.en;
  const media = mediaSections[language] ?? mediaSections.en;
  return { ...(row ? Object.fromEntries(keys.map((key, index) => [key, row[index]])) : {}),
    hdrConfirmTitle: confirmation[0], hdrConfirmBody: confirmation[1], hdrUnavailable: confirmation[2],
    audio: media[0], display: media[1], performance: labels.performance };
}
