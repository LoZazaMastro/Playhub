// @ts-nocheck
// Shortcuts settings renderer, Copyright (C) 2026 LoZazaMastro.
// SPDX-License-Identifier: GPL-3.0-only
// Original card/picker inline styles and navigation preserved from Shortcuts 1.2.2.
import { DFL, SP_REACT as React } from './decky';
import { ICON_CATEGORIES, iconMatchesQuery, renderQamIcon } from './qamIcons';
const h = React.createElement;
let language = 'en';
function currentLanguage() { return language; }
const CustomIcon = ({id,size='1em',style}) => renderQamIcon(id,undefined,size,style);
const TEXT = {
    en: {
        selectedTitle: "QAM tabs",
        availableTitle: "Available plugins",
        emptySelected: "No plugin has been added to the QAM yet.",
        emptyAvailable: "There are no other compatible plugins to add.",
        unavailable: "Plugin unavailable",
        disabled: "Plugin disabled",
        moveUp: "Move up",
        moveDown: "Move down",
        remove: "Remove from QAM",
        add: "Add to QAM",
        active: "The selected tabs are active in the QAM.",
        waiting: "Decky's QAM is not available yet. Preferences will be applied automatically.",
        unsupported: "This Decky version cannot update QAM tabs safely.",
        failed: "QAM tabs could not be updated. Reload Decky and try again.",
        pending: "Preferences were saved. Close and reopen the QAM if the tab bar does not update immediately.",
        count: "{count} active tabs",
        singleCount: "1 active tab",
        chooseIcon: "Choose icon",
        iconPickerTitle: "Choose an icon for {name}",
        originalIcon: "Original plugin icon",
        back: "Back",
        selectIcon: "Use {icon} icon"
    },
    it: {
        selectedTitle: "Schede QAM",
        availableTitle: "Plugin disponibili",
        emptySelected: "Non hai ancora aggiunto nessun plugin al QAM.",
        emptyAvailable: "Non ci sono altri plugin compatibili da aggiungere.",
        unavailable: "Plugin non disponibile",
        disabled: "Plugin disabilitato",
        moveUp: "Sposta su",
        moveDown: "Sposta giù",
        remove: "Rimuovi dal QAM",
        add: "Aggiungi al QAM",
        active: "Le schede selezionate sono attive nel QAM.",
        waiting: "Il QAM di Decky non è ancora disponibile. Le preferenze verranno applicate automaticamente.",
        unsupported: "Questa versione di Decky non consente di aggiornare le schede del QAM in modo sicuro.",
        failed: "Non è stato possibile aggiornare le schede del QAM. Ricarica Decky e riprova.",
        pending: "Le preferenze sono state salvate. Chiudi e riapri il QAM se la barra non si aggiorna subito.",
        count: "{count} schede attive",
        singleCount: "1 scheda attiva",
        chooseIcon: "Scegli icona",
        iconPickerTitle: "Scegli un'icona per {name}",
        originalIcon: "Icona originale del plugin",
        back: "Indietro",
        selectIcon: "Usa l'icona {icon}"
    },
    de: {
        selectedTitle: "QAM-Tabs",
        availableTitle: "Verfügbare Plugins",
        emptySelected: "Es wurde noch kein Plugin zum QAM hinzugefügt.",
        emptyAvailable: "Es sind keine weiteren kompatiblen Plugins verfügbar.",
        unavailable: "Plugin nicht verfügbar",
        disabled: "Plugin deaktiviert",
        moveUp: "Nach oben",
        moveDown: "Nach unten",
        remove: "Aus QAM entfernen",
        add: "Zum QAM hinzufügen",
        active: "Die ausgewählten Tabs sind im QAM aktiv.",
        waiting: "Deckys QAM ist noch nicht verfügbar. Die Einstellungen werden automatisch angewendet.",
        unsupported: "Diese Decky-Version kann QAM-Tabs nicht sicher aktualisieren.",
        failed: "Die QAM-Tabs konnten nicht aktualisiert werden. Lade Decky neu und versuche es erneut.",
        pending: "Die Einstellungen wurden gespeichert. Schließe und öffne das QAM erneut, falls die Tab-Leiste nicht sofort aktualisiert wird.",
        count: "{count} aktive Tabs",
        singleCount: "1 aktiver Tab",
        chooseIcon: "Symbol wählen",
        iconPickerTitle: "Symbol für {name} wählen",
        originalIcon: "Originales Plugin-Symbol",
        back: "Zurück",
        selectIcon: "Symbol {icon} verwenden"
    },
    fr: {
        selectedTitle: "Onglets QAM",
        availableTitle: "Plugins disponibles",
        emptySelected: "Aucun plugin n'a encore été ajouté au QAM.",
        emptyAvailable: "Aucun autre plugin compatible n'est disponible.",
        unavailable: "Plugin indisponible",
        disabled: "Plugin désactivé",
        moveUp: "Monter",
        moveDown: "Descendre",
        remove: "Retirer du QAM",
        add: "Ajouter au QAM",
        active: "Les onglets sélectionnés sont actifs dans le QAM.",
        waiting: "Le QAM de Decky n'est pas encore disponible. Les préférences seront appliquées automatiquement.",
        unsupported: "Cette version de Decky ne peut pas mettre à jour les onglets QAM en toute sécurité.",
        failed: "Impossible de mettre à jour les onglets QAM. Rechargez Decky et réessayez.",
        pending: "Les préférences ont été enregistrées. Fermez puis rouvrez le QAM si la barre ne se met pas à jour immédiatement.",
        count: "{count} onglets actifs",
        singleCount: "1 onglet actif",
        chooseIcon: "Choisir l'icône",
        iconPickerTitle: "Choisir une icône pour {name}",
        originalIcon: "Icône originale du plugin",
        back: "Retour",
        selectIcon: "Utiliser l'icône {icon}"
    },
    es: {
        selectedTitle: "Pestañas QAM",
        availableTitle: "Plugins disponibles",
        emptySelected: "Todavía no has añadido ningún plugin al QAM.",
        emptyAvailable: "No hay más plugins compatibles para añadir.",
        unavailable: "Plugin no disponible",
        disabled: "Plugin desactivado",
        moveUp: "Subir",
        moveDown: "Bajar",
        remove: "Quitar del QAM",
        add: "Añadir al QAM",
        active: "Las pestañas seleccionadas están activas en el QAM.",
        waiting: "El QAM de Decky aún no está disponible. Las preferencias se aplicarán automáticamente.",
        unsupported: "Esta versión de Decky no puede actualizar las pestañas QAM de forma segura.",
        failed: "No se pudieron actualizar las pestañas QAM. Recarga Decky e inténtalo de nuevo.",
        pending: "Las preferencias se guardaron. Cierra y vuelve a abrir el QAM si la barra no se actualiza de inmediato.",
        count: "{count} pestañas activas",
        singleCount: "1 pestaña activa",
        chooseIcon: "Elegir icono",
        iconPickerTitle: "Elige un icono para {name}",
        originalIcon: "Icono original del plugin",
        back: "Atrás",
        selectIcon: "Usar el icono {icon}"
    },
    "pt-BR": {
        selectedTitle: "Abas do QAM",
        availableTitle: "Plugins disponíveis",
        emptySelected: "Nenhum plugin foi adicionado ao QAM ainda.",
        emptyAvailable: "Não há outros plugins compatíveis para adicionar.",
        unavailable: "Plugin indisponível",
        disabled: "Plugin desativado",
        moveUp: "Mover para cima",
        moveDown: "Mover para baixo",
        remove: "Remover do QAM",
        add: "Adicionar ao QAM",
        active: "As abas selecionadas estão ativas no QAM.",
        waiting: "O QAM do Decky ainda não está disponível. As preferências serão aplicadas automaticamente.",
        unsupported: "Esta versão do Decky não pode atualizar as abas do QAM com segurança.",
        failed: "Não foi possível atualizar as abas do QAM. Recarregue o Decky e tente novamente.",
        pending: "As preferências foram salvas. Feche e reabra o QAM se a barra não atualizar imediatamente.",
        count: "{count} abas ativas",
        singleCount: "1 aba ativa",
        chooseIcon: "Escolher ícone",
        iconPickerTitle: "Escolha um ícone para {name}",
        originalIcon: "Ícone original do plugin",
        back: "Voltar",
        selectIcon: "Usar o ícone {icon}"
    },
    pt: {
        selectedTitle: "Separadores QAM",
        availableTitle: "Plugins disponíveis",
        emptySelected: "Ainda não foi adicionado nenhum plugin ao QAM.",
        emptyAvailable: "Não existem outros plugins compatíveis para adicionar.",
        unavailable: "Plugin indisponível",
        disabled: "Plugin desativado",
        moveUp: "Mover para cima",
        moveDown: "Mover para baixo",
        remove: "Remover do QAM",
        add: "Adicionar ao QAM",
        active: "Os separadores selecionados estão ativos no QAM.",
        waiting: "O QAM do Decky ainda não está disponível. As preferências serão aplicadas automaticamente.",
        unsupported: "Esta versão do Decky não consegue atualizar os separadores QAM em segurança.",
        failed: "Não foi possível atualizar os separadores QAM. Recarregue o Decky e tente novamente.",
        pending: "As preferências foram guardadas. Feche e volte a abrir o QAM se a barra não atualizar de imediato.",
        count: "{count} separadores ativos",
        singleCount: "1 separador ativo",
        chooseIcon: "Escolher ícone",
        iconPickerTitle: "Escolha um ícone para {name}",
        originalIcon: "Ícone original do plugin",
        back: "Voltar",
        selectIcon: "Usar o ícone {icon}"
    },
    ru: {
        selectedTitle: "Вкладки QAM",
        availableTitle: "Доступные плагины",
        emptySelected: "В QAM пока не добавлено ни одного плагина.",
        emptyAvailable: "Других совместимых плагинов нет.",
        unavailable: "Плагин недоступен",
        disabled: "Плагин отключён",
        moveUp: "Переместить вверх",
        moveDown: "Переместить вниз",
        remove: "Удалить из QAM",
        add: "Добавить в QAM",
        active: "Выбранные вкладки активны в QAM.",
        waiting: "QAM Decky пока недоступен. Настройки будут применены автоматически.",
        unsupported: "Эта версия Decky не может безопасно обновлять вкладки QAM.",
        failed: "Не удалось обновить вкладки QAM. Перезагрузите Decky и попробуйте снова.",
        pending: "Настройки сохранены. Закройте и снова откройте QAM, если панель вкладок не обновилась сразу.",
        count: "Активных вкладок: {count}",
        singleCount: "1 активная вкладка",
        chooseIcon: "Выбрать значок",
        iconPickerTitle: "Выберите значок для {name}",
        originalIcon: "Оригинальный значок плагина",
        back: "Назад",
        selectIcon: "Использовать значок {icon}"
    },
    pl: {
        selectedTitle: "Karty QAM",
        availableTitle: "Dostępne wtyczki",
        emptySelected: "Nie dodano jeszcze żadnej wtyczki do QAM.",
        emptyAvailable: "Brak innych zgodnych wtyczek do dodania.",
        unavailable: "Wtyczka niedostępna",
        disabled: "Wtyczka wyłączona",
        moveUp: "Przenieś w górę",
        moveDown: "Przenieś w dół",
        remove: "Usuń z QAM",
        add: "Dodaj do QAM",
        active: "Wybrane karty są aktywne w QAM.",
        waiting: "QAM Decky nie jest jeszcze dostępne. Ustawienia zostaną zastosowane automatycznie.",
        unsupported: "Ta wersja Decky nie może bezpiecznie aktualizować kart QAM.",
        failed: "Nie udało się zaktualizować kart QAM. Przeładuj Decky i spróbuj ponownie.",
        pending: "Ustawienia zapisano. Zamknij i ponownie otwórz QAM, jeśli pasek kart nie zaktualizuje się od razu.",
        count: "Aktywne karty: {count}",
        singleCount: "1 aktywna karta",
        chooseIcon: "Wybierz ikonę",
        iconPickerTitle: "Wybierz ikonę dla {name}",
        originalIcon: "Oryginalna ikona wtyczki",
        back: "Wstecz",
        selectIcon: "Użyj ikony {icon}"
    },
    tr: {
        selectedTitle: "QAM sekmeleri",
        availableTitle: "Kullanılabilir eklentiler",
        emptySelected: "QAM'a henüz bir eklenti eklenmedi.",
        emptyAvailable: "Eklenecek başka uyumlu eklenti yok.",
        unavailable: "Eklenti kullanılamıyor",
        disabled: "Eklenti devre dışı",
        moveUp: "Yukarı taşı",
        moveDown: "Aşağı taşı",
        remove: "QAM'dan kaldır",
        add: "QAM'a ekle",
        active: "Seçilen sekmeler QAM'da etkin.",
        waiting: "Decky QAM henüz kullanılamıyor. Tercihler otomatik uygulanacak.",
        unsupported: "Bu Decky sürümü QAM sekmelerini güvenle güncelleyemiyor.",
        failed: "QAM sekmeleri güncellenemedi. Decky'yi yeniden yükleyip tekrar deneyin.",
        pending: "Tercihler kaydedildi. Sekme çubuğu hemen güncellenmezse QAM'ı kapatıp yeniden açın.",
        count: "{count} etkin sekme",
        singleCount: "1 etkin sekme",
        chooseIcon: "Simge seç",
        iconPickerTitle: "{name} için simge seç",
        originalIcon: "Orijinal eklenti simgesi",
        back: "Geri",
        selectIcon: "{icon} simgesini kullan"
    },
    uk: {
        selectedTitle: "Вкладки QAM",
        availableTitle: "Доступні плагіни",
        emptySelected: "До QAM ще не додано жодного плагіна.",
        emptyAvailable: "Немає інших сумісних плагінів для додавання.",
        unavailable: "Плагін недоступний",
        disabled: "Плагін вимкнено",
        moveUp: "Перемістити вгору",
        moveDown: "Перемістити вниз",
        remove: "Видалити з QAM",
        add: "Додати до QAM",
        active: "Вибрані вкладки активні в QAM.",
        waiting: "QAM Decky ще недоступний. Налаштування буде застосовано автоматично.",
        unsupported: "Ця версія Decky не може безпечно оновлювати вкладки QAM.",
        failed: "Не вдалося оновити вкладки QAM. Перезавантажте Decky та спробуйте ще раз.",
        pending: "Налаштування збережено. Закрийте та знову відкрийте QAM, якщо панель вкладок не оновилася одразу.",
        count: "Активних вкладок: {count}",
        singleCount: "1 активна вкладка",
        chooseIcon: "Вибрати піктограму",
        iconPickerTitle: "Виберіть піктограму для {name}",
        originalIcon: "Оригінальна піктограма плагіна",
        back: "Назад",
        selectIcon: "Використати піктограму {icon}"
    },
    ja: {
        selectedTitle: "QAMタブ",
        availableTitle: "利用可能なプラグイン",
        emptySelected: "QAMにはまだプラグインが追加されていません。",
        emptyAvailable: "追加できる互換プラグインはありません。",
        unavailable: "プラグインは利用できません",
        disabled: "プラグインは無効です",
        moveUp: "上へ移動",
        moveDown: "下へ移動",
        remove: "QAMから削除",
        add: "QAMに追加",
        active: "選択したタブはQAMで有効です。",
        waiting: "DeckyのQAMはまだ利用できません。設定は自動的に適用されます。",
        unsupported: "このDeckyバージョンではQAMタブを安全に更新できません。",
        failed: "QAMタブを更新できませんでした。Deckyを再読み込みして再試行してください。",
        pending: "設定を保存しました。タブバーがすぐ更新されない場合はQAMを閉じて開き直してください。",
        count: "有効なタブ: {count}",
        singleCount: "有効なタブ: 1",
        chooseIcon: "アイコンを選択",
        iconPickerTitle: "{name} のアイコンを選択",
        originalIcon: "プラグインの元のアイコン",
        back: "戻る",
        selectIcon: "{icon} アイコンを使用"
    },
    ko: {
        selectedTitle: "QAM 탭",
        availableTitle: "사용 가능한 플러그인",
        emptySelected: "QAM에 아직 플러그인이 추가되지 않았습니다.",
        emptyAvailable: "추가할 수 있는 호환 플러그인이 없습니다.",
        unavailable: "플러그인을 사용할 수 없음",
        disabled: "플러그인 비활성화됨",
        moveUp: "위로 이동",
        moveDown: "아래로 이동",
        remove: "QAM에서 제거",
        add: "QAM에 추가",
        active: "선택한 탭이 QAM에서 활성화되었습니다.",
        waiting: "Decky QAM을 아직 사용할 수 없습니다. 설정은 자동으로 적용됩니다.",
        unsupported: "이 Decky 버전에서는 QAM 탭을 안전하게 업데이트할 수 없습니다.",
        failed: "QAM 탭을 업데이트할 수 없습니다. Decky를 다시 불러오고 다시 시도하세요.",
        pending: "설정이 저장되었습니다. 탭 바가 바로 업데이트되지 않으면 QAM을 닫았다가 다시 여세요.",
        count: "활성 탭 {count}개",
        singleCount: "활성 탭 1개",
        chooseIcon: "아이콘 선택",
        iconPickerTitle: "{name} 아이콘 선택",
        originalIcon: "플러그인 원본 아이콘",
        back: "뒤로",
        selectIcon: "{icon} 아이콘 사용"
    },
    "zh-CN": {
        selectedTitle: "QAM 标签页",
        availableTitle: "可用插件",
        emptySelected: "尚未向 QAM 添加任何插件。",
        emptyAvailable: "没有其他可添加的兼容插件。",
        unavailable: "插件不可用",
        disabled: "插件已禁用",
        moveUp: "上移",
        moveDown: "下移",
        remove: "从 QAM 移除",
        add: "添加到 QAM",
        active: "所选标签页已在 QAM 中启用。",
        waiting: "Decky 的 QAM 尚不可用。设置将自动应用。",
        unsupported: "此 Decky 版本无法安全更新 QAM 标签页。",
        failed: "无法更新 QAM 标签页。请重新加载 Decky 后重试。",
        pending: "设置已保存。如果标签栏没有立即更新，请关闭并重新打开 QAM。",
        count: "{count} 个活动标签页",
        singleCount: "1 个活动标签页",
        chooseIcon: "选择图标",
        iconPickerTitle: "为 {name} 选择图标",
        originalIcon: "插件原始图标",
        back: "返回",
        selectIcon: "使用 {icon} 图标"
    },
    "zh-TW": {
        selectedTitle: "QAM 分頁",
        availableTitle: "可用外掛",
        emptySelected: "尚未將任何外掛加入 QAM。",
        emptyAvailable: "沒有其他可加入的相容外掛。",
        unavailable: "外掛無法使用",
        disabled: "外掛已停用",
        moveUp: "上移",
        moveDown: "下移",
        remove: "從 QAM 移除",
        add: "加入 QAM",
        active: "所選分頁已在 QAM 中啟用。",
        waiting: "Decky 的 QAM 尚無法使用。設定將自動套用。",
        unsupported: "此 Decky 版本無法安全更新 QAM 分頁。",
        failed: "無法更新 QAM 分頁。請重新載入 Decky 後再試一次。",
        pending: "設定已儲存。如果分頁列沒有立即更新，請關閉並重新開啟 QAM。",
        count: "{count} 個啟用分頁",
        singleCount: "1 個啟用分頁",
        chooseIcon: "選擇圖示",
        iconPickerTitle: "為 {name} 選擇圖示",
        originalIcon: "外掛原始圖示",
        back: "返回",
        selectIcon: "使用 {icon} 圖示"
    },
    nl: {
        selectedTitle: "QAM-tabbladen",
        availableTitle: "Beschikbare plugins",
        emptySelected: "Er is nog geen plugin aan het QAM toegevoegd.",
        emptyAvailable: "Er zijn geen andere compatibele plugins om toe te voegen.",
        unavailable: "Plugin niet beschikbaar",
        disabled: "Plugin uitgeschakeld",
        moveUp: "Omhoog verplaatsen",
        moveDown: "Omlaag verplaatsen",
        remove: "Uit QAM verwijderen",
        add: "Aan QAM toevoegen",
        active: "De geselecteerde tabbladen zijn actief in het QAM.",
        waiting: "Decky's QAM is nog niet beschikbaar. Voorkeuren worden automatisch toegepast.",
        unsupported: "Deze Decky-versie kan QAM-tabbladen niet veilig bijwerken.",
        failed: "QAM-tabbladen konden niet worden bijgewerkt. Herlaad Decky en probeer opnieuw.",
        pending: "Voorkeuren zijn opgeslagen. Sluit en open het QAM opnieuw als de tabbalk niet direct wordt bijgewerkt.",
        count: "{count} actieve tabbladen",
        singleCount: "1 actief tabblad",
        chooseIcon: "Pictogram kiezen",
        iconPickerTitle: "Kies een pictogram voor {name}",
        originalIcon: "Origineel pluginpictogram",
        back: "Terug",
        selectIcon: "Pictogram {icon} gebruiken"
    },
    cs: {
        selectedTitle: "Karty QAM",
        availableTitle: "Dostupné pluginy",
        emptySelected: "Do QAM zatím nebyl přidán žádný plugin.",
        emptyAvailable: "Nejsou k dispozici žádné další kompatibilní pluginy.",
        unavailable: "Plugin není dostupný",
        disabled: "Plugin je vypnutý",
        moveUp: "Posunout nahoru",
        moveDown: "Posunout dolů",
        remove: "Odebrat z QAM",
        add: "Přidat do QAM",
        active: "Vybrané karty jsou v QAM aktivní.",
        waiting: "QAM Decky zatím není dostupné. Nastavení se použije automaticky.",
        unsupported: "Tato verze Decky neumí bezpečně aktualizovat karty QAM.",
        failed: "Karty QAM se nepodařilo aktualizovat. Znovu načtěte Decky a zkuste to znovu.",
        pending: "Nastavení bylo uloženo. Pokud se panel karet neaktualizuje hned, zavřete a znovu otevřete QAM.",
        count: "Aktivní karty: {count}",
        singleCount: "1 aktivní karta",
        chooseIcon: "Vybrat ikonu",
        iconPickerTitle: "Vyberte ikonu pro {name}",
        originalIcon: "Původní ikona pluginu",
        back: "Zpět",
        selectIcon: "Použít ikonu {icon}"
    },
    sv: {
        selectedTitle: "QAM-flikar",
        availableTitle: "Tillgängliga plugins",
        emptySelected: "Inget plugin har lagts till i QAM ännu.",
        emptyAvailable: "Det finns inga fler kompatibla plugins att lägga till.",
        unavailable: "Plugin ej tillgängligt",
        disabled: "Plugin inaktiverat",
        moveUp: "Flytta upp",
        moveDown: "Flytta ner",
        remove: "Ta bort från QAM",
        add: "Lägg till i QAM",
        active: "De valda flikarna är aktiva i QAM.",
        waiting: "Deckys QAM är inte tillgängligt ännu. Inställningarna tillämpas automatiskt.",
        unsupported: "Den här Decky-versionen kan inte uppdatera QAM-flikar säkert.",
        failed: "QAM-flikarna kunde inte uppdateras. Ladda om Decky och försök igen.",
        pending: "Inställningarna sparades. Stäng och öppna QAM igen om flikraden inte uppdateras direkt.",
        count: "{count} aktiva flikar",
        singleCount: "1 aktiv flik",
        chooseIcon: "Välj ikon",
        iconPickerTitle: "Välj en ikon för {name}",
        originalIcon: "Pluginets originalikon",
        back: "Tillbaka",
        selectIcon: "Använd ikonen {icon}"
    },
    fi: {
        selectedTitle: "QAM-välilehdet",
        availableTitle: "Saatavilla olevat lisäosat",
        emptySelected: "QAM-valikkoon ei ole vielä lisätty lisäosia.",
        emptyAvailable: "Muita yhteensopivia lisäosia ei ole lisättävissä.",
        unavailable: "Lisäosa ei ole käytettävissä",
        disabled: "Lisäosa poistettu käytöstä",
        moveUp: "Siirrä ylös",
        moveDown: "Siirrä alas",
        remove: "Poista QAM-valikosta",
        add: "Lisää QAM-valikkoon",
        active: "Valitut välilehdet ovat käytössä QAM-valikossa.",
        waiting: "Deckyn QAM ei ole vielä käytettävissä. Asetukset otetaan käyttöön automaattisesti.",
        unsupported: "Tämä Decky-versio ei voi päivittää QAM-välilehtiä turvallisesti.",
        failed: "QAM-välilehtiä ei voitu päivittää. Lataa Decky uudelleen ja yritä uudelleen.",
        pending: "Asetukset tallennettiin. Sulje ja avaa QAM uudelleen, jos välilehtipalkki ei päivity heti.",
        count: "{count} aktiivista välilehteä",
        singleCount: "1 aktiivinen välilehti",
        chooseIcon: "Valitse kuvake",
        iconPickerTitle: "Valitse kuvake: {name}",
        originalIcon: "Lisäosan alkuperäinen kuvake",
        back: "Takaisin",
        selectIcon: "Käytä kuvaketta {icon}"
    },
    da: {
        selectedTitle: "QAM-faner",
        availableTitle: "Tilgængelige plugins",
        emptySelected: "Der er endnu ikke tilføjet nogen plugins til QAM.",
        emptyAvailable: "Der er ingen andre kompatible plugins at tilføje.",
        unavailable: "Plugin ikke tilgængeligt",
        disabled: "Plugin deaktiveret",
        moveUp: "Flyt op",
        moveDown: "Flyt ned",
        remove: "Fjern fra QAM",
        add: "Føj til QAM",
        active: "De valgte faner er aktive i QAM.",
        waiting: "Deckys QAM er ikke tilgængeligt endnu. Indstillingerne anvendes automatisk.",
        unsupported: "Denne Decky-version kan ikke opdatere QAM-faner sikkert.",
        failed: "QAM-fanerne kunne ikke opdateres. Genindlæs Decky og prøv igen.",
        pending: "Indstillingerne er gemt. Luk og åbn QAM igen, hvis fanebjælken ikke opdateres med det samme.",
        count: "{count} aktive faner",
        singleCount: "1 aktiv fane",
        chooseIcon: "Vælg ikon",
        iconPickerTitle: "Vælg et ikon til {name}",
        originalIcon: "Pluginets originale ikon",
        back: "Tilbage",
        selectIcon: "Brug ikonet {icon}"
    },
    no: {
        selectedTitle: "QAM-faner",
        availableTitle: "Tilgjengelige plugins",
        emptySelected: "Ingen plugins er lagt til i QAM ennå.",
        emptyAvailable: "Det finnes ingen andre kompatible plugins å legge til.",
        unavailable: "Plugin ikke tilgjengelig",
        disabled: "Plugin deaktivert",
        moveUp: "Flytt opp",
        moveDown: "Flytt ned",
        remove: "Fjern fra QAM",
        add: "Legg til i QAM",
        active: "De valgte fanene er aktive i QAM.",
        waiting: "Deckys QAM er ikke tilgjengelig ennå. Innstillingene brukes automatisk.",
        unsupported: "Denne Decky-versjonen kan ikke oppdatere QAM-faner sikkert.",
        failed: "QAM-fanene kunne ikke oppdateres. Last Decky på nytt og prøv igjen.",
        pending: "Innstillingene er lagret. Lukk og åpne QAM igjen hvis fanelinjen ikke oppdateres med en gang.",
        count: "{count} aktive faner",
        singleCount: "1 aktiv fane",
        chooseIcon: "Velg ikon",
        iconPickerTitle: "Velg et ikon for {name}",
        originalIcon: "Pluginets originale ikon",
        back: "Tilbake",
        selectIcon: "Bruk ikonet {icon}"
    }
};

