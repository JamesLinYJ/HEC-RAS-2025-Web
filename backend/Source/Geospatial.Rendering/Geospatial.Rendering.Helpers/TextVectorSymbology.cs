using System.Drawing;

namespace Geospatial.Rendering.Helpers;

public class TextVectorSymbology : VectorSymbology
{
	public new static TextVectorSymbology DefaultSymbology = new TextVectorSymbology(VectorSymbology.DefaultPenColor, 12.0);

	public static TextVectorSymbology JoshFavoriteSymbology = new TextVectorSymbology(Color.HotPink, 24.0, isBold: true, isItalicized: false, "Comic Sans MS");

	public bool IsBold { get; set; }

	public bool IsItalicized { get; set; }

	public TextVectorSymbology(Color fillColor, double penWidth, Color outlineColor, bool isBold = true, bool isItalicized = false, string font = "Segoe UI")
		: base(fillColor, penWidth, outlineColor)
	{
		IsBold = isBold;
		IsItalicized = isItalicized;
		Font = font;
	}

	public TextVectorSymbology(Color fillColor, double penWidth, bool isBold = true, bool isItalicized = false, string font = "Segoe UI")
		: base(fillColor, penWidth, fillColor)
	{
		IsBold = isBold;
		IsItalicized = isItalicized;
		Font = font;
	}
}
