using Playhub.Integrations;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Playhub.Importing;

internal sealed record ArtworkEditorImageData(byte[] Bytes, uint Width, uint Height);

internal static class ArtworkEditorImageSource
{
    internal const int MaximumBytes = 25 * 1024 * 1024;

    internal static async Task<ArtworkEditorImageData> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute image path is required.");
        ApplicationIntegrationDataStore.GuardPath(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        long length = file.Length;
        if (length is < 1 or > MaximumBytes) throw new InvalidDataException("Image size exceeds preview bounds.");
        byte[] bytes = new byte[(int)length];
        await file.ReadExactlyAsync(bytes, cancellationToken);
        if (file.ReadByte() != -1) throw new InvalidDataException("Image changed while being read.");
        using var stream = await StreamAsync(bytes, cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        uint width = decoder.OrientedPixelWidth, height = decoder.OrientedPixelHeight;
        if (width is < 2 or > 8192 || height is < 2 or > 8192 || (ulong)width * height > 32 * 1024 * 1024)
            throw new InvalidDataException("Image dimensions exceed preview bounds.");
        return new(bytes, width, height);
    }

    internal static async Task<InMemoryRandomAccessStream> StreamAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using var writer = new DataWriter(stream.GetOutputStreamAt(0));
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            stream.Seek(0);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