const NATIVE_TAB_LABELS = {
    en: ["Notifications", "Remote Play Together", "Voice Chat", "Friends", "Settings", "Performance", "Help", "Music", "Decky"],
    it: ["Notifiche", "Remote Play Together", "Chat vocale", "Amici", "Impostazioni", "Prestazioni", "Aiuto", "Musica", "Decky"],
    de: ["Benachrichtigungen", "Remote Play Together", "Sprachchat", "Freunde", "Einstellungen", "Leistung", "Hilfe", "Musik", "Decky"],
    fr: ["Notifications", "Remote Play Together", "Chat vocal", "Amis", "Paramètres", "Performances", "Aide", "Musique", "Decky"],
    es: ["Notificaciones", "Remote Play Together", "Chat de voz", "Amigos", "Ajustes", "Rendimiento", "Ayuda", "Música", "Decky"],
    "pt-BR": ["Notificações", "Remote Play Together", "Chat de voz", "Amigos", "Configurações", "Desempenho", "Ajuda", "Música", "Decky"],
    pt: ["Notificações", "Remote Play Together", "Conversa de voz", "Amigos", "Definições", "Desempenho", "Ajuda", "Música", "Decky"],
    ru: ["Уведомления", "Remote Play Together", "Голосовой чат", "Друзья", "Настройки", "Производительность", "Справка", "Музыка", "Decky"],
    pl: ["Powiadomienia", "Remote Play Together", "Czat głosowy", "Znajomi", "Ustawienia", "Wydajność", "Pomoc", "Muzyka", "Decky"],
    tr: ["Bildirimler", "Remote Play Together", "Sesli sohbet", "Arkadaşlar", "Ayarlar", "Performans", "Yardım", "Müzik", "Decky"],
    uk: ["Сповіщення", "Remote Play Together", "Голосовий чат", "Друзі", "Налаштування", "Продуктивність", "Довідка", "Музика", "Decky"],
    ja: ["通知", "Remote Play Together", "ボイスチャット", "フレンド", "設定", "パフォーマンス", "ヘルプ", "音楽", "Decky"],
    ko: ["알림", "Remote Play Together", "음성 채팅", "친구", "설정", "성능", "도움말", "음악", "Decky"],
    "zh-CN": ["通知", "Remote Play Together", "语音聊天", "好友", "设置", "性能", "帮助", "音乐", "Decky"],
    "zh-TW": ["通知", "Remote Play Together", "語音聊天", "好友", "設定", "效能", "說明", "音樂", "Decky"],
    nl: ["Meldingen", "Remote Play Together", "Spraakchat", "Vrienden", "Instellingen", "Prestaties", "Help", "Muziek", "Decky"],
    cs: ["Oznámení", "Remote Play Together", "Hlasový chat", "Přátelé", "Nastavení", "Výkon", "Nápověda", "Hudba", "Decky"],
    sv: ["Aviseringar", "Remote Play Together", "Röstchatt", "Vänner", "Inställningar", "Prestanda", "Hjälp", "Musik", "Decky"],
    fi: ["Ilmoitukset", "Remote Play Together", "Äänikeskustelu", "Kaverit", "Asetukset", "Suorituskyky", "Ohje", "Musiikki", "Decky"],
    da: ["Notifikationer", "Remote Play Together", "Stemmechat", "Venner", "Indstillinger", "Ydeevne", "Hjælp", "Musik", "Decky"],
    no: ["Varsler", "Remote Play Together", "Talechat", "Venner", "Innstillinger", "Ytelse", "Hjelp", "Musikk", "Decky"]
};

