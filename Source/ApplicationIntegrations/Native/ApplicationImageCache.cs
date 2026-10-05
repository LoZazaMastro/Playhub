using System.Security.Cryptography;
using System.Text;
using Playhub.Integrations;

namespace Playhub.Emulation.Workbench;

/// <summary>Validates acquired image bytes before publishing an app-owned immutable file.</summary>
public sealed class ApplicationImageCache(string root)
{
    private static readonly HttpClient Cdn = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    public static async Task<byte[]> DownloadSteamGridDbAsync(string url,CancellationToken ct)
    {
        if (!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !uri.Host.Equals("cdn2.steamgriddb.com",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported image CDN.");
        using var response = await Cdn.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 25 * 1024 * 1024) throw new InvalidDataException("Image exceeds the supported size.");
        using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer,ct)) > 0) { if(output.Length+count > 25 * 1024 * 1024) throw new InvalidDataException("Image exceeds the supported size."); output.Write(buffer,0,count); }
        return output.ToArray();
    }
    public async Task<string> SaveAsync(string identity, string category, byte[] bytes, CancellationToken ct)
    {
        ApplicationIntegrationIdentity.Validate(identity);
        if (category is not ("cover" or "banner" or "hero" or "logo" or "icon" or "curtain")) throw new ArgumentException("Unknown image category.");
        if (bytes.Length is < 16 or > 25 * 1024 * 1024) throw new InvalidDataException("Image exceeds the supported size.");
        string extension = bytes.AsSpan(0,8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) ? ".png"
            : bytes[0] == 0xff && bytes[1] == 0xd8 ? ".jpg"
            : bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8) ? ".webp"
            : throw new InvalidDataException("Unsupported image format.");
        if (extension == ".png") ValidatePng(bytes);
#if PLAYHUB_NATIVE_INTEGRATIONS
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(ct); await writer.FlushAsync().AsTask(ct); }
        var image = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(ct);
        uint width = image.PixelWidth, height = image.PixelHeight;
#else
        using var input = new MemoryStream(bytes);
        using var image = System.Drawing.Image.FromStream(input,false,true);
        uint width = (uint)image.Width, height = (uint)image.Height;
#endif
        if (width is < 2 or > 8192 || height is < 2 or > 8192) throw new InvalidDataException("Unsupported image dimensions.");
#if PLAYHUB_NATIVE_INTEGRATIONS
        if ((ulong)width * height > 32 * 1024 * 1024) throw new InvalidDataException("Decoded image exceeds the supported size.");
        var pixels = await image.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Straight,new Windows.Graphics.Imaging.BitmapTransform(),
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage).AsTask(ct);
        if (pixels.DetachPixelData().LongLength != (long)width * height * 4) throw new InvalidDataException("Image pixel decode was incomplete.");
#endif
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "\0" + category + "\0" + Convert.ToHexString(SHA256.HashData(bytes))))).ToLowerInvariant();
        string file = Path.Combine(Path.GetFullPath(root),"images",key+extension);
        ApplicationIntegrationDataStore.GuardPath(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary,bytes,ct); ct.ThrowIfCancellationRequested(); File.Move(temporary,file,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(file,ct)).AsSpan().SequenceEqual(SHA256.HashData(bytes))) throw new IOException("Acquired image did not pass readback.");
        return file;
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0,256).Select(value =>
    {
        uint crc = (uint)value; for(int n=0;n<8;n++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; return crc;
    }).ToArray();
    private static void ValidatePng(byte[] bytes)
    {
        int offset = 8; bool data = false, end = false;
        while(offset + 12 <= bytes.Length)
        {
            uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4));
            if (length > (uint)(bytes.Length - offset - 12)) throw new InvalidDataException("PNG chunk is truncated.");
            int count = (int)length;
            var type = bytes.AsSpan(offset+4,4);
            if(offset == 8 && (!type.SequenceEqual("IHDR"u8) || count != 13)) throw new InvalidDataException("PNG header is missing.");
            uint crc = 0xffffffffu;
            foreach(byte value in bytes.AsSpan(offset+4,count+4)) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            if ((crc ^ 0xffffffffu) != System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+8+count,4))) throw new InvalidDataException("PNG chunk checksum is invalid.");
            data |= type.SequenceEqual("IDAT"u8);
            offset += count + 12;
            if(type.SequenceEqual("IEND"u8)) { if(count != 0) throw new InvalidDataException("Invalid PNG end marker."); end = true; break; }
        }
        if(!data || !end || offset != bytes.Length) throw new InvalidDataException("PNG image is incomplete.");
    }
}
