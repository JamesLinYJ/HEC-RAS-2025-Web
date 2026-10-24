using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors.MeshTopology;

public class CycleTree : IComparable<CycleTree>
{
	public struct LinearFeature
	{
		public List<int> nodes;

		public Polyline polyline;

		public LinearFeature(List<int> nodes, DynamicMesh mesh)
		{
			this.nodes = nodes;
			polyline = new Polyline(nodes.Select((int i) => mesh.NodePoints[i]));
		}

		public LinearFeature(List<HalfEdge> hes, DynamicMesh mesh)
		{
			nodes = new List<int>(1 + hes.Count) { mesh.HalfEdgeNodeBack(hes[0]) };
			foreach (HalfEdge he in hes)
			{
				nodes.Add(mesh.HalfEdgeNodeForward(he));
			}
			polyline = new Polyline(nodes.Select((int i) => mesh.NodePoints[i]));
		}
	}

	public enum CycleRelationship
	{
		CONGRUENT,
		SUPERSET,
		SUBSET,
		DISJOINT
	}

	public List<int> nodes;

	public HashSet<int> nodeSet;

	public Polygon poly;

	public List<CycleTree> children;

	public List<LinearFeature> linearFeatures;

	public double cwArea => poly.Area;

	public CycleTree(List<int> nodes, DynamicMesh mesh, List<List<int>> linearFeatures = null)
	{
		poly = new Polygon(nodes.Select((int i) => mesh.NodePoints[i]));
		this.nodes = nodes;
		nodeSet = new HashSet<int>(nodes);
		children = new List<CycleTree>();
		this.linearFeatures = new List<LinearFeature>();
		if (linearFeatures == null)
		{
			return;
		}
		foreach (List<int> linearFeature in linearFeatures)
		{
			this.linearFeatures.Add(new LinearFeature(linearFeature, mesh));
		}
	}

	public CycleTree(List<HalfEdge> hes, DynamicMesh mesh, List<List<HalfEdge>> linearFeatures = null)
	{
		nodes = hes.Select((HalfEdge he) => mesh.HalfEdgeNodeForward(he)).ToList();
		poly = new Polygon(nodes.Select((int i) => mesh.NodePoints[i]));
		nodeSet = new HashSet<int>(nodes);
		children = new List<CycleTree>();
		this.linearFeatures = new List<LinearFeature>();
		if (linearFeatures == null)
		{
			return;
		}
		foreach (List<HalfEdge> linearFeature in linearFeatures)
		{
			this.linearFeatures.Add(new LinearFeature(linearFeature, mesh));
		}
	}

	private CycleTree()
	{
		poly = null;
		nodes = null;
		children = new List<CycleTree>();
		linearFeatures = new List<LinearFeature>();
	}

	public string Summary()
	{
		List<string> stringList = new List<string>();
		SummaryHelper("", stringList);
		return string.Join("\n", stringList);
	}

	private void SummaryHelper(string prefix, List<string> accum)
	{
		if (nodes == null)
		{
			accum.Add($"{prefix}root  {linearFeatures.Count} linear features");
		}
		else
		{
			accum.Add($"{prefix}cwArea: {cwArea} {nodes.Count} nodes  {linearFeatures.Count} linear features");
		}
		string prefix2 = "  " + prefix;
		foreach (CycleTree child in children)
		{
			child.SummaryHelper(prefix2, accum);
		}
	}

	private bool Insert(CycleTree cycleTree, DynamicMesh mesh)
	{
		if (nodes != null)
		{
			switch (RelatePolygons(cycleTree, mesh))
			{
			case CycleRelationship.CONGRUENT:
				children.Add(cycleTree);
				return true;
			case CycleRelationship.SUPERSET:
				foreach (CycleTree child in children)
				{
					if (child.Insert(cycleTree, mesh))
					{
						return true;
					}
				}
				children.Add(cycleTree);
				return true;
			default:
				return false;
			}
		}
		foreach (CycleTree child2 in children)
		{
			if (child2.Insert(cycleTree, mesh))
			{
				return true;
			}
		}
		children.Add(cycleTree);
		return true;
	}

	private bool InsertLinearFeature(LinearFeature linearFeature)
	{
		if (nodes != null)
		{
			if (poly.Contains(linearFeature.polyline))
			{
				foreach (CycleTree child in children)
				{
					if (child.InsertLinearFeature(linearFeature))
					{
						return true;
					}
				}
				linearFeatures.Add(linearFeature);
				return true;
			}
			return false;
		}
		foreach (CycleTree child2 in children)
		{
			if (child2.InsertLinearFeature(linearFeature))
			{
				return true;
			}
		}
		return false;
	}

