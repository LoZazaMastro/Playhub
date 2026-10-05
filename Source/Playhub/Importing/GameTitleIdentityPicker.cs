using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Playhub.Integrations;

namespace Playhub.Importing;

public sealed record GameTitlePickerResult(GameTitleIdentity? Selection, bool Remove = false, IReadOnlyList<GameTitleIdentity>? AllSelections = null);

public static class GameTitleIdentityPicker
{
    public static async Task<GameTitlePickerResult> ShowAsync(XamlRoot root, string title, Func<string,string> text,
        Func<string,string,CancellationToken,Task<IReadOnlyList<GameTitleIdentity>>> search,
        CancellationToken token, bool allowRemove = false, Func<string,string>? initialQuery = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationTokenSource? request = null; int generation = 0; bool closed = false, remove = false;
        var provider = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        provider.Items.Add(new SelectorBarItem { Text = "Playhub Metadata", Tag = "ign" });
        provider.Items.Add(new SelectorBarItem { Text = "Artwork", Tag = "steamgriddb" }); provider.SelectedItem = provider.Items[0];
        var queries = new Dictionary<string,string> { ["ign"] = initialQuery?.Invoke("ign") ?? title,["steamgriddb"] = initialQuery?.Invoke("steamgriddb") ?? title };
        var selections = new Dictionary<string,GameTitleIdentity>(); string currentProvider = "ign";
        var query = new TextBox { Text = queries["ign"], PlaceholderText = text("Cerca titolo"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var find = new Button { Content = text("Cerca") };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); searchRow.ColumnDefinitions.Add(new ColumnDefinition()); searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(provider); Grid.SetColumn(query,1); searchRow.Children.Add(query); Grid.SetColumn(find,2); searchRow.Children.Add(find);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, HorizontalAlignment = HorizontalAlignment.Stretch };
        var progress = new ProgressRing { Width = 36, Height = 36, Visibility = Visibility.Collapsed };
        var notice = new TextBlock { Opacity = .7, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var body = new Grid { Width = 700, Height = 425, RowSpacing = 12 };
        searchRow.Children.Remove(provider);searchRow.ColumnDefinitions.RemoveAt(0);Grid.SetColumn(query,0);Grid.SetColumn(find,1);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(provider);Grid.SetRow(searchRow,1);body.Children.Add(searchRow); Grid.SetRow(notice,2); body.Children.Add(notice);
        var stage = new Grid(); stage.Children.Add(list); stage.Children.Add(progress); Grid.SetRow(stage,3); body.Children.Add(stage);
        var dialog = new ContentDialog { XamlRoot = root, Title = string.Format(text("Cerca di nuovo — {0}"),title), Content = body,
            PrimaryButtonText = text("Usa risultato"), CloseButtonText = text("Chiudi"), IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
        dialog.Resources["ContentDialogMinWidth"] = 760d; dialog.Resources["ContentDialogMaxWidth"] = 760d;
        if (allowRemove)
        {
            dialog.SecondaryButtonText = text("Rimuovi risultato");
            dialog.SecondaryButtonClick += (_,_) => remove = true;
        }
        async Task LoadAsync()
        {
            int current = ++generation; request?.Cancel(); request?.Dispose(); request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var ct = request.Token; list.Items.Clear(); dialog.IsPrimaryButtonEnabled = selections.Count>0; notice.Visibility = Visibility.Collapsed;
            progress.IsActive = true; progress.Visibility = Visibility.Visible;
            try
            {
                var options = await search(currentProvider,query.Text,ct);
                if (closed || current != generation || ct.IsCancellationRequested) return;
                foreach (var option in options)
                {
                    var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(6,10,6,10) };
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock { Text = option.Title, TextTrimming = TextTrimming.CharacterEllipsis });
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
        list.SelectionChanged += (_,_) => { if((list.SelectedItem as ListViewItem)?.Tag is GameTitleIdentity selected){selections[selected.Provider]=selected;query.Text=selected.Title;queries[selected.Provider]=selected.Title;}dialog.IsPrimaryButtonEnabled=selections.Count>0; };
        find.Click += async (_,_) => await LoadAsync();
        query.KeyDown += async (_,args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await LoadAsync(); } };
        provider.SelectionChanged += async (_,_) => { queries[currentProvider]=query.Text;currentProvider=provider.SelectedItem?.Tag as string ?? "ign";query.Text=queries[currentProvider];await LoadAsync(); };
        dialog.Opened += async (_,_) => await LoadAsync();
        dialog.Closed += (_,_) => { closed = true; ++generation; lifetime.Cancel(); };
        using var registration = token.Register(() => root.Content.DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            var response = await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return new(response == ContentDialogResult.Primary ? selections.Values.FirstOrDefault() : null,remove,response == ContentDialogResult.Primary?selections.Values.ToArray():null);
        }
        finally { closed = true; ++generation; lifetime.Cancel(); request?.Cancel(); request?.Dispose(); }
    }
}
