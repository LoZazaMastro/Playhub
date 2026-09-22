using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Playhub.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;

namespace Playhub;

/// <summary>
/// Card "Controllo del TDP" nelle Impostazioni: stato di PawnIO e installazione
/// guidata. Nessuna pagina web, nessun download manuale, nessuno zip: Playhub
/// scarica l'installer ufficiale firmato, lo verifica, lo apre davanti alla
/// propria finestra e riprende da solo quando l'utente ha finito.
/// </summary>
public sealed partial class MainWindow
{
    private Button _pawnIoButton = new();
    private TextBlock _pawnIoStatusText = new();
    private ProgressBar _pawnIoBar = new();
    private bool _pawnIoInstallRunning;
    private bool _pawnIoAutoStartDone;

    private UIElement BuildPawnIoCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE945).ToString(), "Controllo del TDP",
            "Per regolare il consumo della CPU serve PawnIO, un driver firmato che Playhub non sostituisce."));
        card.Children.Add(Body(
            "PawnIO è il driver che permette a Playhub di leggere e scrivere il limite di potenza del processore. " +
            "È altamente raccomandato: senza di lui il controllo del TDP resta spento. " +
            "Playhub lo scarica, ne verifica firma e impronta, e apre l'installazione davanti a questa finestra: " +
            "non devi scaricare né estrarre niente da solo."));

        _pawnIoStatusText = new TextBlock
        {
            Style = StyleResource("PlayhubBodyTextStyle"),
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            // Testo di stato impostato a runtime, gia' tradotto con T().
            Tag = "noloc"
        };
        _pawnIoBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _pawnIoButton = Button("Installa PawnIO", InstallPawnIoAsync, primary: true);

        card.Children.Add(ActionRow(_pawnIoButton));
        card.Children.Add(_pawnIoBar);
        card.Children.Add(_pawnIoStatusText);

        _ = RefreshPawnIoCardAsync();
        return card;
    }

    /// <summary>
    /// Rilegge lo stato del driver fuori dal thread UI e aggiorna la card.
    /// Se l'utente aveva chiesto PawnIO durante l'installazione di Playhub,
    /// la procedura guidata parte da sola la prima volta.
    /// </summary>
    private async Task RefreshPawnIoCardAsync()
    {
        if (_pawnIoInstallRunning) return;

        PawnIoStatus status;
        try
        {
            status = await Task.Run(PawnIoService.Detect);
        }
        catch (Exception ex)
        {
            Diag.Crash("RefreshPawnIoCardAsync", ex);
            status = new PawnIoStatus(PawnIoState.Unknown, "detection_failed", Detail: ex.Message);
        }

        ApplyPawnIoStatus(status);

        if (!_pawnIoAutoStartDone && _settings.PawnIoInstallRequested)
        {
            _pawnIoAutoStartDone = true;
            // La richiesta e' consumata comunque: se qualcosa va storto l'utente
            // ha il pulsante, non un tentativo automatico che si ripete a ogni avvio.
            _settings.PawnIoInstallRequested = false;
            await SaveSettingsSilentlyAsync();
            if (!status.IsUsable && PawnIoInstallService.IsAvailable)
            {
                await InstallPawnIoAsync();
            }
        }
    }

    private void ApplyPawnIoStatus(PawnIoStatus status)
    {
        _pawnIoStatusText.Visibility = Visibility.Visible;
        _pawnIoStatusText.Text = DescribePawnIoStatus(status);

        var pin = PawnIoInstallService.PinProblem();
        if (status.IsUsable)
        {
            _pawnIoButton.Visibility = Visibility.Collapsed;
            _pawnIoButton.IsEnabled = false;
            return;
        }

        _pawnIoButton.Visibility = Visibility.Visible;
        _pawnIoButton.IsEnabled = pin is null && !_pawnIoInstallRunning;
        if (pin is not null)
        {
            _pawnIoStatusText.Text += "\n" + DescribePawnIoReason(pin, null);
        }
    }

    private string DescribePawnIoStatus(PawnIoStatus status)
    {
        var text = DescribePawnIoReason(status.Reason, status.Detail);
        if (status.IsUsable && !string.IsNullOrWhiteSpace(status.Version))
        {
            text += " " + string.Format(T("Versione {0}."), status.Version);
        }
        return text;
    }

    private string DescribePawnIoReason(string reason, string? detail)
    {
        var text = T(reason switch
        {
            "ready" => "PawnIO è installato e attivo.",
            "already_installed" => "PawnIO è installato e attivo.",
            "installed" => "PawnIO è installato e attivo.",
            "service_not_registered" => "PawnIO non è installato: il controllo del TDP resta spento.",
            "service_type_invalid" => "Il servizio PawnIO esiste ma non è un driver kernel valido.",
            "driver_path_unreadable" => "Il registro di Windows non espone un percorso valido per il driver PawnIO.",
            "driver_file_missing" => "PawnIO risulta registrato ma il file del driver non esiste più.",
            "service_disabled" => "Il servizio PawnIO è disabilitato in Windows: va riabilitato o reinstallato.",
            "service_not_running" => "PawnIO è installato ma il suo servizio non è in esecuzione.",
            "service_manager_unavailable" => "Non riesco a interrogare i servizi di Windows per verificare PawnIO.",
            "service_query_denied" => "Windows non consente di leggere lo stato del servizio PawnIO.",
            "service_query_failed" => "La lettura dello stato del servizio PawnIO non è riuscita.",
            "driver_signature_invalid" => "La firma del driver PawnIO installato non è valida: Playhub non lo usa.",
            "platform_unsupported" => "Il controllo del TDP è disponibile solo su Windows.",
            "detection_failed" => "Non riesco a stabilire lo stato di PawnIO.",

            "pin_not_configured" => "L'installazione guidata non è disponibile in questa build: manca il riferimento verificato alla versione di PawnIO.",
            "pin_hash_malformed" => "L'impronta SHA-256 attesa non è valida: installazione bloccata.",
            "pin_url_invalid" => "L'indirizzo di download configurato non è valido: installazione bloccata.",
            "pin_url_not_versioned" => "L'indirizzo di download non punta a una versione precisa: installazione bloccata.",
            "hash_mismatch" => "Il file scaricato non corrisponde a quello atteso (SHA-256). Non è stato eseguito.",
            "not_a_windows_executable" => "Il file scaricato non è un eseguibile di Windows. Non è stato eseguito.",
            "signature_missing" => "Il file scaricato non è firmato. Non è stato eseguito.",
            "signature_file_modified" => "La firma del file scaricato non corrisponde al contenuto. Non è stato eseguito.",
            "signature_distrusted" => "La firma del file scaricato è esplicitamente non attendibile. Non è stato eseguito.",
            "signature_untrusted_root" => "La firma del file scaricato risale a un'autorità non attendibile. Non è stato eseguito.",
            "signature_expired" => "Il certificato del file scaricato è scaduto. Non è stato eseguito.",
            "signature_revoked" => "Il certificato del file scaricato è stato revocato. Non è stato eseguito.",
            "signature_revocation_unavailable" => "Non è stato possibile verificare la revoca del certificato. Non è stato eseguito.",
            "signature_untrusted" => "La firma del file scaricato non è valida. Non è stato eseguito.",
            "signer_mismatch" => "Il file scaricato è firmato da qualcun altro. Non è stato eseguito.",
            "signer_unreadable" => "Non riesco a leggere il firmatario del file scaricato. Non è stato eseguito.",
            "expected_signer_not_configured" => "Il firmatario atteso non è configurato: installazione bloccata.",
            "download_failed" => "Il download di PawnIO non è riuscito.",
            "cancelled" => "Installazione annullata.",
            "installer_cancelled" => "L'installazione di PawnIO è stata annullata o interrotta.",
            "driver_not_active_after_install" => "L'installer è terminato ma il driver PawnIO non risulta ancora attivo. Può servire un riavvio.",
            _ => "Stato di PawnIO non riconosciuto."
        });
        return string.IsNullOrWhiteSpace(detail) ? text : text + " (" + detail + ")";
    }

    private async Task InstallPawnIoAsync()
    {
        if (_pawnIoInstallRunning) return;
        _pawnIoInstallRunning = true;
        _pawnIoButton.IsEnabled = false;
        _pawnIoBar.Value = 0;
        _pawnIoBar.Visibility = Visibility.Visible;
        _pawnIoStatusText.Visibility = Visibility.Visible;
        _pawnIoStatusText.Text = T("Preparazione del download…");

        try
        {
            var progress = new Progress<PawnIoInstallProgress>(update =>
            {
                _pawnIoBar.Value = update.Fraction;
                _pawnIoStatusText.Text = T(update.Message);
            });

            var owner = WindowNative.GetWindowHandle(this);
            var service = new PawnIoInstallService();
            var result = await service.RunAsync(owner, progress, CancellationToken.None);

            _pawnIoStatusText.Text = result.Ok
                ? DescribePawnIoStatus(result.Status)
                : DescribePawnIoReason(result.Reason, result.Detail);

            _pawnIoInstallRunning = false;
            ApplyPawnIoStatus(result.Status);
            if (!result.Ok)
            {
                // Lo stato del driver resta quello vero: la card non finge riuscite.
                _pawnIoStatusText.Text = DescribePawnIoReason(result.Reason, result.Detail)
                    + "\n" + DescribePawnIoStatus(result.Status);
            }
        }
        catch (Exception ex)
        {
            Diag.Crash("InstallPawnIoAsync", ex);
            _pawnIoStatusText.Text = T("Il download di PawnIO non è riuscito.") + " (" + ex.GetType().Name + ")";
        }
        finally
        {
            _pawnIoInstallRunning = false;
            _pawnIoBar.Visibility = Visibility.Collapsed;
            _pawnIoButton.IsEnabled = PawnIoInstallService.IsAvailable;
        }
    }
}