	public static CycleTree CreateCycleTree(DynamicMesh mesh)
	{
		List<List<HalfEdge>> cycles = mesh.FindCycles(mesh.NodeCount, out var linearFeaturesList, onlyCCW: false, findLinear: true, simpleGeometry: true);
		List<CycleTree> cycleTrees = new List<CycleTree>();
		CycleTree cycleTree = new CycleTree();
		for (int i = 0; i < cycles.Count; i++)
		{
			if (cycles[i].Count == 0)
			{
				foreach (List<HalfEdge> halfEdges in linearFeaturesList[i])
				{
					cycleTree.linearFeatures.Add(new LinearFeature(halfEdges, mesh));
				}
			}
			else
			{
				cycleTrees.Add(new CycleTree(cycles[i], mesh, linearFeaturesList[i]));
			}
		}
		cycleTrees.Sort();
		foreach (CycleTree cycleTree2 in cycleTrees)
		{
			cycleTree.Insert(cycleTree2, mesh);
		}
		foreach (CycleTree cycleTree3 in cycleTrees)
		{
			if (cycleTree3.cwArea > 0.0)
			{
				continue;
			}
			foreach (CycleTree child in cycleTree3.children)
			{
				cycleTree3.linearFeatures.AddRange(child.linearFeatures);
				child.linearFeatures.Clear();
			}
		}
		List<LinearFeature> linearFeatures = new List<LinearFeature>();
		foreach (LinearFeature linearFeature in cycleTree.linearFeatures)
		{
			if (!cycleTree.InsertLinearFeature(linearFeature))
			{
				linearFeatures.Add(linearFeature);
			}
		}
		cycleTree.linearFeatures = linearFeatures;
		return cycleTree;
	}

	public bool Congruent(CycleTree other)
	{
		int count = nodes.Count;
		if (count != other.nodes.Count)
		{
			return false;
		}
		if (poly.Extent != other.poly.Extent)
		{
			return false;
		}
		int index = 0;
		int i;
		for (i = 0; i < count && nodes[index] != other.nodes[i]; i++)
		{
		}
		if (i == count)
		{
			return false;
		}
		int areaPolyTerm = ((!(poly.Area * other.poly.Area < 0.0)) ? 1 : (-1));
		for (int j = 0; j < nodes.Count; j++)
		{
			int index2 = (count + i + j * areaPolyTerm) % nodes.Count;
			if (nodes[j] != other.nodes[index2])
			{
				return false;
			}
		}
		return true;
	}

	public CycleRelationship RelatePolygons(CycleTree other, DynamicMesh mesh)
	{
		IEnumerable<int> source = nodeSet.Except(other.nodeSet);
		if (source.Any())
		{
			int index = source.First();
			if (other.poly.Contains(mesh.NodePoints[index]))
			{
				return CycleRelationship.SUBSET;
			}
		}
		IEnumerable<int> source2 = other.nodeSet.Except(nodeSet);
		if (source2.Any())
		{
			int index2 = source2.First();
			if (poly.Contains(mesh.NodePoints[index2]))
			{
				return CycleRelationship.SUPERSET;
			}
		}
		if (source.Any() && source2.Any())
		{
			return CycleRelationship.DISJOINT;
		}
		if (!source.Any() && !source2.Any())
		{
			return CycleRelationship.CONGRUENT;
		}
		if (source.Any() && !source2.Any())
		{
			return CycleRelationship.SUPERSET;
		}
		if (!source.Any() && source2.Any())
		{
			return CycleRelationship.SUBSET;
		}
		throw new DynamicMeshException("Failed to relate two cycles");
	}

	public int CompareTo(CycleTree other)
	{
		if (Congruent(other))
		{
			if (cwArea > 0.0 && other.cwArea < 0.0)
			{
				return -1;
			}
			if (cwArea < 0.0 && other.cwArea > 0.0)
			{
				return 1;
			}
			if (this == other)
			{
				return 0;
			}
			throw new DynamicMeshException("Duplicate cycles");
		}
		if (Math.Abs(cwArea) < Math.Abs(other.cwArea))
		{
			return 1;
		}
		if (Math.Abs(cwArea) > Math.Abs(other.cwArea))
		{
			return -1;
		}
		return 0;
	}
}
