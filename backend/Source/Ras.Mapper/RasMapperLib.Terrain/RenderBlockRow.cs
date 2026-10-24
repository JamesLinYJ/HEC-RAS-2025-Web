using System.Collections.Generic;

namespace RasMapperLib.Terrain;

public class RenderBlockRow
{
	public int index;

	public List<RasterRow> RasterRows;

	public RenderBlockRow(int index)
	{
		RasterRows = new List<RasterRow>();
		this.index = index;
	}
}
