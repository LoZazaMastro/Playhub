const messages: Record<string, { pending: string; failed: string }> = {
  en: { pending: "Open the app to finish closing it or respond to its dialog.", failed: "The window could not be closed. Try again." },
  it: { pending: "Apri l'app per completare la chiusura o rispondere alla sua finestra di dialogo.", failed: "Impossibile chiudere la finestra. Riprova." },
  es: { pending: "Abre la aplicación para terminar de cerrarla o responder a su diálogo.", failed: "No se pudo cerrar la ventana. Inténtalo de nuevo." },
  fr: { pending: "Ouvrez l’application pour terminer sa fermeture ou répondre à sa boîte de dialogue.", failed: "Impossible de fermer la fenêtre. Réessayez." },
  de: { pending: "Öffne die App, um sie zu schließen oder ihren Dialog zu beantworten.", failed: "Das Fenster konnte nicht geschlossen werden. Versuche es erneut." },
  pt: { pending: "Abra o aplicativo para concluir o fechamento ou responder à caixa de diálogo.", failed: "Não foi possível fechar a janela. Tente novamente." },
  uk: { pending: "Відкрийте програму, щоб завершити її закриття або відповісти на діалогове вікно.", failed: "Не вдалося закрити вікно. Спробуйте ще раз." },
  zh: { pending: "打开应用以完成关闭或响应其对话框。", failed: "无法关闭窗口。请重试。" },
  ja: { pending: "アプリを開き、終了操作を完了するかダイアログに応答してください。", failed: "ウィンドウを閉じられませんでした。もう一度お試しください。" },
  ko: { pending: "앱을 열어 종료를 완료하거나 대화 상자에 응답하세요.", failed: "창을 닫을 수 없습니다. 다시 시도하세요." },
  hi: { pending: "ऐप बंद करने की प्रक्रिया पूरी करने या उसके संवाद का जवाब देने के लिए ऐप खोलें।", failed: "विंडो बंद नहीं हो सकी। फिर से कोशिश करें।" },
  ru: { pending: "Откройте приложение, чтобы завершить закрытие или ответить в его диалоговом окне.", failed: "Не удалось закрыть окно. Попробуйте ещё раз." },
};

export function windowCloseCopy(locale: string) { return messages[locale] ?? messages.en; }
