using Playhub.Importing;

namespace Playhub;

public sealed partial class MainWindow
{
    private bool _densityReady;
    private readonly Dictionary<Microsoft.UI.Xaml.Controls.StackPanel, GameCardViewport<Models.UwpGameEntry>> _pcCardViewports = new();
    private void ClosePcCardViewports()
    {
        foreach (var viewport in _pcCardViewports.Values) viewport.Dispose();
        _pcCardViewports.Clear();
    }
    private void InitializeLibraryDensity()
    {
        GameCardLayout.SetSizeStep(_settings.GameCoverSizeStep);
        GameCardLayout.DensityChanged -= LibraryDensityChanged;
        GameCardLayout.DensityChanged += LibraryDensityChanged;
        _densityReady = true;
    }
    private async void LibraryDensityChanged(object? sender, EventArgs args)
    {
        if (!_densityReady) return;
        _settings.GameCoverSizeStep = GameCardLayout.SizeStep;
        // Reflow existing data only. Never scan or refetch artwork on a size change.
        RenderUwpGames(); RenderExecutableGames(); RenderEpicGames(); RenderGogGames();
        await SaveSettingsSilentlyAsync();
    }
    private void CloseLibraryDensity()
    {
        _densityReady = false;
        GameCardLayout.DensityChanged -= LibraryDensityChanged;
    }
}
