using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors.Topology;
using Utility.Memory;

namespace Geospatial.Vectors;

public class MeshTags
{
	public class TagMapping
	{
		public List<List<int>> GroupMembers { get; private set; }

		public List<Dictionary<string, object>> Tags { get; private set; }

		public TagMapping(List<List<int>> groups)
		{
			GroupMembers = groups;
			Tags = new List<Dictionary<string, object>>(GroupMembers.Count);
			for (int i = 0; i < GroupMembers.Count; i++)
			{
				Tags.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));
			}
		}

		public TagMapping(List<List<int>> groups, List<Dictionary<string, object>> tags)
		{
			GroupMembers = groups;
			Tags = tags;
		}

		public void SetGroupTag(int groupIdx, string key, object groupTag)
		{
			Tags[groupIdx][key] = groupTag;
		}

		public TagMapping Clone()
		{
			List<List<int>> groups = GroupMembers.Select((List<int> g) => g.ToList()).ToList();
			List<Dictionary<string, object>> tags = Tags.Select((Dictionary<string, object> t) => new Dictionary<string, object>(t, StringComparer.OrdinalIgnoreCase)).ToList();
			return new TagMapping(groups, tags);
		}

		public IEnumerable<int> Get(string tag, object localValue2)
		{
			List<int> intList = new List<int>();
			for (int i = 0; i < Tags.Count; i++)
			{
				if (Tags[i].TryGetValue(tag, out var localValue) && localValue.Equals(localValue2))
				{
					intList.AddRange(GroupMembers[i]);
				}
			}
			return intList;
		}

		public void ApplyItemOrdering(int[] oldToNew)
		{
			for (int i = 0; i < GroupMembers.Count; i++)
			{
				List<int> groupMember = GroupMembers[i];
				for (int j = 0; j < groupMember.Count; j++)
				{
					groupMember[j] = oldToNew[groupMember[j]];
				}
			}
		}
	}

	public const string ControlPoint = "ControlPoint";

	public const string Arc = "Arc";

	public TagMapping Cells { get; private set; }

	public TagMapping Faces { get; private set; }

	private MeshTags()
	{
	}

	private MeshTags(MeshTags src)
	{
		Cells = src.Cells.Clone();
		Faces = src.Faces.Clone();
	}

	public MeshTags Clone()
	{
		return new MeshTags(this);
	}

	public static List<int> FindCellRegions(Mesh mesh, Geospatial.Vectors.Topology.Topology topo)
	{
		List<int> cellCount = new List<int>(mesh.CellCount);
		for (int i = 0; i < mesh.CellCount; i++)
		{
			cellCount.Add(-1);
		}
		Polygon poly = new Polygon();
		Queue<int> intQueue = new Queue<int>();
		for (int j = 0; j < mesh.CellCount; j++)
		{
			if (cellCount[j] >= 0)
			{
				continue;
			}
			int cellCount2 = (cellCount[j] = topo.TryFindRegionContainingPoint(CellPoint(j)));
			if (cellCount2 < 0)
			{
				continue;
			}
			intQueue.Clear();
			foreach (int cellsAroundCell in mesh.GetCellsAroundCell(j))
			{
				intQueue.Enqueue(cellsAroundCell);
			}
			while (intQueue.Count > 0)
			{
				int dequeue = intQueue.Dequeue();
				if (cellCount[dequeue] >= 0 || !topo.RegionContainsPoint(cellCount2, CellPoint(dequeue)))
				{
					continue;
				}
				cellCount[dequeue] = cellCount2;
				foreach (int cellsAroundCell2 in mesh.GetCellsAroundCell(dequeue))
				{
					if (cellCount[cellsAroundCell2] < 0)
					{
						intQueue.Enqueue(cellsAroundCell2);
					}
				}
			}
		}
		return cellCount;
		Point CellPoint(int cIdx)
		{
			poly.Clear();
			mesh.FillCellPolygon_CCW(cIdx, poly);
			return poly.BestLabelPoint();
		}
	}

	public static List<int> FindFaceArcs(Mesh mesh, Geospatial.Vectors.Topology.Topology topo)
	{
		List<int> faceCount = new List<int>(mesh.FaceCount);
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			faceCount.Add(-1);
		}
		for (int j = 0; j < topo.Arcs.Count; j++)
		{
			Polyline arcPolyline = topo.GetArcPolyline(j);
			int[] intValues = (from t in mesh.FacesAlongPolyline(arcPolyline)
				select t.Item1).ToArray();
			for (int intervalIndex = 0; intervalIndex < intValues.Length; intervalIndex++)
			{
				faceCount[intValues[intervalIndex]] = j;
			}
		}
		return faceCount;
	}

	private static List<int>[] ControlPointCells(Geospatial.Vectors.Topology.Topology topo, List<int>[] regionCells)
	{
		List<int>[] intListArray = new List<int>[topo.ControlPointMetadata.Rows.Count];
		for (int i = 0; i < intListArray.Length; i++)
		{
			intListArray[i] = new List<int>();
		}
		for (int j = 0; j < topo.CompletedRegions.Count; j++)
		{
			int controlPointIndex = topo.CompletedRegions[j].ControlPointIndex;
			if (controlPointIndex >= 0)
			{
				intListArray[controlPointIndex].AddRange(regionCells[j]);
			}
		}
		return intListArray;
	}

	public static MeshTags ComputeFromTopology(Mesh mesh, Geospatial.Vectors.Topology.Topology topo)
	{
		MeshTags meshTags = new MeshTags();
		ComputeFromTopologyCells(mesh, topo, meshTags);
		ComputeFromTopologyFaces(mesh, topo, meshTags);
		return meshTags;
	}

	public static void ComputeFromTopologyCells(Mesh mesh, Geospatial.Vectors.Topology.Topology topo, MeshTags meshtags)
	{
		List<int>[] regionCells = Mesh.RegionCellsLists(FindCellRegions(mesh, topo), topo.CompletedRegions.Count);
		List<int>[] source = ControlPointCells(topo, regionCells);
		meshtags.Cells = new TagMapping(source.ToList());
		for (int i = 0; i < topo.ControlPointMetadata.Rows.Count; i++)
		{
			for (int j = 0; j < topo.ControlPointMetadata.Columns.Count; j++)
			{
				TableColumn tableColumn = topo.ControlPointMetadata.Columns[j];
				if (!MeshExtensions.IsWellKnownColumn(tableColumn.Name))
				{
					object controlPointMetadata = topo.ControlPointMetadata[i][j];
					meshtags.Cells.SetGroupTag(i, tableColumn.Name, controlPointMetadata);
				}
			}
		}
	}

	public static void ComputeFromTopologyFaces(Mesh mesh, Geospatial.Vectors.Topology.Topology topo, MeshTags meshtags)
	{
		List<int>[] source = Mesh.RegionCellsLists(FindFaceArcs(mesh, topo), topo.Arcs.Count);
		meshtags.Faces = new TagMapping(source.ToList());
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			for (int j = 0; j < topo.ArcMetadata.Columns.Count; j++)
			{
				TableColumn tableColumn = topo.ArcMetadata.Columns[j];
				if (!MeshExtensions.IsWellKnownColumn(tableColumn.Name))
				{
					object arcMetadata = topo.ArcMetadata[i][j];
					meshtags.Faces.SetGroupTag(i, tableColumn.Name, arcMetadata);
				}
			}
		}
	}
}
