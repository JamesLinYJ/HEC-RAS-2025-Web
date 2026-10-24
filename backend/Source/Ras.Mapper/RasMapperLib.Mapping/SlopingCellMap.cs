using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[DebuggerDisplay("Cell {Index}: {MapPoints.Count} points")]
[ComVisible(false)]
public class SlopingCellMap
{
	public int Index;

	public List<SlopingCellPoint> MapPoints;

	public SlopingCellMap(int cellIndex, List<SlopingCellPoint> D2MapCellPoints)
	{
		Index = cellIndex;
		MapPoints = D2MapCellPoints;
	}
}
