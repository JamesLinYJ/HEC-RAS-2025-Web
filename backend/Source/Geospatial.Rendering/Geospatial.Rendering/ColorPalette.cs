using System;
using System.Collections.Generic;
using System.Drawing;

namespace Geospatial.Rendering;

public class ColorPalette : IColorPalette
{
	public string Name { get; }

	public List<Color> Colors { get; protected init; }

	public int Count => Colors.Count;

	public ColorPalette(List<Color> colors, string name = null)
	{
		if (colors == null)
		{
			throw new ArgumentNullException("colors");
		}
		Colors = colors;
		Name = name;
	}
}
