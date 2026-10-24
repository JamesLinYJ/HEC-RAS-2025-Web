using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class CoonsRiverParams : BaseParams
{
	public int LtsLevels = 3;

	public bool UseFBF = true;

	private ConceptualMesh _cmesh;

	private Mesh _mesh;

	public override int ReportFrequency { get; set; } = 1;

	public override Extent SystemExtent => CreateMesh().Extent;

	public static CoonsRiverParams Default()
	{
		return new CoonsRiverParams
		{
			NValue = 0f,
			SolveDt = 100.0,
			SolveDuration = 5000.0,
			ReportFrequency = 1
		};
	}

	public override Mesh CreateMesh()
	{
		if (_mesh == null)
		{
			CoonsRiver(out _cmesh).ToMesh(out _mesh, _cmesh);
		}
		return _mesh;
	}

	public override void InitPlanOptions(Plan pl)
	{
		pl.SolverType = SolverType.CPU;
		pl.LtsLevels = (SolverControl.LtsLevelOptions)LtsLevels;
		pl.UseFBF = UseFBF;
	}

	public static ConceptualMesh RiverPatch(out int c1)
	{
		ConceptualMesh conceptualMesh = new ConceptualMesh();
		double[,] doubleValues = new double[31, 2]
		{
			{ 22406.1956, 14206.3419 },
			{ 23820.7812, 14049.1657 },
			{ 25392.543, 13829.119 },
			{ 25502.5663, 16076.7384 },
			{ 25235.3668, 18544.4044 },
			{ 25046.7554, 20147.6014 },
			{ 24308.0274, 21515.0342 },
			{ 23490.7113, 22128.0213 },
			{ 22406.1956, 22348.0679 },
			{ 21195.9391, 22426.656 },
			{ 20064.2706, 22253.7622 },
			{ 19294.1073, 22316.6327 },
			{ 18178.1564, 22505.2441 },
			{ 17030.7703, 22709.5731 },
			{ 16244.8894, 22898.1845 },
			{ 15364.7028, 23023.9255 },
			{ 14390.2105, 23008.2079 },
			{ 13289.9773, 22866.7493 },
			{ 12284.0498, 22536.6793 },
			{ 10397.9356, 21782.2337 },
			{ 10948.0522, 20021.8605 },
			{ 12236.8969, 20304.7776 },
			{ 13965.8349, 20729.1533 },
			{ 15286.1148, 20980.6352 },
			{ 17329.4051, 21043.5056 },
			{ 19341.2602, 20650.5652 },
			{ 20944.4572, 20084.7309 },
			{ 22028.9728, 19408.8734 },
			{ 22673.3951, 18025.723 },
			{ 22516.219, 16265.3498 },
			{ 22406.1956, 14206.3419 }
		};
		List<Point> points = new List<Point>();
		for (int i = 0; i < doubleValues.GetLength(0); i++)
		{
			points.Add(new Point(doubleValues[i, 0], doubleValues[i, 1]));
		}
		int[] coefficients = new int[5] { 0, 2, 19, 20, 30 };
		int[] source = new int[4]
		{
			conceptualMesh.AddNode(points[coefficients[0]]),
			conceptualMesh.AddNode(points[coefficients[1]]),
			conceptualMesh.AddNode(points[coefficients[2]]),
			conceptualMesh.AddNode(points[coefficients[3]])
		};
		c1 = conceptualMesh.AddCell();
		MeshRegionMetadata meshRegionMetadata = new MeshRegionMetadata();
		meshRegionMetadata.PatchType = PatchType.Quad;
		MeshRegionMetadata meshRegionMetadata2 = meshRegionMetadata;
		meshRegionMetadata2.CornerRingNodeSE = 0;
		meshRegionMetadata2.CornerRingNodeNE = 1;
		meshRegionMetadata2.CornerRingNodeNW = 2;
		meshRegionMetadata2.CornerRingNodeSW = 3;
		conceptualMesh.CellRegionMetadata[c1] = meshRegionMetadata2;
		conceptualMesh.SetCellNodes(c1, source.ToList());
		List<HalfEdge> cellFace = conceptualMesh.CellFaces[c1];
		conceptualMesh.FaceRawLine[cellFace[0].fIdx] = new Polyline(points.GetRange(coefficients[0], 1 + coefficients[1] - coefficients[0]));
		conceptualMesh.FaceRawLine[cellFace[1].fIdx] = new Polyline(points.GetRange(coefficients[1], 1 + coefficients[2] - coefficients[1]));
		conceptualMesh.FaceRawLine[cellFace[2].fIdx] = new Polyline(points.GetRange(coefficients[2], 1 + coefficients[3] - coefficients[2]));
		conceptualMesh.FaceRawLine[cellFace[3].fIdx] = new Polyline(points.GetRange(coefficients[3], 1 + coefficients[4] - coefficients[3]));
		return conceptualMesh;
	}

	public static SlidingMesh CoonsRiver(out ConceptualMesh cmesh)
	{
		cmesh = RiverPatch(out var c);
		List<HalfEdge> cellFace = cmesh.CellFaces[c];
		cmesh.SetArcNodesFromSmooth(cellFace[1].fIdx, new DiscretizeLine(30, 2.0));
		cmesh.SetArcNodesFromSmooth(cellFace[3].fIdx, new DiscretizeLine(30, 0.5));
		cmesh.SetArcNodesFromSmooth(cellFace[0].fIdx, new DiscretizeLine(6));
		cmesh.SetArcNodesFromSmooth(cellFace[2].fIdx, new DiscretizeLine(6));
		SlidingMesh slidingMesh = new QuadPatch(cmesh, c).Generate();
		slidingMesh.CheckMesh(out var _);
		return slidingMesh;
	}
}
