const rows = {
  en: ["Show", "Hide", "Move", "Confirm move", "Cancel", "Visible", "Hidden", "Last usable tab", "Reset tabs", "Restores the default tab order and shows all tabs. Other settings stay unchanged."],
  it: ["Mostra", "Nascondi", "Sposta", "Conferma spostamento", "Annulla", "Visibile", "Nascosta", "Ultima tab utilizzabile", "Ripristina le tab", "Ripristina l'ordine iniziale e mostra tutte le tab. Le altre impostazioni restano invariate."],
  de: ["Anzeigen", "Ausblenden", "Verschieben", "Verschieben bestätigen", "Abbrechen", "Sichtbar", "Ausgeblendet", "Letzter nutzbarer Tab", "Tabs zurücksetzen", "Stellt die ursprüngliche Reihenfolge wieder her und zeigt alle Tabs. Andere Einstellungen bleiben erhalten."],
  es: ["Mostrar", "Ocultar", "Mover", "Confirmar movimiento", "Cancelar", "Visible", "Oculta", "Última pestaña disponible", "Restablecer pestañas", "Restaura el orden inicial y muestra todas las pestañas. Los demás ajustes no cambian."],
  fr: ["Afficher", "Masquer", "Déplacer", "Confirmer le déplacement", "Annuler", "Visible", "Masqué", "Dernier onglet utilisable", "Réinitialiser les onglets", "Rétablit l'ordre initial et affiche tous les onglets. Les autres paramètres sont conservés."],
  pt: ["Mostrar", "Ocultar", "Mover", "Confirmar movimento", "Cancelar", "Visível", "Oculta", "Última aba disponível", "Restaurar abas", "Restaura a ordem inicial e mostra todas as abas. As outras configurações não mudam."],
  ru: ["Показать", "Скрыть", "Переместить", "Подтвердить перемещение", "Отмена", "Видна", "Скрыта", "Последняя доступная вкладка", "Сбросить вкладки", "Возвращает исходный порядок и показывает все вкладки. Остальные настройки сохраняются."],
  uk: ["Показати", "Приховати", "Перемістити", "Підтвердити переміщення", "Скасувати", "Видима", "Прихована", "Остання доступна вкладка", "Скинути вкладки", "Відновлює початковий порядок і показує всі вкладки. Решта налаштувань не змінюється."],
  ja: ["表示", "非表示", "移動", "移動を確定", "キャンセル", "表示中", "非表示", "最後の使用可能なタブ", "タブをリセット", "初期の順序に戻し、すべてのタブを表示します。他の設定は変更されません。"],
  ko: ["표시", "숨기기", "이동", "이동 확인", "취소", "표시됨", "숨겨짐", "마지막 사용 가능한 탭", "탭 초기화", "기본 순서로 복원하고 모든 탭을 표시합니다. 다른 설정은 유지됩니다."],
  zh: ["显示", "隐藏", "移动", "确认移动", "取消", "已显示", "已隐藏", "最后一个可用选项卡", "重置选项卡", "恢复默认顺序并显示所有选项卡。其他设置保持不变。"],
  hi: ["दिखाएँ", "छिपाएँ", "खिसकाएँ", "स्थान की पुष्टि करें", "रद्द करें", "दिखाई दे रहा है", "छिपा हुआ", "अंतिम उपलब्ध टैब", "टैब रीसेट करें", "मूल क्रम बहाल करता है और सभी टैब दिखाता है। अन्य सेटिंग्स नहीं बदलतीं।"],
} as const;
const introductions: Record<string, [string, string]> = {
  en: ["Customize tabs", "Choose which tabs to show and in what order."],
  it: ["Personalizza le tab", "Scegli quali tab mostrare e in quale ordine."],
  de: ["Tabs anpassen", "Wähle, welche Tabs in welcher Reihenfolge angezeigt werden."],
  es: ["Personalizar pestañas", "Elige qué pestañas mostrar y en qué orden."],
  fr: ["Personnaliser les onglets", "Choisissez les onglets à afficher et leur ordre."],
  pt: ["Personalizar abas", "Escolha quais abas mostrar e em que ordem."],
  ru: ["Настройка вкладок", "Выберите, какие вкладки показывать и в каком порядке."],
  uk: ["Налаштування вкладок", "Виберіть, які вкладки показувати та в якому порядку."],
  ja: ["タブのカスタマイズ", "表示するタブとその順序を選択します。"],
  ko: ["탭 사용자 지정", "표시할 탭과 순서를 선택하세요."],
  zh: ["自定义选项卡", "选择要显示的选项卡及其顺序。"],
  hi: ["टैब अनुकूलित करें", "चुनें कि कौन से टैब किस क्रम में दिखाने हैं।"],
};
export function tabEditorLocale(locale: string) {
  const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", portuguese: "pt", brazilian: "pt", russian: "ru", ukrainian: "uk", japanese: "ja", koreana: "ko", schinese: "zh", tchinese: "zh", hindi: "hi" };
  const normalized = locale.toLowerCase();
  const key = aliases[normalized] ?? normalized.split(/[-_]/)[0];
  const row = rows[key as keyof typeof rows] ?? rows.en;
  const intro = introductions[key] ?? introductions.en;
  return { show: row[0], hide: row[1], move: row[2], confirm: row[3], cancel: row[4], visible: row[5], hidden: row[6], lastTab: row[7], reset: row[8], resetHint: row[9], title: intro[0], introduction: intro[1] };
}
