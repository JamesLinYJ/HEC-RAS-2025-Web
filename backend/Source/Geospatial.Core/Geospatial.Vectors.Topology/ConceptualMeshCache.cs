using System;
using System.Collections.Concurrent;
using Geospatial.Vectors.MeshTopology;

namespace Geospatial.Vectors.Topology;

public class ConceptualMeshCache
{
	public ConcurrentDictionary<int, SlidingMesh> RegionMeshHash = new ConcurrentDictionary<int, SlidingMesh>();

	private ConcurrentDictionary<int, QuadTransformLaplacian> _quadCache = new ConcurrentDictionary<int, QuadTransformLaplacian>();

	public int Recover(Topology topo, SlidingMesh[] meshes)
	{
		if (RegionMeshHash.Count == 0)
		{
			return 0;
		}
		int ordinal = 0;
		for (int i = 0; i < topo.CompletedRegions.Count; i++)
		{
			int regionFullHash = topo.GetRegionFullHash(i);
			if (RegionMeshHash.TryGetValue(regionFullHash, out var meshes2))
			{
				meshes[i] = meshes2;
				ordinal++;
			}
		}
		return ordinal;
	}

	public void Set(Topology topo, SlidingMesh[] meshes)
	{
		RegionMeshHash.Clear();
		for (int i = 0; i < topo.CompletedRegions.Count; i++)
		{
			int regionFullHash = topo.GetRegionFullHash(i);
			RegionMeshHash[regionFullHash] = meshes[i];
		}
	}

	private static int QuadHash(ConceptualMesh cm, int regionIdx, (Point, Point, Point, Point) corners)
	{
		return HashCode.Combine(cm.HashRegion(regionIdx), corners);
	}

	public QuadTransformLaplacian TryGetQuadTransform(ConceptualMesh cm, int regionIdx, (Point, Point, Point, Point) corners)
	{
		int key = QuadHash(cm, regionIdx, corners);
		if (_quadCache.TryGetValue(key, out var localValue))
		{
			return localValue;
		}
		return null;
	}

	public void SetQuadTransform(ConceptualMesh cm, int regionIdx, (Point, Point, Point, Point) corners, QuadTransformLaplacian qtl)
	{
		int key = QuadHash(cm, regionIdx, corners);
		_quadCache[key] = qtl;
	}
}
