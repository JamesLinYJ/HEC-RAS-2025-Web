using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.DataStructures;
using Geospatial.Transformers;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class CartesianPatch : IRegionGenerator
{
	public static class MessageCodes
	{
		public static readonly MessageClass LargeScaleWarned = new MessageClass("CartesianPatch_LargeScaleWarned", MessageSeverity.Warning, "Scale is large relative to region {ControlPointIndex} {RegionIndex}");

		public static Message GetLargeScaleWarned(int controlPointIdx, int regionIdx)
		{
			return LargeScaleWarned.Create(controlPointIdx, regionIdx);
		}
	}

	public double clearanceFactor = 0.2;

	public float targetSizeU;

	public float targetSizeV;

	public float stretchRateFromBoundary;

	public bool castRays = true;

	public ISizeFunction sizeFunction;

	public Point origin;

	public double rotateDegCCW;

	public SlidingMesh.ClipMode clipMode;

	public SlidingMesh result;

	public ConceptualMesh cmesh;

	public int regionIdx;

	private ConceptualMesh origCmesh;

	private int origRegionIdx = -1;

	public CartesianPatch(ConceptualMesh mesh, int cIdx, ISizeFunction sizeFunction)
	{
		cmesh = mesh;
		regionIdx = cIdx;
		this.sizeFunction = sizeFunction;
	}

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (res == null)
		{
			res = new OperationResult();
		}
		if (stretchRateFromBoundary == 0f)
		{
			clipMode = SlidingMesh.ClipMode.RaggedCells;
		}
		else
		{
			clipMode = SlidingMesh.ClipMode.RaggedWithScale;
		}
		reporter.ReportProgress(0);
		result = GenerateOversized();
		reporter.ReportProgress(20);
		origCmesh = cmesh;
		origRegionIdx = regionIdx;
		cmesh = cmesh.GetRegionConceptualMesh(ref regionIdx, out var faceMap);
		ModifyDiscretization(result, reporter);
		try
		{
			result.ClipToBoundaryRagged(cmesh, regionIdx, SlidingMesh.RaggedClipMode.SMOOTH_AND_NODES, includeExterior: true, includeBreaklines: true, clipConstrained: false, result.CellIndex());
		}
		catch (StructuredException ex)
		{
			for (int i = 0; i < ex.Messages.Count; i++)
			{
				Message message = ex.Messages[i];
				if (message.MessageType.Code == "ComplexPolygon_OuterRingNegativeArea")
				{
					ex.Messages[i] = ConceptualMesh.MessageCodes.GetInvalidRegion(origCmesh.CellRegionMetadata[origRegionIdx].ControlPointIndex, origRegionIdx, message.EvaluatedMessage);
				}
			}
			throw;
		}
		reporter.ReportProgress(25);
		result.ClearConstraints();
		int nodeCount = result.NodeCount;
		if (clipMode == SlidingMesh.ClipMode.RaggedWithScale)
		{
			double targetSizeUTargetSizeVTerm = (double)Math.Max(targetSizeU, targetSizeV) / Math.Sqrt(targetSizeU * targetSizeV) - 1.0;
			result.ClipByScaleAndClearance(cmesh, regionIdx, sizeFunction, 0.05 + targetSizeUTargetSizeVTerm, clearanceFactor, includeConstrained: true, reporter.WithProgressModifier(25, 0.7f));
		}
		else
		{
			result.ClipByClearance(cmesh, regionIdx, clearanceFactor, includeConstrained: true, reporter.WithProgressModifier(25, 0.7f));
		}
		if (reporter.CancelRequested)
		{
			return null;
		}
		if (result.CellDeleted.All((bool d) => d))
		{
			res.AddMessage(ConceptualMesh.MessageCodes.GetCartesianTargetSizeNotReached(origCmesh.CellRegionMetadata[origRegionIdx].ControlPointIndex, targetSizeU, targetSizeV));
		}
		HashSet<HalfEdge> origPerimeter = new HashSet<HalfEdge>();
		result.AddNodeArcsWithConstraints(cmesh, regionIdx, includeExterior: true, includeBreaklines: true, origPerimeter, setTargetLength: true);
		result.FillGaps(reporter.WithProgressModifier(95, 0.03f), null, origPerimeter, clipMode == SlidingMesh.ClipMode.RaggedWithScale);
		if (reporter.CancelRequested)
		{
			return null;
		}
		CleanPerimeterCombined(result, nodeCount, reporter);
		reporter.ReportProgress(70);
		CleanPerimeterCombined(result, nodeCount, reporter);
		result.constraints.RemapArcs(faceMap);
		return result;
	}

	public SlidingMesh GenerateOversized()
	{
		ComplexPolygon complexPolygon = cmesh.CellSmoothComplexPolygon(regionIdx);
		origin = complexPolygon.OuterRing.Polygon.ComputeCentroid();
		_ = complexPolygon.Extent;
		RotateTransformer rotateTransformer = new RotateTransformer(origin, 0.0 - rotateDegCCW, RotationUnits.Degrees, Orientation.CounterClockwise);
		Point[] points = complexPolygon.OuterRing.Polygon.Points.ToArray();
		Point[] pointsArray = (Point[])points.Clone();
		rotateTransformer.Transform(points, pointsArray);
		Extent extent = new Polygon(pointsArray).Extent;
		int widthExtentTerm = Math.Max(2, 3 + (int)(0.5 + extent.Width / (double)targetSizeU));
		int heightExtentTerm = Math.Max(2, 3 + (int)(0.5 + extent.Height / (double)targetSizeV));
		Extent extent2 = extent.EnforceMinDimensions((float)(widthExtentTerm - 1) * targetSizeU, (float)(heightExtentTerm - 1) * targetSizeV);
		SlidingMesh slidingMesh = new SlidingMesh();
		int[,] addCartesian = slidingMesh.AddCartesian(widthExtentTerm, heightExtentTerm, extent2.LowerLeft, extent2.UpperRight);
		slidingMesh.Transform(rotateTransformer, reverse: true);
		for (int i = 1; i < addCartesian.GetLength(0); i += 2)
		{
			for (int j = 0; j < addCartesian.GetLength(1); j += 2)
			{
				slidingMesh.FaceTargetLength[addCartesian[i, j]] = targetSizeU;
			}
		}
		for (int k = 0; k < addCartesian.GetLength(0); k += 2)
		{
			for (int l = 1; l < addCartesian.GetLength(1); l += 2)
			{
				slidingMesh.FaceTargetLength[addCartesian[k, l]] = targetSizeV;
			}
		}
		return slidingMesh;
	}

	private OperationResult ModifyDiscretization(SlidingMesh result, ProgressReporter reporter = null)
	{
		SpatialIndex<int> cellIndex = result.CellIndex();
		foreach (int cellBreakline in cmesh.CellBreaklines(regionIdx))
		{
			ModifyArcDiscretization(result, cmesh, cellBreakline, cellIndex);
		}
		foreach (HalfEdge cellFace in cmesh.CellFaces[regionIdx])
		{
			ModifyArcDiscretization(result, cmesh, cellFace.fIdx, cellIndex);
		}
		foreach (int cellIslandFaceIndex in cmesh.CellIslandFaces(regionIdx))
		{
			ModifyArcDiscretization(result, cmesh, cellIslandFaceIndex, cellIndex);
		}
		return cmesh.RepairDiscretizationConflicts(reporter);
		void ModifyArcDiscretization(SlidingMesh cart, ConceptualMesh cmesh, int arcIdx, SpatialIndex<int> cellIndex2)
		{
			if (cmesh.FaceNodeState[arcIdx] != FaceDiscretizationState.FIXED && (cmesh.FaceCells[arcIdx].A == regionIdx || cmesh.FaceCells[arcIdx].A == -1) && (cmesh.FaceCells[arcIdx].B == regionIdx || cmesh.FaceCells[arcIdx].B == -1))
			{
				List<MeshIntersection> crossings = MeshIntersection.RingToCrossingsRobust(cart, cmesh.FaceSmoothLine[arcIdx], cellIndex2);
				Polyline nodeString = cmesh.FaceNodeString[arcIdx];
				cmesh.FaceNodeString[arcIdx] = CombineNodeStrings(nodeString, crossings, cmesh.FaceSmoothLine[arcIdx]);
			}
		}
	}

	public Polyline CombineNodeStrings(Polyline nodeString, List<MeshIntersection> crossings, Polyline smooth)
	{
		List<PointOnPolyline> nearestPointOnPolylineOrdered = smooth.NearestPointOnPolylineOrdered(nodeString.Points);
		Polyline polyline = new Polyline(crossings.Count) { nodeString.First() };
		int i = 1;
		smooth.FirstPointOnPolyline();
		int j = 0;
		double sqrtTargetSizeUTimesTargetSizeV = Math.Sqrt(targetSizeU * targetSizeV);
		for (int pointCountPlusCount = nodeString.PointCount + crossings.Count; pointCountPlusCount >= 0; pointCountPlusCount--)
		{
			sizeFunction.QueryRange(polyline.Last(), checkInside: false, out var maximum, out var minimum);
			if (j < crossings.Count && sqrtTargetSizeUTimesTargetSizeV > 0.95 * minimum && sqrtTargetSizeUTimesTargetSizeV < 1.05 * maximum)
			{
				PointOnPolyline pop = crossings[j].pop;
				polyline.Add(pop.Point);
				j++;
				for (; i < nodeString.Count && !(nearestPointOnPolylineOrdered[i] > pop); i++)
				{
				}
			}
			else
			{
				if (i >= nodeString.Count)
				{
					break;
				}
				PointOnPolyline pointOnPolyline = nearestPointOnPolylineOrdered[i];
				polyline.Add(pointOnPolyline.Point);
				i++;
				for (; j < crossings.Count && !(crossings[j].pop > pointOnPolyline); j++)
				{
				}
			}
		}
		polyline.InplaceDepupe(0.001);
		return polyline;
	}

	public void CleanPerimeterCombined(SlidingMesh result, int nodeCountBefore, ProgressReporter reporter)
	{
		HashSet<int> intSet = new HashSet<int>();
		foreach (int faceIterator in result.FaceIterator())
		{
			if (result.FaceNodes[faceIterator].A >= nodeCountBefore && result.FaceNodes[faceIterator].B >= nodeCountBefore && (result.FaceCells[faceIterator].A < 0 || result.FaceCells[faceIterator].B < 0))
			{
				intSet.Add(result.FaceNodes[faceIterator].A);
				intSet.Add(result.FaceNodes[faceIterator].B);
			}
		}
		OperationResult opRes = new OperationResult();
		TinSizeFunction globalSizeFunction = origCmesh.GetGlobalSizeFunction(opRes);
		PostMergeFaces postMergeFaces = new PostMergeFaces(result, globalSizeFunction);
		postMergeFaces.nodes = intSet.ToList();
		postMergeFaces.enableCollapse = true;
		postMergeFaces.cellScaleFactor = 0.0;
		postMergeFaces.skewFactor = 5.0;
		postMergeFaces.faceSmoothFactor = 30.0;
		postMergeFaces.faceScaleFactor = 0.0;
		postMergeFaces.OptimizeAdaptive(intSet.Count * 4);
		HashSet<int> intSetSet = new HashSet<int>();
		intSetSet.Clear();
		for (int i = nodeCountBefore; i < result.NodeCount; i++)
		{
			if (!result.NodeDeleted[i] && !result.NodeIsFixed(i))
			{
				intSetSet.Add(i);
			}
		}
		new OptimizeNodeSkew(result, intSetSet.ToList()).Optimize(10);
		PostMergeFaces postMergeFaces2 = new PostMergeFaces(result, globalSizeFunction);
		postMergeFaces2.enableCollapse = true;
		postMergeFaces2.nodes = intSetSet.ToList();
		postMergeFaces2.deltaCostThreshold = 0.0;
		postMergeFaces2.minCostThreshold = 0.0;
		postMergeFaces2.cellScaleFactor = 0.0;
		postMergeFaces2.skewFactor = 5.0;
		postMergeFaces2.faceSmoothFactor = 30.0;
		postMergeFaces2.faceScaleFactor = 0.0;
		postMergeFaces2.OptimizeAdaptive(intSetSet.Count * 5);
	}

	public ISizeFunction GetRegionSizeFunction(bool secondary)
	{
		if (!secondary)
		{
			return new ConstantSizeFunction(targetSizeU);
		}
		return new ConstantSizeFunction(targetSizeV);
	}

	private void DumpToJson(SlidingMesh result)
	{
		List<object> objectList = null;
		using FileStream utf8Json = File.Create("C:\\Temp\\cmd.json");
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		if (objectList == null)
		{
			objectList = new List<object>();
		}
		objectList.Add(new List<object> { "SET", "g_raw", result });
		objectList.Add(new List<object> { "SET", "cg_raw", cmesh });
		objectList.Add(new List<object> { "PYTHON", "\r\ng=json_to_mesh(g_raw)\r\ncg=json_to_mesh(cg_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ng.plot_nodes(ax = ax, sizes=7)\r\ng.plot_edges(ax = ax, color = 'm', subedges='smoothStringXY')\r\ng.plot_cells(ax = ax, color = 'orange', subedges='smoothStringXY', alpha = 0.4, zorder = -1)\r\ncg.plot_edges(ax=ax, color='k', subedges='rawStringXY')\r\nfor j in cg.valid_edge_iter():\r\n    ax.plot(cg.edges['nodeStringXY'][j][:,0],\r\n            cg.edges['nodeStringXY'][j][:,1],\r\n            'ro')\r\n" });
		JsonSerializer.Serialize(utf8Json, objectList, options);
	}
}
