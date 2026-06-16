using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PlayhubSetup;

public partial class MainWindow : Window
{
    private readonly SetupMode _mode;
    private string _installDir = Installer.DefaultInstallDir;
    private bool _busy;
    private bool _launchAtEnd;

    public MainWindow(SetupMode mode)
    {
        _mode = mode;
        InitializeComponent();

        PathText.Text = _installDir;

        // Lista lingue (default: inglese, per tutti).
        foreach (var (code, native) in Loc.Languages)
            LangList.Items.Add(new ListBoxItem { Content = native, Tag = code });
        LangList.SelectedIndex = 0; // English

        if (_mode == SetupMode.Uninstall)
        {
            _installDir = Installer.ReadInstallDir();
            // Niente schermata lingua in disinstallazione.
            PanelLanguage.Visibility = Visibility.Collapsed;
            PanelReady.Visibility = Visibility.Visible;
            PathCard.Visibility = Visibility.Collapsed;
            OptionsPanel.Visibility = Visibility.Collapsed;
            BtnLangNext.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Visible;
            BtnPrimary.Visibility = Visibility.Visible;
            ChkRemoveData.Visibility = Visibility.Visible;
        }

        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            // Non avviare il trascinamento se il click è sul pulsante Chiudi
            // (altrimenti DragMove "mangia" il click e la X non funziona).
            if (e.OriginalSource is DependencyObject o && IsInside(o, BtnClose)) return;
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        BtnClose.Click += (_, _) => Close();
        BtnCancel.Click += (_, _) => Close();
        BtnChangePath.Click += (_, _) => ChangePath();
        BtnPrimary.Click += (_, _) => Start();
        BtnDone.Click += (_, _) => Finish();
        BtnLangNext.Click += (_, _) => ContinueFromLanguage();

        ApplyLanguage();
    }

    private void ContinueFromLanguage()
    {
        if (LangList.SelectedItem is ListBoxItem { Tag: string code })
            Loc.Lang = code;
        ApplyLanguage();
        PanelLanguage.Visibility = Visibility.Collapsed;
        PanelReady.Visibility = Visibility.Visible;
        BtnLangNext.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Visible;
        BtnPrimary.Visibility = Visibility.Visible;
    }

    private void ApplyLanguage()
    {
        VersionText.Text = Loc.T("Version") + " " + Installer.AppVersion;

        LangTitle.Text = Loc.T("LangTitle");
        LangSubtitle.Text = Loc.T("LangSubtitle");
        BtnLangNext.Content = Loc.T("Continue");

        FolderLabel.Text = Loc.T("Folder");
        BtnChangePath.Content = Loc.T("Change");
        ChkDesktop.Content = Loc.T("OptDesktop");
        ChkStartMenu.Content = Loc.T("OptStartMenu");
        ChkLaunchEnd.Content = Loc.T("OptLaunchEnd");
        ChkRemoveData.Content = Loc.T("OptRemoveData");
        BtnCancel.Content = Loc.T("Cancel");
        StatusText.Text = Loc.T("Preparing");

        if (_mode == SetupMode.Install)
        {
            ReadyTitle.Text = Loc.T("InstallTitle");
            ReadySubtitle.Text = Loc.T("InstallSubtitle");
            BtnPrimary.Content = Loc.T("Install");
        }
        else
        {
            ReadyTitle.Text = Loc.T("UninstallTitle");
            ReadySubtitle.Text = Loc.T("UninstallSubtitle");
            BtnPrimary.Content = Loc.T("Uninstall");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.ApplyDarkAcrylic(hwnd);
    }

    private void ChangePath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("ChooseFolder"),
            InitialDirectory = _installDir
        };
        if (dialog.ShowDialog(this) == true)
        {
            // Installa in una sottocartella "Playhub" se l'utente sceglie una cartella generica.
            var chosen = dialog.FolderName;
            _installDir = chosen.TrimEnd('\\').EndsWith("Playhub", StringComparison.OrdinalIgnoreCase)
                ? chosen
                : System.IO.Path.Combine(chosen, "Playhub");
            PathText.Text = _installDir;
        }
    }

    private async void Start()
    {
        if (_busy) return;
        _busy = true;
        ShowProgress();

        var progress = new Progress<(double Percent, string Status)>(p =>
        {
            Prog.Value = p.Percent;
            StatusText.Text = p.Status;
        });

        try
        {
            if (_mode == SetupMode.Install)
            {
                _launchAtEnd = ChkLaunchEnd.IsChecked == true;
                var options = new InstallOptions(
                    _installDir,
                    ChkDesktop.IsChecked == true,
                    ChkStartMenu.IsChecked == true,
                    Loc.Lang);
                await Installer.InstallAsync(options, progress);
            }
            else
            {
                await Installer.UninstallAsync(progress, ChkRemoveData.IsChecked == true);
            }

            ShowDone();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Finish()
    {
        if (_mode == SetupMode.Install && _launchAtEnd)
        {
            Installer.LaunchApp(_installDir);
        }
        Close();
    }

    // -------------------------------------------------------------- stati UI
    private void ShowProgress()
    {
        PanelReady.Visibility = Visibility.Collapsed;
        PanelDone.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Visible;
        ProgressTitle.Text = _mode == SetupMode.Install ? Loc.T("Installing") : Loc.T("Uninstalling");
        BtnLangNext.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;
        BtnPrimary.Visibility = Visibility.Collapsed;
        BtnDone.Visibility = Visibility.Collapsed;
        BtnClose.IsEnabled = false;
    }

    private void ShowDone()
    {
        PanelReady.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        PanelDone.Visibility = Visibility.Visible;
        BtnDone.Visibility = Visibility.Visible;
        BtnClose.IsEnabled = true;

        if (_mode == SetupMode.Install)
        {
            DoneTitle.Text = Loc.T("DoneTitle");
            DoneSub.Text = _launchAtEnd ? Loc.T("Launching") : Loc.T("DoneSub");
            BtnDone.Content = Loc.T("Finish");
        }
        else
        {
            DoneTitle.Text = Loc.T("UninstallDone");
            DoneSub.Text = Loc.T("UninstallDoneSub");
            BtnDone.Content = Loc.T("Close");
        }
    }

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }
        return false;
    }

    private void ShowError(string message)
    {
        PanelReady.Visibility = Visibility.Collapsed;
        PanelProgress.Visibility = Visibility.Collapsed;
        PanelDone.Visibility = Visibility.Visible;
        BtnDone.Visibility = Visibility.Visible;
        BtnClose.IsEnabled = true;
        DoneTitle.Text = Loc.T("Error");
        DoneSub.Text = message;
        BtnDone.Content = Loc.T("Close");
    }
}
