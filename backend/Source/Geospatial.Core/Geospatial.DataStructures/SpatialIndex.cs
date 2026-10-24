using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility.DataStructures;
using Utility.Extensions;

namespace Geospatial.DataStructures;

public class SpatialIndex<T> : SpatialIndex, ISpatialQuery<T>
{
	private List<T>[] _bins;

	private HashSet<T>[] _binHashes;

	private PolygonRasterizer _polyRasterizer;

	private static bool AddTriangleAsExtent = true;

	private static bool AddPgonAsExtent = false;

	public Extent Extent => Raster.Extent;

	public int MaxResizedBinCount { get; set; }

	public List<T>[] Bins => _bins;

	public HashSet<T>[] BinHash => _binHashes;

	public List<T> Bin(Pixel px)
	{
		return _bins[Raster.CellIndex(px.Row, px.Column)];
	}

	public List<T> Bin(int row, int col)
	{
		return _bins[Raster.CellIndex(row, col)];
	}

	public override IList[] GetBinsAsBoxedCollection()
	{
		return _bins;
	}

	public static SpatialIndex<T> DefaultSpatialIndex()
	{
		return new SpatialIndex<T>(new Extent(0.0, 1.0, 0.0, 1.0), 1);
	}

	public SpatialIndex(Extent ext, int maxBinCount = 4096)
	{
		Initialize(RasterDefinition.ComputeBoundingRaster(ext, maxBinCount));
	}

	public SpatialIndex(RasterDefinition rasterDefinition)
	{
		Initialize(rasterDefinition);
	}

	private void Initialize(RasterDefinition raster)
	{
		if (raster.IsNull() || raster.Extent.IsInfiniteOrInvalid())
		{
			throw new Exception("Spatial index constructed with invalid bin raster.");
		}
		Raster = raster;
		_bins = new List<T>[Raster.CellCount];
		_binHashes = new HashSet<T>[Raster.CellCount];
		for (int i = 0; i < _bins.Length; i++)
		{
			_bins[i] = new List<T>();
			_binHashes[i] = new HashSet<T>();
		}
		_polyRasterizer = new PolygonRasterizer(Raster);
		MaxResizedBinCount = Raster.CellCount * 2;
	}

	public bool Add(int binIdx, T key)
	{
		HashSet<T> binHashe = _binHashes[binIdx];
		List<T> bin = _bins[binIdx];
		if (binHashe == null)
		{
			binHashe = new HashSet<T>();
			bin = new List<T>();
			_binHashes[binIdx] = binHashe;
			_bins[binIdx] = bin;
		}
		if (binHashe.Add(key))
		{
			bin.Add(key);
			return true;
		}
		return false;
	}

	public void Add(Point pt, T key)
	{
		Add(Raster.CellIndex(pt), key);
	}

	public void Add(double x, double y, T key)
	{
		Add(Raster.CellIndex(x, y), key);
	}

	public void Add(IList<Point> points, T key)
	{
		for (int i = 0; i < points.Count; i++)
		{
			Add(points[i], key);
		}
	}

	public void Add(Segment seg, T key)
	{
		seg.Rasterize(Raster, (key, this), (int idx, (T, SpatialIndex<T>) payload) =>
		{
			payload.Item2.Add(idx, payload.Item1);
		});
	}

	public void Add(Polyline polyline, T key)
	{
		foreach (Segment segment in polyline.Segments)
		{
			Add(segment, key);
		}
	}

