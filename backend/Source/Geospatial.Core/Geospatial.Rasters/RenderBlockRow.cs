using System.Collections.Generic;

namespace Geospatial.Rasters;

public struct RenderBlockRow(int index)
{
	public int Index = index;

	public List<RasterRow> RasterRows = new List<RasterRow>();
}
