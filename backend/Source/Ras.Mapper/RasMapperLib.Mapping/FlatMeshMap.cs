using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RasMapperLib.Mapping;

[ComVisible(false)]
public class FlatMeshMap : MeshMap
{
	public List<FlatCellMap> Cells;

	public FlatMeshMap(int meshIdx, List<FlatCellMap> cellList, List<int> uniqueFaces, List<int> uniqueFacepoints)
		: base(meshIdx, uniqueFaces, uniqueFacepoints)
	{
		Cells = cellList;
	}

	public override List<int> UniqueCells()
	{
		return Cells.Select([SpecialName] (FlatCellMap cell) => cell.Index).ToList();
	}
}
