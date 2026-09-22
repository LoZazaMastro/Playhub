using Playhub.Services;

// Verifiche di ciò che si può controllare senza rete e senza driver: le regole
// del pin di release, la risoluzione dei percorsi NT del driver, e il rifiuto
// di un file non firmato. Il resto (download reale, installer, prompt driver)
// è verificabile solo dal vivo su Windows.
var passed = 0;

// ---------------------------------------------------------------- pin di release
const string url = "https://github.com/namazso/PawnIO.Setup/releases/download/v3.0.0/PawnIO_Setup.exe";
const string sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
const string signer = "CN=Esempio";

Check(PawnIoInstallService.ValidatePin(url, sha, signer) is null, "un pin completo è accettato");
Check(PawnIoInstallService.ValidatePin("", sha, signer) == "pin_not_configured", "URL mancante");
Check(PawnIoInstallService.ValidatePin(url, "", signer) is null, "hash assente: pin facoltativo, la firma resta obbligatoria");
Check(PawnIoInstallService.PinProblem() is null, "il pin di default e' utilizzabile senza intervento manuale");
Check(PawnIoInstallService.InstallerUrl.Contains(PawnIoInstallService.ReleaseTag), "URL e tag della release restano allineati");
Check(PawnIoInstallService.ValidatePin(url, sha, "") == "pin_not_configured", "firmatario mancante");
Check(PawnIoInstallService.ValidatePin(url, "abc", signer) == "pin_hash_malformed", "hash troppo corto");
Check(PawnIoInstallService.ValidatePin(url, new string('z', 64), signer) == "pin_hash_malformed", "hash non esadecimale");
Check(PawnIoInstallService.ValidatePin(url.Replace("https://", "http://"), sha, signer) == "pin_url_invalid", "HTTP rifiutato");
Check(PawnIoInstallService.ValidatePin("https://example.com/a.exe", sha, signer) == "pin_url_invalid", "host non consentito");
Check(PawnIoInstallService.ValidatePin("non-un-url", sha, signer) == "pin_url_invalid", "URL non valido");
Check(PawnIoInstallService.ValidatePin(
    "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_Setup.exe", sha, signer)
    == "pin_url_not_versioned", "\"latest\" rifiutato: l'hash non varrebbe più niente");

// La release e il firmatario sono ora configurati; la verifica della firma resta obbligatoria.
Check(PawnIoInstallService.IsAvailable, "la release configurata abilita l'installazione guidata");

// ------------------------------------------------------- percorsi NT del driver
var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
Check(PawnIoService.ResolveImagePath(null) is null, "ImagePath assente");
Check(PawnIoService.ResolveImagePath("   ") is null, "ImagePath vuoto");
Check(PawnIoService.ResolveImagePath(@"\??\C:\Programmi\PawnIO\PawnIO.sys")
    == @"C:\Programmi\PawnIO\PawnIO.sys", "prefisso \\??\\ rimosso");
Check(PawnIoService.ResolveImagePath(@"\SystemRoot\System32\drivers\PawnIO.sys")
    == Path.Combine(windows, @"System32\drivers\PawnIO.sys"), "\\SystemRoot\\ risolto");
Check(PawnIoService.ResolveImagePath(@"system32\drivers\PawnIO.sys")
    == Path.Combine(windows, @"system32\drivers\PawnIO.sys"), "percorso relativo alla cartella di Windows");
Check(PawnIoService.ResolveImagePath("\"C:\\PawnIO\\PawnIO.sys\"")
    == @"C:\PawnIO\PawnIO.sys", "virgolette rimosse");

// ------------------------------------------------------------------ firma
var file = Path.Combine(Path.GetTempPath(), "playhub-pawnio-test-" + Guid.NewGuid().ToString("N") + ".exe");
File.WriteAllBytes(file, new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
try
{
    var unsigned = AuthenticodeVerifier.Verify(file);
    Check(!unsigned.IsTrusted, "un file non firmato non è attendibile");
    Check(unsigned.Reason is "signature_missing" or "signature_untrusted",
        "e il motivo lo dice: " + unsigned.Reason);
    var mismatched = AuthenticodeVerifier.VerifySignedBy(file, "CN=Chiunque");
    Check(!mismatched.IsTrusted, "il controllo del firmatario non salva un file non firmato");
}
finally
{
    File.Delete(file);
}

// ------------------------------------------------------------ stato del driver
var status = PawnIoService.Detect();
Check(!string.IsNullOrWhiteSpace(status.Reason), "ogni stato ha una causa leggibile");
Check(status.State != PawnIoState.Installed || status.DriverPath is not null,
    "installato significa avere anche il percorso del driver");

Console.WriteLine(passed + " verifiche superate.");
return 0;

void Check(bool condition, string description)
{
    if (!condition)
    {
        Console.Error.WriteLine("FALLITO: " + description);
        Environment.Exit(1);
    }
    passed++;
}
