using System.Collections.Generic;

namespace RasMapperLib.Terrain;

public class RenderBlockColumn
{
	public int index;

	public List<RasterColumn> RasterColumns;

	public RenderBlockColumn(int index)
	{
		RasterColumns = new List<RasterColumn>();
		this.index = index;
	}
}
