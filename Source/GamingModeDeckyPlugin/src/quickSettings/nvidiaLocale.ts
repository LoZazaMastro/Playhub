import { deviceLanguage } from "../deviceInfoLocale";

const rows = {
  en: ["Global driver settings. Changes apply at the next game launch.", "Frame limit", "Off", "Driver default", "Apply", "Vertical sync", "Use game setting", "On", "Power mode", "Maximum performance", "Current", "Driver settings are unavailable.", "The driver settings changed. Refresh to review them.", "The driver could not confirm this change."],
  it: ["Impostazioni globali del driver. Le modifiche si applicano al prossimo avvio del gioco.", "Limite FPS", "Disattivato", "Predefinito del driver", "Applica", "Sincronizzazione verticale", "Impostazione del gioco", "Attivata", "Modalità energetica", "Prestazioni massime", "Attuale", "Impostazioni del driver non disponibili.", "Le impostazioni del driver sono cambiate. Aggiorna per verificarle.", "Il driver non ha confermato la modifica."],
  de: ["Globale Treibereinstellungen. Änderungen gelten beim nächsten Spielstart.", "FPS-Limit", "Aus", "Treibervorgabe", "Anwenden", "Vertikale Synchronisation", "Spieleinstellung verwenden", "Ein", "Energieverwaltung", "Maximale Leistung", "Aktuell", "Treibereinstellungen sind nicht verfügbar.", "Die Treibereinstellungen haben sich geändert. Zum Prüfen aktualisieren.", "Der Treiber konnte die Änderung nicht bestätigen."],
  es: ["Ajustes globales del controlador. Los cambios se aplican en el próximo inicio del juego.", "Límite de FPS", "Desactivado", "Predeterminado del controlador", "Aplicar", "Sincronización vertical", "Ajuste del juego", "Activada", "Modo de energía", "Rendimiento máximo", "Actual", "Los ajustes del controlador no están disponibles.", "Los ajustes del controlador han cambiado. Actualiza para revisarlos.", "El controlador no pudo confirmar el cambio."],
  fr: ["Réglages globaux du pilote. Les changements s’appliquent au prochain lancement du jeu.", "Limite d’images par seconde", "Désactivée", "Valeur du pilote", "Appliquer", "Synchronisation verticale", "Réglage du jeu", "Activée", "Gestion de l’alimentation", "Performances maximales", "Actuel", "Les réglages du pilote sont indisponibles.", "Les réglages du pilote ont changé. Actualisez pour les vérifier.", "Le pilote n’a pas confirmé ce changement."],
  pt: ["Configurações globais do driver. As alterações se aplicam na próxima inicialização do jogo.", "Limite de FPS", "Desativado", "Padrão do driver", "Aplicar", "Sincronização vertical", "Configuração do jogo", "Ativada", "Modo de energia", "Desempenho máximo", "Atual", "As configurações do driver estão indisponíveis.", "As configurações do driver mudaram. Atualize para conferi-las.", "O driver não confirmou a alteração."],
  ru: ["Глобальные настройки драйвера. Изменения применяются при следующем запуске игры.", "Ограничение FPS", "Выкл.", "Настройка драйвера", "Применить", "Вертикальная синхронизация", "Настройка игры", "Вкл.", "Режим питания", "Максимальная производительность", "Сейчас", "Настройки драйвера недоступны.", "Настройки драйвера изменились. Обновите для проверки.", "Драйвер не подтвердил изменение."],
  uk: ["Глобальні налаштування драйвера. Зміни застосовуються під час наступного запуску гри.", "Обмеження FPS", "Вимкнено", "Налаштування драйвера", "Застосувати", "Вертикальна синхронізація", "Налаштування гри", "Увімкнено", "Режим живлення", "Максимальна продуктивність", "Поточне", "Налаштування драйвера недоступні.", "Налаштування драйвера змінилися. Оновіть для перевірки.", "Драйвер не підтвердив зміну."],
  ja: ["ドライバーのグローバル設定です。変更はゲームの次回起動時に反映されます。", "フレームレート上限", "オフ", "ドライバーの既定値", "適用", "垂直同期", "ゲームの設定を使用", "オン", "電力管理", "最大パフォーマンス", "現在", "ドライバー設定を取得できません。", "ドライバー設定が変更されました。更新して確認してください。", "ドライバーで変更を確認できませんでした。"],
  ko: ["드라이버 전역 설정입니다. 변경 사항은 다음 게임 시작 시 적용됩니다.", "프레임 제한", "꺼짐", "드라이버 기본값", "적용", "수직 동기화", "게임 설정 사용", "켜짐", "전원 모드", "최대 성능", "현재", "드라이버 설정을 사용할 수 없습니다.", "드라이버 설정이 변경되었습니다. 새로 고쳐 확인하세요.", "드라이버가 변경 사항을 확인하지 못했습니다."],
  zh: ["全局驱动设置。更改在下次启动游戏时生效。", "帧率上限", "关闭", "驱动默认值", "应用", "垂直同步", "使用游戏设置", "开启", "电源模式", "最高性能", "当前", "驱动设置暂不可用。", "驱动设置已更改，请刷新查看。", "驱动未能确认此更改。"],
  hi: ["ड्राइवर की ग्लोबल सेटिंग। बदलाव गेम की अगली शुरुआत पर लागू होते हैं।", "FPS सीमा", "बंद", "ड्राइवर की डिफ़ॉल्ट सेटिंग", "लागू करें", "वर्टिकल सिंक", "गेम की सेटिंग इस्तेमाल करें", "चालू", "पावर मोड", "अधिकतम प्रदर्शन", "वर्तमान", "ड्राइवर सेटिंग उपलब्ध नहीं हैं।", "ड्राइवर सेटिंग बदल गई हैं। जाँचने के लिए रीफ़्रेश करें।", "ड्राइवर इस बदलाव की पुष्टि नहीं कर सका।"],
} as const;