const CATEGORY_TEXT = {
    "en": {
        "original": "Original icon",
        "applications": "Applications & brands",
        "devices": "Devices & hardware",
        "media": "Media & gaming",
        "files": "Files & cloud",
        "communication": "Communication",
        "navigation": "Navigation & places",
        "security": "Security & people",
        "system": "System & tools",
        "interface": "Interface & controls",
        "data": "Data & charts",
        "timeNature": "Time, weather & nature",
        "commerce": "Commerce & finance",
        "activities": "Activities & transport",
        "lettersNumbers": "Letters & numbers",
        "shapesSymbols": "Shapes & symbols",
        "other": "Other"
    },
    "it": {
        "original": "Icona originale",
        "applications": "Applicazioni e brand",
        "devices": "Dispositivi e hardware",
        "media": "Media e gaming",
        "files": "File e cloud",
        "communication": "Comunicazione",
        "navigation": "Navigazione e luoghi",
        "security": "Sicurezza e persone",
        "system": "Sistema e strumenti",
        "interface": "Interfaccia e controlli",
        "data": "Dati e grafici",
        "timeNature": "Tempo, meteo e natura",
        "commerce": "Commercio e finanza",
        "activities": "Attività e trasporti",
        "lettersNumbers": "Lettere e numeri",
        "shapesSymbols": "Forme e simboli",
        "other": "Altro"
    },
    "de": {
        "original": "Originalsymbol",
        "applications": "Anwendungen & Marken",
        "devices": "Geräte & Hardware",
        "media": "Medien & Gaming",
        "files": "Dateien & Cloud",
        "communication": "Kommunikation",
        "navigation": "Navigation & Orte",
        "security": "Sicherheit & Personen",
        "system": "System & Werkzeuge",
        "interface": "Oberfläche & Steuerung",
        "data": "Daten & Diagramme",
        "timeNature": "Zeit, Wetter & Natur",
        "commerce": "Handel & Finanzen",
        "activities": "Aktivitäten & Verkehr",
        "lettersNumbers": "Buchstaben & Zahlen",
        "shapesSymbols": "Formen & Symbole",
        "other": "Sonstiges"
    },
    "fr": {
        "original": "Icône d’origine",
        "applications": "Applications et marques",
        "devices": "Appareils et matériel",
        "media": "Médias et jeux",
        "files": "Fichiers et cloud",
        "communication": "Communication",
        "navigation": "Navigation et lieux",
        "security": "Sécurité et personnes",
        "system": "Système et outils",
        "interface": "Interface et commandes",
        "data": "Données et graphiques",
        "timeNature": "Temps, météo et nature",
        "commerce": "Commerce et finance",
        "activities": "Activités et transport",
        "lettersNumbers": "Lettres et nombres",
        "shapesSymbols": "Formes et symboles",
        "other": "Autres"
    },
    "es": {
        "original": "Icono original",
        "applications": "Aplicaciones y marcas",
        "devices": "Dispositivos y hardware",
        "media": "Medios y juegos",
        "files": "Archivos y nube",
        "communication": "Comunicación",
        "navigation": "Navegación y lugares",
        "security": "Seguridad y personas",
        "system": "Sistema y herramientas",
        "interface": "Interfaz y controles",
        "data": "Datos y gráficos",
        "timeNature": "Tiempo, clima y naturaleza",
        "commerce": "Comercio y finanzas",
        "activities": "Actividades y transporte",
        "lettersNumbers": "Letras y números",
        "shapesSymbols": "Formas y símbolos",
        "other": "Otros"
    },
    "pt-BR": {
        "original": "Ícone original",
        "applications": "Aplicativos e marcas",
        "devices": "Dispositivos e hardware",
        "media": "Mídia e jogos",
        "files": "Arquivos e nuvem",
        "communication": "Comunicação",
        "navigation": "Navegação e lugares",
        "security": "Segurança e pessoas",
        "system": "Sistema e ferramentas",
        "interface": "Interface e controles",
        "data": "Dados e gráficos",
        "timeNature": "Tempo, clima e natureza",
        "commerce": "Comércio e finanças",
        "activities": "Atividades e transporte",
        "lettersNumbers": "Letras e números",
        "shapesSymbols": "Formas e símbolos",
        "other": "Outros"
    },
    "pt": {
        "original": "Ícone original",
        "applications": "Aplicações e marcas",
        "devices": "Dispositivos e hardware",
        "media": "Multimédia e jogos",
        "files": "Ficheiros e cloud",
        "communication": "Comunicação",
        "navigation": "Navegação e locais",
        "security": "Segurança e pessoas",
        "system": "Sistema e ferramentas",
        "interface": "Interface e controlos",
        "data": "Dados e gráficos",
        "timeNature": "Tempo, meteorologia e natureza",
        "commerce": "Comércio e finanças",
        "activities": "Atividades e transportes",
        "lettersNumbers": "Letras e números",
        "shapesSymbols": "Formas e símbolos",
        "other": "Outros"
    },
    "ru": {
        "original": "Оригинальный значок",
        "applications": "Приложения и бренды",
        "devices": "Устройства и оборудование",
        "media": "Медиа и игры",
        "files": "Файлы и облако",
        "communication": "Связь",
        "navigation": "Навигация и места",
        "security": "Безопасность и люди",
        "system": "Система и инструменты",
        "interface": "Интерфейс и управление",
        "data": "Данные и графики",
        "timeNature": "Время, погода и природа",
        "commerce": "Торговля и финансы",
        "activities": "Активности и транспорт",
        "lettersNumbers": "Буквы и цифры",
        "shapesSymbols": "Формы и символы",
        "other": "Другое"
    },
    "pl": {
        "original": "Oryginalna ikona",
        "applications": "Aplikacje i marki",
        "devices": "Urządzenia i sprzęt",
        "media": "Media i gry",
        "files": "Pliki i chmura",
        "communication": "Komunikacja",
        "navigation": "Nawigacja i miejsca",
        "security": "Bezpieczeństwo i osoby",
        "system": "System i narzędzia",
        "interface": "Interfejs i sterowanie",
        "data": "Dane i wykresy",
        "timeNature": "Czas, pogoda i natura",
        "commerce": "Handel i finanse",
        "activities": "Aktywności i transport",
        "lettersNumbers": "Litery i liczby",
        "shapesSymbols": "Kształty i symbole",
        "other": "Inne"
    },
    "tr": {
        "original": "Orijinal simge",
        "applications": "Uygulamalar ve markalar",
        "devices": "Cihazlar ve donanım",
        "media": "Medya ve oyun",
        "files": "Dosyalar ve bulut",
        "communication": "İletişim",
        "navigation": "Navigasyon ve yerler",
        "security": "Güvenlik ve kişiler",
        "system": "Sistem ve araçlar",
        "interface": "Arayüz ve kontroller",
        "data": "Veri ve grafikler",
        "timeNature": "Zaman, hava ve doğa",
        "commerce": "Ticaret ve finans",
        "activities": "Etkinlikler ve ulaşım",
        "lettersNumbers": "Harfler ve sayılar",
        "shapesSymbols": "Şekiller ve semboller",
        "other": "Diğer"
    },
    "uk": {
        "original": "Оригінальна піктограма",
        "applications": "Застосунки й бренди",
        "devices": "Пристрої та обладнання",
        "media": "Медіа та ігри",
        "files": "Файли та хмара",
        "communication": "Спілкування",
        "navigation": "Навігація та місця",
        "security": "Безпека та люди",
        "system": "Система та інструменти",
        "interface": "Інтерфейс і керування",
        "data": "Дані та графіки",
        "timeNature": "Час, погода та природа",
        "commerce": "Торгівля та фінанси",
        "activities": "Активності та транспорт",
        "lettersNumbers": "Літери та числа",
        "shapesSymbols": "Форми та символи",
        "other": "Інше"
    },
    "ja": {
        "original": "元のアイコン",
        "applications": "アプリとブランド",
        "devices": "デバイスとハードウェア",
        "media": "メディアとゲーム",
        "files": "ファイルとクラウド",
        "communication": "コミュニケーション",
        "navigation": "ナビゲーションと場所",
        "security": "セキュリティと人物",
        "system": "システムとツール",
        "interface": "インターフェースと操作",
        "data": "データとグラフ",
        "timeNature": "時間・天気・自然",
        "commerce": "商取引と金融",
        "activities": "アクティビティと交通",
        "lettersNumbers": "文字と数字",
        "shapesSymbols": "図形と記号",
        "other": "その他"
    },
    "ko": {
        "original": "원래 아이콘",
        "applications": "앱 및 브랜드",
        "devices": "기기 및 하드웨어",
        "media": "미디어 및 게임",
        "files": "파일 및 클라우드",
        "communication": "커뮤니케이션",
        "navigation": "내비게이션 및 장소",
        "security": "보안 및 사용자",
        "system": "시스템 및 도구",
        "interface": "인터페이스 및 컨트롤",
        "data": "데이터 및 차트",
        "timeNature": "시간, 날씨 및 자연",
        "commerce": "상거래 및 금융",
        "activities": "활동 및 교통",
        "lettersNumbers": "문자 및 숫자",
        "shapesSymbols": "도형 및 기호",
        "other": "기타"
    },
    "zh-CN": {
        "original": "原始图标",
        "applications": "应用与品牌",
        "devices": "设备与硬件",
        "media": "媒体与游戏",
        "files": "文件与云端",
        "communication": "通信",
        "navigation": "导航与地点",
        "security": "安全与用户",
        "system": "系统与工具",
        "interface": "界面与控制",
        "data": "数据与图表",
        "timeNature": "时间、天气与自然",
        "commerce": "商业与金融",
        "activities": "活动与交通",
        "lettersNumbers": "字母与数字",
        "shapesSymbols": "形状与符号",
        "other": "其他"
    },
    "zh-TW": {
        "original": "原始圖示",
        "applications": "應用程式與品牌",
        "devices": "裝置與硬體",
        "media": "媒體與遊戲",
        "files": "檔案與雲端",
        "communication": "通訊",
        "navigation": "導航與地點",
        "security": "安全與使用者",
        "system": "系統與工具",
        "interface": "介面與控制",
        "data": "資料與圖表",
        "timeNature": "時間、天氣與自然",
        "commerce": "商業與金融",
        "activities": "活動與交通",
        "lettersNumbers": "字母與數字",
        "shapesSymbols": "形狀與符號",
        "other": "其他"
    },
    "nl": {
        "original": "Origineel pictogram",
        "applications": "Apps en merken",
        "devices": "Apparaten en hardware",
        "media": "Media en gaming",
        "files": "Bestanden en cloud",
        "communication": "Communicatie",
        "navigation": "Navigatie en plaatsen",
        "security": "Beveiliging en personen",
        "system": "Systeem en hulpmiddelen",
        "interface": "Interface en bediening",
        "data": "Gegevens en grafieken",
        "timeNature": "Tijd, weer en natuur",
        "commerce": "Handel en financiën",
        "activities": "Activiteiten en vervoer",
        "lettersNumbers": "Letters en cijfers",
        "shapesSymbols": "Vormen en symbolen",
        "other": "Overig"
    },
    "cs": {
        "original": "Původní ikona",
        "applications": "Aplikace a značky",
        "devices": "Zařízení a hardware",
        "media": "Média a hry",
        "files": "Soubory a cloud",
        "communication": "Komunikace",
        "navigation": "Navigace a místa",
        "security": "Zabezpečení a lidé",
        "system": "Systém a nástroje",
        "interface": "Rozhraní a ovládání",
        "data": "Data a grafy",
        "timeNature": "Čas, počasí a příroda",
        "commerce": "Obchod a finance",
        "activities": "Aktivity a doprava",
        "lettersNumbers": "Písmena a čísla",
        "shapesSymbols": "Tvary a symboly",
        "other": "Ostatní"
    },
    "sv": {
        "original": "Originalikon",
        "applications": "Appar och varumärken",
        "devices": "Enheter och hårdvara",
        "media": "Media och spel",
        "files": "Filer och moln",
        "communication": "Kommunikation",
        "navigation": "Navigering och platser",
        "security": "Säkerhet och personer",
        "system": "System och verktyg",
        "interface": "Gränssnitt och kontroller",
        "data": "Data och diagram",
        "timeNature": "Tid, väder och natur",
        "commerce": "Handel och ekonomi",
        "activities": "Aktiviteter och transport",
        "lettersNumbers": "Bokstäver och siffror",
        "shapesSymbols": "Former och symboler",
        "other": "Övrigt"
    },
    "fi": {
        "original": "Alkuperäinen kuvake",
        "applications": "Sovellukset ja brändit",
        "devices": "Laitteet ja laitteisto",
        "media": "Media ja pelaaminen",
        "files": "Tiedostot ja pilvi",
        "communication": "Viestintä",
        "navigation": "Navigointi ja paikat",
        "security": "Suojaus ja ihmiset",
        "system": "Järjestelmä ja työkalut",
        "interface": "Käyttöliittymä ja ohjaimet",
        "data": "Tiedot ja kaaviot",
        "timeNature": "Aika, sää ja luonto",
        "commerce": "Kauppa ja talous",
        "activities": "Aktiviteetit ja liikenne",
        "lettersNumbers": "Kirjaimet ja numerot",
        "shapesSymbols": "Muodot ja symbolit",
        "other": "Muut"
    },
    "da": {
        "original": "Originalt ikon",
        "applications": "Apps og brands",
        "devices": "Enheder og hardware",
        "media": "Medier og gaming",
        "files": "Filer og cloud",
        "communication": "Kommunikation",
        "navigation": "Navigation og steder",
        "security": "Sikkerhed og personer",
        "system": "System og værktøjer",
        "interface": "Grænseflade og kontroller",
        "data": "Data og diagrammer",
        "timeNature": "Tid, vejr og natur",
        "commerce": "Handel og finans",
        "activities": "Aktiviteter og transport",
        "lettersNumbers": "Bogstaver og tal",
        "shapesSymbols": "Former og symboler",
        "other": "Andet"
    },
    "no": {
        "original": "Originalikon",
        "applications": "Apper og merkevarer",
        "devices": "Enheter og maskinvare",
        "media": "Medier og spill",
        "files": "Filer og sky",
        "communication": "Kommunikasjon",
        "navigation": "Navigasjon og steder",
        "security": "Sikkerhet og personer",
        "system": "System og verktøy",
        "interface": "Grensesnitt og kontroller",
        "data": "Data og diagrammer",
        "timeNature": "Tid, vær og natur",
        "commerce": "Handel og finans",
        "activities": "Aktiviteter og transport",
        "lettersNumbers": "Bokstaver og tall",
        "shapesSymbols": "Former og symboler",
        "other": "Annet"
    }
};

