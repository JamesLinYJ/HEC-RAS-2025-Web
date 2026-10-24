using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[ComVisible(false)]
public class SlopingMeshMap : MeshMap
{
	public List<SlopingCellMap> Cells;

	public SlopingMeshMap(int meshIndex, List<SlopingCellMap> d2MapCells, List<int> faces, List<int> facepoints)
		: base(meshIndex, faces, facepoints)
	{
		Cells = d2MapCells;
	}

	public override List<int> UniqueCells()
	{
		return Cells.Select([SpecialName] (SlopingCellMap cell) => cell.Index).ToList();
	}
}
