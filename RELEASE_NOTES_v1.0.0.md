<!--
  Copia/incolla questo testo nel corpo della release su GitHub.
  (GitHub > Releases > Draft a new release)
  Tag:   v1.0.0      <-- importante: l'aggiornamento in-app confronta questo tag
  Title: Playhub 1.0.0
  Asset da caricare:  Playhub-Setup.exe   (e, opzionale, Playhub-Setup.exe.sha256)
-->

# Playhub 1.0.0

Your Windows 11 gaming PC, with the soul of a console. 🎮

## ⬇️ Install
Download **`Playhub-Setup.exe`** qui sotto ed eseguilo.
- Installazione **per-utente**, senza permessi di amministratore.
- Scegli la lingua (12 disponibili), poi **Install**.
- Compare nel menu Start e in **App installate**: disinstallabile col tasto destro.

## ✨ Cosa include
- **Plugin Store** per i plugin DeckyLoader (Launch Curtain, Now Playing, Metadata, ThemeDeck, Weather e altri).
- **Gaming Mode**: avvio diretto in Steam Big Picture, come una console.
- **Importa Giochi Xbox / Game Pass** in Steam con le artwork corrette.
- **Temi e personalizzazione**: accent, sfondo, CSS Loader, backup artwork di Steam.
- **12 lingue** e **notifica di aggiornamento** integrata.
- Installer nativo con UI scura/acrilica in continuità con l'app.

## 💻 Requisiti
- Windows 11 (x64). Su Windows 10 l'app funziona con tema scuro solido (niente acrilico).

## 🔄 Aggiornamenti
Playhub controlla automaticamente le nuove release qui su GitHub e te lo segnala in-app.

## ⚠️ Note
- L'installer **non è firmato**: al primo avvio Windows SmartScreen può mostrare
  "Windows ha protetto il PC" → *Ulteriori informazioni* → *Esegui comunque*.
- Verifica integrità (opzionale):
  ```
  certutil -hashfile Playhub-Setup.exe SHA256
  ```
  e confronta con `Playhub-Setup.exe.sha256`.

## 🙏 Crediti (componenti MIT)
UWPHook © 2016 Brian Lima · VDFParser © 2016 Victor Gama · SharpSteam © 2020 Brian Lima

---
Made by **Andrea Sgarro (ZazaMastro)** — MIT License.