function categoryText(key) {
    const table = CATEGORY_TEXT[currentLanguage()] ?? CATEGORY_TEXT.en;
    return table[key] ?? CATEGORY_TEXT.en[key] ?? key;
}

const SEARCH_TEXT = {
    "en": {
        "label": "Search icons",
        "placeholder": "Search by Tabler filename",
        "empty": "No matching icons."
    },
    "it": {
        "label": "Cerca icone",
        "placeholder": "Cerca per nome file Tabler",
        "empty": "Nessuna icona corrispondente."
    },
    "de": {
        "label": "Symbole suchen",
        "placeholder": "Nach Tabler-Dateiname suchen",
        "empty": "Keine passenden Symbole."
    },
    "fr": {
        "label": "Rechercher des icônes",
        "placeholder": "Rechercher par nom de fichier Tabler",
        "empty": "Aucune icône correspondante."
    },
    "es": {
        "label": "Buscar iconos",
        "placeholder": "Buscar por nombre de archivo Tabler",
        "empty": "No hay iconos coincidentes."
    },
    "pt-BR": {
        "label": "Pesquisar ícones",
        "placeholder": "Pesquisar pelo nome do arquivo Tabler",
        "empty": "Nenhum ícone correspondente."
    },
    "pt": {
        "label": "Pesquisar ícones",
        "placeholder": "Pesquisar pelo nome do ficheiro Tabler",
        "empty": "Nenhum ícone correspondente."
    },
    "ru": {
        "label": "Поиск значков",
        "placeholder": "Поиск по имени файла Tabler",
        "empty": "Подходящих значков нет."
    },
    "pl": {
        "label": "Szukaj ikon",
        "placeholder": "Szukaj według nazwy pliku Tabler",
        "empty": "Brak pasujących ikon."
    },
    "tr": {
        "label": "Simge ara",
        "placeholder": "Tabler dosya adına göre ara",
        "empty": "Eşleşen simge yok."
    },
    "uk": {
        "label": "Пошук піктограм",
        "placeholder": "Пошук за назвою файлу Tabler",
        "empty": "Відповідних піктограм немає."
    },
    "ja": {
        "label": "アイコンを検索",
        "placeholder": "Tabler のファイル名で検索",
        "empty": "一致するアイコンはありません。"
    },
    "ko": {
        "label": "아이콘 검색",
        "placeholder": "Tabler 파일 이름으로 검색",
        "empty": "일치하는 아이콘이 없습니다."
    },
    "zh-CN": {
        "label": "搜索图标",
        "placeholder": "按 Tabler 文件名搜索",
        "empty": "没有匹配的图标。"
    },
    "zh-TW": {
        "label": "搜尋圖示",
        "placeholder": "依 Tabler 檔名搜尋",
        "empty": "沒有符合的圖示。"
    },
    "nl": {
        "label": "Pictogrammen zoeken",
        "placeholder": "Zoeken op Tabler-bestandsnaam",
        "empty": "Geen overeenkomende pictogrammen."
    },
    "cs": {
        "label": "Hledat ikony",
        "placeholder": "Hledat podle názvu souboru Tabler",
        "empty": "Žádné odpovídající ikony."
    },
    "sv": {
        "label": "Sök ikoner",
        "placeholder": "Sök efter Tabler-filnamn",
        "empty": "Inga matchande ikoner."
    },
    "fi": {
        "label": "Hae kuvakkeita",
        "placeholder": "Hae Tabler-tiedostonimellä",
        "empty": "Ei vastaavia kuvakkeita."
    },
    "da": {
        "label": "Søg efter ikoner",
        "placeholder": "Søg efter Tabler-filnavn",
        "empty": "Ingen matchende ikoner."
    },
    "no": {
        "label": "Søk etter ikoner",
        "placeholder": "Søk etter Tabler-filnavn",
        "empty": "Ingen samsvarende ikoner."
    }
};

