using System.Collections.Generic;

namespace Geospatial.Rasters;

public struct RenderBlockColumn(int index)
{
	public int Index = index;

	public List<RasterColumn> RasterColumns = new List<RasterColumn>();
}
