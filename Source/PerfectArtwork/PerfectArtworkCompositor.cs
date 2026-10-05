using System.Security.Cryptography;
using System.Text.Json.Nodes;
#if PLAYHUB_NATIVE_INTEGRATIONS
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
#endif

namespace Playhub.Integrations;

public sealed record PerfectArtworkResult(string Path, string PristineSourcePath, string Sha256, string SourceSha256, string LogoSha256, int Width, int Height);

public sealed class PerfectArtworkCompositor(string root)
{
    private readonly string _root = System.IO.Path.GetFullPath(root);
    private static readonly SemaphoreSlim Worker = new(1, 1);

    public async Task<PerfectArtworkResult> ComposeAsync(string stableIdentity, string target, string sourcePath, string logoPath,
        CancellationToken ct = default, string? pristineSourcePath = null, PerfectArtworkLayout? layout = null)
    {
        ApplicationIntegrationIdentity.Validate(stableIdentity);
        if (target is not ("hero" or "banner")) throw new ArgumentException("Unknown composition target.");
        ApplicationIntegrationDataStore.GuardPath(_root);
        await Worker.WaitAsync(ct);
        try
        {
            var store = new ApplicationIntegrationDataStore(_root);
            string category = "perfect_" + target;
            string directory = System.IO.Path.Combine(_root, "compositions", ApplicationIntegrationDataStore.Key(stableIdentity), target);
            ApplicationIntegrationDataStore.GuardPath(directory);
            var existing = await store.ReadCategoryAsync(stableIdentity, "artwork", ct);
            var previous = existing[category] as JsonObject ?? new JsonObject();
            byte[] current = await ReadAsync(sourcePath, ct);
            string currentHash = Hash(current);
            string provenance = System.IO.Path.Combine(directory, currentHash + ".provenance.json");
            if (pristineSourcePath is null && previous["sha256"]?.ToString() != currentHash && File.Exists(provenance))
            {
                ApplicationIntegrationDataStore.GuardPath(provenance);
                if (new FileInfo(provenance).Length > 16 * 1024) throw new IOException("Invalid composition provenance.");
                previous = JsonNode.Parse(await File.ReadAllBytesAsync(provenance, ct)) as JsonObject ?? throw new IOException("Invalid composition provenance.");
                if (previous["sha256"]?.ToString() != currentHash) throw new IOException("The composition provenance changed.");
            }
            if (pristineSourcePath is not null) current = await ReadAsync(pristineSourcePath, ct);
            else if (previous["sha256"]?.ToString() == currentHash)
            {
                string preserved = previous["pristineSourcePath"]?.ToString() ?? throw new IOException("The original artwork is unavailable.");
                GuardOwned(preserved, directory);
                current = await ReadAsync(preserved, ct);
                if (Hash(current) != previous["sourceSha256"]?.ToString()) throw new IOException("The original artwork changed.");
            }
            else if (System.IO.Path.GetFullPath(sourcePath).StartsWith(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The cached composition has no verified pristine source.");
            byte[] logo = await ReadAsync(logoPath, ct);
            var backgroundPixels = await DecodeAsync(current, ct);
            var logoPixels = await DecodeAsync(logo, ct);
            var placement = (layout ?? new PerfectArtworkLayout()).Normalize();
            var output = await Task.Run(() => PerfectArtworkPixels.Render(backgroundPixels, logoPixels, target, ct, placement), ct);
            byte[] encoded = await EncodeAsync(output, ct);
            string sourceHash = Hash(current), logoHash = Hash(logo), outputHash = Hash(encoded);
            ApplicationIntegrationDataStore.GuardPath(directory); Directory.CreateDirectory(directory);
            string original = System.IO.Path.Combine(directory, sourceHash + SourceExtension(current));
            string path = System.IO.Path.Combine(directory, outputHash + ".jpg");
            await PublishAsync(original, current, ct);
            await PublishAsync(path, encoded, ct);
            var info = new JsonObject
            {
                ["path"] = path, ["pristineSourcePath"] = original, ["sha256"] = outputHash,
                ["sourceSha256"] = sourceHash, ["logoSha256"] = logoHash, ["width"] = output.Width, ["height"] = output.Height,
                ["layout"] = System.Text.Json.JsonSerializer.SerializeToNode(placement)
            };
            string outputProvenance = System.IO.Path.Combine(directory, outputHash + ".provenance.json");
            // Identical output pixels may have equivalent original inputs; retain the first verified provenance.
            if (!File.Exists(outputProvenance)) await PublishAsync(outputProvenance, System.Text.Encoding.UTF8.GetBytes(info.ToJsonString()), ct);
            await store.PatchAsync(stableIdentity, "artwork", new JsonObject
            {
                [category] = info
            }, ct);
            return new(path, original, outputHash, sourceHash, logoHash, output.Width, output.Height);
        }
        finally { Worker.Release(); }
    }

    private static void GuardOwned(string path, string directory)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The original artwork is outside the app cache.");
        ApplicationIntegrationDataStore.GuardPath(full);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string SourceExtension(byte[] bytes) => bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) ? ".png"
        : bytes[0] == 0xff && bytes[1] == 0xd8 ? ".jpg"
        : bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8) ? ".webp"
        : throw new InvalidDataException("Unsupported original artwork format.");
    private static async Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        ApplicationIntegrationDataStore.GuardPath(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 16 or > 25 * 1024 * 1024) throw new InvalidDataException("Unsupported artwork file.");
        return await File.ReadAllBytesAsync(path, ct);
    }
    private static async Task<ArtworkPixels> DecodeAsync(byte[] bytes, CancellationToken ct)
    {
#if PLAYHUB_NATIVE_INTEGRATIONS
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(ct); await writer.FlushAsync().AsTask(ct); }
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        uint width = decoder.OrientedPixelWidth, height = decoder.OrientedPixelHeight;
        if (width is < 2 or > 8192 || height is < 2 or > 8192 || (ulong)width * height > 32 * 1024 * 1024) throw new InvalidDataException("Unsupported artwork dimensions.");
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(ct);
        var pixels = data.DetachPixelData();
        if (pixels.LongLength != (long)width * height * 4) throw new InvalidDataException("Artwork pixel decode was incomplete.");
        return new((int)width, (int)height, pixels);
