using System.Collections.Generic;

namespace RasMapperLib.Terrain;

public class TileColumn
{
	public int Index;

	public List<RenderBlockColumn> RenderBlockColumns;

	public TileColumn(int index)
	{
		RenderBlockColumns = new List<RenderBlockColumn>();
		Index = index;
	}
}
