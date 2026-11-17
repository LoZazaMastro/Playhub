const rows: Record<string, string[]> = {
  en: ["Active in the game", "Paused while the menu is open", "Waiting for the game", "Activation failed", "Off", "Status unknown"],
  it: ["Attivo nel gioco", "In pausa mentre il menu è aperto", "In attesa del gioco", "Attivazione non riuscita", "Disattivato", "Stato sconosciuto"],
  de: ["Im Spiel aktiv", "Bei geöffnetem Menü pausiert", "Wartet auf das Spiel", "Aktivierung fehlgeschlagen", "Aus", "Status unbekannt"],
  es: ["Activo en el juego", "En pausa con el menú abierto", "Esperando al juego", "No se pudo activar", "Desactivado", "Estado desconocido"],
  fr: ["Actif dans le jeu", "En pause pendant l’ouverture du menu", "En attente du jeu", "Échec de l’activation", "Désactivé", "État inconnu"],
  pt: ["Ativo no jogo", "Pausado enquanto o menu está aberto", "Aguardando o jogo", "Falha na ativação", "Desativado", "Estado desconhecido"],
  ru: ["Активно в игре", "Приостановлено, пока открыто меню", "Ожидание игры", "Не удалось включить", "Выключено", "Состояние неизвестно"],
  uk: ["Активно в грі", "Призупинено, поки відкрите меню", "Очікування гри", "Не вдалося ввімкнути", "Вимкнено", "Стан невідомий"],
  zh: ["已在游戏中启用", "菜单打开时暂停", "正在等待游戏", "启用失败", "已关闭", "状态未知"],
  ja: ["ゲーム内で動作中", "メニュー表示中は一時停止", "ゲームを待機中", "有効化に失敗しました", "オフ", "状態不明"],
  ko: ["게임에서 작동 중", "메뉴가 열려 있는 동안 일시 정지", "게임 대기 중", "활성화 실패", "꺼짐", "상태 알 수 없음"],
  hi: ["गेम में सक्रिय", "मेनू खुला रहने तक रुका हुआ", "गेम की प्रतीक्षा में", "सक्रिय नहीं हो सका", "बंद", "स्थिति अज्ञात"],
};
export function losslessCaptureLabel(locale: string, state?: string): string {
  const aliases: Record<string, string> = { english: "en", italian: "it", german: "de", spanish: "es", latam: "es", french: "fr", brazilian: "pt", portuguese: "pt", russian: "ru", ukrainian: "uk", schinese: "zh", tchinese: "zh", japanese: "ja", koreana: "ko", hindi: "hi" };
  const key = locale.toLowerCase().split(/[-_]/)[0];
  const index = ["active", "suspended", "pending", "failed", "inactive", "unknown"].indexOf(state ?? "");
  return index < 0 ? "" : (rows[aliases[key] ?? key] ?? rows.en)[index];
}