function searchText(key) {
    const table = SEARCH_TEXT[currentLanguage()] ?? SEARCH_TEXT.en;
    return table[key] ?? SEARCH_TEXT.en[key] ?? key;
}

function text(key, values = {}) {
    const table = TEXT[currentLanguage()] ?? TEXT.en;
    let value = table[key] ?? TEXT.en[key] ?? key;
    for (const [name, replacement] of Object.entries(values)) {
        value = value.replaceAll(`{${name}}`, String(replacement));
    }
    return value;
}

function iconBase(children, props = {}) {
    return h(
        "svg",
        {
            viewBox: "0 0 24 24",
            width: props.size ?? "1em",
            height: props.size ?? "1em",
            fill: "none",
            stroke: "currentColor",
            strokeWidth: 2,
            strokeLinecap: "round",
            strokeLinejoin: "round",
            style: props.style,
            "aria-hidden": true
        },
        children
    );
}

const UI_ICONS = {
    shortcuts: `<path d="M4 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4" /> <path d="M14 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4" /> <path d="M4 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4" /> <path d="M14 17h6m-3 -3v6" />`,
    plus: `<path d="M12 5l0 14" /> <path d="M5 12l14 0" />`,
    up: `<path d="M6 15l6 -6l6 6" />`,
    down: `<path d="M6 9l6 6l6 -6" />`,
    trash: `<path d="M4 7l16 0" /> <path d="M10 11l0 6" /> <path d="M14 11l0 6" /> <path d="M5 7l1 12a2 2 0 0 0 2 2h8a2 2 0 0 0 2 -2l1 -12" /> <path d="M9 7v-3a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v3" />`,
    back: `<path d="M5 12l14 0" /> <path d="M5 12l6 6" /> <path d="M5 12l6 -6" />`
};

