using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly Dictionary<(string Path, int Width), BitmapImage> _pluginBitmapCache = new();
    private readonly Dictionary<string, DateTimeOffset> _pluginPreviewFailures = new(StringComparer.Ordinal);

    private Image? CreatePluginPreviewImage(DeckyPluginInfo plugin, int width)
    {
        var path = PluginImagePath(plugin);
        if (path is null && plugin.IsPlayhubPlugin) return null;
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        var resolving = false;
        var fallback = false;

        void ShowStoreMark()
        {
            path = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "DeckyStoreBadge.png");
            fallback = true;
            image.Stretch = Stretch.Uniform;
            image.Source = CachedPluginBitmap(path, width);
        }

        void RejectCurrentImage()
        {
            if (path is null || fallback) return;
            rejected.Add(path);
            if (_pluginPreviewFailures.Count >= 256)
                _pluginPreviewFailures.Remove(_pluginPreviewFailures.MinBy(pair => pair.Value).Key);
            _pluginPreviewFailures[path] = DateTimeOffset.UtcNow;
            foreach (var key in _pluginBitmapCache.Keys.Where(key => key.Path == path).ToArray())
                _pluginBitmapCache.Remove(key);
            image.Source = null;
        }

        async Task ResolveAsync()
        {
            if (resolving || !image.IsLoaded || fallback) return;
            resolving = true;
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var failure in _pluginPreviewFailures.Where(pair => now - pair.Value < TimeSpan.FromMinutes(5)))
                    rejected.Add(failure.Key);
                if (path is not null && !rejected.Contains(path))
                {
                    image.Source = CachedPluginBitmap(path, width);
                    return;
                }
                path = await _catalog.FindPluginPreviewAsync(plugin, rejected);
                if (!image.IsLoaded) return;
                if (path is null)
                {
                    // Use the bundled store mark only when no real repository image is reachable.
                    ShowStoreMark();
                    return;
                }
                image.Source = CachedPluginBitmap(path, width);
            }
            catch
            {
                RejectCurrentImage();
                ShowStoreMark();
            }
            finally { resolving = false; }
        }

        image.Loaded += async (_, _) => await ResolveAsync();
        image.ImageFailed += (_, _) =>
        {
            RejectCurrentImage();
            // Defer so a synchronous cached failure cannot re-enter source assignment.
            DispatcherQueue.TryEnqueue(async () => await ResolveAsync());
        };
        return image;
    }

    private BitmapImage CachedPluginBitmap(string path, int width)
    {
        var key = (path, width);
        if (_pluginBitmapCache.TryGetValue(key, out var bitmap)) return bitmap;
        // Keep decoded artwork through row recycling and navigation, with bounded retention.
        while (_pluginBitmapCache.Count >= 64)
            _pluginBitmapCache.Remove(_pluginBitmapCache.Keys.First());
        bitmap = new BitmapImage { DecodePixelWidth = width, UriSource = new Uri(path) };
        _pluginBitmapCache.Add(key, bitmap);
        return bitmap;
    }
}
