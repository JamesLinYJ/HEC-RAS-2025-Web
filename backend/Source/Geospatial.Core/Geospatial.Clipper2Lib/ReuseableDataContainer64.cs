using System.Collections.Generic;

namespace Geospatial.Clipper2Lib;

public class ReuseableDataContainer64
{
	internal readonly List<LocalMinima> _minimaList;

	internal readonly List<Vertex> _vertexList;

	public ReuseableDataContainer64()
	{
		_minimaList = new List<LocalMinima>();
		_vertexList = new List<Vertex>();
	}

	public void Clear()
	{
		_minimaList.Clear();
		_vertexList.Clear();
	}

	public void AddPaths(Paths64 paths, PathType pt, bool isOpen)
	{
		ClipperEngine.AddPathsToVertexList(paths, pt, isOpen, _minimaList, _vertexList);
	}
}
