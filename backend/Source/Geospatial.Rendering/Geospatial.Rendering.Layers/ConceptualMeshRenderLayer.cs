using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Systems;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Utility;
using Utility.DataStructures;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class ConceptualMeshRenderLayer : TopologyRenderLayer
{
	private bool _showRegionIndexing;

	private bool _showGlobalSizeFunction;

	private bool _showSizeFunctionEdges;

	private bool _showArcCellSizes = true;

	private bool _showRegionCellSizes = true;

	private bool _showRegionSizePreviews = true;

	private readonly Versioned<RasterSymbology> _sizeFunctionSymbologyCache = new Versioned<RasterSymbology>();

	private readonly Lock _lock = new Lock();

	private TinRenderLayer _sizeFunctionRenderLayer;

	[PlotOption("Show Region Indices")]
	public bool ShowRegionIndexing
	{
		get
		{
			return _showRegionIndexing;
		}
		set
		{
			if (_showRegionIndexing != value)
			{
				_showRegionIndexing = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Global Size Function")]
	public bool ShowGlobalSizeFunction
	{
		get
		{
			return _showGlobalSizeFunction;
		}
		set
		{
			if (_showGlobalSizeFunction != value)
			{
				_showGlobalSizeFunction = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Size Function TIN")]
	public bool ShowSizeFunctionEdges
	{
		get
		{
			return _showSizeFunctionEdges;
		}
		set
		{
			if (_showSizeFunctionEdges != value)
			{
				_showSizeFunctionEdges = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Arc Cell Sizes")]
	public bool ShowArcCellSizes
	{
		get
		{
			return _showArcCellSizes;
		}
		set
		{
			if (_showArcCellSizes != value)
			{
				_showArcCellSizes = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Region Cell Sizes")]
	public bool ShowRegionCellSizes
	{
		get
		{
			return _showRegionCellSizes;
		}
		set
		{
			if (_showRegionCellSizes != value)
			{
				_showRegionCellSizes = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("Show Region Size Previews")]
	public bool ShowRegionSizePreviews
	{
		get
		{
			return _showRegionSizePreviews;
		}
		set
		{
			if (_showRegionSizePreviews != value)
			{
				_showRegionSizePreviews = value;
				IncrementRenderState();
			}
		}
	}

	public RasterSymbology SizeFunctionRasterSymbology(Tin sizeFunctionTin)
	{
		return _sizeFunctionSymbologyCache.Get(sizeFunctionTin.FullVersion, () => ComputeSizeFunctionSymbology(sizeFunctionTin));
	}

	public ConceptualMeshRenderLayer(Any<Topology> topo)
		: base(topo)
	{
	}

	protected override bool RenderInternalHelper(Topology topo, PlotRequest request, ref object cache)
	{
		bool result = base.RenderInternalHelper(topo, request, ref cache);
		if (!ValidExtent.Overlaps(request.RasterQuery.Extent))
		{
			return result;
		}
		int cellCount = request.RasterQuery.CellCount;
		if (ShowGlobalSizeFunction)
		{
			Memory<int> mem;
			using (SharedMemoryPool<int>.Rent(cellCount, 0, out mem))
			{
				if (RenderInternalSizeFunctionHelper(topo, request, mem))
				{
					request.Splat(mem);
				}
			}
		}
		if (ShowRegionSizePreviews)
		{
			List<int> elements = topo.RegionSpatialIndex.GetElements(request.RasterQuery.Extent);
			List<Segment> segments = new List<Segment>(elements.Count * 4);
			foreach (int element in elements)
			{
				Geospatial.Vectors.Topology.Region region = topo.CompletedRegions[element];
				if (region.ControlPointIndices.Count == 0)
				{
					continue;
				}
				ComplexPolygon regionComplexPolygon = topo.GetRegionComplexPolygon(element);
				if (!regionComplexPolygon.Extent.Overlaps(request.RasterQuery.Extent))
				{
					continue;
				}
				int controlPointIndex = region.ControlPointIndex;
				MeshRegionMetadata controlPointData = topo.GetControlPointData(controlPointIndex);
				Geospatial.Vectors.Point bestLabelPoint = regionComplexPolygon.BestLabelPoint;
				if (request.RasterQuery.Extent.Extend(controlPointData.ActualPrimaryCellSize / 2f).Contains(bestLabelPoint))
				{
					float actualPrimaryCellSize = controlPointData.ActualPrimaryCellSize;
					float actualSecondaryConstantSize = controlPointData.ActualSecondaryConstantSize;
					if (float.IsNaN(actualSecondaryConstantSize) || float.IsInfinity(actualSecondaryConstantSize) || actualSecondaryConstantSize <= 0f)
					{
						actualSecondaryConstantSize = actualPrimaryCellSize;
					}
					float scaledActualPrimaryCellSize = actualPrimaryCellSize / 2f;
					double xComponent = bestLabelPoint.X - (double)scaledActualPrimaryCellSize;
					double yComponent = bestLabelPoint.Y - (double)scaledActualPrimaryCellSize;
					if (controlPointData.PatchType == PatchType.TrianglePaver)
					{
						Geospatial.Vectors.Point point = new Geospatial.Vectors.Point(xComponent, yComponent);
						Geospatial.Vectors.Point point2 = new Geospatial.Vectors.Point((double)actualPrimaryCellSize + xComponent, yComponent);
						Geospatial.Vectors.Point point3 = new Geospatial.Vectors.Point((double)(actualPrimaryCellSize * 0.5f) + xComponent, yComponent + (double)(actualPrimaryCellSize * (float)Math.Sqrt(3.0) / 2f));
						segments.Add(new Segment(point, point2));
						segments.Add(new Segment(point2, point3));
						segments.Add(new Segment(point3, point));
					}
					else if (controlPointData.PatchType == PatchType.Quad)
					{
						Extent extent = Extent.CenteredOn(bestLabelPoint, actualPrimaryCellSize, actualSecondaryConstantSize);
						segments.Add(extent.BottomWall);
						segments.Add(extent.LeftWall);
						segments.Add(extent.RightWall);
						segments.Add(extent.TopWall);
					}
					else if (controlPointData.PatchType == PatchType.Cartesian)
					{
						float rotationDegrees = controlPointData.RotationDegrees;
						Extent extent2 = Extent.CenteredOn(bestLabelPoint, actualPrimaryCellSize, actualSecondaryConstantSize);
						RotateTransformer transformer = new RotateTransformer(bestLabelPoint, rotationDegrees, RotationUnits.Degrees, Orientation.CounterClockwise);
						segments.Add(extent2.BottomWall.Transform(transformer));
						segments.Add(extent2.LeftWall.Transform(transformer));
						segments.Add(extent2.RightWall.Transform(transformer));
						segments.Add(extent2.TopWall.Transform(transformer));
					}
				}
			}
			PolylineRenderLayer.Renderer.Render(request, segments, new VectorSymbology(Color.FromArgb(255, 0, 0, 0), 3.0, default));
		}
		if (ShowArcCellSizes)
		{
			List<FeatureSegmentSubset> elements2 = topo.ArcSpatialIndex.GetElements(request.RasterQuery.Extent, dedupe: false);
			elements2.Dedupe();
			foreach (FeatureSegmentSubset elements22 in elements2)
			{
				Geospatial.Vectors.Point arcMidpoint = topo.GetArcMidpoint(elements22.Fid);
				if (!request.RasterQuery.Extent.Contains(arcMidpoint))
				{
					continue;
				}
				ArcMetadata arcData = topo.GetArcData(elements22.Fid);
				string arcDataTerm;
				if (arcData.SizeFunction == LinearSizeFunction.CountWithBias)
				{
					if (arcData.Count < 1)
					{
						continue;
					}
					arcDataTerm = arcData.Count.ToString();
				}
				else
				{
					float constantSize = arcData.ConstantSize;
					if (float.IsNaN(constantSize) || constantSize < 1f)
					{
						continue;
					}
					arcDataTerm = constantSize.ToString("0.#");
				}
				request.AddLabel(new LabelDefinition(arcDataTerm, arcMidpoint));
			}
		}
		if (ShowRegionIndexing || ShowRegionCellSizes)
		{
			List<int> elements3 = topo.RegionSpatialIndex.GetElements(request.RasterQuery.Extent);
			for (int i = 0; i < elements3.Count; i++)
			{
				int regionIdx = elements3[i];
				int controlPointIndex2 = topo.CompletedRegions[regionIdx].ControlPointIndex;
				if (controlPointIndex2 < 0)
				{
					continue;
				}
				ComplexPolygon regionComplexPolygon2 = topo.GetRegionComplexPolygon(regionIdx);
				if (!regionComplexPolygon2.Extent.Overlaps(request.RasterQuery.Extent))
				{
					continue;
				}
				Geospatial.Vectors.Point bestLabelPoint2 = regionComplexPolygon2.BestLabelPoint;
				if (!request.RasterQuery.Extent.Contains(bestLabelPoint2))
				{
					continue;
				}
				if (ShowRegionCellSizes)
				{
					MeshRegionMetadata controlPointData2 = topo.GetControlPointData(controlPointIndex2);
					if (controlPointData2.HasPrimaryCellSize && controlPointData2.HasSecondaryCellSize)
					{
						request.AddLabel(new LabelDefinition(controlPointData2.ActualPrimaryCellSize.ToString("0.#") + " / " + controlPointData2.ActualSecondaryConstantSize.ToString("0.#"), bestLabelPoint2));
					}
					else if (controlPointData2.HasPrimaryCellSize)
					{
						request.AddLabel(new LabelDefinition(controlPointData2.ActualPrimaryCellSize.ToString("0.#"), bestLabelPoint2));
					}
					else if (controlPointData2.HasSecondaryCellSize)
					{
						request.AddLabel(new LabelDefinition("_  / " + controlPointData2.ActualSecondaryConstantSize.ToString("0.#"), bestLabelPoint2));
					}
				}
				if (ShowRegionIndexing)
				{
					request.AddLabel(new LazyLabelDefinition<int>((int idx) => $"Region {regionIdx}, Control {idx}", controlPointIndex2, bestLabelPoint2));
				}
			}
		}
		return result;
	}

	private RasterSymbology ComputeSizeFunctionSymbology(Tin sizeFunctionTin)
	{
		double minValue = double.MinValue;
		double maxValue = double.MaxValue;
		List<TriangleDefinition> triangleDefinition = sizeFunctionTin.TriangleDefinition;
		IList<double> z = sizeFunctionTin.Z;
		for (int i = 0; i < triangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition2 = triangleDefinition[i];
			if (!triangleDefinition2.IsDeleted)
			{
				double p0TriangleDefinition2Term = z[triangleDefinition2.p0];
				minValue = Math.Max(minValue, p0TriangleDefinition2Term);
				maxValue = Math.Min(maxValue, p0TriangleDefinition2Term);
				p0TriangleDefinition2Term = z[triangleDefinition2.p1];
				minValue = Math.Max(minValue, p0TriangleDefinition2Term);
				maxValue = Math.Min(maxValue, p0TriangleDefinition2Term);
				p0TriangleDefinition2Term = z[triangleDefinition2.p2];
				minValue = Math.Max(minValue, p0TriangleDefinition2Term);
				maxValue = Math.Min(maxValue, p0TriangleDefinition2Term);
			}
		}
		minValue = Math.Max(minValue, maxValue + 0.1);
		return new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.SedimentConcentration, new double[2] { maxValue, minValue }));
	}

	private TinRenderLayer GetSizeFunctionRenderLayerThreadsafe(Tin tin)
	{
		TinRenderLayer sizeFunctionRenderLayer = _sizeFunctionRenderLayer;
		if (sizeFunctionRenderLayer != null && sizeFunctionRenderLayer.Tin == tin)
		{
			return sizeFunctionRenderLayer;
		}
		sizeFunctionRenderLayer = new TinRenderLayer(tin);
		sizeFunctionRenderLayer.RenderEdges = true;
		_sizeFunctionRenderLayer = sizeFunctionRenderLayer;
		return sizeFunctionRenderLayer;
	}

	protected bool RenderInternalSizeFunctionHelper(Topology topo, PlotRequest request, Memory<int> clrBuf)
	{
		if (!ShowGlobalSizeFunction)
		{
			return false;
		}
		bool anyPaint = false;
		TinSizeFunction globalSizeFunction = topo.GlobalSizeFunction;
		if (globalSizeFunction == null)
		{
			return false;
		}
		Tin tin = globalSizeFunction.Tin;
		if (tin == null || !tin.Extent.Overlaps(request.RasterQuery.Extent))
		{
			return false;
		}
		IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(request.RasterQuery.CellCount, -9999f, out var rasterData);
		tin.Rasterize(request.RasterQuery, (int px, int tIdx, BarycentricWeights bw) =>
		{
			Span<float> rasterDataSpan = rasterData.Span;
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[tIdx];
			if (!triangleDefinition.IsDeleted)
			{
				float interpolate = (float)bw.Interpolate(tin.Z[triangleDefinition.p0], tin.Z[triangleDefinition.p1], tin.Z[triangleDefinition.p2]);
				rasterDataSpan[px] = interpolate;
				anyPaint = true;
			}
		});
		if (!anyPaint)
		{
			memoryOwner.Dispose();
		}
		else
		{
			request.SetRasterData_TransferOwnership(this, memoryOwner);
			RasterRenderHelper.Render(rasterData, request, SizeFunctionRasterSymbology(tin));
			if (ShowSizeFunctionEdges)
			{
				GetSizeFunctionRenderLayerThreadsafe(tin).Render(request);
			}
		}
		return anyPaint;
	}
}
