using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GamingMode.Services;

// Colori dominanti dell'icona di un'applicazione: servono a costruire lo sfondo
// del banner quando non esiste una grafica ufficiale. L'icona viene ridotta a
// pochi pixel e i colori vengono raggruppati per tonalita', tenendo quelli piu'
// vivi: la media pura darebbe sempre un grigio spento.
public sealed record OverlayIconColors(Color Primary, Color Secondary);

// Ritaglio del bordo trasparente di un'icona.
//
// I loghi delle app del Microsoft Store hanno un margine vuoto molto ampio
// intorno al disegno: messi accanto all'icona di un programma, a parita' di
// riquadro, sembrano meta'. Qui si trova il rettangolo dei pixel davvero
// disegnati e si tiene solo quello, cosi' tutte le icone occupano lo stesso
// spazio visivo.
public static class OverlayIconTrim
{
	public static ImageSource Trim(ImageSource source)
	{
		if (source is not BitmapSource bitmap) return source;
		try
		{
			FormatConvertedBitmap converted = new(bitmap, PixelFormats.Bgra32, null, 0);
			int width = converted.PixelWidth;
			int height = converted.PixelHeight;
			if (width < 4 || height < 4) return source;
			int stride = width * 4;
			byte[] pixels = new byte[stride * height];
			converted.CopyPixels(pixels, stride, 0);

			int left = width;
			int top = height;
			int right = -1;
			int bottom = -1;
			for (int y = 0; y < height; y++)
			{
				int row = y * stride;
				for (int x = 0; x < width; x++)
				{
					if (pixels[row + (x * 4) + 3] < 24) continue;
					if (x < left) left = x;
					if (x > right) right = x;
					if (y < top) top = y;
					if (y > bottom) bottom = y;
				}
			}
			if (right <= left || bottom <= top) return source;

			// Se il disegno riempie gia' quasi tutto non c'e' niente da togliere.
			double coverage = (right - left + 1) / (double)width * ((bottom - top + 1) / (double)height);
			if (coverage > 0.82) return source;

			CroppedBitmap cropped = new(converted, new System.Windows.Int32Rect(left, top, right - left + 1, bottom - top + 1));
			cropped.Freeze();
			return cropped;
		}
		catch
		{
			return source;
		}
	}
}

public static class OverlayIconPalette
{
	private static readonly Dictionary<ImageSource, OverlayIconColors> Cache = new();
	private static readonly OverlayIconColors Neutral = new(
		Color.FromRgb(0x4A, 0x4F, 0x57),
		Color.FromRgb(0x23, 0x26, 0x2B));

	public static OverlayIconColors Extract(ImageSource? source)
	{
		if (source is not BitmapSource bitmap) return Neutral;
		lock (Cache)
		{
			if (Cache.TryGetValue(source, out OverlayIconColors? cached)) return cached;
		}

		OverlayIconColors result = Compute(bitmap);
		lock (Cache)
		{
			Cache[source] = result;
		}
		return result;
	}

	private static OverlayIconColors Compute(BitmapSource bitmap)
	{
		try
		{
			const int side = 32;
			TransformedBitmap scaled = new(
				new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0),
				new ScaleTransform(side / (double)bitmap.PixelWidth, side / (double)bitmap.PixelHeight));
			int width = scaled.PixelWidth;
			int height = scaled.PixelHeight;
			if (width <= 0 || height <= 0) return Neutral;
			int stride = width * 4;
			byte[] pixels = new byte[stride * height];
			scaled.CopyPixels(pixels, stride, 0);

			// Raggruppamento per tonalita' (12 spicchi): vince lo spicchio con piu'
			// "peso", dove il peso premia i pixel saturi e non trasparenti.
			double[] weight = new double[12];
			double[] sumR = new double[12];
			double[] sumG = new double[12];
			double[] sumB = new double[12];
			for (int i = 0; i < pixels.Length; i += 4)
			{
				byte alpha = pixels[i + 3];
				if (alpha < 96) continue;
				double b = pixels[i];
				double g = pixels[i + 1];
				double r = pixels[i + 2];
				double max = Math.Max(r, Math.Max(g, b));
				double min = Math.Min(r, Math.Min(g, b));
				if (max < 28) continue;
				double saturation = max <= 0 ? 0 : (max - min) / max;
				double hue = Hue(r, g, b, max, min);
				int bucket = (int)(hue / 30) % 12;
				double value = (saturation * saturation * 3) + 0.05;
				weight[bucket] += value;
				sumR[bucket] += r * value;
				sumG[bucket] += g * value;
				sumB[bucket] += b * value;
			}

			int best = 0;
			for (int i = 1; i < 12; i++)
			{
				if (weight[i] > weight[best]) best = i;
			}
			if (weight[best] <= 0) return Neutral;

			Color primary = Color.FromRgb(
				Clamp(sumR[best] / weight[best]),
				Clamp(sumG[best] / weight[best]),
				Clamp(sumB[best] / weight[best]));
			primary = Enrich(primary);
			return new OverlayIconColors(primary, Companion(primary));
		}
		catch
		{
			return Neutral;
		}
	}

	private static double Hue(double r, double g, double b, double max, double min)
	{
		double delta = max - min;
		if (delta <= 0.0001) return 0;
		double hue;
		if (max == r) hue = 60 * (((g - b) / delta) % 6);
		else if (max == g) hue = 60 * (((b - r) / delta) + 2);
		else hue = 60 * (((r - g) / delta) + 4);
		return hue < 0 ? hue + 360 : hue;
	}

	// Il colore estratto puo' essere troppo chiaro o troppo cupo per fare da
	// fondo: viene riportato in una fascia leggibile mantenendo la tonalita'.
	private static Color Enrich(Color color)
	{
		double r = color.R;
		double g = color.G;
		double b = color.B;
		double max = Math.Max(r, Math.Max(g, b));
		if (max <= 0) return Neutral.Primary;
		double target = Math.Clamp(max, 110, 205);
		double factor = target / max;
		return Color.FromRgb(Clamp(r * factor), Clamp(g * factor), Clamp(b * factor));
	}

	// Secondo colore del gradiente: stessa famiglia ma smorzata verso un grigio
	// caldo, come nella grafica di riferimento.
	private static Color Companion(Color color)
	{
		double grey = (color.R * 0.30) + (color.G * 0.59) + (color.B * 0.11);
		double blend = 0.72;
		return Color.FromRgb(
			Clamp((color.R * (1 - blend)) + (grey * blend * 1.05)),
			Clamp((color.G * (1 - blend)) + (grey * blend * 1.02)),
			Clamp((color.B * (1 - blend)) + (grey * blend)));
	}

	private static byte Clamp(double value) => (byte)Math.Clamp(value, 0, 255);
}
