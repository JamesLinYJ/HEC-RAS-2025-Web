using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Attributes;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class MeshRenderLayer : RenderLayer, IVectorRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection
{
	public enum MeshQualityMode
	{
		[Description("None")]
		[EnumDisplayName("None")]
		None,
		[Description("Skew")]
		[EnumDisplayName("Skew")]
		Skew,
		[Description("Area Ratio")]
		[EnumDisplayName("Area Ratio")]
		AreaRatio,
		[Description("Center Spacing")]
		[EnumDisplayName("Center Spacing")]
		CenterSpacing,
		[Description("Orthogonality")]
		[EnumDisplayName("Orthogonality")]
		Orthogonality,
		[Description("Circumcenter Error")]
		[EnumDisplayName("Circumcenter Error")]
		Circumcenter,
		[Description("Cell Index")]
		[EnumDisplayName("Cell Index")]
		CellIndex,
		[Description("Node Degree")]
		[EnumDisplayName("Node Degree")]
		Degree,
		[Description("Composote")]
		[EnumDisplayName("Composite")]
		Composite
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("MeshRenderLayer");

	public static int HitTestPixelCount = 5;

	public static readonly VectorSymbology DefaultVectorSymbology = new VectorSymbology(Color.FromArgb(128, 0, 0, 0), 1.0);

	private readonly IMeshCollection _meshes;

	private bool _labelCells;

	private bool _labelFaces;

	private bool _labelFacePoints;

	private bool _renderPerimeter;

	private MeshQualityMode _renderQuality;

	private VersionTracker _symbologyVersionTracker;

	private RasterSymbology _rasterSymb = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.GrayToRed, new double[2] { 0.0, 1.1 }));

	private static ThreadLocal<List<Segment>> _segmentsToRenderCache = new ThreadLocal<List<Segment>>(() => new List<Segment>());

	private Versioned<PolygonCollection> _perimetersCache = new Versioned<PolygonCollection>();

	public IMeshCollection Meshes => _meshes;

	public int MeshCount => _meshes.Count;

	public int Count => _meshes.Count;

	public override Extent ValidExtent => _meshes.Extent;

	public VectorSymbology VectorSymbology { get; set; } = DefaultEditSymbology.MeshSymbology;

	[PlotOption("LabelCells", "Label Cells")]
	public bool LabelCells
	{
		get
		{
			return _labelCells;
		}
		set
		{
			if (_labelCells != value)
			{
				_labelCells = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("LabelFaces", "Label Faces")]
	public bool LabelFaces
	{
		get
		{
			return _labelFaces;
		}
		set
		{
			if (_labelFaces != value)
			{
				_labelFaces = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("LabelFacePoints", "Label Face Points")]
	public bool LabelFacePoints
	{
		get
		{
			return _labelFacePoints;
		}
		set
		{
			if (_labelFacePoints != value)
			{
				_labelFacePoints = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("RenderPerimeter", "Render the perimeter of this mesh")]
	public bool RenderPerimeter
	{
		get
		{
			return _renderPerimeter;
		}
		set
		{
			if (_renderPerimeter != value)
			{
				_renderPerimeter = value;
				IncrementRenderState();
			}
		}
	}

	[PlotOption("RenderQuality", "Render mesh quality")]
	public MeshQualityMode RenderQuality
	{
		get
		{
			return _renderQuality;
		}
		set
		{
			if (_renderQuality != value)
			{
				_renderQuality = value;
				IncrementRenderState();
			}
		}
	}

	public SelectedSet SelectedSet { get; } = new SelectedSet();

	public RasterSymbology MeshQualityRasterSymbology
	{
		get
		{
			return _rasterSymb;
		}
		set
		{
			_symbologyVersionTracker.Assign(ref _rasterSymb, value);
		}
	}

	private PolygonCollection Perimeters => _perimetersCache.Get(_meshes.VersionXY, _meshes, (IMeshCollection meshes) =>
	{
		PolygonCollection polygonCollection = new PolygonCollection();
		for (int i = 0; i < meshes.Count; i++)
		{
			polygonCollection.Add(meshes[i].Perimeter);
		}
		return polygonCollection;
	});

	public MeshRenderLayer(Mesh mesh)
		: this(new Mesh[1] { mesh })
	{
	}

	public MeshRenderLayer(IEnumerable<Mesh> meshes)
		: this(new MeshCollection(meshes))
	{
	}

	public MeshRenderLayer(IMeshCollection meshes)
	{
		_meshes = meshes;
	}

	public ISpatialVersion GetSpatialVersionContext()
	{
		return _meshes;
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		try
		{
			_meshes.Lock.AcquireReaderLock(1);
			try
			{
				if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
				{
					return false;
				}
				bool result = RenderInternalHelper(plot, ref cache);
				Dictionary<string, List<ILabelDefinition>> enabledScreenAnnotations = GetEnabledScreenAnnotations(plot.RasterQuery);
				if (enabledScreenAnnotations != null)
				{
					plot.Labels?.Clear();
					foreach (List<ILabelDefinition> iLabelDefinitions in enabledScreenAnnotations.Values)
					{
						for (int i = 0; i < iLabelDefinitions.Count; i++)
						{
							plot.AddLabel(iLabelDefinitions[i]);
						}
					}
				}
				return result;
			}
			finally
			{
				_meshes.Lock.ReleaseReaderLock();
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Possibly failed to acquire read lock.");
			return false;
		}
	}

	protected bool RenderInternalHelper(PlotRequest request, ref object cache)
	{
		if (!ValidExtent.Overlaps(request.RasterQuery.Extent))
		{
			return false;
		}
		_ = request.RasterQuery.CellCount;
		VectorSymbology.PenColor.ToArgb();
		bool condition = false;
		RenderInternalQualityHelper(request);
		List<Segment> segments = _segmentsToRenderCache.Value;
		segments.Clear();
		for (int i = 0; i < _meshes.Count; i++)
		{
			Mesh mesh = _meshes[i];
			foreach (int element in mesh.MetadataCache.GetFaceSpatialIndex().GetElements(request.RasterQuery.Extent))
			{
				mesh.AppendFaceSegments(element, segments);
			}
		}
		if (RenderPerimeter)
		{
			condition |= PolylineRenderLayer.Renderer.Render<Polygon, IPolygonCollection>(request, Perimeters, (int idx, IPolygonCollection polygons) => polygons[idx], Meshes.GetSegmentSpatialIndex().GetElements(request.RasterQuery), VectorSymbology);
		}
		return condition | PolylineRenderLayer.Renderer.Render(request, segments, VectorSymbology);
	}

	protected bool RenderInternalQualityHelper(PlotRequest request)
	{
		if (RenderQuality == MeshQualityMode.None)
		{
			return false;
		}
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, 0, out mem))
		{
			bool condition = false;
			for (int i = 0; i < _meshes.Count; i++)
			{
				Mesh mesh = _meshes[i];
				List<int> elements = mesh.MetadataCache.GetCellSpatialIndex().GetElements(request.RasterQuery.Extent);
				PolygonRasterizer polygonRasterizer = new PolygonRasterizer(request.RasterQuery);
				float[] floatValues = null;
				switch (RenderQuality)
				{
				case MeshQualityMode.Skew:
					floatValues = mesh.MetadataCache.CellSkewness;
					break;
				case MeshQualityMode.CenterSpacing:
					floatValues = mesh.MetadataCache.CellSpacing;
					break;
				case MeshQualityMode.Circumcenter:
					floatValues = mesh.MetadataCache.CellCircumcenter;
					break;
				case MeshQualityMode.AreaRatio:
					floatValues = mesh.MetadataCache.CellAreaRatio;
					break;
				case MeshQualityMode.Orthogonality:
					floatValues = mesh.MetadataCache.CellOrthogonality;
					break;
				case MeshQualityMode.CellIndex:
				{
					int cellCount = mesh.CellCount;
					if (cellCount <= 1)
					{
						floatValues = new float[cellCount];
						break;
					}
					floatValues = new float[cellCount];
					float cellCountOffset = cellCount - 1;
					for (int j = 0; j < cellCount; j++)
					{
						floatValues[j] = (float)j / cellCountOffset;
					}
					break;
				}
				case MeshQualityMode.Degree:
					floatValues = mesh.MetadataCache.CellDegree;
					break;
				case MeshQualityMode.Composite:
					floatValues = mesh.MetadataCache.CellCompositeMetric;
					break;
				}
				if (floatValues == null)
				{
					continue;
				}
				foreach (int element in elements)
				{
					_ = ref mesh.Cells[element];
					condition |= polygonRasterizer.Rasterize<Polygon, (Memory<int>, int)>(t: (mem, MeshQualityRasterSymbology.SurfaceFill.ColorARGB(floatValues[element], MeshQualityRasterSymbology.Alpha)), polygon: mesh.CellPolygons[element], fillCellAction: RENDER);
				}
			}
			if (condition)
			{
				request.Splat(mem);
			}
			return condition;
		}
		static void RENDER(int idx, (Memory<int>, int) payload)
		{
			payload.Item1.Span[idx] = payload.Item2;
		}
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + Meshes.VersionXY + _symbologyVersionTracker.GetVersion(MeshQualityRasterSymbology) + VectorSymbology.VersionXY;
	}

	public void AddHoverAction(Func<int, MeshElementType, int, MapHover> onHover)
	{
		AddMapHover((PlotRequest plot, HitTestArgs args) =>
		{
			if (HitTest(args, out var element, out var meshIndex, out var elementIndex))
			{
				Mesh m = _meshes[meshIndex];
				Highlight(plot, m, element, elementIndex);
				return onHover(meshIndex, element, elementIndex);
			}
			return (MapHover)null;
		});
	}

	public bool Highlight(PlotRequest request, int mIdx, MeshElementType elType, int elIdx)
	{
		return Highlight(request, _meshes[mIdx], elType, elIdx);
	}

	public bool Highlight(PlotRequest request, Mesh m, MeshElementType elType, int elIdx)
	{
		return elType switch
		{
			MeshElementType.Cell => PolygonRenderLayer.Highlight(m.CellPolygon(elIdx), VectorSymbology.GetHighlightSymbology(), request), 
			MeshElementType.Face => PolylineRenderLayer.Paint(m.FacePolyline(elIdx), VectorSymbology.GetHighlightSymbology(), request), 
			MeshElementType.FacePoint => PointRenderLayer.Paint(m.FacePoints[elIdx].Point, VectorSymbology.GetHighlightSymbology(), request), 
			_ => throw new NotImplementedException(), 
		};
	}

	public bool HitTest(HitTestArgs args, out MeshElementType element, out int meshIndex, out int elementIndex)
	{
		for (int i = 0; i < _meshes.Count; i++)
		{
			Mesh mesh = _meshes[i];
			if (mesh?.MetadataCache == null)
			{
				_logger.LogWarning("Skipping hit-test of mesh {idx} of {count} in {name}: {missing} is null.", i, _meshes.Count, DisplayName, (mesh == null) ? "Mesh" : "MetadataCache");
				continue;
			}
			Extent extent = mesh.Extent;
			Extent extent2 = args.HitBox(HitTestPixelCount);
			double hitTestPixelCountTimesCellSize = (double)HitTestPixelCount * args.Window.CellSize;
			if (!extent.MBROverlaps(extent2))
			{
				continue;
			}
			List<int> elements = mesh.MetadataCache.GetFacePointSpatialIndex().GetElements(extent2);
			for (int j = 0; j < elements.Count; j++)
			{
				int element2 = elements[j];
				Geospatial.Vectors.Point point = mesh.FacePoints[element2].Point;
				if (extent2.Contains(point))
				{
					meshIndex = i;
					element = MeshElementType.FacePoint;
					elementIndex = element2;
					return true;
				}
			}
			elements = mesh.MetadataCache.GetFaceSpatialIndex().GetElements(extent2);
			for (int k = 0; k < elements.Count; k++)
			{
				int element3 = elements[k];
				if (mesh.DistanceToFace(element3, args.QueryPoint) <= hitTestPixelCountTimesCellSize)
				{
					meshIndex = i;
					element = MeshElementType.Face;
					elementIndex = element3;
					return true;
				}
			}
			elements = mesh.MetadataCache.GetCellSpatialIndex().GetElements(extent2);
			Polygon polygon = new Polygon();
			for (int l = 0; l < elements.Count; l++)
			{
				int element4 = elements[l];
				polygon.Clear();
				mesh.FillCellPolygon_CCW(element4, polygon);
				if (polygon.Contains(args.QueryPoint))
				{
					meshIndex = i;
					element = MeshElementType.Cell;
					elementIndex = element4;
					return true;
				}
			}
		}
		meshIndex = -1;
		element = MeshElementType.Unknown;
		elementIndex = -1;
		return false;
	}

	public void AddDefaultLabelHoverAction()
	{
		AddHoverAction((int mIdx, MeshElementType el, int idx) =>
		{
			string textValue = "";
			switch (el)
			{
			case MeshElementType.Cell:
				textValue = "Cell";
				break;
			case MeshElementType.Face:
				textValue = "Face";
				break;
			case MeshElementType.FacePoint:
				textValue = "FacePoint";
				break;
			}
			return new MapHover
			{
				Label = textValue + idx.ToString().InSquareBrackets()
			};
		});
	}

	public override List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		if (!HitTest(args, out var element, out var meshIndex, out var elementIndex))
		{
			return null;
		}
		_ = _meshes[meshIndex];
		string textValue = "";
		switch (element)
		{
		case MeshElementType.Cell:
			textValue = "Cell";
			break;
		case MeshElementType.Face:
			textValue = "Face";
			break;
		case MeshElementType.FacePoint:
			textValue = "FacePoint";
			break;
		}
		return new List<MapHover>
		{
			new MapHover
			{
				Label = textValue + elementIndex.ToString().InSquareBrackets()
			}
		};
	}

	public bool Select(Extent selectExt, SelectMode mode)
	{
		_logger.LogWarning("Mesh selection not implemented");
		return false;
	}

	public bool Select(HitTestArgs selectArgs, SelectMode mode)
	{
		_logger.LogWarning("Mesh selection not implemented");
		return false;
	}

	public bool Select(Polyline intersectionLine, SelectMode mode)
	{
		_logger.LogWarning("Mesh selection not implemented");
		return false;
	}

	public bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		_logger.LogWarning("Mesh selection not implemented");
		return false;
	}

	public bool RenderSelected(PlotRequest plot)
	{
		return false;
	}

	public IList<object> GetSelectedVectors()
	{
		return GetSelectedVectors(SelectedSet, Meshes);
	}

	public static List<object> GetSelectedVectors(SelectedSet selectedSet, IMeshCollection meshes)
	{
		if (selectedSet.Count == 0)
		{
			return null;
		}
		return new List<object>(selectedSet.Elements.Select((int idx) => meshes[idx].Perimeter));
	}

	public IList<object> GetHitTestVectors(HitTestArgs args, out int _hitTestedIdx)
	{
		if (!HitTest(args, out var element, out var meshIndex, out var elementIndex))
		{
			_hitTestedIdx = -1;
			return default(CompactList<object>);
		}
		List<object> objectList = new List<object>();
		switch (element)
		{
		case MeshElementType.Cell:
			objectList.Add(Meshes[meshIndex].CellPolygons[elementIndex]);
			break;
		case MeshElementType.Face:
			objectList.Add(Meshes[meshIndex].FacePolyline(elementIndex));
			break;
		case MeshElementType.FacePoint:
			objectList.Add(Meshes[meshIndex].FacePoints[elementIndex].Point);
			break;
		}
		_hitTestedIdx = (meshIndex << 24) | elementIndex;
		return objectList;
	}

	public override Dictionary<string, List<ILabelDefinition>> GetEnabledScreenAnnotations(RasterDefinition raster)
	{
		if (!LabelCells && !LabelFacePoints && !LabelFaces)
		{
			return null;
		}
		Dictionary<string, List<ILabelDefinition>> iLabelDefinitionListByString = new Dictionary<string, List<ILabelDefinition>>();
		List<ILabelDefinition> iLabelDefinitions = null;
		List<ILabelDefinition> iLabelDefinitionsList = null;
		List<ILabelDefinition> iLabelDefinitions2 = null;
		if (LabelCells)
		{
			iLabelDefinitionsList = (iLabelDefinitionListByString["Cell"] = new List<ILabelDefinition>());
		}
		if (LabelFaces)
		{
			iLabelDefinitions = (iLabelDefinitionListByString["Face"] = new List<ILabelDefinition>());
		}
		HashSet<int> intSet = null;
		if (LabelFacePoints)
		{
			intSet = new HashSet<int>();
			iLabelDefinitions2 = (iLabelDefinitionListByString["FacePoint"] = new List<ILabelDefinition>());
		}
		for (int i = 0; i < _meshes.Count; i++)
		{
			Mesh mesh = _meshes[i];
			SpatialIndex<int> cellSpatialIndex = mesh.MetadataCache.GetCellSpatialIndex();
			cellSpatialIndex.GetBinRange(raster.Extent, out var completelyCoveredBins, out var partiallyCoveredBins);
			int binTerm = 0;
			foreach (int completelyCoveredBin in completelyCoveredBins)
			{
				binTerm += cellSpatialIndex.Bins[completelyCoveredBin]?.Count ?? 0;
			}
			foreach (int partiallyCoveredBin in partiallyCoveredBins)
			{
				int ordinal = binTerm;
				List<int> bin = cellSpatialIndex.Bins[partiallyCoveredBin];
				binTerm = ordinal + ((bin != null) ? (bin.Count / 2) : 0);
			}
			if (binTerm > 1000)
			{
				continue;
			}
			completelyCoveredBins.AddRange(partiallyCoveredBins);
			HashSet<int> intSetSet = new HashSet<int>();
			HashSet<int> intSet2 = new HashSet<int>();
			foreach (int completelyCoveredBin2 in completelyCoveredBins)
			{
				List<int> binList = cellSpatialIndex.Bins[completelyCoveredBin2];
				if (binList.IsNullOrEmpty())
				{
					continue;
				}
				foreach (int bin2 in binList)
				{
					if (!intSetSet.Add(bin2))
					{
						continue;
					}
					Cell cell = mesh.Cells[bin2];
					if (LabelCells && raster.Extent.Contains(mesh.CellCenters[bin2]))
					{
						iLabelDefinitionsList.Add(new LazyLabelDefinition<int>((int idx) => "C[" + idx + "]", bin2, mesh.CellCenters[bin2]));
					}
					if (LabelFaces)
					{
						Span<int> faces = cell.Faces.Span;
						for (int intervalIndex = 0; intervalIndex < faces.Length; intervalIndex++)
						{
							int faceIndex = faces[intervalIndex];
							if (!intSet2.Add(faceIndex))
							{
								continue;
							}
							_ = ref mesh.Faces[faceIndex];
							Geospatial.Vectors.Point pt = mesh.FaceMidPoint(faceIndex);
							if (raster.Extent.Contains(pt))
							{
								iLabelDefinitions.Add(new LazyLabelDefinition<int>((int idx) => "F[" + idx + "]", faceIndex, pt));
							}
						}
					}
					if (!LabelFacePoints)
					{
						continue;
					}
					Span<int> facesSpan = cell.Faces.Span;
					for (int intervalIndex2 = 0; intervalIndex2 < facesSpan.Length; intervalIndex2++)
					{
						int faceIndex2 = facesSpan[intervalIndex2];
						Face face = mesh.Faces[faceIndex2];
						if (intSet.Add(face.fpA))
						{
							Geospatial.Vectors.Point point = mesh.FacePoints[face.fpA].Point;
							if (raster.Extent.Contains(point))
							{
								iLabelDefinitions2.Add(new LazyLabelDefinition<int>((int idx) => "FP[" + idx + "]", face.fpA, point));
							}
						}
						if (!intSet.Add(face.fpB))
						{
							continue;
						}
						Geospatial.Vectors.Point point2 = mesh.FacePoints[face.fpB].Point;
						if (raster.Extent.Contains(point2))
						{
							iLabelDefinitions2.Add(new LazyLabelDefinition<int>((int idx) => "FP[" + idx + "]", face.fpB, point2));
						}
					}
				}
			}
		}
		return iLabelDefinitionListByString;
	}

	[SupportedOSPlatform("windows")]
	public static void DebugView(Mesh m)
	{
		RasterDefinition rdef = RasterDefinition.ComputeBoundingRaster(m.Extent, 1048576);
		new MeshRenderLayer(m).Render(rdef).DebugView();
	}
}