#else
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes);
            using var image = System.Drawing.Image.FromStream(stream, true, true);
            if (image.Width is < 2 or > 8192 || image.Height is < 2 or > 8192 || (long)image.Width * image.Height > 32 * 1024 * 1024)
                throw new InvalidDataException("Unsupported artwork dimensions.");
            using var bitmap = new System.Drawing.Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            { graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; graphics.DrawImageUnscaled(image, 0, 0); }
            var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
                for (int y = 0; y < bitmap.Height; y++)
                { ct.ThrowIfCancellationRequested(); System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4); }
                return new ArtworkPixels(bitmap.Width, bitmap.Height, pixels);
            }
            finally { bitmap.UnlockBits(data); }
        }, ct);
#endif
    }
    private static async Task<byte[]> EncodeAsync(ArtworkPixels pixels, CancellationToken ct)
    {
#if PLAYHUB_NATIVE_INTEGRATIONS
        using var stream = new InMemoryRandomAccessStream();
        var properties = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(.92f, Windows.Foundation.PropertyType.Single) };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, properties).AsTask(ct);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)pixels.Width, (uint)pixels.Height, 96, 96, pixels.Bgra);
        await encoder.FlushAsync().AsTask(ct);
        if (stream.Size > 25 * 1024 * 1024) throw new InvalidDataException("Composed artwork exceeds the supported size.");
        stream.Seek(0); using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size).AsTask(ct); var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); return bytes;
#else
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = new System.Drawing.Bitmap(pixels.Width, pixels.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new(0, 0, pixels.Width, pixels.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < pixels.Height; y++)
                { ct.ThrowIfCancellationRequested(); System.Runtime.InteropServices.Marshal.Copy(pixels.Bgra, y * pixels.Width * 4, data.Scan0 + y * data.Stride, pixels.Width * 4); }
            }
            finally { bitmap.UnlockBits(data); }
            using var stream = new MemoryStream();
            using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
            parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
            bitmap.Save(stream, System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().Single(codec => codec.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid), parameters);
            ct.ThrowIfCancellationRequested();
            if (stream.Length > 25 * 1024 * 1024) throw new InvalidDataException("Composed artwork exceeds the supported size.");
            return stream.ToArray();
        }, ct);
#endif
    }
    private static async Task PublishAsync(string path, byte[] bytes, CancellationToken ct)
    {
        ApplicationIntegrationDataStore.GuardPath(path);
        if (File.Exists(path))
        {
            if (Hash(await File.ReadAllBytesAsync(path, ct)) != Hash(bytes)) throw new IOException("The cached artwork changed.");
            return;
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, ct); ct.ThrowIfCancellationRequested(); File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