function UiIcon({ id, size = "1em", style }) {
    return h("svg", {
        viewBox: "0 0 24 24",
        width: size,
        height: size,
        fill: "none",
        stroke: "currentColor",
        strokeWidth: 2,
        strokeLinecap: "round",
        strokeLinejoin: "round",
        style,
        "aria-hidden": true,
        dangerouslySetInnerHTML: { __html: UI_ICONS[id] ?? UI_ICONS.shortcuts }
    });
}

function ShortcutsIcon(props = {}) {
    return h(UiIcon, { id: "shortcuts", ...props });
}

function PlusIcon(props = {}) {
    return h(UiIcon, { id: "plus", ...props });
}

function UpIcon(props = {}) {
    return h(UiIcon, { id: "up", ...props });
}

function DownIcon(props = {}) {
    return h(UiIcon, { id: "down", ...props });
}

function TrashIcon(props = {}) {
    return h(UiIcon, { id: "trash", ...props });
}

function BackIcon(props = {}) {
    return h(UiIcon, { id: "back", ...props });
}

function MissingIcon(props = {}) {
    return iconBase([
        h("circle", { key: "a", cx: 12, cy: 12, r: 9 }),
        h("path", { key: "b", d: "M9.7 9a2.5 2.5 0 0 1 4.6 1.4c0 1.8-2.3 2-2.3 3.6" }),
        h("path", { key: "c", d: "M12 17h.01" })
    ], props);
}

function entryIcon(entry, size = 22) {
    if (entry.iconChoice && entry.iconChoice !== "original") {
        return h(CustomIcon, { id: entry.iconChoice, size });
    }
    return entry.icon ?? h(MissingIcon, { size });
}

function focusBySelector(currentTarget, selector) {
    const doc = currentTarget?.ownerDocument;
    const target = doc?.querySelector?.(selector);
    if (!target || typeof target.focus !== "function") return false;
    target.focus();
    return true;
}

function focusMainPrimary(currentTarget, rowIndex, selectedCount) {
    if (rowIndex < 0) return false;
    const control = rowIndex < selectedCount ? "name" : "available";
    return focusBySelector(currentTarget, `[data-shortcuts-main-row="${rowIndex}"][data-shortcuts-main-control="${control}"]`);
}

function handleSelectedNameNav(event, rowIndex, selectedCount, totalRows) {
    if (event.key === "ArrowDown") {
        if (focusBySelector(event.currentTarget, `[data-shortcuts-main-row="${rowIndex}"][data-shortcuts-main-control="up"]`)) {
            event.preventDefault();
            event.stopPropagation();
        }
    } else if (event.key === "ArrowUp") {
        if (focusMainPrimary(event.currentTarget, rowIndex - 1, selectedCount)) {
            event.preventDefault();
            event.stopPropagation();
        }
    }
}

