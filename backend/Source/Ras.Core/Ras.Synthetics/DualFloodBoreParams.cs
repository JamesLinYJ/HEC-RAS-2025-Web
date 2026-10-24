using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class DualFloodBoreParams : FloodBoreParams
{
	private List<Extent> _subExtents;

	private Mesh _mesh;

	public override Extent SystemExtent => new Extent(0.0, (double)CellsWide * CellSize, 0.0, CellSize * (double)(2 * CellsTall + 1));

	public override Mesh CreateMesh()
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		Topology topology = new Topology();
		_subExtents = new List<Extent>();
		for (int i = 0; i < 2; i++)
		{
			int ordinal = 4 * i;
			double cellSizeCellsTallTerm = CellSize * (double)i * (double)(1 + CellsTall);
			Extent extent = new Extent(0.0, CellSize * (double)CellsWide, cellSizeCellsTallTerm, cellSizeCellsTallTerm + CellSize * (double)CellsTall);
			_subExtents.Add(extent);
			topology.AddNode(extent.LowerLeft);
			topology.AddNode(extent.LowerRight);
			topology.AddNode(extent.UpperRight);
			topology.AddNode(extent.UpperLeft);
			topology.AddArc(ordinal, ordinal + 1, null);
			topology.AddArc(ordinal + 1, ordinal + 2, null);
			topology.AddArc(ordinal + 2, ordinal + 3, null);
			topology.AddArc(ordinal + 3, ordinal, null);
			topology.AddRegionControlPoint(extent.Center);
		}
		topology.EnsureMeshMetadataColumns();
		for (int j = 0; j < 2; j++)
		{
			topology.SetControlPointData(j, PatchType.Quad, SizeFunction.Constant, (float)CellSize, 0.1f, 0f, 0f);
			topology.SetControlPointRecipeString(j, "Default()");
		}
		for (int k = 0; k < 2; k++)
		{
			int ordinal2 = 4 * k;
			for (int l = 0; l < 4; l++)
			{
				topology.SetArcData(ordinal2 + l, LinearSizeFunction.Constant, (float)CellSize, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: false);
			}
		}
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out _mesh);
		return _mesh;
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		CreateMesh();
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>();
		foreach (Extent subExtent in _subExtents)
		{
			Polyline pline = Polyline.FromSegment(subExtent.LeftWall.Scale(0.01, 0.99));
			BoundaryConditionLine upstreamBC = GetUpstreamBC(bc, pline);
			if (upstreamBC != null)
			{
				boundaryConditionLines.Add(upstreamBC);
			}
		}
		return boundaryConditionLines;
	}

	public override int[] GetCellMapping(Mesh mesh)
	{
		if (_subExtents == null)
		{
			CreateMesh();
		}
		int[] cellMapping = new int[mesh.CellCount];
		for (int i = 0; i < cellMapping.Length; i++)
		{
			for (int j = 0; j < _subExtents.Count; j++)
			{
				if (_subExtents[j].Contains(mesh.CellCenters[i]))
				{
					cellMapping[i] = j;
					break;
				}
			}
		}
		return cellMapping;
	}
}
