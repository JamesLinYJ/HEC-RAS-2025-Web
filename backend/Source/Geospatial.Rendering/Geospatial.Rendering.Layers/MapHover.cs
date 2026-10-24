using System.Diagnostics;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

[DebuggerDisplay("{Label}")]
public class MapHover
{
	public Point Point { get; set; }

	public string Label { get; set; }

	public MapHover()
	{
	}

	public MapHover(Point pt, string lbl)
	{
		Point = pt;
		Label = lbl;
	}
}
