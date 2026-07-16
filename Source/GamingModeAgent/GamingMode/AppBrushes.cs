using System.Windows.Media;

namespace GamingMode;

public static class AppBrushes
{
	public static readonly Brush Yellow = Make("#fcba03");

	public static readonly Brush Ink = Make("#111111");

	public static readonly Brush Paper = Make("#f7f7f3");

	private static SolidColorBrush Make(string hex)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}
}