function handleSelectedActionNav(event, rowIndex, control, selectedCount, totalRows) {
    const order = ["up", "down", "remove"];
    const index = order.indexOf(control);
    if (event.key === "ArrowLeft" && index > 0) {
        if (focusBySelector(event.currentTarget, `[data-shortcuts-main-row="${rowIndex}"][data-shortcuts-main-control="${order[index - 1]}"]`)) {
            event.preventDefault();
            event.stopPropagation();
        }
    } else if (event.key === "ArrowRight" && index < order.length - 1) {
        if (focusBySelector(event.currentTarget, `[data-shortcuts-main-row="${rowIndex}"][data-shortcuts-main-control="${order[index + 1]}"]`)) {
            event.preventDefault();
            event.stopPropagation();
        }
    } else if (event.key === "ArrowUp") {
        if (focusBySelector(event.currentTarget, `[data-shortcuts-main-row="${rowIndex}"][data-shortcuts-main-control="name"]`)) {
            event.preventDefault();
            event.stopPropagation();
        }
    } else if (event.key === "ArrowDown") {
        if (focusMainPrimary(event.currentTarget, rowIndex + 1, selectedCount)) {
            event.preventDefault();
            event.stopPropagation();
        }
    }
}

function handleAvailableNav(event, rowIndex, selectedCount, totalRows) {
    if (event.key === "ArrowUp") {
        if (focusMainPrimary(event.currentTarget, rowIndex - 1, selectedCount)) {
            event.preventDefault();
            event.stopPropagation();
        }
    } else if (event.key === "ArrowDown") {
        if (focusMainPrimary(event.currentTarget, rowIndex + 1, selectedCount)) {
            event.preventDefault();
            event.stopPropagation();
        }
    }
}

function focusIconChoice(currentTarget, index) {
    return focusBySelector(currentTarget, `[data-shortcuts-icon-index="${index}"]`);
}

function handleIconPickerNav(event, index, totalChoices, columns = 4) {
    let nextIndex = null;
    if (event.key === "ArrowLeft") nextIndex = index > 0 ? index - 1 : null;
    else if (event.key === "ArrowRight") nextIndex = index < totalChoices - 1 ? index + 1 : null;
    else if (event.key === "ArrowUp") {
        if (index < columns) {
            if (focusBySelector(event.currentTarget, '[data-shortcuts-icon-back="true"]')) {
                event.preventDefault();
                event.stopPropagation();
            }
            return;
        }
        nextIndex = index - columns;
    } else if (event.key === "ArrowDown") {
        nextIndex = index + columns < totalChoices ? index + columns : null;
    }
    if (nextIndex !== null && focusIconChoice(event.currentTarget, nextIndex)) {
        event.preventDefault();
        event.stopPropagation();
    }
}


function cardActionButton(icon, description, onClick, disabled = false, extraProps = null) {
    const Button = DFL.DialogButton;
    return h(
        Button,
        {
            ...(extraProps ?? {}),
            disabled,
            onClick,
            onOKActionDescription: description,
            "aria-label": description,
            style: {
                flex: "1 1 0",
                minWidth: 0,
                height: 38,
                padding: 8,
                display: "grid",
                placeItems: "center"
            }
        },
        icon
    );
}

function PluginIdentity({ entry, muted = false, useSelectedIcon = false }) {
    return h(
        "div",
        {
            style: {
                display: "flex",
                alignItems: "center",
                gap: 12,
                width: "100%",
                maxWidth: "100%",
                minWidth: 0,
                overflow: "hidden",
                opacity: muted ? 0.65 : 1
            }
        },
        h(
            "div",
            {
                className: "shortcuts-identity-icon",
                style: {
                    width: 26,
                    height: 26,
                    flex: "0 0 26px",
                    display: "grid",
                    placeItems: "center",
                    fontSize: 22,
                    overflow: "hidden"
                }
            },
            h("style", null, `.shortcuts-identity-icon svg, .shortcuts-identity-icon img {
                display:block; width:100% !important; height:100% !important;
                max-width:100%; max-height:100%; object-fit:contain;
            }`),
            useSelectedIcon ? entryIcon(entry, 22) : entry.icon ?? h(MissingIcon, { size: 22 })
        ),
        h(
            "div",
            { style: { width: 0, flex: "1 1 auto", minWidth: 0, maxWidth: "100%", overflow: "hidden" } },
            h(
                "div",
                {
                    title: entry.name,
                    style: {
                        display: "block",
                        width: "100%",
                        maxWidth: "100%",
                        minWidth: 0,
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                        fontWeight: 600
                    }
                },
                entry.name
            ),
            !entry.available
                ? h(
                    "div",
                    {
                        style: {
                            width: "100%",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                            fontSize: 12,
                            opacity: 0.72,
                            marginTop: 2
                        }
                    },
                    entry.disabled ? text("disabled") : text("unavailable")
                )
                : null
        )
    );
}

function ActiveTabCard({ entry, index, total, runtime, onChooseIcon }) {
    const Focusable = DFL.Focusable ?? "div";
    const NameButton = DFL.DialogButton;
    const customizable = entry.kind === "shortcut" && typeof onChooseIcon === "function";
    const actions = [
        {
            key: "up",
            icon: h(UpIcon, { size: 18 }),
            description: text("moveUp"),
            disabled: index === 0,
            onClick: () => runtime.moveTab(entry.key, -1)
        },
        {
            key: "down",
            icon: h(DownIcon, { size: 18 }),
            description: text("moveDown"),
            disabled: index === total - 1,
            onClick: () => runtime.moveTab(entry.key, 1)
        }
    ];
    if (entry.kind === "steam") {
        actions.push({
            key: "visibility",
            icon: h(CustomIcon, { id: entry.hidden ? "eye-off-outline" : "eye", size: 18 }),
            description: text(entry.hidden ? "add" : "remove"),
            disabled: false,
            onClick: () => runtime.toggleNativeTab(entry.key)
        });
    }
    if (entry.kind === "shortcut") {
        actions.push({
            key: "remove",
            icon: h(TrashIcon, { size: 18 }),
            description: text("remove"),
            disabled: false,
            onClick: () => runtime.remove(entry.pluginName || entry.name)
        });
    }
    return h(
        DFL.PanelSectionRow,
        { key: entry.key },
        h(
            "div",
            {
                style: {
                    width: "100%",
                    maxWidth: "100%",
                    minWidth: 0,
                    marginBottom: 12,
                    padding: 10,
                    borderRadius: 9,
                    background: "rgba(255,255,255,0.055)",
                    boxSizing: "border-box",
                    overflow: "hidden"
                }
            },
            h(
                Focusable,
                {
                    "flow-children": "column",
                    style: { width: "100%", minWidth: 0, maxWidth: "100%" }
                },
                h(
                    NameButton,
                    {
                        onClick: customizable ? onChooseIcon : () => undefined,
                        onOKActionDescription: customizable ? text("chooseIcon") : entry.name,
                        "aria-label": customizable ? `${text("chooseIcon")}: ${entry.name}` : entry.name,
                        style: {
                            display: "block",
                            width: "100%",
                            maxWidth: "100%",
                            minWidth: 0,
                            height: 46,
                            padding: "0 12px",
                            marginBottom: 9,
                            overflow: "hidden",
                            boxSizing: "border-box"
                        }
                    },
                    h(
                        "div",
                        { style: { width: "100%", minWidth: 0, maxWidth: "100%", overflow: "hidden" } },
                        h(PluginIdentity, { entry, muted: !entry.available, useSelectedIcon: true })
                    )
                ),
                h(
                    Focusable,
                    {
                        "flow-children": "row",
                        style: { display: "flex", gap: 8, width: "100%", minWidth: 0 }
                    },
                    ...actions.map((action) => cardActionButton(
                        action.icon,
                        action.description,
                        action.onClick,
                        action.disabled,
                        { key: action.key }
                    ))
                )
            )
        )
    );
}

function AvailableRow({ entry, runtime, rowIndex, selectedCount, totalRows }) {
    return h(
        DFL.PanelSectionRow,
        { key: entry.name },
        h(
            "div",
            {
                style: {
                    width: "100%",
                    maxWidth: "100%",
                    minWidth: 0,
                    marginBottom: 10,
                    overflow: "hidden"
                }
            },
            h(
                DFL.ButtonItem,
                {
                    "data-shortcuts-main-row": String(rowIndex),
                    "data-shortcuts-main-control": "available",
                    onKeyDown: (event) => handleAvailableNav(event, rowIndex, selectedCount, totalRows),
                    layout: "below",
                    onClick: () => runtime.add(entry.name),
                    bottomSeparator: "none",
                    style: {
                        width: "100%",
                        maxWidth: "100%",
                        minWidth: 0,
                        overflow: "hidden",
                        boxSizing: "border-box"
                    }
                },
                h(
                    "div",
                    {
                        style: {
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "space-between",
                            gap: 12,
                            width: "100%",
                            maxWidth: "100%",
                            minWidth: 0,
                            overflow: "hidden"
                        }
                    },
                    h("div", { style: { width: 0, flex: "1 1 auto", minWidth: 0, maxWidth: "calc(100% - 34px)", overflow: "hidden" } }, h(PluginIdentity, { entry })),
                    h("div", { style: { flex: "0 0 22px", width: 22, fontSize: 20, display: "grid", placeItems: "center" } }, h(PlusIcon, { size: 20 }))
                )
            )
        )
    );
}

