using System.Collections.Generic;

namespace RasMapperLib.Terrain;

public class TileRow
{
	public int Index;

	public List<RenderBlockRow> RenderBlockRows;

	public TileRow(int index)
	{
		RenderBlockRows = new List<RenderBlockRow>();
		Index = index;
	}
}
