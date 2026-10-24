using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[DebuggerDisplay("Cell {Index}: {MapPoints.Count} points")]
[ComVisible(false)]
public class FlatCellMap
{
	public int Index;

	public List<int> MapPoints;

	public FlatCellMap(int c, List<int> points)
	{
		MapPoints = new List<int>();
		Index = c;
		MapPoints = points;
	}
}
