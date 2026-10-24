using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics;
using Ras.Layers;
using Utility;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Memory;
using Utility.Unsafe;

namespace Ras.Mapping;

public class GeometryMapPoints
{
	private enum QueryType
	{
		None,
		Raster,
		Points,
		Line
	}

	private struct PixelComputeBuffer(int nFacepoints)
	{
		public float[] CWCanGive = new float[nFacepoints];

		public float[] CCWCanGive = new float[nFacepoints];

		public float[] XProducts = new float[nFacepoints];

		public int Count = nFacepoints;

		public void Resize(int nFacepoints)
		{
			CWCanGive = new float[nFacepoints];
			CCWCanGive = new float[nFacepoints];
			XProducts = new float[nFacepoints];
			Count = nFacepoints;
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("GeometryMapPoints");

	private static readonly List<FlatMeshMap> _emptyFlat2d = new List<FlatMeshMap>();

	private static readonly List<SlopingMeshMap> _emptySloped2d = new List<SlopingMeshMap>();

	private readonly Geometry _geometry;

	private readonly GeometrySamplingParameters _params;

	private static readonly ThreadLocal<PolygonRasterizer> _pgRasterizers = new ThreadLocal<PolygonRasterizer>(() => PolygonRasterizer.MakeUninitializedPolygonRasterizer());

	private static readonly ThreadLocal<List<PixelRange>> _prCache = new ThreadLocal<List<PixelRange>>(() => new List<PixelRange>());

	private List<FlatMeshMap> _flatAreas;

	private List<SlopingMeshMap> _slopingAreas;

	private readonly RasterDefinition _raster = RasterDefinition.NullRaster;

	private readonly int _pointCount = -1;

	private Pool<Point[]> MaxFaceCountPerCellPlusOnePointPool = new Pool<Point[]>(() => new Point[9]);

	public Geometry Geometry => _geometry;

	public LogAggregator RasterizeTimingsLogger { get; set; }

	public List<FlatMeshMap> FlatMapped2DAreas => _flatAreas ?? _emptyFlat2d;

	public List<SlopingMeshMap> SlopingMapped2DAreas => _slopingAreas ?? _emptySloped2d;

	public bool HasMapped2DCells
	{
		get
		{
			if (SlopingMapped2DAreas == null || SlopingMapped2DAreas.Count <= 0)
			{
				if (FlatMapped2DAreas != null)
				{
					return FlatMapped2DAreas.Count > 0;
				}
				return false;
			}
			return true;
		}
	}

	public RasterDefinition Raster => _raster;

	public int InputCount => _pointCount;

	public int MappedPixelCount { get; set; }

	public static LogAggregator CreateDefaultRasterTimings(ILogger logger)
	{
		return new LogAggregator(logger, "Geometry Rasterized: {Duration} ms.", 100);
	}

	public GeometryMapPoints(Geometry geometry, RasterDefinition raster, GeometrySamplingParameters parameters = null)
	{
		_geometry = geometry;
		_raster = raster;
		_pointCount = _raster.CellCount;
		_params = parameters ?? GeometrySamplingParameters.Default;
		Stopwatch stopwatch = Stopwatch.StartNew();
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		if (flowAreaLayer.Count == 0)
		{
			return;
		}
		int length = raster.Rows * raster.Cols;
		if (flowAreaLayer.Count == 1)
		{
			if (raster.Overlaps(flowAreaLayer.Extent))
			{
				Rasterize2DFlowAreasWithSpatialIndex(null);
			}
		}
		else
		{
			BitArray mask = new BitArray(length);
			if (flowAreaLayer.Count > 0 && raster.Overlaps(flowAreaLayer.Extent))
			{
				Rasterize2DFlowAreasWithSpatialIndex(mask);
			}
		}
		stopwatch.Stop();
		MappedPixelCount = ComputeMappedPixelCount();
		if (MappedPixelCount > 0)
		{
			RasterizeTimingsLogger?.Add(stopwatch);
		}
	}

	public GeometryMapPoints(Geometry geometry, Point pt, GeometrySamplingParameters parameters = null)
		: this(geometry, new Point[1] { pt }, parameters)
	{
	}

	public GeometryMapPoints(Geometry geometry, List<Point> points, GeometrySamplingParameters parameters = null)
		: this(geometry, UnsafeHelpers.GetListBackingBuffer(points), parameters)
	{
	}

	public GeometryMapPoints(Geometry geometry, Memory<Point> points, GeometrySamplingParameters parameters = null)
	{
		_geometry = geometry;
		_pointCount = points.Length;
		_params = parameters ?? GeometrySamplingParameters.Default;
		if (_pointCount == 0 || _geometry.FlowAreaLayer.Count == 0)
		{
			return;
		}
		Extent ext = PointCollection.ComputeExtent(points).Intersection(_geometry.Extent);
		if (!ext.IsNull())
		{
			if (ext.Width == 0.0 || ext.Height == 0.0)
			{
				ext = ext.Extend(0.01);
			}
			BitArray mask = new BitArray(points.Length);
			SpatialIndex<int> spatialIndex = new SpatialIndex<int>(ext, SpatialIndex.ReasonableBinCount(points.Length));
			spatialIndex.AddRangeIfInBounds(points);
			if (_params.ForceHorizontalRendering)
			{
				Compute2DPointsFlat(points, ext, mask);
			}
			else
			{
				Compute2DPointsSloped(points, spatialIndex, mask);
			}
			MappedPixelCount = ComputeMappedPixelCount();
		}
	}

	private int ComputeMappedPixelCount()
	{
		int mappedPixelCount = 0;
		if (_flatAreas != null)
		{
			mappedPixelCount += _flatAreas.Sum((FlatMeshMap area) => area.Cells.Sum((FlatCellMap cell) => cell.PointIndexes.Count));
		}
		if (_slopingAreas != null)
		{
			foreach (SlopingMeshMap slopingArea in _slopingAreas)
			{
				for (int intervalIndex = 0; intervalIndex < slopingArea.Cells.Count; intervalIndex++)
				{
					Span<PointRange> pixelRanges = slopingArea.Cells[intervalIndex].PixelRanges.Span;
					for (int intervalIndex2 = 0; intervalIndex2 < pixelRanges.Length; intervalIndex2++)
					{
						PointRange pointRange = pixelRanges[intervalIndex2];
						mappedPixelCount += pointRange.QueryEndIndex - pointRange.QueryStartIndex + 1;
					}
				}
			}
		}
		return mappedPixelCount;
	}

	private void Rasterize2DFlowAreasWithSpatialIndex(BitArray mask)
	{
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		for (int i = 0; i < flowAreaLayer.Count; i++)
		{
			int meshIdx = i;
			Mesh mesh = flowAreaLayer[i].Mesh;
			if (!mesh.IsValid() || !_raster.Overlaps(mesh.Extent))
			{
				continue;
			}
			List<int> elements = mesh.GetCellSpatialIndex().GetElements(_raster);
			if (elements.Count == 0)
			{
				continue;
			}
			List<int> slopedCells = null;
			if (_params.CellPixelRenderingCutoff == 0f)
			{
				slopedCells = elements;
			}
			else
			{
				double[] allCellAreasSorted = mesh.MetadataCache.GetAllCellAreasSorted();
				SplitCellsOnThreshold(mesh, elements, allCellAreasSorted, out var flatCells, out slopedCells);
				if (flatCells != null && flatCells.Count > 0)
				{
					if (mask == null)
					{
						List<FlatCellMap> map = ComputeRasterizedPixelLocations_Serial(flatCells, mesh);
						AddMesh(i, map);
					}
					else
					{
						List<FlatCellMap> map2 = ComputeRasterizedPixelLocations(flatCells, mesh, mask);
						AddMesh(i, map2);
					}
				}
			}
			if (slopedCells != null && slopedCells.Count > 0)
			{
				if (mask == null)
				{
					List<SlopingCellMap> map3 = ComputeSlopedCells_Serial(slopedCells, meshIdx);
					AddMesh(i, map3);
				}
				else
				{
					List<SlopingCellMap> map4 = ComputeSlopedCells(slopedCells, meshIdx, mask);
					AddMesh(i, map4);
				}
			}
		}
	}

	private void SplitCellsOnThreshold(Mesh mesh, List<int> nearbyCells, double[] areas, out List<int> flatCells, out List<int> slopedCells)
	{
		if (_params.ForceHorizontalRendering)
		{
			flatCells = nearbyCells;
			slopedCells = null;
			return;
		}
		double cellAreaTimesCellPixelRenderingCutoff = _raster.CellArea * (double)_params.CellPixelRenderingCutoff;
		MeshStatistics meshStatistics = mesh.GetMeshStatistics();
		double cellMaximumArea = meshStatistics.CellMaximumArea;
		double cellMinimumArea = meshStatistics.CellMinimumArea;
		if (double.IsNaN(cellMaximumArea) || double.IsNaN(cellMinimumArea))
		{
			cellMaximumArea = double.MaxValue;
			cellMinimumArea = 0.0;
		}
		if (cellMinimumArea > cellAreaTimesCellPixelRenderingCutoff)
		{
			flatCells = null;
			slopedCells = nearbyCells;
			return;
		}
		if (cellMaximumArea < cellAreaTimesCellPixelRenderingCutoff)
		{
			flatCells = nearbyCells;
			slopedCells = null;
			return;
		}
		int binarySearch = Array.BinarySearch(areas, cellAreaTimesCellPixelRenderingCutoff);
		if (binarySearch < 0)
		{
			binarySearch = ~binarySearch;
			binarySearch--;
		}
		flatCells = new List<int>(binarySearch + 1);
		slopedCells = new List<int>(areas.Length - binarySearch - 1);
		foreach (int nearbyCell in nearbyCells)
		{
			if (areas[nearbyCell] <= cellAreaTimesCellPixelRenderingCutoff)
			{
				flatCells.Add(nearbyCell);
			}
			else
			{
				slopedCells.Add(nearbyCell);
			}
		}
	}

	private List<FlatCellMap> ComputeRasterizedPixelLocations(List<int> candidateCells, Mesh mesh, BitArray mask)
	{
		ThreadLocal<PolygonRasterizer> threadLocalRasterizers = new ThreadLocal<PolygonRasterizer>(() => new PolygonRasterizer(_raster));
		ThreadLocal<List<FlatCellMap>> threadLocalCellLists = new ThreadLocal<List<FlatCellMap>>(() => new List<FlatCellMap>());
		new ExtentRasterizer(_raster);
		ConcurrentBag<List<FlatCellMap>> cellListThreadsafeCollection = new ConcurrentBag<List<FlatCellMap>>();
		System.Threading.Tasks.Parallel.ForEach(candidateCells, (int cIdx) =>
		{
			bool isValueCreated = threadLocalCellLists.IsValueCreated;
			List<FlatCellMap> flatCellMaps = threadLocalCellLists.Value;
			if (!isValueCreated)
			{
				cellListThreadsafeCollection.Add(flatCellMaps);
			}
			PolygonRasterizer polygonRasterizer = threadLocalRasterizers.Value;
			Polygon polygon = mesh.CellPolygon(cIdx);
			double areaOverArea = _raster.Extent.Intersection(polygon.Extent).Area / _raster.Area;
			List<int> cellCountTimesAreaOverArea = new List<int>((int)((double)_raster.CellCount * areaOverArea));
			polygonRasterizer.Rasterize(polygon, (mask, cellCountTimesAreaOverArea), (int n, (BitArray, List<int>) payload) =>
			{
				var (bitArray, intList) = payload;
				if (!bitArray[n])
				{
					intList.Add(n);
					bitArray[n] = true;
				}
			});
			if (cellCountTimesAreaOverArea.Count != 0)
			{
				flatCellMaps.Add(new FlatCellMap(cIdx, cellCountTimesAreaOverArea));
			}
		});
		List<FlatCellMap> flatCellMapsList = new List<FlatCellMap>();
		foreach (List<FlatCellMap> cellListThreadsafeCollectionList in cellListThreadsafeCollection)
		{
			flatCellMapsList.AddRange(cellListThreadsafeCollectionList);
		}
		threadLocalRasterizers.Dispose();
		threadLocalCellLists.Dispose();
		return flatCellMapsList;
	}

	private List<FlatCellMap> ComputeRasterizedPixelLocations_Serial(List<int> candidateCells, Mesh mesh)
	{
		List<FlatCellMap> flatCellMaps = new List<FlatCellMap>();
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(Raster);
		for (int i = 0; i < candidateCells.Count; i++)
		{
			int candidateCellIndex = candidateCells[i];
			Polygon polygon = mesh.CellPolygon(candidateCellIndex);
			double areaOverArea = _raster.Extent.Intersection(polygon.Extent).Area / _raster.Area;
			List<int> cellCountTimesAreaOverArea = new List<int>((int)((double)_raster.CellCount * areaOverArea));
			polygonRasterizer.Rasterize(polygon, cellCountTimesAreaOverArea, (int n, List<int> payload) =>
			{
				payload.Add(n);
			});
			if (cellCountTimesAreaOverArea.Count != 0)
			{
				flatCellMaps.Add(new FlatCellMap(candidateCellIndex, cellCountTimesAreaOverArea));
			}
		}
		return flatCellMaps;
	}

	private void BensWeights(Cell cell, int cIdx, double[,] cellFpXPs, Span<float> fpWeights, Span<float> velocityWeights, Span<float> XProducts, Span<float> buf2, Span<double> buf3)
	{
		int length = cell.Faces.Length;
		Span<double> buf3Span = buf3;
		double computed = 0.0;
		for (int i = 0; i < length; i++)
		{
			int ordinal = i;
			int lengthOffset = i - 1;
			if (lengthOffset == -1)
			{
				lengthOffset = length - 1;
			}
			double unity = 1.0;
			for (int j = 0; j < length; j++)
			{
				if (j != ordinal && j != lengthOffset)
				{
					unity *= (double)XProducts[j];
				}
			}
			double buf3Span2 = unity * cellFpXPs[cIdx, i];
			buf3Span[i] = buf3Span2;
			computed += buf3Span2;
		}
		bool condition = false;
		for (int k = 0; k < length; k++)
		{
			if (buf3Span[k] < 0.0)
			{
				condition = true;
			}
			fpWeights[k] = (float)(buf3Span[k] / computed);
		}
		if (condition)
		{
			float fpWeightSum = 0f;
			for (int l = 0; l < length; l++)
			{
				if (fpWeights[l] < 0f)
				{
					fpWeights[l] = 0f;
				}
				else
				{
					fpWeightSum += fpWeights[l];
				}
			}
			if (fpWeightSum != 0f)
			{
				for (int m = 0; m < length; m++)
				{
					fpWeights[m] /= fpWeightSum;
				}
			}
		}
		fpWeights.CopyTo(velocityWeights);
		Donate(velocityWeights, length, XProducts, buf2);
	}

	private void Donate(Span<float> stencilWeights, int nFacePoints, Span<float> buf1, Span<float> buf2)
	{
		Span<float> buf1Span = buf1;
		Span<float> buf2Span = buf2;
		for (int i = 0; i < nFacePoints; i++)
		{
			int nFacePointsOffset = i - 1;
			if (nFacePointsOffset == -1)
			{
				nFacePointsOffset = nFacePoints - 1;
			}
			int ordinal = i + 1;
			if (ordinal == nFacePoints)
			{
				ordinal = 0;
			}
			float stencilWeight = stencilWeights[i];
			float stencilWeight2 = stencilWeights[nFacePointsOffset];
			float stencilWeight3 = stencilWeights[ordinal];
			float stencilWeight2PlusStencilWeight3 = stencilWeight2 + stencilWeight3;
			if (stencilWeight2PlusStencilWeight3 == 0f)
			{
				buf1Span[i] = 0f;
				buf2Span[i] = 0f;
			}
			else
			{
				float stencilWeightTerm = stencilWeight / stencilWeight2PlusStencilWeight3;
				buf1Span[i] = stencilWeightTerm * stencilWeight2;
				buf2Span[i] = stencilWeightTerm * stencilWeight3;
			}
		}
		for (int j = 0; j < nFacePoints; j++)
		{
			int index = j;
			int ordinal2 = j + 1;
			if (ordinal2 == nFacePoints)
			{
				ordinal2 = 0;
			}
			float buf22 = buf2Span[index];
			float buf12 = buf1Span[ordinal2];
			float buf22Buf12Term = Math.Min(buf22, buf12);
			stencilWeights[index] -= buf22Buf12Term;
			stencilWeights[ordinal2] -= buf22Buf12Term;
			stencilWeights[nFacePoints + j] = buf22Buf12Term * 2f;
		}
	}

	private void GetPixelXProducts(Mesh mesh, int cIdx, Point p, Span<float> XProducts)
	{
		Cell cell = mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		for (int i = 0; i < length; i++)
		{
			int faceIndex = faces[i];
			Face face = mesh.Faces[faceIndex];
			int fPPrev = face.GetFPPrev(cIdx);
			int fPNext = face.GetFPNext(cIdx);
			Point b = mesh.FacePoints[fPPrev].Point;
			Point c = mesh.FacePoints[fPNext].Point;
			XProducts[i] = (float)Vector.CrossProduct(in p, in b, in c);
		}
	}

	private float[] GetPixelXProducts(Mesh mesh, int cIdx, List<Point> points, PixelComputeBuffer[] buf)
	{
		Cell cell = mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		int length = faces.Length;
		float[] xProducts = buf[0].XProducts;
		Point a = points.First();
		for (int i = 0; i < length; i++)
		{
			int faceIndex = faces[i];
			Face face = mesh.Faces[faceIndex];
			int fPPrev = face.GetFPPrev(cIdx);
			int fPNext = face.GetFPNext(cIdx);
			Point b = mesh.FacePoints[fPPrev].Point;
			Point c = mesh.FacePoints[fPNext].Point;
			xProducts[i] = (float)Vector.CrossProduct(in a, in b, in c);
		}
		return xProducts;
	}

	private void ComputeUniqueFacesAndFacepoints(List<int> cellList, Mesh mesh, out List<int> uniqueFPs, out List<int> uniqueFaces)
	{
		if ((double)cellList.Count > 0.1 * (double)mesh.CellCount || cellList.Count == 0)
		{
			uniqueFPs = null;
			uniqueFaces = null;
			return;
		}
		uniqueFPs = new List<int>();
		uniqueFaces = new List<int>();
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		foreach (int cell in cellList)
		{
			_ = ref mesh.Cells[cell];
			foreach (int cellFacePoint in mesh.CellFacePoints(cell))
			{
				if (!intSet.Add(cellFacePoint))
				{
					continue;
				}
				foreach (int face in mesh.FacePoints[cellFacePoint].Faces)
				{
					intSetSet.Add(face);
				}
			}
		}
		uniqueFPs = intSet.ToList();
		uniqueFaces = intSetSet.ToList();
	}

	private List<SlopingCellMap> ComputeSlopedCells(List<int> slopedCells, int meshIdx, BitArray mask)
	{
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		Mesh mesh = flowAreaLayer[meshIdx].Mesh;
		double[,] cellFpXPs = mesh.MetadataCache.GetFacePointCrossProductWeights();
		ThreadLocal<List<SlopingCellMap>> threadLocalCellLists = new ThreadLocal<List<SlopingCellMap>>(() => new List<SlopingCellMap>());
		ThreadLocal<PolygonRasterizer> threadLocalRasterizer = new ThreadLocal<PolygonRasterizer>(() => new PolygonRasterizer(Raster));
		ConcurrentBag<List<SlopingCellMap>> trackCellLists = new ConcurrentBag<List<SlopingCellMap>>();
		_ = mesh.AreaTolerance;
		Utility.Parallel.SmartFor(slopedCells.Count, (int startLocal, int endLocal) =>
		{
			PoolHandle<Point[]> poolHandle = MaxFaceCountPerCellPlusOnePointPool.Acquire();
			Point[] cellPts = poolHandle.Data;
			for (int i = startLocal; i <= endLocal; i++)
			{
				int cIdx = slopedCells[i];
				Cell cell = mesh.Cells[cIdx];
				if (mesh.CellIsVirtual(cIdx))
				{
					MaxFaceCountPerCellPlusOnePointPool.Return(poolHandle);
					return;
				}
				bool isValueCreated = threadLocalCellLists.IsValueCreated;
				List<SlopingCellMap> slopingCellMaps = threadLocalCellLists.Value;
				if (!isValueCreated)
				{
					trackCellLists.Add(slopingCellMaps);
				}
				PolygonRasterizer polygonRasterizer = threadLocalRasterizer.Value;
				Polygon polygon = mesh.CellPolygon(cIdx);
				double areaOverArea = _raster.Extent.Intersection(polygon.Extent).Area / _raster.Area;
				List<int> pixelsInThisCell = new List<int>((int)((double)_raster.CellCount * areaOverArea));
				polygonRasterizer.Rasterize(polygon, (mask, pixelsInThisCell), (int n, (BitArray, List<int>) payload) =>
				{
					var (bitArray, _) = payload;
					if (!bitArray[n])
					{
						payload.Item2.Add(n);
						bitArray[n] = true;
					}
				});
				if (pixelsInThisCell.Count > 0)
				{
					Span<int> faces = cell.Faces.Span;
					if (faces.Length < 2)
					{
						MaxFaceCountPerCellPlusOnePointPool.Return(poolHandle);
						return;
					}
					if (faces.Length == 2)
					{
						Face face = mesh.Faces[faces[0]];
						FacePoint facePoint = mesh.FacePoints[face.GetFPPrev(cIdx)];
						FacePoint facePoint2 = mesh.FacePoints[face.GetFPNext(cIdx)];
						Point fp1Pt = facePoint.Point;
						Point fp2Pt = facePoint2.Point;
						Utility.Parallel.SmartFor(pixelsInThisCell.Count, (int start, int finish) =>
						{
							for (int j = start; j <= finish; j++)
							{
								float[] floatValues = new float[2];
								float[] floatValuesArray = new float[4];
								int cellIndex = pixelsInThisCell[j];
								Point point = _raster.CellCenter(cellIndex);
								float distanceTo = (float)point.DistanceTo(fp1Pt);
								float distanceTo2 = (float)point.DistanceTo(fp2Pt);
								if (distanceTo == 0f)
								{
									floatValues[0] = 1f;
									floatValues[1] = 0f;
								}
								else if (distanceTo2 == 0f)
								{
									floatValues[0] = 0f;
									floatValues[1] = 1f;
								}
								else
								{
									float distanceToPlusDistanceTo2 = distanceTo + distanceTo2;
									float distanceToOverDistanceToPlusDistanceTo2 = distanceTo / distanceToPlusDistanceTo2;
									float distanceTo2OverDistanceToPlusDistanceTo2 = distanceTo2 / distanceToPlusDistanceTo2;
									floatValues[0] = 1f - distanceToOverDistanceToPlusDistanceTo2;
									floatValues[1] = 1f - distanceTo2OverDistanceToPlusDistanceTo2;
								}
								floatValuesArray[0] = floatValues[0];
								floatValuesArray[1] = floatValues[1];
							}
						}, 256);
						throw new NotImplementedException();
					}
					int faceCount = faces.Length;
					int fullStencilSize = faceCount * 2;
					int perPxSize = fullStencilSize;
					int count = pixelsInThisCell.Count * perPxSize;
					Memory<float> stencilBuffer = new float[count];
					mesh.CellCornerPoints(cIdx, cellPts);
					double rMinX = Raster.MinX;
					double rMaxY = Raster.MaxY;
					double rCS = Raster.CellSize;
					int rCols = Raster.Cols;
					Utility.Parallel.SmartFor(pixelsInThisCell.Count, (int start, int finish) =>
					{
						Span<float> xProducts = stackalloc float[faceCount];
						Span<float> buf = stackalloc float[faceCount];
						Span<double> buf2 = stackalloc double[faceCount];
						Span<float> stencilBufferSpan = stencilBuffer.Span;
						Span<float> floatSpan = stackalloc float[faceCount];
						for (int j = start; j <= finish; j++)
						{
							int pixelsInThisCell2 = pixelsInThisCell[j];
							Span<float> velocityWeights = stencilBufferSpan.Slice(j * perPxSize, fullStencilSize);
							int pixelsInThisCell2RColsTerm = (int)Math.Floor((double)pixelsInThisCell2 / (double)rCols);
							int pixelsInThisCell2WithRCols = pixelsInThisCell2 % rCols;
							double x = rMinX + rCS * ((double)pixelsInThisCell2WithRCols + 0.5);
							double y = rMaxY - rCS * ((double)pixelsInThisCell2RColsTerm + 0.5);
							Point a = new Point(x, y);
							for (int k = 0; k < faceCount; k++)
							{
								Point b = cellPts[k];
								Point c = cellPts[k + 1];
								xProducts[k] = (float)Vector.CrossProduct(in a, in b, in c);
								if (xProducts[k] == 0f)
								{
									xProducts[k] = 1E-05f;
								}
							}
							BensWeights(cell, cIdx, cellFpXPs, floatSpan.Slice(0, faceCount), velocityWeights, xProducts, buf, buf2);
						}
					}, _params.ParallelizePixelsWithinCellsIfMoreThan);
					PointRange[] ranges = PointRange.ComputeRanges(pixelsInThisCell);
					slopingCellMaps.Add(new SlopingCellMap(cIdx, ranges, stencilBuffer));
				}
			}
			MaxFaceCountPerCellPlusOnePointPool.Return(poolHandle);
		}, _params.ParallelizeAcrossCellsIfMoreThan);
		List<SlopingCellMap> sum = new List<SlopingCellMap>(trackCellLists.Sum((List<SlopingCellMap> slopingCellMapsList) => slopingCellMapsList.Count));
		foreach (List<SlopingCellMap> trackCellList in trackCellLists)
		{
			sum.AddRange(trackCellList);
		}
		threadLocalCellLists.Dispose();
		threadLocalRasterizer.Dispose();
		return sum;
	}

	private List<SlopingCellMap> ComputeSlopedCells_Serial(List<int> slopedCells, int meshIdx)
	{
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		List<SlopingCellMap> slopingCellMaps = new List<SlopingCellMap>();
		BufferAllocator<PointRange> pointRangeBuffer = new BufferAllocator<PointRange>(10, 4 * slopedCells.Count, 100, Math.Max(2, slopedCells.Count / 4));
		Mesh mesh = flowAreaLayer[meshIdx].Mesh;
		double[,] facePointCrossProductWeights = mesh.MetadataCache.GetFacePointCrossProductWeights();
		_ = mesh.AreaTolerance;
		PolygonRasterizer polygonRasterizer = _pgRasterizers.Value;
		polygonRasterizer.Reset(Raster);
		int maxFaceCount = mesh.MetadataCache.MaxFaceCount;
		Span<float> xProducts = stackalloc float[maxFaceCount];
		Span<float> buf = stackalloc float[maxFaceCount];
		Span<double> buf2 = stackalloc double[maxFaceCount];
		Span<float> floatSpan = stackalloc float[maxFaceCount];
		Span<Point> cellPts = stackalloc Point[maxFaceCount + 1];
		List<PixelRange> pixelRanges = _prCache.Value;
		Polygon polygon = new Polygon(maxFaceCount);
		Memory<float> floatSpan2 = Memory<float>.Empty;
		int cellCountRasterTerm = Raster.CellCount * 4 * 2 / 16;
		for (int i = 0; i < slopedCells.Count; i++)
		{
			int slopedCellIndex = slopedCells[i];
			Cell cell = mesh.Cells[slopedCellIndex];
			if (!mesh.CellExtent(slopedCellIndex).Overlaps(Raster.Extent))
			{
				continue;
			}
			int length = cell.Faces.Span.Length;
			if (length < 2)
			{
				_logger.LogError("Cell error: < 2 faces");
				continue;
			}
			if (length == 2)
			{
				_logger.LogError("TODO: Not rendering results for cells with 2 faces.");
				continue;
			}
			polygon.Clear();
			mesh.FillCellPolygon_CCW(slopedCellIndex, polygon);
			pixelRanges.Clear();
			polygonRasterizer.Rasterize(polygon, pixelRanges, (int row, int start, int stop, List<PixelRange> payload) =>
			{
				payload.Add(new PixelRange(row, row, start, stop));
			});
			if (pixelRanges.Count == 0)
			{
				continue;
			}
			int scaledLength = length * 2;
			int scaledLength2 = scaledLength;
			int colCountSum = 0;
			for (int intervalIndex = 0; intervalIndex < pixelRanges.Count; intervalIndex++)
			{
				colCountSum += pixelRanges[intervalIndex].ColCount;
			}
			int colCountSumTerm = colCountSum * scaledLength2;
			if (floatSpan2.Length < colCountSumTerm)
			{
				floatSpan2 = new float[Math.Max(cellCountRasterTerm, colCountSumTerm)];
			}
			Memory<float> fullStencilWeights = floatSpan2.Slice(0, colCountSumTerm);
			floatSpan2 = floatSpan2.Slice(colCountSumTerm);
			mesh.CellCornerPoints(slopedCellIndex, cellPts);
			double minX = Raster.MinX;
			double maxY = Raster.MaxY;
			double cellSize = Raster.CellSize;
			_ = Raster.Cols;
			Span<float> fullStencilWeightsSpan = fullStencilWeights.Span;
			int ordinal = 0;
			for (int intervalIndex2 = 0; intervalIndex2 < pixelRanges.Count; intervalIndex2++)
			{
				PixelRange pixelRange = pixelRanges[intervalIndex2];
				int minRow = pixelRange.MinRow;
				for (int minCol = pixelRange.MinCol; minCol <= pixelRange.MaxCol; minCol++)
				{
					Span<float> velocityWeights = fullStencilWeightsSpan.Slice(ordinal * scaledLength2, scaledLength);
					double x = minX + cellSize * ((double)minCol + 0.5);
					double y = maxY - cellSize * ((double)minRow + 0.5);
					Point a = new Point(x, y);
					for (int intervalIndex3 = 0; intervalIndex3 < length; intervalIndex3++)
					{
						Point b = cellPts[intervalIndex3];
						Point c = cellPts[intervalIndex3 + 1];
						xProducts[intervalIndex3] = (float)Vector.CrossProduct(in a, in b, in c);
						if (xProducts[intervalIndex3] == 0f)
						{
							xProducts[intervalIndex3] = 1E-05f;
						}
					}
					BensWeights(cell, slopedCellIndex, facePointCrossProductWeights, floatSpan.Slice(0, length), velocityWeights, xProducts, buf, buf2);
					ordinal++;
				}
			}
			Memory<PointRange> cellPoints = PointRange.ComputeRangesFromRowScanlines(pixelRanges, pointRangeBuffer, Raster);
			slopingCellMaps.Add(new SlopingCellMap(slopedCellIndex, cellPoints, fullStencilWeights));
		}
		return slopingCellMaps;
	}

	private void Compute2DPointsSloped(Memory<Point> ipts, SpatialIndex<int> spatialExtent, BitArray mask)
	{
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		if (flowAreaLayer.Extent.IsNull() || !flowAreaLayer.Extent.MBROverlaps(spatialExtent.Extent))
		{
			return;
		}
		int count = flowAreaLayer.Count;
		Span<Point> iptsSpan = ipts.Span;
		for (int i = 0; i < count; i++)
		{
			Mesh mesh = flowAreaLayer[i].Mesh;
			if (!mesh.IsValid() || !mesh.Extent.Overlaps(spatialExtent.Extent))
			{
				continue;
			}
			SpatialIndex<int> cellSpatialIndex = mesh.GetCellSpatialIndex();
			Dictionary<int, FlatCellMap> flatCellMapByInt = new Dictionary<int, FlatCellMap>();
			_ = (float)i / (float)count;
			int length = ipts.Length;
			for (int j = 0; j < length; j++)
			{
				if (mask[j])
				{
					continue;
				}
				Point point = iptsSpan[j];
				foreach (int element in cellSpatialIndex.GetElements(point.X, point.Y))
				{
					_ = ref mesh.Cells[element];
					Polygon polygon = mesh.CellPolygon(element);
					if (polygon != null && polygon.ContainsFuzzy(point.X, point.Y))
					{
						if (flatCellMapByInt.ContainsKey(element))
						{
							flatCellMapByInt[element].PointIndexes.Add(j);
						}
						else
						{
							flatCellMapByInt.Add(element, new FlatCellMap(element, new CompactList<int>(j)));
						}
						mask[j] = true;
						break;
					}
				}
			}
			if (flatCellMapByInt.Count > 0)
			{
				List<SlopingCellMap> d2MapCells = ConvertFlatToSloped(mesh, ipts, flatCellMapByInt.Values.ToList());
				List<int> uniqueFPs = null;
				List<int> uniqueFaces = null;
				ComputeUniqueFacesAndFacepoints(flatCellMapByInt.Values.Select((FlatCellMap ss) => ss.Index).ToList(), mesh, out uniqueFPs, out uniqueFaces);
				if (_slopingAreas == null)
				{
					_slopingAreas = new List<SlopingMeshMap>();
				}
				_slopingAreas.Add(new SlopingMeshMap(mesh, i, flowAreaLayer[i].PropertyTables, d2MapCells, uniqueFaces, uniqueFPs));
			}
		}
	}

	private List<SlopingCellMap> ConvertFlatToSloped(Mesh mesh, Memory<Point> ipts, List<FlatCellMap> flatVals)
	{
		List<SlopingCellMap> slopingCellMaps = new List<SlopingCellMap>();
		_ = mesh.AreaTolerance;
		double[,] facePointCrossProductWeights = mesh.MetadataCache.GetFacePointCrossProductWeights();
		int ordinal = 8;
		Span<float> xProducts = stackalloc float[ordinal];
		Span<float> floatSpan = stackalloc float[ordinal];
		Span<float> buf = stackalloc float[ordinal];
		Span<double> buf2 = stackalloc double[ordinal];
		Span<Point> iptsSpan = ipts.Span;
		PoolHandle<Point[]> poolHandle = MaxFaceCountPerCellPlusOnePointPool.Acquire();
		Point[] data = poolHandle.Data;
		foreach (FlatCellMap flatVal in flatVals)
		{
			int index = flatVal.Index;
			Cell cell = mesh.Cells[index];
			int length = cell.Faces.Length;
			int scaledLength = length * 2;
			mesh.CellCornerPoints(index, data);
			int scaledLength2 = scaledLength;
			Memory<float> fullStencilWeights = new float[flatVal.MapPoints.Count * scaledLength2];
			Span<float> fullStencilWeightsSpan = fullStencilWeights.Span;
			for (int i = 0; i < flatVal.PointIndexes.Count; i++)
			{
				int index2 = flatVal.PointIndexes[i];
				Point a = iptsSpan[index2];
				Span<float> velocityWeights = fullStencilWeightsSpan.Slice(i * scaledLength2, scaledLength);
				for (int j = 0; j < length; j++)
				{
					Point b = data[j];
					Point c = data[j + 1];
					xProducts[j] = (float)Vector.CrossProduct(in a, in b, in c);
					if (xProducts[j] == 0f)
					{
						xProducts[j] = 1E-05f;
					}
				}
				BensWeights(cell, index, facePointCrossProductWeights, floatSpan.Slice(0, length), velocityWeights, xProducts, buf, buf2);
			}
			PointRange[] ranges = PointRange.ComputeRanges(flatVal.PointIndexes);
			SlopingCellMap slopingCellMap = new SlopingCellMap(index, ranges, fullStencilWeights);
			slopingCellMaps.Add(slopingCellMap);
		}
		MaxFaceCountPerCellPlusOnePointPool.Return(poolHandle);
		return slopingCellMaps;
	}

	private void Compute2DPointsFlat(Memory<Point> ipts, Extent ext, BitArray mask)
	{
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		if (flowAreaLayer.Extent.IsNull() || !flowAreaLayer.Extent.MBROverlaps(ext))
		{
			return;
		}
		Span<Point> iptsSpan = ipts.Span;
		for (int i = 0; i < flowAreaLayer.Count; i++)
		{
			Mesh mesh = flowAreaLayer[i].Mesh;
			if (!mesh.IsValid() || !mesh.Extent.Overlaps(in ext))
			{
				continue;
			}
			SpatialIndex<int> cellSpatialIndex = mesh.GetCellSpatialIndex();
			Dictionary<int, FlatCellMap> flatCellMapByInt = new Dictionary<int, FlatCellMap>();
			for (int j = 0; j < iptsSpan.Length; j++)
			{
				if (mask[j])
				{
					continue;
				}
				Point point = iptsSpan[j];
				foreach (int element in cellSpatialIndex.GetElements(point.X, point.Y))
				{
					_ = ref mesh.Cells[element];
					Polygon polygon = mesh.CellPolygon(element);
					if (polygon != null && polygon.Contains(point))
					{
						if (flatCellMapByInt.ContainsKey(element))
						{
							flatCellMapByInt[element].PointIndexes.Add(j);
						}
						else
						{
							flatCellMapByInt.Add(element, new FlatCellMap(element, new CompactList<int>(j)));
						}
						mask[j] = true;
					}
				}
			}
			if (flatCellMapByInt.Count > 0)
			{
				AddMesh(i, flatCellMapByInt.Values.ToList());
			}
		}
	}

	private void AddMesh(int midx, List<FlatCellMap> map)
	{
		if (_flatAreas == null)
		{
			_flatAreas = new List<FlatMeshMap>();
		}
		FlowArea flowArea = _geometry.FlowAreaLayer[midx];
		_flatAreas.Add(new FlatMeshMap(flowArea.Mesh, midx, flowArea.PropertyTables, map));
	}

	private void AddMesh(int midx, List<SlopingCellMap> map)
	{
		if (_slopingAreas == null)
		{
			_slopingAreas = new List<SlopingMeshMap>();
		}
		FlowArea flowArea = _geometry.FlowAreaLayer[midx];
		_slopingAreas.Add(new SlopingMeshMap(flowArea.Mesh, midx, flowArea.PropertyTables, map));
	}

	private void SubsampleIntersections(Polyline pline, ref List<PointOnPolyline> intersections, double maxSegLength)
	{
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>();
		for (int i = 0; i < intersections.Count - 1; i++)
		{
			PointOnPolyline ptOnPoly = intersections[i];
			PointOnPolyline ptOnPoly2 = intersections[i + 1];
			double distanceAlong = pline.DistanceAlong(ptOnPoly);
			double distanceAlongPlineTerm = pline.DistanceAlong(ptOnPoly2) - distanceAlong;
			if (!(distanceAlongPlineTerm > maxSegLength))
			{
				continue;
			}
			int count = (int)Math.Ceiling(distanceAlongPlineTerm / maxSegLength);
			if (count > 0)
			{
				double countTerm = distanceAlongPlineTerm / (double)count;
				for (int j = 1; j <= count; j++)
				{
					pointOnPolylines.Add(pline.PointAtDistance(distanceAlong + (double)j * countTerm));
				}
			}
		}
		intersections.AddRange(pointOnPolylines);
		intersections = PointOnPolyline.Sort(intersections);
	}

	private void ComputeBlockRanges(Polyline pline, out List<Tuple<double, double>> blocked2d)
	{
		FlowAreaLayer flowAreaLayer = _geometry.FlowAreaLayer;
		Extent extent = pline.Extent;
		blocked2d = new List<Tuple<double, double>>();
		foreach (Mesh mesh in flowAreaLayer.Select((FlowArea fa) => fa.Mesh))
		{
			if (!mesh.IsValid())
			{
				continue;
			}
			Extent extent2 = mesh.Perimeter.Extent;
			if (!extent.MBROverlaps(extent2))
			{
				continue;
			}
			Polygon perimeter = mesh.Perimeter;
			if (Polygon.IsValidPolygon(perimeter))
			{
				IEnumerable<Tuple<double, double>> c2Enum = from tup in perimeter.EntryExitPoints(pline)
					select new Tuple<double, double>(pline.DistanceAlong(tup.Item1), pline.DistanceAlong(tup.Item2));
				blocked2d = CombineBlockRange(blocked2d, c2Enum);
			}
		}
	}

	private List<Tuple<double, double>> CombineBlockRange(IEnumerable<Tuple<double, double>> c1Enum, IEnumerable<Tuple<double, double>> c2Enum)
	{
		List<Tuple<double, double>> valueTupleList = new List<Tuple<double, double>>();
		List<Tuple<double, double>> orderBy = c1Enum.OrderBy((Tuple<double, double> tup) => tup.Item1).ToList();
		List<Tuple<double, double>> orderByList = c2Enum.OrderBy((Tuple<double, double> tup) => tup.Item1).ToList();
		int ordinal = 0;
		int ordinal2 = 0;
		while (true)
		{
			if (ordinal == orderBy.Count)
			{
				valueTupleList.AddRange(orderByList.GetRange(ordinal2, orderByList.Count - ordinal2));
				return valueTupleList;
			}
			if (ordinal2 == orderByList.Count)
			{
				valueTupleList.AddRange(orderByList.GetRange(ordinal, orderBy.Count - ordinal));
			}
			double orderByTerm = orderBy[ordinal].Item1;
			double orderByTerm2 = orderBy[ordinal].Item2;
			double orderByListTerm = orderByList[ordinal2].Item1;
			double orderByListTerm2 = orderByList[ordinal2].Item2;
			if (orderByListTerm == orderByTerm2)
			{
				valueTupleList.Add(new Tuple<double, double>(orderByTerm, orderByListTerm2));
				ordinal++;
				ordinal2++;
				continue;
			}
			if (orderByTerm == orderByListTerm2)
			{
				valueTupleList.Add(new Tuple<double, double>(orderByListTerm, orderByTerm2));
				ordinal++;
				ordinal2++;
				continue;
			}
			if (orderByTerm > orderByListTerm2)
			{
				valueTupleList.Add(new Tuple<double, double>(orderByListTerm, orderByListTerm2));
				ordinal2++;
				continue;
			}
			if (orderByListTerm > orderByTerm2)
			{
				valueTupleList.Add(new Tuple<double, double>(orderByListTerm, orderByListTerm2));
				ordinal++;
				continue;
			}
			if (orderByTerm > orderByListTerm)
			{
				valueTupleList.Add(new Tuple<double, double>(orderByListTerm, orderByTerm2));
				ordinal++;
				ordinal2++;
				continue;
			}
			if (!(orderByListTerm > orderByTerm))
			{
				break;
			}
			valueTupleList.Add(new Tuple<double, double>(orderByTerm, orderByListTerm2));
			ordinal++;
			ordinal2++;
		}
		throw new ArgumentException();
	}

	public void UniqueMappedElements(bool expandStencil, out HashSet<int> uniqueCells, out HashSet<int> uniqueFaces, out HashSet<int> uniqueNodes)
	{
		uniqueCells = new HashSet<int>();
		uniqueFaces = new HashSet<int>();
		uniqueNodes = new HashSet<int>();
		if (MappedPixelCount == 0)
		{
			return;
		}
		Mesh mesh = _geometry.FlowAreaLayer[0].Mesh;
		SlopingMeshMap slopingMeshMap = SlopingMapped2DAreas.FirstOrDefault();
		if (slopingMeshMap != null)
		{
			if (slopingMeshMap.MeshIndex != 0)
			{
				throw new Exception("Only works with single-mesh systems.");
			}
			foreach (SlopingCellMap cell2 in slopingMeshMap.Cells)
			{
				int cellIndex = cell2.CellIndex;
				MapCell(mesh, cellIndex, expandStencil, uniqueCells, uniqueFaces, uniqueNodes);
			}
		}
		FlatMeshMap flatMeshMap = FlatMapped2DAreas.FirstOrDefault();
		if (flatMeshMap == null)
		{
			return;
		}
		if (flatMeshMap.MeshIndex != 0)
		{
			throw new Exception("Only works with single-mesh systems.");
		}
		foreach (FlatCellMap cell3 in flatMeshMap.Cells)
		{
			int index = cell3.Index;
			MapCell(mesh, index, expandStencil, uniqueCells, uniqueFaces, uniqueNodes);
		}
		static void MapCell(Mesh m, int cIdx, bool stencil, HashSet<int> cs, HashSet<int> fs, HashSet<int> ns)
		{
			if (cIdx != -1 && cs.Add(cIdx))
			{
				Cell cell = m.Cells[cIdx];
				Span<int> faces = cell.Faces.Span;
				for (int i = 0; i < faces.Length; i++)
				{
					int faceIndex = faces[i];
					fs.Add(faceIndex);
					Face face = m.Faces[faceIndex];
					ns.Add(face.fpA);
					ns.Add(face.fpB);
					if (stencil)
					{
						int cIdx2 = face.OtherCell(cIdx);
						MapCell(m, cIdx2, stencil: false, cs, fs, ns);
					}
				}
			}
		}
	}

	public string Search(int searchPixelIdx)
	{
		for (int i = 0; i < _flatAreas.Count; i++)
		{
			FlatMeshMap flatMeshMap = _flatAreas[i];
			for (int j = 0; j < flatMeshMap.Cells.Count; j++)
			{
				FlatCellMap flatCellMap = flatMeshMap.Cells[j];
				for (int k = 0; k < flatCellMap.PointIndexes.Count; k++)
				{
					if (flatCellMap.PointIndexes[k] == searchPixelIdx)
					{
						return "Flat Mesh[" + i + "].Cells[" + j + "].MapPoints[" + k + "]";
					}
				}
			}
		}
		return "Not found";
	}
}