const power = {
  en: ["Adaptive", "Driver controlled", "Consistent performance", "Minimum power", "Optimal power"],
  it: ["Adattiva", "Gestita dal driver", "Prestazioni costanti", "Consumo minimo", "Consumo ottimale"],
  de: ["Adaptiv", "Treibergesteuert", "Konstante Leistung", "Minimaler Energieverbrauch", "Optimaler Energieverbrauch"],
  es: ["Adaptativa", "Controlada por el controlador", "Rendimiento constante", "Consumo mínimo", "Consumo óptimo"],
  fr: ["Adaptative", "Gérée par le pilote", "Performances constantes", "Consommation minimale", "Consommation optimale"],
  pt: ["Adaptativa", "Controlada pelo driver", "Desempenho constante", "Consumo mínimo", "Consumo ideal"],
  ru: ["Адаптивный", "Управление драйвером", "Стабильная производительность", "Минимальное энергопотребление", "Оптимальное энергопотребление"],
  uk: ["Адаптивний", "Керування драйвером", "Стабільна продуктивність", "Мінімальне енергоспоживання", "Оптимальне енергоспоживання"],
  ja: ["適応型", "ドライバーによる制御", "安定したパフォーマンス", "最小電力", "最適電力"],
  ko: ["적응형", "드라이버 제어", "일정한 성능", "최소 전력", "최적 전력"],
  zh: ["自适应", "驱动控制", "稳定性能", "最低功耗", "最优功耗"],
  hi: ["अनुकूली", "ड्राइवर द्वारा नियंत्रित", "स्थिर प्रदर्शन", "न्यूनतम पावर", "अनुकूलित पावर"],
} as const;

export function nvidiaLocale(locale: string) {
  const language = deviceLanguage(locale), row = rows[language], p = power[language];
  return { scope: row[0], fps: row[1], off: row[2], driverDefault: row[3], apply: row[4], vsync: row[5], game: row[6], on: row[7], power: row[8], maximum: row[9], current: row[10], unavailable: row[11], changed: row[12], failed: row[13],
    powerCurrent: { 0: p[0], 2: p[1], 3: p[2], 4: p[3], 5: p[4] } as Record<number, string> };
}
