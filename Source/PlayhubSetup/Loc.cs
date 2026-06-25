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
        { "Done!", "Fatto!", "¡Hecho!", "Terminé !", "Fertig!", "Concluído!", "Готово!", "完成！", "完了！", "완료!", "पूरा हुआ!", "Готово!" },

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

        ["OptRemoveUWPHook"] = new[]
        { "Also uninstall UWPHook", "Disinstalla anche UWPHook", "Desinstalar también UWPHook", "Désinstaller aussi UWPHook", "UWPHook ebenfalls deinstallieren", "Desinstalar também o UWPHook", "Також видалити UWPHook", "同时卸载 UWPHook", "UWPHook もアンインストールする", "UWPHook도 제거", "UWPHook को भी अनइंस्टॉल करें", "Также удалить UWPHook" },

        ["InstallingUWPHook"] = new[]
        { "Installing UWPHook…", "Installazione di UWPHook…", "Instalando UWPHook…", "Installation de UWPHook…", "UWPHook wird installiert…", "Instalando o UWPHook…", "Встановлення UWPHook…", "正在安装 UWPHook…", "UWPHook をインストール中…", "UWPHook 설치 중…", "UWPHook इंस्टॉल हो रहा है…", "Установка UWPHook…" },

        ["RemovingUWPHook"] = new[]
        { "Uninstalling UWPHook…", "Disinstallazione di UWPHook…", "Desinstalando UWPHook…", "Désinstallation de UWPHook…", "UWPHook wird deinstalliert…", "Desinstalando o UWPHook…", "Видалення UWPHook…", "正在卸载 UWPHook…", "UWPHook をアンインストール中…", "UWPHook 제거 중…", "UWPHook अनइंस्टॉल हो रहा है…", "Удаление UWPHook…" },

        ["FilesInUse"] = new[]
        { "Playhub is still running or some installation files are in use. Close Playhub and try again.", "Playhub è ancora in esecuzione o alcuni file di installazione sono in uso. Chiudi Playhub e riprova.", "Playhub sigue abierto o algunos archivos de instalación están en uso. Cierra Playhub e inténtalo de nuevo.", "Playhub est encore ouvert ou des fichiers d'installation sont utilisés. Fermez Playhub et réessayez.", "Playhub läuft noch oder Installationsdateien werden verwendet. Schließe Playhub und versuche es erneut.", "O Playhub ainda está aberto ou alguns arquivos de instalação estão em uso. Feche o Playhub e tente novamente.", "Playhub ще працює або деякі файли встановлення використовуються. Закрий Playhub і спробуй ще раз.", "Playhub 仍在运行，或某些安装文件正在使用。请关闭 Playhub 后重试。", "Playhub がまだ実行中か、インストールファイルが使用中です。Playhub を閉じて再試行してください。", "Playhub가 아직 실행 중이거나 설치 파일이 사용 중입니다. Playhub를 닫고 다시 시도하세요.", "Playhub अभी चल रहा है या कुछ इंस्टॉलेशन फ़ाइलें इस्तेमाल हो रही हैं। Playhub बंद करके फिर कोशिश करें।", "Playhub всё ещё запущен или файлы установки используются. Закрой Playhub и попробуй снова." },

        ["AccessDenied"] = new[]
        { "Windows denied access to the installation files. Try again or choose another folder.", "Windows ha negato l'accesso ai file di installazione. Riprova o scegli un'altra cartella.", "Windows ha denegado el acceso a los archivos de instalación. Inténtalo de nuevo o elige otra carpeta.", "Windows a refusé l'accès aux fichiers d'installation. Réessayez ou choisissez un autre dossier.", "Windows hat den Zugriff auf die Installationsdateien verweigert. Versuche es erneut oder wähle einen anderen Ordner.", "O Windows negou acesso aos arquivos de instalação. Tente novamente ou escolha outra pasta.", "Windows заборонила доступ до файлів встановлення. Спробуй ще раз або вибери іншу папку.", "Windows 拒绝访问安装文件。请重试或选择其他文件夹。", "Windows がインストールファイルへのアクセスを拒否しました。再試行するか別のフォルダーを選んでください。", "Windows가 설치 파일에 대한 액세스를 거부했습니다. 다시 시도하거나 다른 폴더를 선택하세요.", "Windows ने इंस्टॉलेशन फ़ाइलों तक पहुंच रोक दी। फिर कोशिश करें या कोई दूसरा फ़ोल्डर चुनें।", "Windows запретила доступ к файлам установки. Попробуй снова или выбери другую папку." },

        ["PackageError"] = new[]
        { "The Playhub installation package is missing or damaged. Download it again and retry.", "Il pacchetto di installazione di Playhub manca o è danneggiato. Scaricalo di nuovo e riprova.", "El paquete de instalación de Playhub no está o está dañado. Descárgalo de nuevo e inténtalo otra vez.", "Le paquet d'installation de Playhub est absent ou endommagé. Téléchargez-le à nouveau et réessayez.", "Das Playhub-Installationspaket fehlt oder ist beschädigt. Lade es erneut herunter und versuche es noch einmal.", "O pacote de instalação do Playhub está ausente ou danificado. Baixe-o novamente e tente de novo.", "Пакет встановлення Playhub відсутній або пошкоджений. Завантаж його знову та повтори спробу.", "Playhub 安装包缺失或已损坏。请重新下载后重试。", "Playhub のインストールパッケージが見つからないか破損しています。再ダウンロードして再試行してください。", "Playhub 설치 패키지가 없거나 손상되었습니다. 다시 다운로드한 뒤 시도하세요.", "Playhub इंस्टॉलेशन पैकेज गायब या खराब है। इसे फिर डाउनलोड करके कोशिश करें।", "Установочный пакет Playhub отсутствует или повреждён. Скачай его снова и повтори попытку." },

        ["UnexpectedError"] = new[]
        { "The operation could not be completed. Restart Windows and try again.", "Non è stato possibile completare l'operazione. Riavvia Windows e riprova.", "No se pudo completar la operación. Reinicia Windows e inténtalo de nuevo.", "L'opération n'a pas pu être terminée. Redémarrez Windows et réessayez.", "Der Vorgang konnte nicht abgeschlossen werden. Starte Windows neu und versuche es erneut.", "Não foi possível concluir a operação. Reinicie o Windows e tente novamente.", "Не вдалося завершити операцію. Перезапусти Windows і спробуй ще раз.", "无法完成操作。请重启 Windows 后重试。", "操作を完了できませんでした。Windows を再起動して再試行してください。", "작업을 완료할 수 없습니다. Windows를 다시 시작한 뒤 시도하세요.", "कार्रवाई पूरी नहीं हो सकी। Windows रीस्टार्ट करके फिर कोशिश करें।", "Не удалось завершить операцию. Перезапусти Windows и попробуй снова." },

        ["RemovingData"] = new[]
        { "Removing settings & data…", "Rimozione impostazioni e dati…", "Eliminando ajustes y datos…", "Suppression des réglages et données…", "Einstellungen und Daten werden entfernt…", "Removendo configurações e dados…", "Видалення налаштувань і даних…", "正在删除设置和数据…", "設定とデータを削除中…", "설정 및 데이터 제거 중…", "सेटिंग्स और डेटा हट रहे हैं…", "Удаление настроек и данных…" },

        ["UninstallDone"] = new[]
        { "Uninstall complete", "Disinstallazione completata", "Desinstalación completada", "Désinstallation terminée", "Deinstallation abgeschlossen", "Desinstalação concluída", "Видалення завершено", "卸载完成", "アンインストール完了", "제거 완료", "अनइंस्टॉल पूर्ण", "Удаление завершено" },

        ["UninstallDoneSub"] = new[]
        { "Playhub was removed for this user.", "Playhub è stato rimosso da questo utente.", "Playhub se eliminó para este usuario.", "Playhub a été supprimé pour cet utilisateur.", "Playhub wurde für diesen Benutzer entfernt.", "O Playhub foi removido para este usuário.", "Playhub видалено для цього користувача.", "已为此用户移除 Playhub。", "このユーザーの Playhub を削除しました。", "이 사용자의 Playhub가 제거되었습니다.", "इस उपयोगकर्ता के लिए Playhub हटा दिया गया।", "Playhub удалён для этого пользователя." },

        ["AgreeTitle"] = new[]
        { "Terms of Use", "Termini di utilizzo", "Términos de uso", "Conditions d'utilisation", "Nutzungsbedingungen", "Termos de uso", "Умови використання", "使用条款", "利用規約", "이용 약관", "उपयोग की शर्तें", "Условия использования" },

        ["AgreeSubtitle"] = new[]
        { "Read and accept to continue the installation.", "Leggi e accetta per continuare l'installazione.", "Lee y acepta para continuar la instalación.", "Lis et accepte pour continuer l'installation.", "Lies und akzeptiere, um die Installation fortzusetzen.", "Leia e aceite para continuar a instalação.", "Прочитай і прийми, щоб продовжити встановлення.", "阅读并接受以继续安装。", "お読みのうえ同意するとインストールを続行できます。", "읽고 동의하면 설치를 계속합니다.", "इंस्टॉलेशन जारी रखने के लिए पढ़ें और स्वीकार करें।", "Прочитайте и примите, чтобы продолжить установку." },

        ["Accept"] = new[]
        { "I accept", "Accetto", "Acepto", "J'accepte", "Ich stimme zu", "Aceito", "Приймаю", "我接受", "同意する", "동의합니다", "मैं स्वीकार करता हूँ", "Принимаю" },

        ["Decline"] = new[]
        { "I decline", "Rifiuto", "Rechazo", "Je refuse", "Ich lehne ab", "Recuso", "Відхиляю", "我拒绝", "同意しない", "거부합니다", "मैं अस्वीकार करता हूँ", "Отклоняю" },

        ["AgreeBody"] = new[]
        {
            "Playhub is free and open-source software, distributed under the MIT license. These terms apply to Playhub and all of its plugins.\n\nNO WARRANTY. The software is provided \"as is\" and \"as available\", without warranties of any kind, express or implied, including without limitation the warranties of merchantability, fitness for a particular purpose, and non-infringement.\n\nLIMITATION OF LIABILITY. To the maximum extent permitted by applicable law, the author shall in no event be liable for any direct, indirect, incidental, special, consequential or punitive damages, nor for loss of data, lost profits, malfunctions, account suspensions or bans, or problems with Windows, Steam or other services, arising from the use of or inability to use Playhub or its plugins. You use the software entirely at your own risk.\n\nINDEMNIFICATION. You agree to hold the author harmless from any third-party claim or demand arising from your use of the software or your breach of these terms or of third-party terms of service.\n\nNO AFFILIATION. Playhub is not affiliated with, sponsored by, or endorsed by Valve (Steam), Microsoft (Xbox), Epic Games, GOG, or the Decky Loader project. All trademarks belong to their respective owners.\n\nRESPONSIBLE USE. Playhub creates shortcuts and manages plugins and settings; games stay installed in their respective launchers. Some features change Windows or Steam settings (for example developer mode, shell switching, CEF debugging). You are solely responsible for complying with the terms of service of Steam and the other platforms you use.\n\nBy accepting, you declare that you have read, understood and accepted these terms. If you do not accept, the installation will not continue.",
            "Playhub è un software gratuito e open source, distribuito sotto licenza MIT. Questi termini valgono per Playhub e per tutti i suoi plugin.\n\nNESSUNA GARANZIA. Il software è fornito \"così com'è\" e \"come disponibile\", senza garanzie di alcun tipo, esplicite o implicite, incluse a titolo esemplificativo le garanzie di commerciabilità, idoneità a uno scopo specifico e non violazione di diritti di terzi.\n\nLIMITAZIONE DI RESPONSABILITÀ. Nella misura massima consentita dalla legge applicabile, l'autore non sarà in alcun caso responsabile per danni diretti, indiretti, incidentali, speciali, consequenziali o punitivi, né per perdite di dati, mancati guadagni, malfunzionamenti, sospensioni o blocchi di account o problemi con Windows, Steam o altri servizi, derivanti dall'uso o dall'impossibilità di usare Playhub o i suoi plugin. Usi il software a tuo esclusivo rischio.\n\nMANLEVA. Accetti di tenere indenne l'autore da qualsiasi pretesa o richiesta di terzi derivante dal tuo uso del software o dalla violazione di questi termini o dei termini di servizio di terze parti.\n\nASSENZA DI AFFILIAZIONE. Playhub non è affiliato, sponsorizzato né approvato da Valve (Steam), Microsoft (Xbox), Epic Games, GOG o dal progetto Decky Loader. Tutti i marchi appartengono ai rispettivi proprietari.\n\nUSO RESPONSABILE. Playhub crea scorciatoie e gestisce plugin e impostazioni; i giochi restano installati nei rispettivi launcher. Alcune funzioni modificano impostazioni di Windows o di Steam (ad esempio modalità sviluppatore, cambio shell, debug CEF). Sei l'unico responsabile del rispetto dei termini di servizio di Steam e delle altre piattaforme che utilizzi.\n\nAccettando dichiari di aver letto, compreso e accettato questi termini. Se non accetti, l'installazione non proseguirà.",
            "Playhub es un software gratuito y de código abierto, distribuido bajo la licencia MIT. Estos términos se aplican a Playhub y a todos sus plugins.\n\nSIN GARANTÍA. El software se proporciona \"tal cual\" y \"según disponibilidad\", sin garantías de ningún tipo, expresas o implícitas, incluidas, entre otras, las garantías de comerciabilidad, idoneidad para un fin determinado y no infracción.\n\nLIMITACIÓN DE RESPONSABILIDAD. En la máxima medida permitida por la ley aplicable, el autor no será responsable en ningún caso de daños directos, indirectos, incidentales, especiales, consecuentes o punitivos, ni de pérdida de datos, lucro cesante, fallos, suspensiones o bloqueos de cuenta, o problemas con Windows, Steam u otros servicios, derivados del uso o de la imposibilidad de usar Playhub o sus plugins. Usas el software bajo tu exclusivo riesgo.\n\nINDEMNIZACIÓN. Aceptas mantener indemne al autor frente a cualquier reclamación de terceros derivada de tu uso del software o del incumplimiento de estos términos o de los términos de servicio de terceros.\n\nSIN AFILIACIÓN. Playhub no está afiliado, patrocinado ni respaldado por Valve (Steam), Microsoft (Xbox), Epic Games, GOG ni el proyecto Decky Loader. Todas las marcas pertenecen a sus respectivos propietarios.\n\nUSO RESPONSABLE. Playhub crea accesos directos y gestiona plugins y ajustes; los juegos permanecen instalados en sus respectivos launchers. Algunas funciones modifican ajustes de Windows o de Steam (por ejemplo, modo de desarrollador, cambio de shell, depuración CEF). Eres el único responsable de cumplir los términos de servicio de Steam y de las demás plataformas que uses.\n\nAl aceptar, declaras haber leído, comprendido y aceptado estos términos. Si no aceptas, la instalación no continuará.",
            "Playhub est un logiciel gratuit et open source, distribué sous licence MIT. Ces conditions s'appliquent à Playhub et à tous ses plugins.\n\nAUCUNE GARANTIE. Le logiciel est fourni \"tel quel\" et \"selon disponibilité\", sans garantie d'aucune sorte, expresse ou implicite, y compris notamment les garanties de qualité marchande, d'adéquation à un usage particulier et d'absence de contrefaçon.\n\nLIMITATION DE RESPONSABILITÉ. Dans toute la mesure permise par la loi applicable, l'auteur ne pourra en aucun cas être tenu responsable de dommages directs, indirects, accessoires, spéciaux, consécutifs ou punitifs, ni de pertes de données, de pertes de profits, de dysfonctionnements, de suspensions ou de bannissements de compte, ou de problèmes avec Windows, Steam ou d'autres services, résultant de l'utilisation ou de l'impossibilité d'utiliser Playhub ou ses plugins. Vous utilisez le logiciel à vos propres risques.\n\nGARANTIE D'INDEMNISATION. Vous acceptez de dégager l'auteur de toute responsabilité face à toute réclamation de tiers résultant de votre utilisation du logiciel ou de la violation de ces conditions ou des conditions de service de tiers.\n\nAUCUNE AFFILIATION. Playhub n'est ni affilié, ni sponsorisé, ni approuvé par Valve (Steam), Microsoft (Xbox), Epic Games, GOG ou le projet Decky Loader. Toutes les marques appartiennent à leurs propriétaires respectifs.\n\nUTILISATION RESPONSABLE. Playhub crée des raccourcis et gère des plugins et des réglages ; les jeux restent installés dans leurs launchers respectifs. Certaines fonctions modifient des réglages de Windows ou de Steam (par exemple le mode développeur, le changement de shell, le débogage CEF). Vous êtes seul responsable du respect des conditions de service de Steam et des autres plateformes que vous utilisez.\n\nEn acceptant, vous déclarez avoir lu, compris et accepté ces conditions. Si vous n'acceptez pas, l'installation ne se poursuivra pas.",
            "Playhub ist eine kostenlose Open-Source-Software, die unter der MIT-Lizenz vertrieben wird. Diese Bedingungen gelten für Playhub und alle seine Plugins.\n\nKEINE GEWÄHRLEISTUNG. Die Software wird \"wie besehen\" und \"wie verfügbar\" bereitgestellt, ohne jegliche ausdrückliche oder stillschweigende Gewährleistung, einschließlich, aber nicht beschränkt auf die Gewährleistung der Marktgängigkeit, der Eignung für einen bestimmten Zweck und der Nichtverletzung von Rechten.\n\nHAFTUNGSBESCHRÄNKUNG. Soweit nach geltendem Recht zulässig, haftet der Autor in keinem Fall für direkte, indirekte, beiläufige, besondere, Folge- oder Strafschäden, noch für Datenverlust, entgangenen Gewinn, Fehlfunktionen, Kontosperrungen oder -bannungen oder Probleme mit Windows, Steam oder anderen Diensten, die sich aus der Nutzung oder der Unmöglichkeit der Nutzung von Playhub oder seinen Plugins ergeben. Du nutzt die Software auf eigenes Risiko.\n\nFREISTELLUNG. Du erklärst dich bereit, den Autor von allen Ansprüchen Dritter freizustellen, die sich aus deiner Nutzung der Software oder aus deinem Verstoß gegen diese Bedingungen oder gegen Nutzungsbedingungen Dritter ergeben.\n\nKEINE ZUGEHÖRIGKEIT. Playhub ist nicht mit Valve (Steam), Microsoft (Xbox), Epic Games, GOG oder dem Decky-Loader-Projekt verbunden, wird von diesen nicht gesponsert oder unterstützt. Alle Marken gehören ihren jeweiligen Eigentümern.\n\nVERANTWORTUNGSVOLLE NUTZUNG. Playhub erstellt Verknüpfungen und verwaltet Plugins und Einstellungen; Spiele bleiben in ihren jeweiligen Launchern installiert. Einige Funktionen ändern Windows- oder Steam-Einstellungen (zum Beispiel Entwicklermodus, Shell-Wechsel, CEF-Debugging). Du bist allein dafür verantwortlich, die Nutzungsbedingungen von Steam und der anderen von dir genutzten Plattformen einzuhalten.\n\nMit dem Akzeptieren erklärst du, diese Bedingungen gelesen, verstanden und akzeptiert zu haben. Wenn du nicht zustimmst, wird die Installation nicht fortgesetzt.",
            "O Playhub é um software gratuito e de código aberto, distribuído sob a licença MIT. Estes termos aplicam-se ao Playhub e a todos os seus plugins.\n\nSEM GARANTIA. O software é fornecido \"como está\" e \"conforme disponível\", sem garantias de qualquer tipo, expressas ou implícitas, incluindo, sem limitação, as garantias de comercialização, adequação a um fim específico e não violação.\n\nLIMITAÇÃO DE RESPONSABILIDADE. Na máxima extensão permitida pela lei aplicável, o autor não será, em nenhuma hipótese, responsável por danos diretos, indiretos, incidentais, especiais, consequenciais ou punitivos, nem por perda de dados, lucros cessantes, falhas, suspensões ou banimentos de conta, ou problemas com Windows, Steam ou outros serviços, decorrentes do uso ou da impossibilidade de uso do Playhub ou dos seus plugins. Você usa o software por sua conta e risco.\n\nINDENIZAÇÃO. Você concorda em isentar o autor de qualquer reclamação de terceiros decorrente do seu uso do software ou da violação destes termos ou dos termos de serviço de terceiros.\n\nSEM AFILIAÇÃO. O Playhub não é afiliado, patrocinado nem endossado pela Valve (Steam), Microsoft (Xbox), Epic Games, GOG ou pelo projeto Decky Loader. Todas as marcas pertencem aos seus respectivos proprietários.\n\nUSO RESPONSÁVEL. O Playhub cria atalhos e gerencia plugins e configurações; os jogos permanecem instalados nos seus respectivos launchers. Algumas funções alteram configurações do Windows ou do Steam (por exemplo, modo de desenvolvedor, troca de shell, depuração CEF). Você é o único responsável por cumprir os termos de serviço do Steam e das demais plataformas que utiliza.\n\nAo aceitar, você declara ter lido, compreendido e aceitado estes termos. Se não aceitar, a instalação não continuará.",
            "Playhub — це безкоштовне програмне забезпечення з відкритим кодом, що поширюється за ліцензією MIT. Ці умови стосуються Playhub і всіх його плагінів.\n\nБЕЗ ГАРАНТІЙ. Програма надається \"як є\" та \"як доступно\", без жодних гарантій, явних чи неявних, зокрема гарантій придатності для продажу, придатності для певної мети та невпорушення прав.\n\nОБМЕЖЕННЯ ВІДПОВІДАЛЬНОСТІ. У максимальному обсязі, дозволеному чинним законодавством, автор за жодних обставин не несе відповідальності за прямі, непрямі, випадкові, особливі, побічні чи штрафні збитки, а також за втрату даних, упущену вигоду, збої, призупинення чи блокування облікових записів або проблеми з Windows, Steam чи іншими сервісами, що виникли внаслідок використання або неможливості використання Playhub чи його плагінів. Ви використовуєте програму виключно на власний ризик.\n\nВІДШКОДУВАННЯ. Ви погоджуєтеся звільнити автора від відповідальності за будь-які претензії третіх осіб, що виникають через ваше використання програми або порушення цих умов чи умов сторонніх сервісів.\n\nВІДСУТНІСТЬ АФІЛІАЦІЇ. Playhub не пов'язаний із Valve (Steam), Microsoft (Xbox), Epic Games, GOG чи проєктом Decky Loader, не спонсорується та не схвалюється ними. Усі торгові марки належать їхнім власникам.\n\nВІДПОВІДАЛЬНЕ ВИКОРИСТАННЯ. Playhub створює ярлики та керує плагінами й налаштуваннями; ігри залишаються встановленими у відповідних лаунчерах. Деякі функції змінюють налаштування Windows чи Steam (наприклад, режим розробника, зміну оболонки, налагодження CEF). Ви несете одноосібну відповідальність за дотримання умов обслуговування Steam та інших платформ, якими користуєтесь.\n\nПриймаючи, ви підтверджуєте, що прочитали, зрозуміли та прийняли ці умови. Якщо ви не приймаєте, встановлення не продовжиться.",
            "Playhub 是一款免费的开源软件，依据 MIT 许可证发布。本条款适用于 Playhub 及其所有插件。\n\n无担保。本软件按\"现状\"和\"现有\"方式提供，不提供任何明示或暗示的担保，包括但不限于适销性、特定用途适用性和不侵权的担保。\n\n责任限制。在适用法律允许的最大范围内，作者在任何情况下均不对任何直接、间接、附带、特殊、后果性或惩罚性损害负责，也不对因使用或无法使用 Playhub 或其插件而导致的数据丢失、利润损失、故障、账号封禁或冻结，或与 Windows、Steam 或其他服务相关的问题负责。你需自行承担使用本软件的全部风险。\n\n赔偿。你同意使作者免于因你使用本软件或违反本条款或第三方服务条款而引起的任何第三方索赔。\n\n无隶属关系。Playhub 与 Valve（Steam）、Microsoft（Xbox）、Epic Games、GOG 或 Decky Loader 项目均无隶属、赞助或认可关系。所有商标归各自所有者所有。\n\n负责任地使用。Playhub 只创建快捷方式并管理插件和设置；游戏仍安装在各自的启动器中。部分功能会更改 Windows 或 Steam 设置（例如开发者模式、外壳切换、CEF 调试）。你需自行负责遵守 Steam 及你使用的其他平台的服务条款。\n\n点击接受即表示你已阅读、理解并接受本条款。如果你不接受，安装将不会继续。",
            "Playhub は MIT ライセンスで配布される無料のオープンソースソフトウェアです。本規約は Playhub およびそのすべてのプラグインに適用されます。\n\n保証なし。本ソフトウェアは「現状のまま」かつ「提供可能な範囲で」提供され、商品性、特定目的への適合性、権利非侵害の保証を含め、明示・黙示を問わずいかなる保証もありません。\n\n責任の制限。適用法で認められる最大限の範囲で、作者は、Playhub またはそのプラグインの使用もしくは使用不能から生じる直接的・間接的・付随的・特別・結果的・懲罰的損害、ならびにデータ損失、逸失利益、不具合、アカウントの停止・凍結、Windows・Steam その他サービスに関する問題について、一切責任を負いません。本ソフトウェアの利用は自己責任で行ってください。\n\n補償。あなたは、本ソフトウェアの利用、または本規約もしくは第三者の利用規約への違反から生じる第三者からの請求について、作者を免責することに同意します。\n\n無関係。Playhub は Valve（Steam）、Microsoft（Xbox）、Epic Games、GOG、Decky Loader プロジェクトと提携・後援・承認の関係はありません。すべての商標は各所有者に帰属します。\n\n責任ある利用。Playhub はショートカットを作成し、プラグインと設定を管理します。ゲームは各ランチャーにインストールされたままです。一部の機能は Windows や Steam の設定（開発者モード、シェル切り替え、CEF デバッグなど）を変更します。Steam や利用する他のプラットフォームの利用規約の順守は、すべてあなたの責任です。\n\n同意することで、本規約を読み、理解し、受け入れたものとみなされます。同意しない場合、インストールは続行されません。",
            "Playhub은 MIT 라이선스로 배포되는 무료 오픈 소스 소프트웨어입니다. 본 약관은 Playhub와 그 모든 플러그인에 적용됩니다.\n\n무보증. 본 소프트웨어는 \"있는 그대로\" 및 \"이용 가능한 상태로\" 제공되며, 상품성, 특정 목적 적합성, 권리 비침해 보증을 포함하되 이에 국한되지 않는 명시적이거나 묵시적인 어떠한 보증도 제공하지 않습니다.\n\n책임의 제한. 관련 법률이 허용하는 최대 범위 내에서, 작성자는 Playhub 또는 그 플러그인의 사용 또는 사용 불능으로 인해 발생하는 직접적, 간접적, 부수적, 특별, 결과적 또는 징벌적 손해, 데이터 손실, 이익 손실, 오작동, 계정 정지 또는 차단, Windows, Steam 또는 기타 서비스 관련 문제에 대해 어떠한 경우에도 책임지지 않습니다. 본 소프트웨어 사용에 따른 모든 위험은 사용자 본인이 부담합니다.\n\n면책. 사용자는 본 소프트웨어 사용 또는 본 약관이나 제3자 서비스 약관 위반으로 발생하는 제3자의 청구로부터 작성자를 면책하는 데 동의합니다.\n\n비제휴. Playhub는 Valve(Steam), Microsoft(Xbox), Epic Games, GOG 또는 Decky Loader 프로젝트와 제휴, 후원, 보증 관계가 없습니다. 모든 상표는 각 소유자에게 귀속됩니다.\n\n책임 있는 사용. Playhub는 바로 가기를 만들고 플러그인과 설정을 관리합니다. 게임은 각 런처에 설치된 상태로 유지됩니다. 일부 기능은 Windows 또는 Steam 설정(예: 개발자 모드, 셸 전환, CEF 디버깅)을 변경합니다. Steam 및 사용하는 기타 플랫폼의 서비스 약관 준수는 전적으로 사용자 본인의 책임입니다.\n\n동의하면 본 약관을 읽고 이해하였으며 수락한 것으로 간주됩니다. 동의하지 않으면 설치가 계속되지 않습니다.",
            "Playhub एक मुफ़्त और ओपन-सोर्स सॉफ़्टवेयर है, जो MIT लाइसेंस के तहत वितरित किया जाता है। ये शर्तें Playhub और इसके सभी प्लगइन पर लागू होती हैं।\n\nकोई वारंटी नहीं। सॉफ़्टवेयर \"जैसा है\" और \"जैसा उपलब्ध है\" के आधार पर प्रदान किया जाता है, बिना किसी प्रकार की स्पष्ट या निहित वारंटी के, जिसमें बिक्री-योग्यता, किसी विशेष उद्देश्य हेतु उपयुक्तता और गैर-उल्लंघन की वारंटियाँ शामिल हैं पर इन्हीं तक सीमित नहीं।\n\nदायित्व की सीमा। लागू कानून द्वारा अनुमत अधिकतम सीमा तक, लेखक किसी भी स्थिति में प्रत्यक्ष, अप्रत्यक्ष, आकस्मिक, विशेष, परिणामी या दंडात्मक क्षति, और न ही डेटा हानि, लाभ हानि, खराबी, खाता निलंबन या प्रतिबंध, या Windows, Steam या अन्य सेवाओं से जुड़ी समस्याओं के लिए ज़िम्मेदार होगा, जो Playhub या उसके प्लगइन के उपयोग या उपयोग न कर पाने से उत्पन्न हों। आप सॉफ़्टवेयर का उपयोग पूरी तरह अपने जोखिम पर करते हैं।\n\nक्षतिपूर्ति। आप सहमत हैं कि आपके सॉफ़्टवेयर उपयोग या इन शर्तों या किसी तीसरे पक्ष की सेवा शर्तों के उल्लंघन से उत्पन्न किसी भी तीसरे-पक्ष के दावे से लेखक को हानिरहित रखेंगे।\n\nकोई संबद्धता नहीं। Playhub, Valve (Steam), Microsoft (Xbox), Epic Games, GOG या Decky Loader परियोजना से संबद्ध, प्रायोजित या अनुमोदित नहीं है। सभी ट्रेडमार्क उनके संबंधित स्वामियों के हैं।\n\nज़िम्मेदार उपयोग। Playhub शॉर्टकट बनाता है और प्लगइन व सेटिंग्स प्रबंधित करता है; गेम अपने-अपने लॉन्चर में इंस्टॉल रहते हैं। कुछ सुविधाएँ Windows या Steam सेटिंग्स बदलती हैं (जैसे डेवलपर मोड, शेल स्विचिंग, CEF डिबगिंग)। Steam और आपके द्वारा उपयोग किए जाने वाले अन्य प्लेटफ़ॉर्म की सेवा शर्तों का पालन करना पूरी तरह आपकी ज़िम्मेदारी है।\n\nस्वीकार करके आप घोषित करते हैं कि आपने इन शर्तों को पढ़ा, समझा और स्वीकार किया है। यदि आप स्वीकार नहीं करते, तो इंस्टॉलेशन जारी नहीं रहेगा।",
            "Playhub — это бесплатное программное обеспечение с открытым исходным кодом, распространяемое по лицензии MIT. Настоящие условия применяются к Playhub и всем его плагинам.\n\nБЕЗ ГАРАНТИЙ. Программа предоставляется \"как есть\" и \"по мере доступности\", без каких-либо гарантий, явных или подразумеваемых, включая, помимо прочего, гарантии товарной пригодности, пригодности для определённой цели и ненарушения прав.\n\nОГРАНИЧЕНИЕ ОТВЕТСТВЕННОСТИ. В максимальной степени, разрешённой применимым законодательством, автор ни при каких обстоятельствах не несёт ответственности за прямые, косвенные, случайные, особые, последующие или штрафные убытки, а также за потерю данных, упущенную выгоду, сбои, приостановку или блокировку учётных записей либо проблемы с Windows, Steam или другими сервисами, возникшие в результате использования или невозможности использования Playhub или его плагинов. Вы используете программу исключительно на свой риск.\n\nВОЗМЕЩЕНИЕ УЩЕРБА. Вы соглашаетесь оградить автора от любых претензий третьих лиц, возникающих в связи с вашим использованием программы или нарушением настоящих условий либо условий обслуживания третьих сторон.\n\nОТСУТСТВИЕ АФФИЛИАЦИИ. Playhub не связан с Valve (Steam), Microsoft (Xbox), Epic Games, GOG или проектом Decky Loader, не спонсируется и не одобряется ими. Все товарные знаки принадлежат их владельцам.\n\nОТВЕТСТВЕННОЕ ИСПОЛЬЗОВАНИЕ. Playhub создаёт ярлыки и управляет плагинами и настройками; игры остаются установленными в своих лаунчерах. Некоторые функции изменяют настройки Windows или Steam (например, режим разработчика, смену оболочки, отладку CEF). Вы несёте единоличную ответственность за соблюдение условий обслуживания Steam и других используемых вами платформ.\n\nПринимая, вы заявляете, что прочитали, поняли и приняли настоящие условия. Если вы не принимаете их, установка не будет продолжена."
        },
    };
}
