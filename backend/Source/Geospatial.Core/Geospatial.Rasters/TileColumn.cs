using System.Collections.Generic;

namespace Geospatial.Rasters;

public struct TileColumn(int index)
{
	public int Index = index;

	public List<RenderBlockColumn> RenderBlockColumns = new List<RenderBlockColumn>();
}
