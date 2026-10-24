using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters.TileProcessing.Contours;

public sealed class ContourTileState
{
	public List<List<Point>> Fragments { get; } = new List<List<Point>>();
}
