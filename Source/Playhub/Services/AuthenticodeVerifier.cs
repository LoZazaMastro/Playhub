using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Playhub.Services;

/// <param name="IsTrusted">true solo se WinVerifyTrust ha approvato il file.</param>
/// <param name="Reason">Codice stabile: signature_valid, signature_missing, signature_untrusted, ...</param>
/// <param name="SignerSubject">Subject del certificato del firmatario, se leggibile.</param>
public sealed record AuthenticodeResult(
    bool IsTrusted,
    string Reason,
    string? SignerSubject = null,
    string? Thumbprint = null,
    string? Detail = null);

/// <summary>
/// Verifica Authenticode di un file tramite WinVerifyTrust, senza interfaccia
/// e senza mai chiedere niente all'utente. Usata prima di eseguire qualunque
/// binario scaricato e per controllare il driver PawnIO gia' installato.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x00000100;
    private const uint WTD_UICONTEXT_EXECUTE = 1;

    // HRESULT documentati da WinVerifyTrust.
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
    private const int TRUST_E_SUBJECT_NOT_TRUSTED = unchecked((int)0x800B0004);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
    private const int CERT_E_REVOKED = unchecked((int)0x800B010C);
    private const int CERT_E_REVOCATION_FAILURE = unchecked((int)0x800B010E);
    private const int CRYPT_E_SECURITY_SETTINGS = unchecked((int)0x80092026);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WINTRUST_DATA data);

    /// <summary>
    /// Verifica il file. Il controllo revoche viene tentato sull'intera catena;
    /// se la CRL non e' raggiungibile (macchina offline) si ripete SENZA revoche
    /// e lo si dichiara in Detail: la firma resta verificata, la revoca no.
    /// Qualunque altro esito diverso da "valida" e' un rifiuto.
    /// </summary>
    public static AuthenticodeResult Verify(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AuthenticodeResult(false, "platform_unsupported");
        }

        var result = RunWinVerifyTrust(filePath, WTD_REVOKE_WHOLECHAIN);
        var revocationUnavailable = false;
        if (result is CERT_E_REVOCATION_FAILURE or CRYPT_E_SECURITY_SETTINGS)
        {
            revocationUnavailable = true;
            result = RunWinVerifyTrust(filePath, WTD_REVOKE_NONE);
        }

        if (result != 0)
        {
            return new AuthenticodeResult(false, ReasonFor(result), Detail: Hresult(result));
        }

        var (subject, thumbprint, readError) = ReadSigner(filePath);
        return new AuthenticodeResult(
            true,
            "signature_valid",
            subject,
            thumbprint,
            revocationUnavailable
                ? "Firma valida; controllo revoche non disponibile (nessuna rete o CRL irraggiungibile)."
                : readError);
    }

    /// <summary>
    /// Verifica la firma e in piu' pretende che il subject del firmatario
    /// contenga <paramref name="expectedSubjectFragment"/>. Un file firmato da
    /// qualcun altro viene rifiutato con reason "signer_mismatch".
    /// </summary>
    public static AuthenticodeResult VerifySignedBy(string filePath, string expectedSubjectFragment)
    {
        var verified = Verify(filePath);
        if (!verified.IsTrusted) return verified;
        if (string.IsNullOrWhiteSpace(expectedSubjectFragment))
        {
            return verified with { IsTrusted = false, Reason = "expected_signer_not_configured" };
        }
        if (verified.SignerSubject is null)
        {
            return verified with { IsTrusted = false, Reason = "signer_unreadable" };
        }
        if (!verified.SignerSubject.Contains(expectedSubjectFragment, StringComparison.OrdinalIgnoreCase))
        {
            return verified with
            {
                IsTrusted = false,
                Reason = "signer_mismatch",
                Detail = "Atteso: " + expectedSubjectFragment + " · trovato: " + verified.SignerSubject
            };
        }
        return verified;
    }

    private static int RunWinVerifyTrust(string filePath, uint revocationChecks)
    {
        var file = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = revocationChecks,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = filePointer,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_SAFER_FLAG,
                dwUIContext = WTD_UICONTEXT_EXECUTE
            };

            var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            // Obbligatorio: senza la CLOSE lo stato resta allocato nel processo.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result;
        }
        catch (Exception ex)
        {
            Diag.Crash("AuthenticodeVerifier.RunWinVerifyTrust", ex);
            return TRUST_E_SUBJECT_NOT_TRUSTED;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(filePointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }

    private static (string? Subject, string? Thumbprint, string? Error) ReadSigner(string filePath)
    {
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            return (certificate.Subject, certificate.Thumbprint, null);
        }
        catch (Exception ex)
        {
            Diag.Crash("AuthenticodeVerifier.ReadSigner", ex);
            return (null, null, "Firmatario non leggibile: " + ex.GetType().Name);
        }
    }

    private static string ReasonFor(int hresult) => hresult switch
    {
        TRUST_E_NOSIGNATURE => "signature_missing",
        TRUST_E_BAD_DIGEST => "signature_file_modified",
        TRUST_E_EXPLICIT_DISTRUST => "signature_distrusted",
        CERT_E_UNTRUSTEDROOT => "signature_untrusted_root",
        CERT_E_EXPIRED => "signature_expired",
        CERT_E_REVOKED => "signature_revoked",
        CERT_E_REVOCATION_FAILURE => "signature_revocation_unavailable",
        _ => "signature_untrusted"
    };

    private static string Hresult(int value) => "HRESULT 0x" + value.ToString("X8");
}
