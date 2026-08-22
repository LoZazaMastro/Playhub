# Playhub Focus Rescue.
# Lanciato dall'agente SOLO in Gaming Mode (come "processo personalizzato").
#
# Problema risolto: con un gioco in esecuzione, premendo il tasto Steam/QAM
# Steam reagisce (suono) ma la finestra Big Picture NON viene in primo piano.
# Causa: il "foreground lock" di Windows (in Gaming Mode non c'e' Explorer come
# shell e Steam e' un processo in background rispetto al gioco), piu' eventuali
# giochi che si impostano da soli WS_EX_TOPMOST. NON e' colpa del borderless
# dell'agente (che usa SWP_NOACTIVATE|SWP_NOZORDER e non ruba mai il focus).
#
# Funzionamento: mini server HTTP in loopback sulla porta 47992. Il plugin
# Decky di Playhub (dentro la CEF di Steam) notifica:
#   POST /focus/steam -> l'overlay/menu Steam si sta aprendo sopra un gioco:
#                        se Steam non e' gia' davanti, togli il TOPMOST al
#                        gioco, porta Big Picture sopra e prova a dargli focus.
#   POST /focus/game  -> l'overlay si e' chiuso: ripristina il TOPMOST ai
#                        giochi che lo avevano e restituisci il focus al gioco.
#   GET  /health      -> diagnostica.
#
# Il borderless fullscreen dell'agente resta INTATTO: qui si tocca solo il bit
# WS_EX_TOPMOST (e lo si ripristina) e lo z-order della finestra di Steam.
# Se nessuno preme il tasto Steam, questo processo non fa assolutamente nulla.

$ErrorActionPreference = 'SilentlyContinue'

$logPath = Join-Path $env:APPDATA 'GamingMode\playhub-focus.log'
$port = 47992

function Write-Log([string]$message) {
    try {
        "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) $message" | Add-Content -LiteralPath $logPath -Encoding UTF8
    }
    catch {
    }
}

