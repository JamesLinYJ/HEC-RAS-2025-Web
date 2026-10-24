using System.Collections.Generic;
using System.Drawing;

namespace Geospatial.Rendering;

public interface IColorPalette
{
	string Name { get; }

	List<Color> Colors { get; }
}
