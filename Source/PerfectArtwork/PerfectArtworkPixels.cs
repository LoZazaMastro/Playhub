namespace Playhub.Integrations;

public sealed record ArtworkPixels(int Width, int Height, byte[] Bgra);
public readonly record struct ArtworkPixelBounds(int X,int Y,int Width,int Height);

/// <summary>Original raster implementation of Playhub's centered background and left-quarter logo layout.</summary>
public static class PerfectArtworkPixels
{
    public static ArtworkPixels Render(ArtworkPixels background, ArtworkPixels logo, string target, CancellationToken ct = default, PerfectArtworkLayout? layout = null)
    {
        Validate(background); Validate(logo);
        var placement=(layout ?? new PerfectArtworkLayout()).Normalize();
        var (width, height) = target switch { "hero" => (3840, 1240), "banner" => (1926, 900), _ => throw new ArgumentException("Unknown composition target.") };
        var output = new byte[checked(width * height * 4)];
        double scale = Math.Max((double)width / background.Width, (double)height / background.Height)*placement.BackgroundScale/100;
        double left = (width - background.Width * scale)*placement.BackgroundX/100, top = (height - background.Height * scale)*placement.BackgroundY/100;
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                var pixel = Sample(background, (x + .5 - left) / scale - .5, (y + .5 - top) / scale - .5);
                int at = (y * width + x) * 4;
                output[at] = (byte)(pixel.B * pixel.A / 255*placement.BackgroundOpacity/100); output[at + 1] = (byte)(pixel.G * pixel.A / 255*placement.BackgroundOpacity/100);
                output[at + 2] = (byte)(pixel.R * pixel.A / 255*placement.BackgroundOpacity/100); output[at + 3] = 255;
            }
        }
        if(!placement.ShowLogo)return new(width,height,output);
        var bounds=LogoBounds(logo,ct);
        int x0=bounds.X,y0=bounds.Y,sw=bounds.Width,sh=bounds.Height;
        double logoScale = Math.Min(width * .28 / sw, height * .72 / sh)*placement.LogoScale/100;
        double lw = sw * logoScale, lh = sh * logoScale, lx = width * placement.LogoX/100 - lw / 2, ly = height * placement.LogoY/100 - lh / 2;
        int xa = Math.Max(0, (int)Math.Floor(lx)), xb = Math.Min(width, (int)Math.Ceiling(lx + lw));
        int ya = Math.Max(0, (int)Math.Floor(ly)), yb = Math.Min(height, (int)Math.Ceiling(ly + lh));
        var mask = new byte[width * height];
        for (int y = ya; y < yb; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = xa; x < xb; x++) mask[y * width + x] = (byte)Sample(logo, x0 + (x + .5 - lx) / logoScale - .5, y0 + (y + .5 - ly) / logoScale - .5).A;
        }
        int blur = (int)Math.Round(width * .006*placement.ShadowBlur/40), offset = (int)Math.Round(width * .003);
        // Three separable box passes approximate a soft Gaussian shadow in linear work.
        if (blur>0) for (int pass = 0; pass < 3; pass++) mask = Blur(mask, width, height, Math.Max(1, blur / 3), ct);
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                double shadow = y >= offset ? mask[(y - offset) * width + x] / 255d * placement.ShadowOpacity/100 : 0;
                for (int channel = 0; channel < 3; channel++) output[at + channel] = (byte)(output[at + channel] * (1 - shadow));
            }
        }
        for (int y = ya; y < yb; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = xa; x < xb; x++)
            {
                var pixel = Sample(logo, x0 + (x + .5 - lx) / logoScale - .5, y0 + (y + .5 - ly) / logoScale - .5);
                double alpha = pixel.A / 255d; int at = (y * width + x) * 4;
                output[at] = (byte)Math.Clamp(pixel.B * alpha + output[at] * (1 - alpha), 0, 255);
                output[at + 1] = (byte)Math.Clamp(pixel.G * alpha + output[at + 1] * (1 - alpha), 0, 255);
                output[at + 2] = (byte)Math.Clamp(pixel.R * alpha + output[at + 2] * (1 - alpha), 0, 255);
            }
        }
        return new(width, height, output);
    }

    public static ArtworkPixelBounds LogoBounds(ArtworkPixels logo,CancellationToken ct=default)
    {
        Validate(logo);
        int x0=logo.Width,y0=logo.Height,x1=-1,y1=-1;
        for(int y=0;y<logo.Height;y++)
        {
            ct.ThrowIfCancellationRequested();
            for(int x=0;x<logo.Width;x++) if(logo.Bgra[(y*logo.Width+x)*4+3]>8)
            { x0=Math.Min(x0,x); x1=Math.Max(x1,x); y0=Math.Min(y0,y); y1=Math.Max(y1,y); }
        }
        if(x1<x0||y1<y0) throw new InvalidDataException("The logo is empty.");
        x0=Math.Max(0,x0-2);y0=Math.Max(0,y0-2);x1=Math.Min(logo.Width-1,x1+2);y1=Math.Min(logo.Height-1,y1+2);
        return new(x0,y0,x1-x0+1,y1-y0+1);
    }

    private static void Validate(ArtworkPixels image)
    {
        if (image.Width is < 2 or > 8192 || image.Height is < 2 or > 8192 || (long)image.Width * image.Height > 32 * 1024 * 1024 || image.Bgra.LongLength != (long)image.Width * image.Height * 4)
            throw new InvalidDataException("Unsupported artwork pixels.");
    }
    private static (double B, double G, double R, double A) Sample(ArtworkPixels image, double x, double y)
    {
        x = Math.Clamp(x, 0, image.Width - 1); y = Math.Clamp(y, 0, image.Height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
        double fx = x - x0, fy = y - y0;
        var offsets = (A: (y0 * image.Width + x0) * 4, B: (y0 * image.Width + x1) * 4, C: (y1 * image.Width + x0) * 4, D: (y1 * image.Width + x1) * 4);
        double Weight(int offset, int channel) => image.Bgra[offset + channel] * image.Bgra[offset + 3] / 255d;
        double Alpha() => (image.Bgra[offsets.A + 3] * (1 - fx) + image.Bgra[offsets.B + 3] * fx) * (1 - fy)
            + (image.Bgra[offsets.C + 3] * (1 - fx) + image.Bgra[offsets.D + 3] * fx) * fy;
        double alpha = Alpha();
        double Component(int channel) => alpha <= 0 ? 0 : ((Weight(offsets.A, channel) * (1 - fx) + Weight(offsets.B, channel) * fx) * (1 - fy)
            + (Weight(offsets.C, channel) * (1 - fx) + Weight(offsets.D, channel) * fx) * fy) * 255 / alpha;
        return (Component(0), Component(1), Component(2), alpha);
    }
    private static byte[] Blur(byte[] input, int width, int height, int radius, CancellationToken ct)
    {
        var horizontal = new byte[input.Length]; var output = new byte[input.Length]; int count = 2 * radius + 1;
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested(); int sum = 0;
            for (int x = 0; x <= radius && x < width; x++) sum += input[y * width + x];
            for (int x = 0; x < width; x++)
            {
                horizontal[y * width + x] = (byte)(sum / count);
                if (x - radius >= 0) sum -= input[y * width + x - radius];
                if (x + radius + 1 < width) sum += input[y * width + x + radius + 1];
            }
        }
        for (int x = 0; x < width; x++)
        {
            ct.ThrowIfCancellationRequested(); int sum = 0;
            for (int y = 0; y <= radius && y < height; y++) sum += horizontal[y * width + x];
            for (int y = 0; y < height; y++)
            {
                output[y * width + x] = (byte)(sum / count);
                if (y - radius >= 0) sum -= horizontal[(y - radius) * width + x];
                if (y + radius + 1 < height) sum += horizontal[(y + radius + 1) * width + x];
            }
        }
        return output;
    }
}