# --- Win32: compilato una volta con Add-Type (C# 5 compatibile). -------------
$nativeSource = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayhubFocusRescue
{
    public static class Native
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOPMOST = 0x00000008L;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const byte VK_MENU = 0x12;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int ASFW_ANY = -1;

        // Stato tra /focus/steam e /focus/game.
        private static readonly List<IntPtr> Demoted = new List<IntPtr>();
        private static IntPtr _savedGame = IntPtr.Zero;
        private static IntPtr _bpmTopmosted = IntPtr.Zero;

        private static string GetTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static bool IsSteamProcess(uint pid)
        {
            try
            {
                var name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                return name == "steam" || name == "steamwebhelper" || name == "gameoverlayui";
            }
            catch
            {
                return false;
            }
        }

        private static List<IntPtr> TopLevelWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
            return list;
        }

        // La finestra gamepadui: titolo "Steam Big Picture Mode" (classe SDL_app
        // come ripiego) appartenente a steam.exe/steamwebhelper.
        private static IntPtr FindBigPictureWindow()
        {
            IntPtr byTitle = IntPtr.Zero, byClass = IntPtr.Zero;
            foreach (var h in TopLevelWindows())
            {
                if (!IsWindowVisible(h)) continue;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (!IsSteamProcess(pid)) continue;
                var title = GetTitle(h);
                if (title.IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    byTitle = h;
                    break;
                }
                if (byClass == IntPtr.Zero &&
                    string.Equals(GetClass(h), "SDL_app", StringComparison.OrdinalIgnoreCase))
                {
                    byClass = h;
                }
            }
            return byTitle != IntPtr.Zero ? byTitle : byClass;
        }

        private static void TryFocus(IntPtr target, IntPtr currentForeground)
        {
            AllowSetForegroundWindow(ASFW_ANY);

            uint fgPid, targetPid;
            var fgThread = GetWindowThreadProcessId(currentForeground, out fgPid);
            var targetThread = GetWindowThreadProcessId(target, out targetPid);
            var myThread = GetCurrentThreadId();

            var attachedFg = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
            var attachedTarget = targetThread != 0 && targetThread != myThread && AttachThreadInput(myThread, targetThread, true);
            try
            {
                // Pressione ALT "a vuoto": sblocca SetForegroundWindow quando il
                // sistema rifiuta il cambio di primo piano da processi esterni.
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                SetForegroundWindow(target);
                BringWindowToTop(target);
            }
            finally
            {
                if (attachedFg) AttachThreadInput(myThread, fgThread, false);
                if (attachedTarget) AttachThreadInput(myThread, targetThread, false);
            }
        }

        // POST /focus/steam
        public static string RescueSteam()
        {
            var bpm = FindBigPictureWindow();
            if (bpm == IntPtr.Zero) return "no-bpm-window";

            var fg = GetForegroundWindow();
            uint fgPid;
            GetWindowThreadProcessId(fg, out fgPid);

            // Guardia anti-regressione: se Steam e' gia' davanti (utenti senza
            // il problema, o retry dopo un rescue riuscito) non fare nulla.
            if (fg == bpm || IsSteamProcess(fgPid)) return "steam-already-foreground";

            _savedGame = fg;

            // 1) Togli WS_EX_TOPMOST alle finestre (non-Steam) che lo hanno:
            //    una finestra topmost coprirebbe Big Picture in ogni caso.
            foreach (var h in TopLevelWindows())
            {
                if (h == bpm || !IsWindowVisible(h)) continue;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (IsSteamProcess(pid)) continue;
                var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                if ((ex & WS_EX_TOPMOST) != 0)
                {
                    SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    if (!Demoted.Contains(h)) Demoted.Add(h);
                }
            }

            // 2) Big Picture sopra a tutto finche' l'overlay e' aperto: la
            //    visibilita' e' garantita anche se il focus venisse negato
            //    (l'input del pad passa comunque da Steam Input).
            SetWindowPos(bpm, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _bpmTopmosted = bpm;

            // 3) Prova comunque a dare il vero focus a Big Picture.
            TryFocus(bpm, fg);
            return "rescued";
        }

        private static void ReleaseSteamLayer()
        {
            if (_bpmTopmosted != IntPtr.Zero)
            {
                if (IsWindow(_bpmTopmosted))
                {
                    SetWindowPos(_bpmTopmosted, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                _bpmTopmosted = IntPtr.Zero;
            }

            // Ripristina il TOPMOST ai giochi che lo avevano davvero.
            foreach (var h in Demoted)
            {
                if (IsWindow(h) && IsWindowVisible(h))
                {
                    SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            Demoted.Clear();
        }

        // POST /focus/game
        public static string RestoreGame()
        {
            ReleaseSteamLayer();

            // Se il primo piano e' rimasto a Steam, restituiscilo al gioco.
            if (_savedGame != IntPtr.Zero && IsWindow(_savedGame) && IsWindowVisible(_savedGame))
            {
                var fg = GetForegroundWindow();
                uint fgPid;
                GetWindowThreadProcessId(fg, out fgPid);
                if (IsSteamProcess(fgPid))
                {
                    TryFocus(_savedGame, fg);
                }
            }
            _savedGame = IntPtr.Zero;
            return "restored";
        }

        // POST /focus/release
        //
        // L'utente ha scelto esplicitamente un'altra finestra dal task
        // switcher. Si rimuove il TOPMOST temporaneo di Steam e si ripristinano
        // gli stili originali, ma non si riattiva la vecchia finestra: sara' il
        // task switcher ad attivare direttamente quella scelta.
        public static string ReleaseForSelection()
        {
            ReleaseSteamLayer();
            _savedGame = IntPtr.Zero;
            return "released";
        }
    }
}
'@

try {
    Add-Type -TypeDefinition $nativeSource -Language CSharp
}
catch {
    Write-Log "Add-Type fallita: $_"
    exit 1
}

# --- Mini server HTTP su TCP puro (niente http.sys: nessuna URL ACL). --------
try {
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
    $listener.Start()
}
catch {
    # Porta occupata: un'altra istanza e' gia' attiva. Uscita silenziosa.
    Write-Log "Porta $port occupata (istanza gia' attiva?): esco."
    exit 0
}

Write-Log "--- Focus Rescue avviato (porta $port) ---"

function Send-Response($stream, [int]$code, [string]$body) {
    $reason = if ($code -eq 200) { 'OK' } elseif ($code -eq 404) { 'Not Found' } else { 'No Content' }
    $payload = [System.Text.Encoding]::UTF8.GetBytes($body)
    $headers = "HTTP/1.1 $code $reason`r`n" +
        "Content-Type: application/json`r`n" +
        "Content-Length: $($payload.Length)`r`n" +
        "Access-Control-Allow-Origin: *`r`n" +
        "Access-Control-Allow-Methods: GET, POST, OPTIONS`r`n" +
        "Connection: close`r`n`r`n"
    $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($headers)
    $stream.Write($headerBytes, 0, $headerBytes.Length)
    $stream.Write($payload, 0, $payload.Length)
    $stream.Flush()
}

$steamSeen = $false
$steamGoneSince = $null
$lastSteamCheck = Get-Date

while ($true) {
    # Watchdog: quando Steam sparisce (cambio modalita' in corso) esci con lui.
    if (((Get-Date) - $lastSteamCheck).TotalSeconds -ge 5) {
        $lastSteamCheck = Get-Date
        $steam = Get-Process steam -ErrorAction SilentlyContinue
        if ($steam) {
            $steamSeen = $true
            $steamGoneSince = $null
        }
        elseif ($steamSeen) {
            if (-not $steamGoneSince) { $steamGoneSince = Get-Date }
            elseif (((Get-Date) - $steamGoneSince).TotalSeconds -ge 30) {
                Write-Log 'Steam chiuso: esco.'
                break
            }
        }
    }

    if (-not $listener.Pending()) {
        Start-Sleep -Milliseconds 50
        continue
    }

    $client = $null
    try {
        $client = $listener.AcceptTcpClient()
        $client.ReceiveTimeout = 2000
        $stream = $client.GetStream()
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)

        $requestLine = $reader.ReadLine()
        if (-not $requestLine) { continue }
        # Scarta gli header (le richieste del plugin non hanno body rilevante).
        while ($true) {
            $line = $reader.ReadLine()
            if ($null -eq $line -or $line -eq '') { break }
        }

        $parts = $requestLine.Split(' ')
        $method = $parts[0]
        $path = if ($parts.Length -gt 1) { $parts[1] } else { '/' }

        if ($method -eq 'OPTIONS') {
            Send-Response $stream 204 ''
        }
        elseif ($method -eq 'GET' -and $path -eq '/health') {
            Send-Response $stream 200 '{"ok":true,"service":"playhub-focus-rescue"}'
        }
        elseif ($method -eq 'POST' -and $path -eq '/focus/steam') {
            $result = [PlayhubFocusRescue.Native]::RescueSteam()
            Write-Log "focus/steam -> $result"
            Send-Response $stream 200 "{`"result`":`"$result`"}"
        }
        elseif ($method -eq 'POST' -and $path -eq '/focus/game') {
            $result = [PlayhubFocusRescue.Native]::RestoreGame()
            Write-Log "focus/game -> $result"
            Send-Response $stream 200 "{`"result`":`"$result`"}"
        }
        elseif ($method -eq 'POST' -and $path -eq '/focus/release') {
            $result = [PlayhubFocusRescue.Native]::ReleaseForSelection()
            Write-Log "focus/release -> $result"
            Send-Response $stream 200 "{`"result`":`"$result`"}"
        }
        else {
            Send-Response $stream 404 '{"error":"not-found"}'
        }
    }
    catch {
        Write-Log "Errore richiesta: $_"
    }
    finally {
        if ($client) { $client.Close() }
    }
}

$listener.Stop()
Write-Log '--- Focus Rescue terminato ---'
