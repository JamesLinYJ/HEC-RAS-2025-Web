using System.Collections.Generic;

namespace RasMapperLib.Mapping;

public abstract class MeshMap
{
	public int MeshIndex { get; }

	public List<int> UniqueFaces { get; }

	public List<int> UniqueFacePoints { get; }

	public MeshMap(int meshIdx, List<int> uniqueFaces, List<int> uniqueFacepoints)
	{
		MeshIndex = meshIdx;
		UniqueFaces = uniqueFaces;
		UniqueFacePoints = uniqueFacepoints;
	}

	public abstract List<int> UniqueCells();
}