	public void Add(Extent extent, T key)
	{
		PixelRange pixelRange = Raster.GetPixelRange(extent);
		if (pixelRange.IsNull())
		{
			return;
		}
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				Add(Raster.CellIndex(i, j), key);
			}
		}
	}

	public void Add(Triangle tri, T key)
	{
		if (AddTriangleAsExtent)
		{
			Add(tri.Extent, key);
			return;
		}
		tri.Rasterize(Raster, (this, key), (int n, (SpatialIndex<T>, T) payload) =>
		{
			payload.Item1.Add(n, payload.Item2);
		});
	}

	public void Add<TPolygon>(TPolygon polygon, T key) where TPolygon : ISimpleRing
	{
		if (AddPgonAsExtent)
		{
			_ = polygon.Extent;
			Add(polygon.Extent, key);
		}
		else
		{
			_polyRasterizer.RasterizePartialPixel(polygon, (this, key), (int n, (SpatialIndex<T>, T) payload) =>
			{
				payload.Item1.Add(n, payload.Item2);
			});
		}
	}

	public void Add(ComplexPolygon polygon, T key)
	{
		_polyRasterizer.RasterizePartialPixel(polygon, (this, key), (int n, (SpatialIndex<T>, T) payload) =>
		{
			payload.Item1.Add(n, payload.Item2);
		});
	}

	public void AddInBounds(Point pt, T key)
	{
		int row = Raster.Row(in pt.Y);
		int col = Raster.Col(in pt.X);
		if (row >= 0 && row < Raster.Rows && col >= 0 && col < Raster.Cols)
		{
			Add(Raster.CellIndex(row, col), key);
		}
	}

	public bool TryAddWithResize(Point point, T key)
	{
		if (!Resize(point.X, point.Y))
		{
			return false;
		}
		Add(Raster.CellIndex(point), key);
		return true;
	}

	private void AddRange(PixelRange range, List<T> segmentList, out int binsWithData)
	{
		binsWithData = 0;
		for (int i = range.MinRow; i <= range.MaxRow; i++)
		{
			for (int j = range.MinCol; j <= range.MaxCol; j++)
			{
				List<T> bin = _bins[Raster.CellIndex(i, j)];
				if (!bin.IsNullOrEmpty())
				{
					binsWithData++;
					segmentList.AddRange(bin);
				}
			}
		}
	}

	public List<T> GetElementsAtPoints(IList<Point> pts, bool ensureDistinct = true)
	{
		HashSet<int> intSet = new HashSet<int>();
		for (int i = 0; i < pts.Count; i++)
		{
			Point pt = pts[i];
			if (Raster.Contains(pt))
			{
				intSet.Add(Raster.CellIndex(pt));
			}
		}
		List<T> elementsAtPoints = new List<T>();
		foreach (int ordinal in intSet)
		{
			elementsAtPoints.AddRange(_bins[ordinal]);
		}
		if (ensureDistinct)
		{
			elementsAtPoints = elementsAtPoints.DedupeInPlace();
		}
		return elementsAtPoints;
	}

	public List<T> GetElements(double x, double y)
	{
		if (Raster.Contains(x, y))
		{
			int cellIndex = Raster.CellIndex(x, y);
			return _bins[cellIndex];
		}
		return new List<T>();
	}

	public List<T> GetElements(Point pt)
	{
		return GetElements(pt.X, pt.Y);
	}

	public List<T> GetElements(Segment segment, bool dedupe = true)
	{
		List<T> segmentList = new List<T>();
		int binCt = 0;
		if (segment.ClipToExtent(Raster))
		{
			segment.Rasterize(Raster, (int n) =>
			{
				List<T> bin = _bins[n];
				if (!bin.IsNullOrEmpty())
				{
					binCt++;
					segmentList.AddRange(bin);
				}
			});
		}
		if (binCt <= 1 || !dedupe)
		{
			return segmentList;
		}
		return segmentList.DedupeInPlace();
	}

	public List<T> GetElements(Polyline pline, bool dedupe = true)
	{
		List<T> items = new List<T>();
		HashSet<int> intSet = new HashSet<int>();
		foreach (Segment segment in pline.Segments)
		{
			if (segment.ClipToExtent(Raster))
			{
				segment.Rasterize(Raster, intSet, (int n, HashSet<int> payload) =>
				{
					payload.Add(n);
				});
			}
		}
		int ordinal = 0;
		foreach (int ordinal2 in intSet)
		{
			List<T> bin = _bins[ordinal2];
			if (!bin.IsNullOrEmpty())
			{
				ordinal++;
				items.AddRange(bin);
			}
		}
		if (ordinal <= 1 || !dedupe)
		{
			return items;
		}
		return items.DedupeInPlace();
	}

	public List<T> GetElements(Polygon pgon)
	{
		throw new Exception("Don't call this. It doesn't work. It's here to make sure you don't accidentally call the polyline version.");
	}

	public List<T> GetElements(Segment segment, double distance, bool dedupe = true)
	{
		if (double.IsNaN(distance) || distance <= 0.0)
		{
			throw new ArgumentException("distance");
		}
		if (segment.Length < Raster.CellSize * 4.0)
		{
			return GetElements(segment.MinX - distance, segment.MaxX + distance, segment.MinY - distance, segment.MaxY + distance);
		}
		List<T> items = new List<T>();
		bool[,] intersectedBins = new bool[Raster.Rows, Raster.Cols];
		int startRow = int.MaxValue;
		int endRow = int.MinValue;
		Vector vector = segment.UnitVector().Ratio(distance);
		Vector vector2 = segment.PerpendicularUnitVector().Ratio(distance);
		Point point = segment.A.Translate(0.0 - vector.X + vector2.X, 0.0 - vector.Y + vector2.Y);
		Point point2 = segment.B.Translate(vector.X + vector2.X, vector.Y + vector2.Y);
		Point point3 = segment.B.Translate(vector.X - vector2.X, vector.Y - vector2.Y);
		Point point4 = segment.A.Translate(0.0 - vector.X - vector2.X, 0.0 - vector.Y - vector2.Y);
		Segment segment2 = new Segment(point, point2);
		Segment segment3 = new Segment(point2, point3);
		Segment segment4 = new Segment(point3, point4);
		Segment segment5 = new Segment(point4, point);
		Action<Pixel> pixelAction = (Pixel px) =>
		{
			intersectedBins[px.Row, px.Column] = true;
			if (px.Row < startRow)
			{
				startRow = px.Row;
			}
			if (px.Row > endRow)
			{
				endRow = px.Row;
			}
		};
		segment2.Rasterize(Raster, pixelAction);
		segment3.Rasterize(Raster, pixelAction);
		segment4.Rasterize(Raster, pixelAction);
		segment5.Rasterize(Raster, pixelAction);
		for (int startRow2 = startRow; startRow2 <= endRow; startRow2++)
		{
			int ordinal = -1;
			int count = -1;
			for (int count2 = 0; count2 < Raster.Cols; count2++)
			{
				if (intersectedBins[startRow2, count2])
				{
					if (ordinal == -1)
					{
						ordinal = count2;
					}
					else
					{
						count = count2;
					}
				}
			}
			if (ordinal != -1)
			{
				for (int ordinal2 = ordinal + 1; ordinal2 < count; ordinal2++)
				{
					intersectedBins[startRow2, ordinal2] = true;
				}
			}
		}
		int ordinal3 = 0;
		for (int startRow3 = startRow; startRow3 <= endRow; startRow3++)
		{
			for (int intervalIndex = 0; intervalIndex < Raster.Cols; intervalIndex++)
			{
				if (intersectedBins[startRow3, intervalIndex])
				{
					List<T> bin = _bins[startRow3 * Raster.Cols + intervalIndex];
					if (!bin.IsNullOrEmpty())
					{
						ordinal3++;
						items.AddRange(bin);
					}
				}
			}
		}
		if (ordinal3 <= 1 || !dedupe)
		{
			return items;
		}
		return items.DedupeInPlace();
	}

	public List<T> GetElements(double x, double y, double distance, bool dedupe = true)
	{
		return GetElements(x - distance, x + distance, y - distance, y + distance, dedupe);
	}

	public List<T> GetElements(Point ptm, double distance, bool dedupe = true)
	{
		return GetElements(ptm.X - distance, ptm.X + distance, ptm.Y - distance, ptm.Y + distance, dedupe);
	}

	List<T> ISpatialQuery<T>.GetElements(Point pt, double distance, out bool isApproximate)
	{
		isApproximate = true;
		return GetElements(pt, distance);
	}

	List<T> ISpatialQuery<T>.GetElements(Extent ext, out bool isApproximate)
	{
		isApproximate = true;
		return GetElements(ext);
	}

	public void GetBinRange(Extent ext, out List<int> completelyCoveredBins, out List<int> partiallyCoveredBins)
	{
		completelyCoveredBins = new List<int>();
		partiallyCoveredBins = new List<int>();
		PixelRange pixelRange = Raster.GetPixelRange(ext);
		if (pixelRange.IsNull())
		{
			return;
		}
		bool maxYExtTerm = ext.MaxY > Raster.MaxY;
		bool minYExtTerm = ext.MinY < Raster.MinY;
		bool minXExtTerm = ext.MinX < Raster.MinX;
		bool maxXExtTerm = ext.MaxX > Raster.MaxX;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			bool minRowPixelRangeTerm = (i == pixelRange.MinRow && !maxYExtTerm) || (i == pixelRange.MaxRow && !minYExtTerm);
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				if (minRowPixelRangeTerm || (j == pixelRange.MinCol && !minXExtTerm) || (j == pixelRange.MaxCol && !maxXExtTerm))
				{
					partiallyCoveredBins.Add(Raster.CellIndex(i, j));
				}
				else
				{
					completelyCoveredBins.Add(Raster.CellIndex(i, j));
				}
			}
		}
	}

	public void GetBinRange(Polygon pgon, out List<int> completelyCoveredBins, out List<int> partiallyCoveredBins)
	{
		completelyCoveredBins = new List<int>();
		partiallyCoveredBins = new List<int>();
		Extent extent = pgon.Extent;
		PixelRange pixelRange = Raster.GetPixelRange(extent);
		if (pixelRange.IsNull())
		{
			return;
		}
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				Extent ext = Raster.CellExtent(i, j);
				if (pgon.Contains(ext))
				{
					completelyCoveredBins.Add(Raster.CellIndex(i, j));
				}
				else if (pgon.AreaIntersects(ext))
				{
					partiallyCoveredBins.Add(Raster.CellIndex(i, j));
				}
			}
		}
	}

	public void GetBinRangeBuffered(Polygon pgon, double buffer, out List<int> completelyCoveredBins, out List<int> partiallyCoveredBins)
	{
		completelyCoveredBins = new List<int>();
		partiallyCoveredBins = new List<int>();
		Extent extent = pgon.Extent.Extend(buffer);
		if (!Raster.Overlaps(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY))
		{
			return;
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Raster);
		for (int i = 0; i < pgon.SegmentCount; i++)
		{
			spatialIndex.Add(pgon.Segments[i], i);
		}
		Tin devTin = pgon.DevTin;
		int t = 0;
		for (int j = 0; j < spatialIndex.Bins.Length; j++)
		{
			if (spatialIndex.Bins[j].Count > 0)
			{
				partiallyCoveredBins.Add(j);
				continue;
			}
			Point point = spatialIndex.Raster.CellCenter(j);
			if (devTin.ContainsPoint(point, ref t))
			{
				completelyCoveredBins.Add(j);
			}
		}
	}

	public List<T> GetElements(IExtent extent, bool dedupe = true)
	{
		return GetElements(extent.Extent, dedupe);
	}

	public List<T> GetElements(Extent extent, bool dedupe = true)
	{
		return GetElements(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY, dedupe);
	}

	public HashSet<T> GetElementsDistinct(Extent extent)
	{
		return GetElementsDistinct(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY);
	}

	public List<T> GetElements(RasterDefinition raster, bool dedupe = true)
	{
		return GetElements(raster.Extent, dedupe);
	}

	public HashSet<T> GetElementsDistinct(RasterDefinition raster)
	{
		return GetElementsDistinct(raster.Extent);
	}

	public List<T> GetElements(double minX, double maxX, double minY, double maxY, bool dedupe = true)
	{
		if (!Raster.Overlaps(minX, maxX, minY, maxY))
		{
			return new List<T>();
		}
		PixelRange pixelRange = Raster.GetPixelRange(minX, maxX, minY, maxY);
		AddRange(pixelRange, out var segmentList, out var binsWithData);
		if (binsWithData <= 1 || !dedupe)
		{
			return segmentList;
		}
		return segmentList.DedupeInPlace();
	}

	public HashSet<T> GetElementsDistinct(double minX, double maxX, double minY, double maxY)
	{
		if (!Raster.Overlaps(minX, maxX, minY, maxY))
		{
			return new HashSet<T>();
		}
		PixelRange pixelRange = Raster.GetPixelRange(minX, maxX, minY, maxY);
		AddRangeDeduped(pixelRange, out var segmentList, out var _);
		return segmentList;
	}

	private void AddRange(PixelRange range, out List<T> segmentList, out int binsWithData)
	{
		int binCountSum = 0;
		binsWithData = 0;
		for (int i = range.MinRow; i <= range.MaxRow; i++)
		{
			for (int j = range.MinCol; j <= range.MaxCol; j++)
			{
				List<T> bin = _bins[Raster.CellIndex(i, j)];
				if (!bin.IsNullOrEmpty())
				{
					binsWithData++;
					binCountSum += bin.Count;
				}
			}
		}
		segmentList = new List<T>(binCountSum);
		for (int k = range.MinRow; k <= range.MaxRow; k++)
		{
			for (int l = range.MinCol; l <= range.MaxCol; l++)
			{
				List<T> binList = _bins[Raster.CellIndex(k, l)];
				if (!binList.IsNullOrEmpty())
				{
					segmentList.AddRange(binList);
				}
			}
		}
	}

	private void AddRangeDeduped(PixelRange range, out HashSet<T> segmentList, out int binsWithData)
	{
		int binCountSum = 0;
		binsWithData = 0;
		for (int i = range.MinRow; i <= range.MaxRow; i++)
		{
			for (int j = range.MinCol; j <= range.MaxCol; j++)
			{
				List<T> bin = _bins[Raster.CellIndex(i, j)];
				if (!bin.IsNullOrEmpty())
				{
					binsWithData++;
					binCountSum += bin.Count;
				}
			}
		}
		segmentList = new HashSet<T>(binCountSum);
		for (int k = range.MinRow; k <= range.MaxRow; k++)
		{
			for (int l = range.MinCol; l <= range.MaxCol; l++)
			{
				List<T> binList = _bins[Raster.CellIndex(k, l)];
				if (!binList.IsNullOrEmpty())
				{
					segmentList.AddRange(binList);
				}
			}
		}
	}

	public bool TryGetNearestElement(Point pt, Func<Point, T, double> distanceToPoint, out T nearestElement, out double actualDistance)
	{
		Pixel pixel = Raster.CellIndex2D(pt);
		int pixelRowsTerm;
		if (Raster.Contains(pt))
		{
			pixelRowsTerm = 0;
		}
		else
		{
			int pixelTerm = -pixel.X;
			int pixelColsTerm = pixel.X - (Raster.Cols - 1);
			int pixelTerm2 = -pixel.Y;
			pixelRowsTerm = Math.Max(val2: pixel.Y - (Raster.Rows - 1), val1: Math.Max(Math.Max(pixelTerm, pixelColsTerm), pixelTerm2));
			if (pixelRowsTerm < 0)
			{
				pixelRowsTerm = 0;
			}
		}
		nearestElement = default;
		actualDistance = double.MaxValue;
		while (true)
		{
			PixelRange range = pixel.GetRange(pixelRowsTerm);
			int boundedColumnStart = Raster.BoundedColumn(range.Left);
			int boundedColumnEnd = Raster.BoundedColumn(range.Right);
			for (int i = boundedColumnStart; i <= boundedColumnEnd; i++)
			{
				if (i == boundedColumnStart || i == boundedColumnEnd)
				{
					int boundedRowStart = Raster.BoundedRow(range.Top);
					int boundedRowEnd = Raster.BoundedRow(range.Bottom);
					for (int j = boundedRowStart; j <= boundedRowEnd; j++)
					{
						Pixel rowcol = new Pixel(j, i);
						int binIdx = Raster.CellIndex(rowcol);
						CheckBinNearestElement_Running(pt, binIdx, distanceToPoint, ref nearestElement, ref actualDistance);
					}
					continue;
				}
				if (range.Top >= 0 && range.Top < Raster.Rows)
				{
					Pixel rowcol2 = new Pixel(range.Top, i);
					int binIdx2 = Raster.CellIndex(rowcol2);
					CheckBinNearestElement_Running(pt, binIdx2, distanceToPoint, ref nearestElement, ref actualDistance);
				}
				if (range.Bottom >= 0 && range.Bottom < Raster.Rows)
				{
					Pixel rowcol3 = new Pixel(range.Bottom, i);
					int binIdx3 = Raster.CellIndex(rowcol3);
					CheckBinNearestElement_Running(pt, binIdx3, distanceToPoint, ref nearestElement, ref actualDistance);
				}
			}
			Raster.RangeExtent(range).NearestPoint(pt, out var actualDistance2);
			if (actualDistance <= actualDistance2)
			{
				return true;
			}
			if (range.IsOrExceedsFullRange(Raster))
			{
				break;
			}
			pixelRowsTerm++;
		}
		return actualDistance < double.MaxValue;
	}

	private void TryGetNearestElement_BruteForceAllBins(Point pt, Func<Point, T, double> distanceToPoint, out T nearestElement, out double actualDistance)
	{
		nearestElement = default;
		actualDistance = double.MaxValue;
		for (int i = 0; i < Bins.Length; i++)
		{
			CheckBinNearestElement_Running(pt, i, distanceToPoint, ref nearestElement, ref actualDistance);
		}
	}

	private void CheckBinNearestElement_Running(Point pt, int binIdx, Func<Point, T, double> distanceToPoint, ref T nearestElement, ref double actualDistance)
	{
		List<T> bin = Bins[binIdx];
		if (bin == null || bin.Count == 0)
		{
			return;
		}
		for (int i = 0; i < bin.Count; i++)
		{
			T bin2 = bin[i];
			double actualDistance2 = distanceToPoint(pt, bin2);
			if (actualDistance2 < actualDistance)
			{
				actualDistance = actualDistance2;
				nearestElement = bin2;
			}
		}
	}

	public bool Resize(Extent ext)
	{
		if (Resize(ext.Left, ext.Top))
		{
			return Resize(ext.Right, ext.Bot);
		}
		return false;
	}

	public bool Resize(Segment seg)
	{
		if (Resize(seg.A.X, seg.A.Y))
		{
			return Resize(seg.B.X, seg.B.Y);
		}
		return false;
	}

	public bool Resize(Polyline pline)
	{
		return Resize(pline.Extent);
	}

	public bool Resize(double x, double y)
	{
		if (Raster.Contains(x, y))
		{
			return true;
		}
		GetBinExpansion(x, y, out var tilesLeft, out var tilesRight, out var tilesUp, out var tilesDown);
		return Resize(tilesLeft, tilesRight, tilesUp, tilesDown);
	}

	public bool Resize(int tilesLeft, int tilesRight, int tilesUp, int tilesDown)
	{
		throw new NotImplementedException();
	}

	public int NewBinCount(double addX, double addY)
	{
		GetBinExpansion(addX, addY, out var tilesLeft, out var tilesRight, out var tilesUp, out var tilesDown);
		return (Raster.Cols + tilesLeft + tilesRight) * (Raster.Rows + tilesUp + tilesDown);
	}

	private void GetBinExpansion(double x, double y, out int tilesLeft, out int tilesRight, out int tilesUp, out int tilesDown)
	{
		double cellSize = Raster.CellSize;
		if (x < Raster.Left)
		{
			double leftRasterTerm = Raster.Left - x;
			tilesLeft = (int)Math.Ceiling(leftRasterTerm / cellSize);
			tilesRight = 0;
		}
		else if (x > Raster.Right)
		{
			double rightRasterTerm = x - Raster.Right;
			tilesLeft = 0;
			tilesRight = (int)Math.Ceiling(rightRasterTerm / cellSize);
		}
		else
		{
			tilesLeft = 0;
			tilesRight = 0;
		}
		if (y < Raster.Bottom)
		{
			double bottomRasterTerm = Raster.Bottom - y;
			tilesUp = 0;
			tilesDown = (int)Math.Ceiling(bottomRasterTerm / cellSize);
		}
		else if (y > Raster.Top)
		{
			double topRasterTerm = y - Raster.Top;
			tilesUp = (int)Math.Ceiling(topRasterTerm / cellSize);
			tilesDown = 0;
		}
		else
		{
			tilesUp = 0;
			tilesDown = 0;
		}
	}

	public void BruteForceBinPairs(int binIdx, Action<T, T> action)
	{
		List<T> bin = Bins[binIdx];
		if (bin == null || bin.Count == 0)
		{
			return;
		}
		for (int i = 0; i < bin.Count - 1; i++)
		{
			T elementAt = bin.ElementAt(i);
			for (int j = i + 1; j < bin.Count; j++)
			{
				T elementAt2 = bin.ElementAt(j);
				action(elementAt, elementAt2);
			}
		}
	}

	public string Find(T localValue)
	{
		for (int i = 0; i <= Bins.Count() - 1; i++)
		{
			if (Bins[i].Contains(localValue))
			{
				string pointTerm = "Item " + localValue.ToString() + " found in bin " + i + ", at ";
				Point point = Raster.CellCenter(i);
				pointTerm = pointTerm + "(" + point.X + "," + point.Y + ")";
				return pointTerm + " (cellsize = " + Raster.CellSize + ")";
			}
		}
		return "Item not found";
	}
}
public abstract class SpatialIndex : IRasterDefinition
{
	private const int MaxBinCount = 1048576;

