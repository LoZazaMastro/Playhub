using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Playhub.Importing;
using System.Collections.ObjectModel;

namespace Playhub.Importing;

/// <summary>Retains game data while creating native cards only for nearby viewport rows.</summary>
internal sealed class GameCardViewport<T> : IDisposable
{
    private readonly Func<T, FrameworkElement> _createCard;
    private readonly Action<FrameworkElement> _releaseCard;
    private readonly Func<double> _coverAspect;
    private readonly IReadOnlyList<T> _items;
    private readonly ObservableCollection<IReadOnlyList<T>> _rows = [];
    private readonly HashSet<ContentPresenter> _prepared = [];
    private int _columns;
    private double _aspect;
    private int _sizeStep;
    private double _cardWidth;
    private readonly double _initialWidth;
    private bool _disposed;
    private bool _reflowing;
    public ItemsRepeater View { get; }

    public GameCardViewport(IEnumerable<T> items, Func<T, FrameworkElement> createCard,
        Action<FrameworkElement> releaseCard, Func<double> coverAspect, double initialWidth)
    {
        _items = items.ToArray(); _createCard = createCard; _releaseCard = releaseCard; _coverAspect = coverAspect;
        _aspect = coverAspect(); _sizeStep = GameCardLayout.SizeStep;
        _cardWidth = GameCardLayout.CardWidth(initialWidth);
        _initialWidth = Math.Max(GameCardLayout.MinimumWidth, initialWidth);
        _columns = GameCardLayout.ColumnCount(initialWidth);
        View = new ItemsRepeater
        {
            ItemsSource = _rows,
            ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><ContentPresenter HorizontalContentAlignment=\"Stretch\" VerticalContentAlignment=\"Stretch\" /></DataTemplate>"),
            Layout = new StackLayout { Spacing = GameCardLayout.ColumnSpacing },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalCacheLength = .5
        };
        View.ElementPrepared += OnPrepared; View.ElementClearing += OnClearing;
        View.SizeChanged += OnSizeChanged; View.Loaded += OnLoaded; View.Unloaded += OnUnloaded;
        RebuildRows();
    }

    private void OnPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not ContentPresenter presenter) return;
        _prepared.Add(presenter); QueueRow(presenter, args.Index);
    }
    private void QueueRow(ContentPresenter presenter, int index)
    {
        if (_disposed || index < 0 || index >= _rows.Count) return;
        ReleaseRow(presenter.Content as Grid); presenter.Content = null;
        var ticket = new object(); presenter.Tag = ticket;
        var items = _rows[index]; var columns = _columns;
        var available = View.ActualWidth > 0 ? View.ActualWidth : _initialWidth;
        var width = GameCardLayout.CardWidth(available);
        presenter.MinHeight = Math.Max(1, width - 24) * _aspect + 220;
        presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
        // Event registration may pump messages. Construct controls outside the repeater's layout pass.
        View.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_disposed || !View.IsLoaded || !ReferenceEquals(presenter.Tag, ticket) || View.GetElementIndex(presenter) != index) return;
            var row = new Grid { ColumnSpacing = GameCardLayout.ColumnSpacing,
                Width = width * columns + GameCardLayout.ColumnSpacing * (columns - 1),
                HorizontalAlignment = HorizontalAlignment.Center };
            // An auto row takes the tallest card's natural size. Stretch each card
            // into that shared slot rather than centering a shorter card in it.
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < columns; column++) row.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
            for (var column = 0; column < items.Count; column++)
            {
                if (_disposed || !ReferenceEquals(presenter.Tag, ticket)) { ReleaseRow(row); return; }
                var card = _createCard(items[column]);
                card.HorizontalAlignment = HorizontalAlignment.Stretch;
                card.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(card, column); row.Children.Add(card);
            }
            if (_disposed || !ReferenceEquals(presenter.Tag, ticket)) { ReleaseRow(row); return; }
            presenter.Content = row; presenter.MinHeight = 0;
        });
    }
    private void OnClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not ContentPresenter presenter) return;
        _prepared.Remove(presenter); ReleasePresenter(presenter);
    }
    private void ReleasePresenter(ContentPresenter presenter)
    {
        presenter.Tag = null; ReleaseRow(presenter.Content as Grid); presenter.Content = null;
    }
    private void ReleaseRow(Grid? row)
    {
        if (row == null) return;
        foreach (var card in row.Children.OfType<FrameworkElement>().ToArray()) _releaseCard(card);
        row.Children.Clear();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        RefreshLayout();
    }
    internal void RefreshLayout()
    {
        if (_disposed || _reflowing) return;
        var columns = View.ActualWidth > 0 ? GameCardLayout.ColumnCount(View.ActualWidth) : _columns;
        var aspect = _coverAspect();
        var cardWidth = GameCardLayout.CardWidth(View.ActualWidth > 0 ? View.ActualWidth : _initialWidth);
        if (columns == _columns && _sizeStep == GameCardLayout.SizeStep &&
            Math.Abs(cardWidth - _cardWidth) < .001 && Math.Abs(aspect - _aspect) < .001) return;
        _columns = columns; _sizeStep = GameCardLayout.SizeStep;
        _cardWidth = cardWidth; _aspect = aspect; RebuildRows();
    }
    private void RebuildRows()
    {
        _reflowing = true;
        try
        {
            foreach (var presenter in _prepared.ToArray()) ReleasePresenter(presenter);
            _rows.Clear();
            for (var start = 0; start < _items.Count; start += _columns) _rows.Add(_items.Skip(start).Take(_columns).ToArray());
        }
        finally { _reflowing = false; }
    }
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        GameCardLayout.DensityChanged -= OnDensityChanged;
        GameCardLayout.DensityChanged += OnDensityChanged;
        RefreshLayout();
        foreach (var presenter in _prepared.ToArray())
            if (presenter.Content == null) QueueRow(presenter, View.GetElementIndex(presenter));
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        GameCardLayout.DensityChanged -= OnDensityChanged;
        foreach (var presenter in _prepared.ToArray()) ReleasePresenter(presenter);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        GameCardLayout.DensityChanged -= OnDensityChanged;
        foreach (var presenter in _prepared.ToArray()) ReleasePresenter(presenter);
        _prepared.Clear(); _rows.Clear();
        View.ElementPrepared -= OnPrepared; View.ElementClearing -= OnClearing;
        View.SizeChanged -= OnSizeChanged; View.Loaded -= OnLoaded; View.Unloaded -= OnUnloaded;
    }
    private void OnDensityChanged(object? sender, EventArgs args) => RefreshLayout();
#if PLAYHUB_UI_REVIEW
    internal int ReviewRealizedRows => _prepared.Count(presenter => presenter.Content is Grid);
    internal int ReviewRealizedCards => _prepared.Sum(presenter => (presenter.Content as Grid)?.Children.Count ?? 0);
#endif
}
