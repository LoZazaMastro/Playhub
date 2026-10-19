using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Playhub.Integrations;
using Windows.Foundation;

namespace Playhub.Importing;

public sealed record GameTitlePickerResult(GameTitleIdentity? Selection, bool Remove = false, IReadOnlyList<GameTitleIdentity>? AllSelections = null);

public static class GameTitleIdentityPicker
{
    /// <summary>Search for the title used by the artwork editor.</summary>
    public static IDisposable Attach(Button anchor, GameInfoContext game, string initialProvider, Func<string,string> text,
        CancellationToken token, Func<GameTitleIdentity,Task>? changed = null, Func<Task>? beforeChange = null,
        Func<UIElement>? connectionCard = null, Func<bool>? isConnected = null)
    {
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationTokenSource? request = null;
        bool closed = false, busy = false, isOpen = false; int generation = 0, openingGeneration = 0, queryRevision = 0;
        TaskCompletionSource<bool>? hideCompletion = null;
        if (initialProvider != "steamgriddb") throw new ArgumentException("Choose SteamGridDB.");
        string currentProvider = initialProvider;
        var heading = new TextBlock { Text = "Artwork · SteamGridDB",
            FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var query = new TextBox { PlaceholderText = text("Cerca titolo"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var find = new Button { Content = text("Cerca") };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition());
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(query); Grid.SetColumn(find,1); searchRow.Children.Add(find);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, Visibility = Visibility.Collapsed };
        var progress = new ProgressRing { Width = 28, Height = 28, Visibility = Visibility.Collapsed };
        var results = new ListView { IsItemClickEnabled = false, SelectionMode = ListViewSelectionMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var resultStyle = new Style(typeof(ListViewItem));
        resultStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        results.ItemContainerStyle = resultStyle;
        ScrollViewer.SetHorizontalScrollMode(results, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        var stage = new Grid(); stage.Children.Add(results); stage.Children.Add(progress);
        var connection = connectionCard is null ? null : new ScrollViewer { Content = connectionCard(),
            Visibility = Visibility.Collapsed, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (connection is not null) stage.Children.Add(connection);
        var body = new Grid { RowSpacing = 12, Width = 620, Height = 360 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(heading); Grid.SetRow(searchRow,1); body.Children.Add(searchRow);
        Grid.SetRow(notice,2); body.Children.Add(notice); Grid.SetRow(stage,3); body.Children.Add(stage);
        var flyout = new Flyout { Content = body, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        anchor.Flyout = flyout;
        anchor.IsEnabled = game.SearchTitlesAsync is not null && game.RefetchTitleAsync is not null;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(anchor,text("Cerca di nuovo"));
        async Task SearchAsync()
        {
            if (closed || !isOpen || busy || game.SearchTitlesAsync is null) return;
            if (connection is not null)
            {
                bool ready = isConnected?.Invoke() != false;
                connection.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
                results.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
                if (!ready) return;
            }
            int revision = ++generation;
            request?.Cancel(); request?.Dispose(); request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var ct = request.Token; string provider = currentProvider, title = query.Text.Trim();
            results.Items.Clear(); notice.Visibility = Visibility.Collapsed;
            progress.IsActive = true; progress.Visibility = Visibility.Visible;
            try
            {
                var found = await game.SearchTitlesAsync(provider,title,ct);
                if (closed || revision != generation || ct.IsCancellationRequested) return;
                foreach (var candidate in found)
                {
                    var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(6,10,6,10) };
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock { Text = candidate.Title, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None });
                    var year = new TextBlock { Text = candidate.Year?.ToString() ?? "", Opacity = .65 };
                    Grid.SetColumn(year,1); row.Children.Add(year);
                    var item = new Button { Content = row, Tag = candidate, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8), MinWidth = 0,
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,candidate.Title + " " + candidate.Year);
                    item.Click += async (_, _) => await ApplyAsync(candidate);
                    results.Items.Add(item);
                }
                if (found.Count == 0) { notice.Text = text("Nessun risultato trovato."); notice.Visibility = Visibility.Visible; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception) { if (!closed && revision == generation) { notice.Text = text("Ricerca non riuscita. Riprova."); notice.Visibility = Visibility.Visible; } }
            finally { if (!closed && revision == generation) { progress.IsActive = false; progress.Visibility = Visibility.Collapsed; } }
        }
        async Task ApplyAsync(GameTitleIdentity selected)
        {
            if (busy || closed || selected.Provider != initialProvider) return;
            busy = true; ++generation; request?.Cancel(); query.IsEnabled = false; find.IsEnabled = false; results.IsEnabled = false;
            progress.IsActive = true; progress.Visibility = Visibility.Visible;
            // A fast failure can arrive before Hide's native close animation ends.
            // Wait for Closed before refetching so a retry can reliably reopen.
            var hidden = isOpen ? (hideCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task : Task.CompletedTask;
            flyout.Hide();
            try
            {
                await hidden.WaitAsync(lifetime.Token);
                if (beforeChange is not null) await beforeChange();
                lifetime.Token.ThrowIfCancellationRequested();
                await game.RefetchTitleAsync!(selected,lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                query.Text = selected.Title;
                if (changed is not null) await changed(selected);
                flyout.Hide();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                if (!closed && !lifetime.IsCancellationRequested)
                {
                    flyout.ShowAt(anchor);
                    notice.Text = text("Ricerca non riuscita. Riprova."); notice.Visibility = Visibility.Visible;
                }
            }
            finally { busy = false; if (!closed) { query.IsEnabled = true; find.IsEnabled = true; results.IsEnabled = true; progress.IsActive = false; progress.Visibility = Visibility.Collapsed; } }
        }
        find.Click += async (_,_) => await SearchAsync();
        query.TextChanged += (_,_) => ++queryRevision;
        query.KeyDown += async (_,args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await SearchAsync(); } };
        flyout.Opening += (_, _) =>
        {
            if (anchor.XamlRoot is { } root) { body.Width = Math.Min(620,Math.Max(240,root.Size.Width - 96)); body.Height = Math.Min(360,Math.Max(180,root.Size.Height - 180)); }
            var presenterStyle=new Style(typeof(FlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,0d));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty,body.Width+48));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty,body.Width+48));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty,body.Height+48));
            flyout.FlyoutPresenterStyle=presenterStyle;
        };
        flyout.Opened += async (_,_) =>
        {
            if (closed) return;
            isOpen = true;
            if (busy) return;
            int opening = ++openingGeneration, originalQueryRevision = queryRevision;
            try
            {
                string savedQuery = game.ReadTitleQueryAsync is not null
                    ? await game.ReadTitleQueryAsync(initialProvider,lifetime.Token) : game.Title;
                if (closed || !isOpen || opening != openingGeneration || originalQueryRevision != queryRevision) return;
                query.Text = savedQuery; await SearchAsync();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception) { if (!closed && isOpen && opening == openingGeneration) { notice.Text = text("Ricerca non riuscita. Riprova."); notice.Visibility = Visibility.Visible; } }
        };
        flyout.Closed += (_,_) =>
        {
            isOpen = false; ++openingGeneration;
            var completion = hideCompletion; hideCompletion = null; completion?.TrySetResult(true);
            if (!closed && !busy) { ++generation; request?.Cancel(); }
        };
        return new PickerLifetime(() => { closed = true; ++generation; lifetime.Cancel(); request?.Cancel(); request?.Dispose(); flyout.Hide(); lifetime.Dispose(); anchor.Flyout = null; });
    }
    private sealed class PickerLifetime(Action close) : IDisposable
    {
        private Action? _close = close;
        public void Dispose() => Interlocked.Exchange(ref _close,null)?.Invoke();
    }

    public static async Task<GameTitlePickerResult> ShowAsync(XamlRoot root, string title, Func<string,string> text,
        Func<string,string,CancellationToken,Task<IReadOnlyList<GameTitleIdentity>>> search,
        CancellationToken token, bool allowRemove = false, Func<string,string>? initialQuery = null,
        Func<UIElement>? connectionCard = null, Func<bool>? isConnected = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationTokenSource? request = null; int generation = 0; bool closed = false, remove = false;
        var provider = new TextBlock { Text = "Artwork · SteamGridDB", FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var selections = new Dictionary<string,GameTitleIdentity>(); const string currentProvider = "steamgriddb";
        var query = new TextBox { Text = initialQuery?.Invoke(currentProvider) ?? title, PlaceholderText = text("Cerca titolo"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var find = new Button { Content = text("Cerca") };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); searchRow.ColumnDefinitions.Add(new ColumnDefinition()); searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(provider); Grid.SetColumn(query,1); searchRow.Children.Add(query); Grid.SetColumn(find,2); searchRow.Children.Add(find);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, HorizontalAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var progress = new ProgressRing { Width = 36, Height = 36, Visibility = Visibility.Collapsed };
        var notice = new TextBlock { Opacity = .7, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var body = new Grid { MinWidth = 0, Height = 425, RowSpacing = 12 };
        searchRow.Children.Remove(provider);searchRow.ColumnDefinitions.RemoveAt(0);Grid.SetColumn(query,0);Grid.SetColumn(find,1);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(provider);Grid.SetRow(searchRow,1);body.Children.Add(searchRow); Grid.SetRow(notice,2); body.Children.Add(notice);
        var stage = new Grid(); stage.Children.Add(list); stage.Children.Add(progress); Grid.SetRow(stage,3); body.Children.Add(stage);
        var connection = connectionCard is null ? null : new ScrollViewer { Content = connectionCard(),
            Visibility = Visibility.Collapsed, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (connection is not null) stage.Children.Add(connection);
        var dialog = new ContentDialog { XamlRoot = root, Title = string.Format(text("Cerca di nuovo — {0}"),title), Content = body,
            PrimaryButtonText = text("Usa risultato"), CloseButtonText = text("Chiudi"), IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
        void Resize()
        {
            double width = Math.Min(760, Math.Max(240, root.Size.Width - 48));
            double height = Math.Max(240, root.Size.Height - 48);
            dialog.Resources["ContentDialogMinWidth"] = width;
            dialog.Resources["ContentDialogMaxWidth"] = width;
            body.Width = Math.Max(180, width - 64);
            var surface = NativeContentDialogLayout.ResizeSurface(dialog, width, height);
            var viewport = NativeContentDialogLayout.FindContentViewport(dialog);
            double budget = surface is not null && viewport is not null
                ? NativeContentDialogLayout.ContentHeightBudget(surface, viewport, body, height) : height - 160;
            body.Height = Math.Max(100, Math.Min(425, double.IsFinite(budget) ? budget : height - 160));
        }
        TypedEventHandler<XamlRoot, XamlRootChangedEventArgs> resized = (_, _) => Resize();
        Resize();
        root.Changed += resized;
        if (allowRemove)
        {
            dialog.SecondaryButtonText = text("Rimuovi risultato");
            dialog.SecondaryButtonClick += (_,_) => remove = true;
        }
        async Task LoadAsync()
        {
            int current = ++generation; request?.Cancel(); request?.Dispose(); request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var ct = request.Token; list.Items.Clear(); dialog.IsPrimaryButtonEnabled = selections.Count>0; notice.Visibility = Visibility.Collapsed;
            if (connection is not null)
            {
                bool ready = currentProvider != "steamgriddb" || isConnected?.Invoke() != false;
                connection.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
                list.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
                if (!ready) { progress.IsActive = false; progress.Visibility = Visibility.Collapsed; return; }
            }
            progress.IsActive = true; progress.Visibility = Visibility.Visible;
            try
            {
                var options = await search(currentProvider,query.Text,ct);
                if (closed || current != generation || ct.IsCancellationRequested) return;
                foreach (var option in options)
                {
                    var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(6,10,6,10) };
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock { Text = option.Title, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None });
                    var year = new TextBlock { Text = option.Year?.ToString() ?? "", Opacity = .65 }; Grid.SetColumn(year,1); row.Children.Add(year);
                    list.Items.Add(new ListViewItem { Content = row, Tag = option, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                }
                if (options.Count == 0) { notice.Text = text("Nessun risultato trovato."); notice.Visibility = Visibility.Visible; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception)
            {
                if (!closed && current == generation) { notice.Text = text("Ricerca non riuscita. Riprova."); notice.Visibility = Visibility.Visible; }
            }
            finally { if (!closed && current == generation) { progress.IsActive = false; progress.Visibility = Visibility.Collapsed; } }
        }
        list.SelectionChanged += (_,_) => { if((list.SelectedItem as ListViewItem)?.Tag is GameTitleIdentity selected){selections[selected.Provider]=selected;query.Text=selected.Title;}dialog.IsPrimaryButtonEnabled=selections.Count>0; };
        find.Click += async (_,_) => await LoadAsync();
        query.KeyDown += async (_,args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await LoadAsync(); } };
        dialog.Opened += async (_,_) => { Resize(); await LoadAsync(); };
        dialog.Closed += (_,_) => { closed = true; ++generation; lifetime.Cancel(); };
        using var registration = token.Register(() => root.Content.DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            var response = await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return new(response == ContentDialogResult.Primary ? selections.Values.FirstOrDefault() : null,remove,response == ContentDialogResult.Primary?selections.Values.ToArray():null);
        }
        finally { root.Changed -= resized; closed = true; ++generation; lifetime.Cancel(); request?.Cancel(); request?.Dispose(); }
    }
}