	public RasterDefinition Raster { get; protected set; }

	RasterDefinition IRasterDefinition.RasterDefinition => Raster;

	public abstract IList[] GetBinsAsBoxedCollection();

	public static int ReasonableBinCount(int numValues)
	{
		return Math.Max(1, Math.Min(1048576, (int)((double)numValues / 32.0)));
	}

	public static SpatialIndex<int> FromPoints(IList<Point> pts)
	{
		if (pts.IsNullOrEmpty())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(PointCollection.ComputeExtent(pts), ReasonableBinCount(pts.Count));
		for (int i = 0; i < pts.Count; i++)
		{
			spatialIndex.Add(pts[i], i);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> FromPoints<T>(IList<T> pts, Func<T, Point> getPoint)
	{
		if (pts.IsNullOrEmpty())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(PointCollection.ComputeExtent(pts, getPoint), ReasonableBinCount(pts.Count));
		for (int i = 0; i < pts.Count; i++)
		{
			spatialIndex.Add(getPoint(pts[i]), i);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> OLD_ComputeSegmentSpatialIndex(Polyline polyline)
	{
		Extent extent = polyline.Extent;
		int count = polyline.Segments.Count;
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, ReasonableBinCount(count));
		for (int i = 0; i < count; i++)
		{
			spatialIndex.Add(polyline.Segments[i], i);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> GetMultipointSpatialIndex(IList<PointCollection> multiPts)
	{
		throw new NotImplementedException();
	}

	public static SpatialIndex<Tuple<int, int>> ComputeSegmentSpatialIndex(IList<Polyline> plines, RasterDefinition? rasterDef = null)
	{
		RasterDefinition rasterDefinition = (rasterDef.HasValue ? rasterDef.Value : RasterDefinition.NullRaster);
		if (plines.Count == 0)
		{
			if (rasterDefinition.IsNull())
			{
				return SpatialIndex<Tuple<int, int>>.DefaultSpatialIndex();
			}
			return new SpatialIndex<Tuple<int, int>>(rasterDefinition);
		}
		SpatialIndex<Tuple<int, int>> spatialIndex = null;
		if (!rasterDefinition.IsNull())
		{
			spatialIndex = new SpatialIndex<Tuple<int, int>>(rasterDefinition);
		}
		else
		{
			Extent ext = Extent.Union(plines).EnforceMinDimensions(0.01, 0.01).RatioAroundCenter(1.01);
			if (ext.IsNull())
			{
				return SpatialIndex<Tuple<int, int>>.DefaultSpatialIndex();
			}
			int numValues = plines.Sum((Polyline pl) => pl?.SegmentCount ?? 0);
			spatialIndex = new SpatialIndex<Tuple<int, int>>(ext, ReasonableBinCount(numValues));
		}
		for (int intervalIndex = 0; intervalIndex < plines.Count; intervalIndex++)
		{
			Polyline polyline = plines[intervalIndex];
			if (polyline != null)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < polyline.SegmentCount; intervalIndex2++)
				{
					Segment seg = polyline.Segments[intervalIndex2];
					spatialIndex.Add(seg, new Tuple<int, int>(intervalIndex, intervalIndex2));
				}
			}
		}
		return spatialIndex;
	}

	public static SpatialIndex<FeatureSegmentSubset> ComputeSegmentSpatialIndex<L>(IList<L> plines, RasterDefinition? rasterDef = null) where L : Polyline
	{
		RasterDefinition rasterDefinition = (rasterDef.HasValue ? rasterDef.Value : RasterDefinition.NullRaster);
		if (plines.Count == 0)
		{
			if (rasterDefinition.IsNull())
			{
				return SpatialIndex<FeatureSegmentSubset>.DefaultSpatialIndex();
			}
			return new SpatialIndex<FeatureSegmentSubset>(rasterDefinition);
		}
		SpatialIndex<FeatureSegmentSubset> spatialIndex = null;
		if (!rasterDefinition.IsNull())
		{
			spatialIndex = new SpatialIndex<FeatureSegmentSubset>(rasterDefinition);
		}
		else
		{
			Extent ext = Extent.Union(plines).EnforceMinDimensions(0.01, 0.01).RatioAroundCenter(1.01);
			if (ext.IsNull())
			{
				return SpatialIndex<FeatureSegmentSubset>.DefaultSpatialIndex();
			}
			int numValues = plines.Sum((L pl) => pl?.SegmentCount ?? 0);
			spatialIndex = new SpatialIndex<FeatureSegmentSubset>(ext, ReasonableBinCount(numValues));
		}
		for (int intervalIndex = 0; intervalIndex < plines.Count; intervalIndex++)
		{
			L pline = plines[intervalIndex];
			if (pline != null)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < pline.SegmentCount; intervalIndex2++)
				{
					Segment segment = pline.Segments[intervalIndex2];
					segment.Rasterize(t: (spatialIndex, intervalIndex, intervalIndex2), raster: spatialIndex.Raster, pixelAction: RasterizeFSSOperator);
				}
			}
		}
		return spatialIndex;
	}

	public static void RasterizeFSSOperator(int binIndex, (SpatialIndex<FeatureSegmentSubset>, int, int) payload)
	{
		SpatialIndex<FeatureSegmentSubset> payloadTerm = payload.Item1;
		int fid = payload.Item2;
		int payloadTerm2 = payload.Item3;
		List<FeatureSegmentSubset> bin = payloadTerm.Bins[binIndex];
		if (bin != null && bin.Count != 0)
		{
			if (bin[bin.Count - 1].Fid == fid)
			{
				FeatureSegmentSubset bin2 = bin[bin.Count - 1];
				ref CompactList<Range1D> ranges = ref bin2.Ranges;
				Range1D range1D = ranges[ranges.Count - 1];
				if (range1D.End == payloadTerm2 - 1)
				{
					Range1D ranges22 = new Range1D(range1D.Start, payloadTerm2);
					ref CompactList<Range1D> ranges2 = ref bin2.Ranges;
					ranges2[ranges2.Count - 1] = ranges22;
				}
				else
				{
					Range1D range1D2 = new Range1D(payloadTerm2, payloadTerm2);
					bin2.Ranges.Add(range1D2);
				}
				bin[bin.Count - 1] = bin2;
				return;
			}
		}
		CompactList<Range1D> ranges3 = new CompactList<Range1D>(new Range1D(payloadTerm2, payloadTerm2));
		FeatureSegmentSubset featureSegmentSubset = new FeatureSegmentSubset();
		featureSegmentSubset.Fid = fid;
		featureSegmentSubset.Ranges = ranges3;
		FeatureSegmentSubset key = featureSegmentSubset;
		payloadTerm.Add(binIndex, key);
	}

	public static SpatialIndex<Tuple<int, int>> ComputeSegmentSpatialIndex(List<Polyline> plines, RasterDefinition? rasterDef = null)
	{
		return ComputeSegmentSpatialIndex((IList<Polyline>)plines, rasterDef);
	}

	public static SpatialIndex<Tuple<int, int>> ComputeSegmentSpatialIndex(Polyline[] plines, RasterDefinition? rasterDef = null)
	{
		return ComputeSegmentSpatialIndex((IList<Polyline>)plines, rasterDef);
	}

	public static SpatialIndex<int> ComputeSpatialIndex(IList<Polyline> plines, RasterDefinition? rasterDef = null)
	{
		RasterDefinition rasterDefinition = (rasterDef.HasValue ? rasterDef.Value : RasterDefinition.NullRaster);
		if (plines.Count == 0)
		{
			if (rasterDefinition.IsNull())
			{
				return SpatialIndex<int>.DefaultSpatialIndex();
			}
			return new SpatialIndex<int>(rasterDefinition);
		}
		SpatialIndex<int> spatialIndex = null;
		if (!rasterDefinition.IsNull())
		{
			spatialIndex = new SpatialIndex<int>(rasterDefinition);
		}
		else
		{
			Extent ext = Extent.Union(plines).EnforceMinDimensions(0.01, 0.01).RatioAroundCenter(1.01);
			if (ext.IsNull())
			{
				return SpatialIndex<int>.DefaultSpatialIndex();
			}
			int numValues = plines.Sum((Polyline pl) => pl?.SegmentCount ?? 0);
			spatialIndex = new SpatialIndex<int>(ext, ReasonableBinCount(numValues));
		}
		for (int intervalIndex = 0; intervalIndex < plines.Count; intervalIndex++)
		{
			Polyline polyline = plines[intervalIndex];
			if (polyline != null)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < polyline.SegmentCount; intervalIndex2++)
				{
					Segment seg = polyline.Segments[intervalIndex2];
					spatialIndex.Add(seg, intervalIndex);
				}
			}
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> FromPolygons(IList<Polygon> polygons)
	{
		if (polygons.IsNullOrEmpty())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent.Union(polygons), ReasonableBinCount(polygons.Sum((Polygon pl) => pl.SegmentCount)));
		for (int intervalIndex = 0; intervalIndex < polygons.Count; intervalIndex++)
		{
			Polygon polygon = polygons[intervalIndex];
			spatialIndex.Add(polygon.Extent, intervalIndex);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> FromComplexPolygons(IList<ComplexPolygon> polygons)
	{
		if (polygons.IsNullOrEmpty())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent.Union(polygons), ReasonableBinCount(polygons.Sum((ComplexPolygon pl) => pl.SegmentCount)));
		for (int intervalIndex = 0; intervalIndex < polygons.Count; intervalIndex++)
		{
			ComplexPolygon complexPolygon = polygons[intervalIndex];
			spatialIndex.Add(complexPolygon.Extent, intervalIndex);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> FromMeshes(IList<Mesh> meshes)
	{
		if (meshes.IsNullOrEmpty())
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent.Union(meshes), ReasonableBinCount(meshes.Sum((Mesh mesh2) => mesh2.Perimeter.SegmentCount)));
		for (int intervalIndex = 0; intervalIndex < meshes.Count; intervalIndex++)
		{
			Mesh mesh = meshes[intervalIndex];
			spatialIndex.Add(mesh.Extent, intervalIndex);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> FromExtents(IList<Extent> extents)
	{
		if (extents.IsNullOrEmpty())
		{
			return null;
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent.Union(extents), ReasonableBinCount(extents.Count));
		for (int i = 0; i < extents.Count; i++)
		{
			spatialIndex.Add(extents[i], i);
		}
		return spatialIndex;
	}

	public static int NearestPoint(SpatialIndex<int> spIdx, IList<Point> points, Point queryPoint, double maxReach, out double actualDistance)
	{
		List<int> elements = spIdx.GetElements(queryPoint, maxReach);
		actualDistance = double.MaxValue;
		if (elements.Count == 0)
		{
			return -1;
		}
		int result = -1;
		foreach (int element in elements)
		{
			double actualDistance2 = points[element].DistanceTo(queryPoint);
			if (actualDistance2 < actualDistance)
			{
				result = element;
				actualDistance = actualDistance2;
			}
		}
		if (actualDistance <= maxReach)
		{
			return result;
		}
		actualDistance = double.MaxValue;
		return -1;
	}
}
