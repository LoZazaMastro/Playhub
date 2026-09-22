using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

public enum PawnIoInstallPhase
{
    Preparing,
    Downloading,
    Verifying,
    WaitingForUser,
    Confirming,
    Completed,
    Failed
}

public sealed record PawnIoInstallProgress(PawnIoInstallPhase Phase, double Fraction, string Message);

/// <param name="Reason">Codice stabile: dice esattamente quale controllo ha fallito.</param>
public sealed record PawnIoInstallResult(bool Ok, string Reason, PawnIoStatus Status, string? Detail = null);

/// <summary>
/// Installazione guidata di PawnIO senza mai mandare l'utente su una pagina web:
/// Playhub scarica l'installer ufficiale firmato, lo verifica, lo apre davanti
/// alla propria finestra, aspetta che l'utente finisca e poi rilegge lo stato.
///
/// Non c'e' installazione silenziosa: l'installer di PawnIO e il prompt driver di
/// Windows devono essere visti e accettati dall'utente.
/// </summary>
public sealed class PawnIoInstallService
{
    // ======================================================================
    //  PIN DELLA RELEASE
    //  ---------------------------------------------------------------------
    //  URL e firmatario sono OBBLIGATORI e gia' valorizzati: l'installer viene
    //  eseguito solo se la firma Authenticode e' valida E il firmatario e'
    //  quello atteso. Questo e' il controllo che conta: un binario sostituito
    //  lungo il percorso non ha la firma di namazso.
    //
    //  ExpectedSha256 e' un pin OPZIONALE alla singola build. Se valorizzato
    //  viene imposto; se vuoto si applicano solo gli altri controlli e il
    //  flusso resta utilizzabile. Per pinnarlo, su una macchina fidata:
    //      certutil -hashfile PawnIO_setup.exe SHA256
    //  e incolla i 64 caratteri qui sotto. Aggiornando la versione vanno
    //  cambiati insieme ReleaseTag, InstallerUrl e ExpectedSha256.
    //
    //  Fonte dell'URL: https://github.com/namazso/PawnIO.Setup/releases
    //  (release 2.2.0, asset PawnIO_setup.exe). Mai "latest": e' un alias che
    //  nel tempo punta a una release diversa.
    // ======================================================================
    public const string ReleaseTag = "2.2.0";
    public const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    /// <summary>Pin opzionale alla singola build. Vuoto = non imposto.</summary>
    public const string ExpectedSha256 = "";
    public const string ExpectedSignerSubject = "namazso";
    private const string InstallerFileName = "PawnIO_setup.exe";
    public const string ProjectPage = "https://pawnio.eu";

    private const long MinimumInstallerBytes = 64 * 1024;
    private const long MaximumInstallerBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(25);
    private const int MaximumRedirects = 5;

    // Il download parte da github.com e viene rediretto allo storage degli asset.
    // I redirect non sono seguiti alla cieca: ogni salto deve essere https e su
    // uno di questi host.
    private static readonly string[] AllowedHosts =
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        return client;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private const int ASFW_ANY = -1;

    /// <summary>null se il pin e' completo, altrimenti il motivo del rifiuto.</summary>
    public static string? PinProblem() => ValidatePin(InstallerUrl, ExpectedSha256, ExpectedSignerSubject);