function EmptyRow({ children }) {
    return h(
        DFL.PanelSectionRow,
        null,
        h("div", { style: { opacity: 0.7, lineHeight: 1.4, padding: "6px 0 10px" } }, children)
    );
}

function IconPicker({ entry, runtime, onBack }) {
    const Focusable = DFL.Focusable ?? "div";
    const Button = DFL.DialogButton;
    const TextField = DFL.TextField;
    const [query, setQuery] = React.useState("");
    const selected = entry.iconChoice ?? "original";
    const normalizedQuery = query.trim().toLowerCase().replace(/\.svg$/i, "");
    const filteredCategories = ICON_CATEGORIES.map((category) => ({
        ...category,
        icons: normalizedQuery
            ? category.icons.filter((id) => iconMatchesQuery(id, normalizedQuery))
            : category.icons
    })).filter((category) => category.icons.length > 0);
    const visibleIds = filteredCategories.flatMap((category) => category.icons);
    const totalChoices = visibleIds.length + 1;
    let globalIndex = 1;

    const originalSection = h(
        DFL.PanelSection,
        { title: categoryText("original") },
        h(
            DFL.PanelSectionRow,
            null,
            h(
                Focusable,
                { "flow-children": "grid", style: { display: "grid", gridTemplateColumns: "repeat(4, minmax(0, 1fr))", gap: 10, width: "100%", minWidth: 0, paddingBottom: 8 } },
                h(
                    Button,
                    {
                        "data-shortcuts-icon-index": "0",
                        onKeyDown: (event) => {
                            if (event.key === "ArrowUp") {
                                if (focusBySelector(event.currentTarget, '[data-shortcuts-icon-search="true"]')) { event.preventDefault(); event.stopPropagation(); }
                                return;
                            }
                            handleIconPickerNav(event, 0, totalChoices, 4);
                        },
                        onClick: () => { runtime.setIcon(entry.name, "original"); onBack(); },
                        onOKActionDescription: text("originalIcon"),
                        "aria-label": text("originalIcon"),
                        style: { minWidth: 0, width: "100%", height: 64, padding: 8, display: "grid", placeItems: "center", overflow: "hidden", outline: selected === "original" ? "2px solid currentColor" : "none", outlineOffset: -3 }
                    },
                    entry.icon ?? h(MissingIcon, { size: 28 })
                )
            )
        )
    );

    const categorySections = filteredCategories.map((category) => {
        const items = category.icons.map((id) => {
            const index = globalIndex++;
            return h(
                Button,
                {
                    key: id,
                    "data-shortcuts-icon-index": String(index),
                    onKeyDown: (event) => handleIconPickerNav(event, index, totalChoices, 4),
                    onClick: () => { runtime.setIcon(entry.name, id); onBack(); },
                    onOKActionDescription: text("selectIcon", { icon: id }),
                    "aria-label": id,
                    title: id,
                    style: { minWidth: 0, width: "100%", height: 64, padding: 8, display: "grid", placeItems: "center", overflow: "hidden", outline: selected === id ? "2px solid currentColor" : "none", outlineOffset: -3 }
                },
                h(CustomIcon, { id, size: 28 })
            );
        });
        return h(
            DFL.PanelSection,
            { key: category.id, title: categoryText(category.id) },
            h(DFL.PanelSectionRow, null, h(Focusable, { "flow-children": "grid", style: { display: "grid", gridTemplateColumns: "repeat(4, minmax(0, 1fr))", gap: 10, width: "100%", minWidth: 0, paddingBottom: 8 } }, items))
        );
    });

    const searchSection = h(
        DFL.PanelSection,
        { title: searchText("label") },
        h(
            DFL.PanelSectionRow,
            null,
            TextField
                ? h(TextField, {
                    "data-shortcuts-icon-search": "true",
                    value: query,
                    placeholder: searchText("placeholder"),
                    bShowClearAction: true,
                    bAlwaysShowClearAction: true,
                    onChange: (event) => setQuery(event.currentTarget.value),
                    onKeyDown: (event) => {
                        if (event.key === "ArrowUp") {
                            if (focusBySelector(event.currentTarget, '[data-shortcuts-icon-back="true"]')) { event.preventDefault(); event.stopPropagation(); }
                        } else if (event.key === "ArrowDown") {
                            if (focusIconChoice(event.currentTarget, 0)) { event.preventDefault(); event.stopPropagation(); }
                        }
                    },
                    style: { width: "100%", maxWidth: "100%", minWidth: 0 }
                })
                : h("input", { "data-shortcuts-icon-search": "true", value: query, placeholder: searchText("placeholder"), onChange: (event) => setQuery(event.currentTarget.value), style: { width: "100%", boxSizing: "border-box" } })
        )
    );

    const emptySearch = normalizedQuery && visibleIds.length === 0
        ? h(DFL.PanelSection, { title: searchText("label") }, h(EmptyRow, null, searchText("empty")))
        : null;

    return h(
        Focusable,
        { onCancelButton: onBack, onCancel: onBack, "flow-children": "column", style: { width: "100%", minWidth: 0, paddingBottom: 18 } },
        h(
            "div",
            { style: { display: "flex", alignItems: "center", gap: 10, padding: "0 16px 14px", width: "100%", maxWidth: "100%", minWidth: 0, overflow: "hidden", boxSizing: "border-box" } },
            h(Button, {
                "data-shortcuts-icon-back": "true",
                onKeyDown: (event) => { if (event.key === "ArrowDown" && focusBySelector(event.currentTarget, '[data-shortcuts-icon-search="true"]')) { event.preventDefault(); event.stopPropagation(); } },
                onClick: onBack,
                onOKActionDescription: text("back"),
                "aria-label": text("back"),
                style: { width: 42, minWidth: 42, height: 36, padding: 8, display: "grid", placeItems: "center" }
            }, h(BackIcon, { size: 18 })),
            h("div", { title: entry.name, style: { width: 0, minWidth: 0, flex: "1 1 auto", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", fontWeight: 600 } }, text("iconPickerTitle", { name: entry.name }))
        ),
        searchSection,
        originalSection,
        ...categorySections,
        emptySearch
    );
}

function ShortcutsSettings({ runtime }) {
    const subscribe = React.useCallback((listener) => runtime.subscribe(listener), [runtime]);
    const getSnapshot = React.useCallback(() => runtime.getSnapshot(), [runtime]);
    const snapshot = React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
    const [pickerName, setPickerName] = React.useState(null);
    const byName = new Map(snapshot.entries.map((entry) => [entry.name, entry]));
    const activeEntries = snapshot.tabs ?? [];
    const availableEntries = snapshot.entries.filter((entry) => entry.available && !entry.selected);
    const selectedCount = activeEntries.length;
    const totalRows = activeEntries.length + availableEntries.length;
    const pickerEntry = pickerName
        ? byName.get(pickerName) ?? activeEntries.find((entry) => entry.pluginName === pickerName)
        : null;

    React.useEffect(() => {
        if (pickerName && !snapshot.selected.includes(pickerName)) setPickerName(null);
    }, [pickerName, snapshot.fingerprint]);

    if (pickerEntry) {
        return h(IconPicker, { entry: pickerEntry, runtime, onBack: () => setPickerName(null) });
    }

    const Focusable = DFL.Focusable ?? "div";
    return h(
        Focusable,
        { "flow-children": "grid", style: { paddingBottom: 18, width: "100%", minWidth: 0 } },
        h(
            DFL.PanelSection,
            { title: text("selectedTitle") },
            activeEntries.length === 0
                ? h(EmptyRow, null, text("emptySelected"))
                : activeEntries.map((entry, index) => h(ActiveTabCard, {
                    key: entry.key,
                    entry,
                    index,
                    total: activeEntries.length,
                    runtime,
                    rowIndex: index,
                    selectedCount,
                    totalRows,
                    onChooseIcon: entry.kind === "shortcut"
                        ? () => setPickerName(entry.pluginName || entry.name)
                        : null
                }))
        ),
        h(
            DFL.PanelSection,
            { title: text("availableTitle") },
            availableEntries.length === 0
                ? h(EmptyRow, null, text("emptyAvailable"))
                : availableEntries.map((entry, index) => h(AvailableRow, { key: entry.name, entry, runtime, rowIndex: selectedCount + index, selectedCount, totalRows }))
        )
    );
}


export function QamShortcutsView({runtime,locale}) { language=locale.split(/[-_]/)[0];return h(ShortcutsSettings,{runtime}); }


export function qamNativeLabel(key, fallback, locale) {
 if (!/^Steam \d+$/.test(fallback)) return fallback;
 const labels=NATIVE_TAB_LABELS[locale]??NATIVE_TAB_LABELS[locale?.split(/[-_]/)[0]]??NATIVE_TAB_LABELS.en;
 return labels[Number(key.replace("steam:",""))]??fallback;
}
