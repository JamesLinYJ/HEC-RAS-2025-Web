using System.Collections.Generic;

namespace Geospatial.Rasters;

public struct TileRow(int index)
{
	public int Index = index;

	public List<RenderBlockRow> RenderBlockRows = new List<RenderBlockRow>();
}