    /// <summary>
    /// Regole del pin, isolate perche' siano verificabili senza rete: HTTPS, host
    /// noto, versione esplicita nell'URL, hash di 64 esadecimali, firmatario atteso.
    /// </summary>
    internal static string? ValidatePin(string url, string sha256, string signerSubject)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(signerSubject))
        {
            return "pin_not_configured";
        }
        // L'hash e' un pin facoltativo alla singola build: assente si accetta,
        // presente deve essere ben formato, altrimenti verificherebbe niente.
        if (!string.IsNullOrWhiteSpace(sha256) && (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)))
        {
            return "pin_hash_malformed";
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsAllowedHost(uri))
        {
            return "pin_url_invalid";
        }
        // "latest" e' un alias che punta a una release diversa nel tempo: il pin
        // dell'hash non varrebbe piu' niente.
        if (uri.AbsolutePath.Contains("/latest", StringComparison.OrdinalIgnoreCase))
        {
            return "pin_url_not_versioned";
        }
        return null;
    }

    public static bool IsAvailable => PinProblem() is null;

    /// <summary>
    /// Scarica, verifica, esegue davanti alla finestra di Playhub, aspetta la
    /// fine del processo e rilegge lo stato del driver.
    /// </summary>
    /// <param name="ownerWindow">HWND della finestra di Playhub, per riportare in primo piano l'installer.</param>
    public async Task<PawnIoInstallResult> RunAsync(
        IntPtr ownerWindow,
        IProgress<PawnIoInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure("platform_unsupported", null);
        }

        var pin = PinProblem();
        if (pin is not null)
        {
            // Fallimento pulito e dichiarato: nessun download, nessuna esecuzione.
            return Failure(pin, "Costanti di release non valorizzate in PawnIoInstallService.");
        }

        var already = PawnIoService.Detect();
        if (already.IsUsable)
        {
            Report(progress, PawnIoInstallPhase.Completed, 1, "PawnIO è già installato.");
            return new PawnIoInstallResult(true, "already_installed", already);
        }

        // (d) Cartella temporanea dell'utente, mai una cartella di sistema.
        var folder = Path.Combine(Path.GetTempPath(), "Playhub", "pawnio",
            Guid.NewGuid().ToString("N"));
        var installer = Path.Combine(folder, InstallerFileName);

        try
        {
            Report(progress, PawnIoInstallPhase.Preparing, 0, "Preparazione del download…");
            Directory.CreateDirectory(folder);

            // ---------------------------------------------------------- (a)+(b)
            var digest = await DownloadAsync(installer, progress, cancellationToken).ConfigureAwait(false);
            Report(progress, PawnIoInstallPhase.Verifying, 0.90, "Verifica del file scaricato…");

            if (!string.IsNullOrWhiteSpace(ExpectedSha256) && !string.Equals(digest, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Failure("hash_mismatch",
                    "Atteso " + ExpectedSha256.ToLowerInvariant() + ", ottenuto " + digest.ToLowerInvariant());
            }

            await using (var header = File.OpenRead(installer))
            {
                if (header.ReadByte() != 'M' || header.ReadByte() != 'Z')
                {
                    return Failure("not_a_windows_executable", null);
                }
            }

            // ------------------------------------------------------------- (c)
            var signature = AuthenticodeVerifier.VerifySignedBy(installer, ExpectedSignerSubject);
            if (!signature.IsTrusted)
            {
                return Failure(signature.Reason, signature.Detail);
            }

            // ------------------------------------------------- esecuzione visibile
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, PawnIoInstallPhase.WaitingForUser, 0.95,
                "Completa l'installazione di PawnIO nella finestra che si è aperta.");

            var exitCode = await LaunchAndWaitAsync(installer, ownerWindow, cancellationToken).ConfigureAwait(false);

            Report(progress, PawnIoInstallPhase.Confirming, 0.98, "Controllo del driver…");
            var status = await WaitForDriverAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsUsable)
            {
                Report(progress, PawnIoInstallPhase.Completed, 1, "PawnIO è installato e attivo.");
                return new PawnIoInstallResult(true, "installed", status);
            }

            // L'installer puo' essere stato chiuso, annullato, o il prompt driver
            // rifiutato: lo stato riletto e' la verita', non il codice di uscita.
            return new PawnIoInstallResult(false,
                exitCode == 0 ? "driver_not_active_after_install" : "installer_cancelled",
                status,
                "Codice di uscita dell'installer: " + exitCode + "; stato driver: " + status.Reason);
        }
        catch (OperationCanceledException)
        {
            return Failure("cancelled", null);
        }
        catch (Exception ex)
        {
            Diag.Crash("PawnIoInstallService.RunAsync", ex);
            return Failure("download_failed", ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            // Il binario scaricato non resta in giro dopo l'uso.
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) { Diag.Crash("PawnIoInstallService.Cleanup", ex); }
        }
    }

    private static PawnIoInstallResult Failure(string reason, string? detail)
        => new(false, reason, PawnIoService.Detect(), detail);

    /// <summary>Scarica l'installer calcolando lo SHA-256 mentre scorre. Restituisce il digest.</summary>
    private static async Task<string> DownloadAsync(
        string destination,
        IProgress<PawnIoInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(InstallerUrl, UriKind.Absolute);
        using var stallTimeout = new CancellationTokenSource(StallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stallTimeout.Token);
        var token = linked.Token;

        HttpResponseMessage? response = null;
        try
        {
            for (var hop = 0; ; hop++)
            {
                if (hop > MaximumRedirects) throw new InvalidDataException("Troppi reindirizzamenti.");
                if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Reindirizzamento non HTTPS.");
                if (!IsAllowedHost(uri)) throw new InvalidDataException("Host non consentito: " + uri.Host);

                response?.Dispose();
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);

                var code = (int)response.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location
                        ?? throw new InvalidDataException("Reindirizzamento senza destinazione.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                break;
            }

            var total = response!.Content.Headers.ContentLength ?? 0;
            if (total > MaximumInstallerBytes)
            {
                throw new InvalidDataException("L'installer indicato è troppo grande.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[128 * 1024];
            long received = 0;
            var clock = Stopwatch.StartNew();
            while (true)
            {
                stallTimeout.CancelAfter(StallTimeout);
                var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                stallTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (read == 0) break;

                received += read;
                if (received > MaximumInstallerBytes)
                {
                    throw new InvalidDataException("L'installer scaricato è troppo grande.");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);

                if (clock.ElapsedMilliseconds >= 50)
                {
                    var fraction = total > 0 ? Math.Clamp(received / (double)total, 0, 1) : 0;
                    Report(progress, PawnIoInstallPhase.Downloading, 0.85 * fraction, "Download di PawnIO…");
                    clock.Restart();
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            try { output.Flush(flushToDisk: true); } catch (Exception ex) { Diag.Crash("PawnIoInstallService.Flush", ex); }

            if (received < MinimumInstallerBytes || (total > 0 && received != total))
            {
                throw new InvalidDataException("Il download dell'installer è incompleto.");
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static bool IsAllowedHost(Uri uri)
        => AllowedHosts.Any(host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Apre l'installer in primo piano davanti a Playhub e aspetta che finisca.
    /// UseShellExecute: l'installer si eleva da solo col suo manifest e mostra
    /// UAC e il prompt di installazione driver, come deve.
    /// </summary>
    private static async Task<int> LaunchAndWaitAsync(string installer, IntPtr ownerWindow, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(installer)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installer) ?? "",
            WindowStyle = ProcessWindowStyle.Normal
        };

        // Cede il diritto di primo piano al processo che stiamo per avviare:
        // senza questo Windows lo aprirebbe lampeggiando nella barra.
        try { AllowSetForegroundWindow(ASFW_ANY); }
        catch (Exception ex) { Diag.Crash("PawnIoInstallService.AllowForeground", ex); }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Impossibile avviare l'installer di PawnIO.");

        _ = ownerWindow; // la finestra di Playhub resta dov'e': non la nascondiamo.
        _ = Task.Run(() => BringToFront(process), CancellationToken.None);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static void BringToFront(Process process)
    {
        // La finestra dell'installer puo' comparire dopo qualche istante (UAC).
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                if (process.HasExited) return;
                process.Refresh();
                var window = process.MainWindowHandle;
                if (window != IntPtr.Zero)
                {
                    SetForegroundWindow(window);
                    return;
                }
            }
            catch (Exception ex)
            {
                Diag.Crash("PawnIoInstallService.BringToFront", ex);
                return;
            }
            Thread.Sleep(250);
        }
    }

    /// <summary>
    /// Dopo l'installer il servizio puo' impiegare qualche secondo a risultare
    /// registrato e in esecuzione. Si rilegge finche' non e' pronto o scade.
    /// </summary>
    private static async Task<PawnIoStatus> WaitForDriverAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ConfirmTimeout;
        var status = PawnIoService.Detect();
        while (!status.IsUsable && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            status = PawnIoService.Detect();
        }
        return status;
    }

    private static void Report(IProgress<PawnIoInstallProgress>? progress, PawnIoInstallPhase phase,
        double fraction, string message)
        => progress?.Report(new PawnIoInstallProgress(phase, Math.Clamp(fraction, 0, 1), message));
}
