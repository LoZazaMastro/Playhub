# Playhub Setup — bootstrapper custom

Installer nativo (WPF .NET 8) con la stessa estetica dell'app: finestra **scura
acrilica**, accent **giallo #FFCB0F**, logo Playhub a sinistra, **progress bar
Fluent gialla**, opzioni di scorciatoia e avvio finale. Si registra in
**"App installate"** ed è disinstallabile col **tasto destro dal menu Start**.

## Come si costruisce

```
Source\PlayhubSetup\build-installer.bat
```

Lo script:
1. pubblica l'app (`Playhub.csproj`, self-contained x64) in `..\Playhub\dist_publish`;
2. ne crea un `Payload\payload.zip`;
3. compila l'installer **single-file** con il payload incorporato.

Risultato: **`Output\Playhub-Setup.exe`** — un unico file da distribuire.

## Cosa fa l'installer

- Installazione **per-utente** in `%LOCALAPPDATA%\Programs\Playhub` → **niente UAC**.
- Opzioni: collegamento **desktop**, **menu Start**, **avvio con Windows**.
- **Avvio automatico** di Playhub a fine installazione (casella spuntabile).
- Registra la voce di disinstallazione (DisplayName, icona, versione, editore).
- Crea un **uninstaller** (`unins-playhub.exe`) dentro la cartella d'installazione.

## Disinstallazione

- Menu Start → tasto destro su Playhub → **Disinstalla**, oppure
- Impostazioni → App installate → Playhub → **Disinstalla**.

L'uninstaller rimuove collegamenti, chiave di registro e file; la cartella
viene eliminata subito dopo l'uscita del processo.

## Note importanti

- **Eseguibile avviato**: l'installer lancia `Playhub.exe`. Se la tua
  distribuzione usa un launcher con nome diverso, cambia `AppExeName` in
  `Installer.cs` (una sola costante).
- **Acrilico**: l'effetto vetro richiede Windows 11. Su Windows 10 le chiamate
  DWM falliscono in silenzio e la finestra resta scura solida (nessun crash).
- **Avviso SmartScreen**: come ogni .exe non firmato, al primo avvio Windows può
  mostrare "Windows ha protetto il PC". Per evitarlo serve firmare l'installer
  con un certificato code-signing (Authenticode). Posso aggiungere al build lo
  step `signtool` quando avrai un certificato.
- **Modalità sviluppo**: se compili l'installer senza aver creato `payload.zip`,
  l'eseguibile cerca `payload.zip` oppure una cartella `dist_publish` accanto a
  sé — comodo per iterare solo sulla UI.
