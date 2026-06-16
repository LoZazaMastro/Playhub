using System;
using System.Collections.Generic;

namespace PlayhubSetup;

/// <summary>
/// Localizzazione dell'installer. Default: inglese per tutti.
/// Ordine delle lingue negli array: en, it, es, fr, de, pt, uk, zh, ja, ko, hi, ru.
/// </summary>
public static class Loc
{
    public static string Lang = "en";

    public static readonly (string Code, string Native)[] Languages =
    {
        ("en", "English"),
        ("it", "Italiano"),
        ("es", "Español"),
        ("fr", "Français"),
        ("de", "Deutsch"),
        ("pt", "Português"),
        ("uk", "Українська"),
        ("zh", "中文"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("hi", "हिन्दी"),
        ("ru", "Русский"),
    };

    private static readonly string[] Order =
        { "en", "it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru" };

    private static int Index()
    {
        var i = Array.IndexOf(Order, Lang);
        return i < 0 ? 0 : i;
    }

    public static string T(string key)
        => Strings.TryGetValue(key, out var values) && Index() < values.Length ? values[Index()] : key;

    private static readonly Dictionary<string, string[]> Strings = new()
    {
        ["Version"] = new[]
        { "version", "versione", "versión", "version", "Version", "versão", "версія", "版本", "バージョン", "버전", "संस्करण", "версия" },

        ["LangTitle"] = new[]
        { "Choose your language", "Scegli la lingua", "Elige el idioma", "Choisissez la langue", "Sprache wählen", "Escolha o idioma", "Виберіть мову", "选择语言", "言語を選択", "언어 선택", "भाषा चुनें", "Выберите язык" },

        ["LangSubtitle"] = new[]
        { "Select the language for Playhub and the installer.", "Seleziona la lingua di Playhub e dell'installer.", "Selecciona el idioma de Playhub y del instalador.", "Sélectionnez la langue de Playhub et de l'installateur.", "Wähle die Sprache für Playhub und das Installationsprogramm.", "Selecione o idioma do Playhub e do instalador.", "Виберіть мову Playhub та інсталятора.", "选择 Playhub 和安装程序的语言。", "Playhub とインストーラーの言語を選択してください。", "Playhub와 설치 프로그램의 언어를 선택하세요.", "Playhub और इंस्टॉलर की भाषा चुनें।", "Выберите язык Playhub и установщика." },

        ["Continue"] = new[]
        { "Continue", "Avanti", "Continuar", "Continuer", "Weiter", "Continuar", "Далі", "继续", "次へ", "계속", "जारी रखें", "Далее" },

        ["InstallTitle"] = new[]
        { "Install Playhub", "Installa Playhub", "Instalar Playhub", "Installer Playhub", "Playhub installieren", "Instalar o Playhub", "Встановити Playhub", "安装 Playhub", "Playhub をインストール", "Playhub 설치", "Playhub इंस्टॉल करें", "Установить Playhub" },

        ["InstallSubtitle"] = new[]
        { "It will be installed for your user.", "Verrà installato per il tuo utente.", "Se instalará para tu usuario.", "Il sera installé pour votre utilisateur.", "Wird für deinen Benutzer installiert.", "Será instalado para o seu usuário.", "Буде встановлено для вашого користувача.", "将为你的用户安装。", "あなたのユーザー用にインストールされます。", "현재 사용자에 맞춰 설치됩니다.", "यह आपके उपयोगकर्ता के लिए इंस्टॉल होगा।", "Будет установлено для вашего пользователя." },

        ["Folder"] = new[]
        { "FOLDER", "CARTELLA", "CARPETA", "DOSSIER", "ORDNER", "PASTA", "ПАПКА", "文件夹", "フォルダー", "폴더", "फ़ोल्डर", "ПАПКА" },

        ["Change"] = new[]
        { "Change", "Cambia", "Cambiar", "Modifier", "Ändern", "Alterar", "Змінити", "更改", "変更", "변경", "बदलें", "Изменить" },

        ["OptDesktop"] = new[]
        { "Create a desktop shortcut", "Crea un collegamento sul desktop", "Crear un acceso directo en el escritorio", "Créer un raccourci sur le bureau", "Verknüpfung auf dem Desktop erstellen", "Criar um atalho na área de trabalho", "Створити ярлик на робочому столі", "创建桌面快捷方式", "デスクトップにショートカットを作成", "바탕 화면에 바로 가기 만들기", "डेस्कटॉप शॉर्टकट बनाएं", "Создать ярлык на рабочем столе" },

        ["OptStartMenu"] = new[]
        { "Add to the Start menu", "Aggiungi al menu Start", "Añadir al menú Inicio", "Ajouter au menu Démarrer", "Zum Startmenü hinzufügen", "Adicionar ao menu Iniciar", "Додати до меню Пуск", "添加到开始菜单", "スタート メニューに追加", "시작 메뉴에 추가", "स्टार्ट मेनू में जोड़ें", "Добавить в меню «Пуск»" },

        ["OptLaunchEnd"] = new[]
        { "Launch Playhub when installation finishes", "Avvia Playhub al termine dell'installazione", "Iniciar Playhub al finalizar la instalación", "Lancer Playhub à la fin de l'installation", "Playhub nach Abschluss der Installation starten", "Iniciar o Playhub ao concluir a instalação", "Запустити Playhub після завершення встановлення", "安装完成后启动 Playhub", "インストール完了後に Playhub を起動", "설치 완료 후 Playhub 실행", "इंस्टॉलेशन पूरा होने पर Playhub चलाएं", "Запустить Playhub после завершения установки" },

        ["Cancel"] = new[]
        { "Cancel", "Annulla", "Cancelar", "Annuler", "Abbrechen", "Cancelar", "Скасувати", "取消", "キャンセル", "취소", "रद्द करें", "Отмена" },

        ["Install"] = new[]
        { "Install", "Installa", "Instalar", "Installer", "Installieren", "Instalar", "Встановити", "安装", "インストール", "설치", "इंस्टॉल करें", "Установить" },

        ["Installing"] = new[]
        { "Installing…", "Installazione in corso…", "Instalando…", "Installation en cours…", "Installation läuft…", "Instalando…", "Встановлення…", "正在安装…", "インストール中…", "설치 중…", "इंस्टॉल हो रहा है…", "Установка…" },

        ["Preparing"] = new[]
        { "Preparing…", "Preparazione…", "Preparando…", "Préparation…", "Vorbereitung…", "Preparando…", "Підготовка…", "正在准备…", "準備中…", "준비 중…", "तैयारी हो रही है…", "Подготовка…" },

        ["CopyingFiles"] = new[]
        { "Copying files…", "Copia dei file…", "Copiando archivos…", "Copie des fichiers…", "Dateien werden kopiert…", "Copiando arquivos…", "Копіювання файлів…", "正在复制文件…", "ファイルをコピー中…", "파일 복사 중…", "फ़ाइलें कॉपी हो रही हैं…", "Копирование файлов…" },

        ["CreatingShortcuts"] = new[]
        { "Creating shortcuts…", "Creazione collegamenti…", "Creando accesos directos…", "Création des raccourcis…", "Verknüpfungen werden erstellt…", "Criando atalhos…", "Створення ярликів…", "正在创建快捷方式…", "ショートカットを作成中…", "바로 가기 만드는 중…", "शॉर्टकट बन रहे हैं…", "Создание ярлыков…" },

        ["Registering"] = new[]
        { "Registering…", "Registrazione…", "Registrando…", "Enregistrement…", "Registrierung…", "Registrando…", "Реєстрація…", "正在注册…", "登録中…", "등록 중…", "पंजीकरण हो रहा है…", "Регистрация…" },

        ["DoneTitle"] = new[]
        { "Installation complete", "Installazione completata", "Instalación completada", "Installation terminée", "Installation abgeschlossen", "Instalação concluída", "Встановлення завершено", "安装完成", "インストール完了", "설치 완료", "इंस्टॉलेशन पूर्ण", "Установка завершена" },

        ["DoneSub"] = new[]
        { "Playhub is ready to use.", "Playhub è pronto all'uso.", "Playhub está listo para usar.", "Playhub est prêt à l'emploi.", "Playhub ist einsatzbereit.", "O Playhub está pronto para uso.", "Playhub готовий до використання.", "Playhub 已可使用。", "Playhub を使う準備ができました。", "Playhub를 사용할 준비가 되었습니다.", "Playhub उपयोग के लिए तैयार है।", "Playhub готов к использованию." },

        ["Launching"] = new[]
        { "Launching Playhub…", "Avvio di Playhub…", "Iniciando Playhub…", "Lancement de Playhub…", "Playhub wird gestartet…", "Iniciando o Playhub…", "Запуск Playhub…", "正在启动 Playhub…", "Playhub を起動中…", "Playhub 실행 중…", "Playhub शुरू हो रहा है…", "Запуск Playhub…" },

        ["Finish"] = new[]
        { "Finish", "Fine", "Finalizar", "Terminer", "Fertig", "Concluir", "Готово", "完成", "完了", "마침", "समाप्त", "Готово" },

        ["Close"] = new[]
        { "Close", "Chiudi", "Cerrar", "Fermer", "Schließen", "Fechar", "Закрити", "关闭", "閉じる", "닫기", "बंद करें", "Закрыть" },

        ["ChooseFolder"] = new[]
        { "Choose where to install Playhub", "Scegli dove installare Playhub", "Elige dónde instalar Playhub", "Choisissez où installer Playhub", "Wähle den Installationsort für Playhub", "Escolha onde instalar o Playhub", "Виберіть, куди встановити Playhub", "选择 Playhub 的安装位置", "Playhub のインストール先を選択", "Playhub 설치 위치 선택", "Playhub इंस्टॉल करने का स्थान चुनें", "Выберите папку для установки Playhub" },

        ["Error"] = new[]
        { "Something went wrong", "Operazione non riuscita", "Algo salió mal", "Une erreur s'est produite", "Etwas ist schiefgelaufen", "Algo deu errado", "Сталася помилка", "出现错误", "問題が発生しました", "문제가 발생했습니다", "कुछ गलत हो गया", "Произошла ошибка" },

        ["UninstallTitle"] = new[]
        { "Uninstall Playhub", "Disinstalla Playhub", "Desinstalar Playhub", "Désinstaller Playhub", "Playhub deinstallieren", "Desinstalar o Playhub", "Видалити Playhub", "卸载 Playhub", "Playhub をアンインストール", "Playhub 제거", "Playhub अनइंस्टॉल करें", "Удалить Playhub" },

        ["UninstallSubtitle"] = new[]
        { "Removes Playhub and its shortcuts for this user.", "Rimuove Playhub e i suoi collegamenti da questo utente.", "Elimina Playhub y sus accesos directos para este usuario.", "Supprime Playhub et ses raccourcis pour cet utilisateur.", "Entfernt Playhub und seine Verknüpfungen für diesen Benutzer.", "Remove o Playhub e seus atalhos para este usuário.", "Видаляє Playhub та його ярлики для цього користувача.", "为此用户移除 Playhub 及其快捷方式。", "このユーザーの Playhub とショートカットを削除します。", "이 사용자의 Playhub와 바로 가기를 제거합니다.", "इस उपयोगकर्ता के लिए Playhub और उसके शॉर्टकट हटाता है।", "Удаляет Playhub и его ярлыки для этого пользователя." },

        ["Uninstall"] = new[]
        { "Uninstall", "Disinstalla", "Desinstalar", "Désinstaller", "Deinstallieren", "Desinstalar", "Видалити", "卸载", "アンインストール", "제거", "अनइंस्टॉल करें", "Удалить" },

        ["Uninstalling"] = new[]
        { "Uninstalling…", "Disinstallazione in corso…", "Desinstalando…", "Désinstallation en cours…", "Deinstallation läuft…", "Desinstalando…", "Видалення…", "正在卸载…", "アンインストール中…", "제거 중…", "अनइंस्टॉल हो रहा है…", "Удаление…" },

        ["RemovingShortcuts"] = new[]
        { "Removing shortcuts…", "Rimozione collegamenti…", "Eliminando accesos directos…", "Suppression des raccourcis…", "Verknüpfungen werden entfernt…", "Removendo atalhos…", "Видалення ярликів…", "正在移除快捷方式…", "ショートカットを削除中…", "바로 가기 제거 중…", "शॉर्टकट हट रहे हैं…", "Удаление ярлыков…" },

        ["RemovingRegistration"] = new[]
        { "Removing registration…", "Rimozione registrazione…", "Eliminando registro…", "Suppression de l'enregistrement…", "Registrierung wird entfernt…", "Removendo registro…", "Видалення реєстрації…", "正在移除注册信息…", "登録を削除中…", "등록 제거 중…", "पंजीकरण हट रहा है…", "Удаление регистрации…" },

        ["RemovingFiles"] = new[]
        { "Removing files…", "Rimozione dei file…", "Eliminando archivos…", "Suppression des fichiers…", "Dateien werden entfernt…", "Removendo arquivos…", "Видалення файлів…", "正在移除文件…", "ファイルを削除中…", "파일 제거 중…", "फ़ाइलें हट रही हैं…", "Удаление файлов…" },

        ["Cleanup"] = new[]
        { "Final cleanup…", "Pulizia finale…", "Limpieza final…", "Nettoyage final…", "Abschließende Bereinigung…", "Limpeza final…", "Завершальне очищення…", "最终清理…", "最終クリーンアップ…", "마지막 정리 중…", "अंतिम सफ़ाई…", "Завершающая очистка…" },

        ["OptRemoveData"] = new[]
        { "Also remove my settings and data", "Rimuovi anche le mie impostazioni e i dati", "Eliminar también mis ajustes y datos", "Supprimer aussi mes réglages et données", "Auch meine Einstellungen und Daten entfernen", "Remover também minhas configurações e dados", "Також видалити мої налаштування та дані", "同时删除我的设置和数据", "設定とデータも削除する", "내 설정과 데이터도 제거", "मेरी सेटिंग्स और डेटा भी हटाएं", "Также удалить мои настройки и данные" },

        ["RemovingData"] = new[]
        { "Removing settings & data…", "Rimozione impostazioni e dati…", "Eliminando ajustes y datos…", "Suppression des réglages et données…", "Einstellungen und Daten werden entfernt…", "Removendo configurações e dados…", "Видалення налаштувань і даних…", "正在删除设置和数据…", "設定とデータを削除中…", "설정 및 데이터 제거 중…", "सेटिंग्स और डेटा हट रहे हैं…", "Удаление настроек и данных…" },

        ["UninstallDone"] = new[]
        { "Uninstall complete", "Disinstallazione completata", "Desinstalación completada", "Désinstallation terminée", "Deinstallation abgeschlossen", "Desinstalação concluída", "Видалення завершено", "卸载完成", "アンインストール完了", "제거 완료", "अनइंस्टॉल पूर्ण", "Удаление завершено" },

        ["UninstallDoneSub"] = new[]
        { "Playhub was removed for this user.", "Playhub è stato rimosso da questo utente.", "Playhub se eliminó para este usuario.", "Playhub a été supprimé pour cet utilisateur.", "Playhub wurde für diesen Benutzer entfernt.", "O Playhub foi removido para este usuário.", "Playhub видалено для цього користувача.", "已为此用户移除 Playhub。", "このユーザーの Playhub を削除しました。", "이 사용자의 Playhub가 제거되었습니다.", "इस उपयोगकर्ता के लिए Playhub हटा दिया गया।", "Playhub удалён для этого пользователя." },
    };
}
