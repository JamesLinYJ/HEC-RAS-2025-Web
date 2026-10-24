using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Geospatial.Rasters;
using Geospatial.Vectors;
using RasMapperLib.TinHelpers;
using Utility.Extensions;

namespace RasMapperLib;

[ComVisible(false)]
public class SpatialIndex<T>
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__43_002D0
	{
		public List<int> intersectedBins;

		public _Closure_0024__43_002D0(_Closure_0024__43_002D0 other)
		{
			if (other != null)
			{
				intersectedBins = other.intersectedBins;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			intersectedBins.Add(n);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__45_002D0
	{
		public bool[,] intersectedBins;

		public int startRow;

		public int endRow;

		public _Closure_0024__45_002D0(_Closure_0024__45_002D0 other)
		{
			if (other != null)
			{
				intersectedBins = other.intersectedBins;
				startRow = other.startRow;
				endRow = other.endRow;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int row, int col)
		{
			intersectedBins[row, col] = true;
			if (row < startRow)
			{
				startRow = row;
			}
			if (row > endRow)
			{
				endRow = row;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int row, int col)
		{
			intersectedBins[row, col] = true;
			if (row < startRow)
			{
				startRow = row;
			}
			if (row > endRow)
			{
				endRow = row;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int row, int col)
		{
			intersectedBins[row, col] = true;
			if (row < startRow)
			{
				startRow = row;
			}
			if (row > endRow)
			{
				endRow = row;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int row, int col)
		{
			intersectedBins[row, col] = true;
			if (row < startRow)
			{
				startRow = row;
			}
			if (row > endRow)
			{
				endRow = row;
			}
		}
	}

	protected RasterM _raster;

	protected List<T>[] _bins;

	private RasterizeSegment _rasterizeSegment;

	private RasterizePolygon _rasterizePolygon;

	public int MaxResizedBinCount;

	public RasterM Raster => _raster;

	public IExtent Extent => _raster;

	public List<T>[] Bins => _bins;

	// C# has no syntax for parameterized property 'Bin'.
	public List<T> get_Bin(int r, int c)
	{
		return _bins[_raster.CellIndex(r, c)];
	}

	public static SpatialIndex<K> DefaultSpatialIndex<K>()
	{
		return new SpatialIndex<K>(new Extent(1.0, 0.0, 1.0, 0.0), 1);
	}

	public SpatialIndex(IExtent extent, int maxBinCount = 4096)
	{
		Initialize(RasterM.ComputeRasterM(maxBinCount, extent));
	}

	public SpatialIndex(RasterM binRaster)
	{
		Initialize(binRaster);
	}

	private void Initialize(RasterM raster)
	{
		_raster = raster.Copy();
		checked
		{
			_bins = new List<T>[_raster.CellCount - 1 + 1];
			int count = _bins.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				_bins[i] = new List<T>();
			}
			_rasterizeSegment = new RasterizeSegment(_raster);
			_rasterizePolygon = new RasterizePolygon(_raster);
			MaxResizedBinCount = _raster.CellCount * 2;
		}
	}

	public bool Resize(Extent ext)
	{
		if (Resize(ext.Left, ext.Top))
		{
			return Resize(ext.Right, ext.Bottom);
		}
		return false;
	}

	public bool Resize(SegmentM seg)
	{
		if (Resize(seg.A.X, seg.A.Y))
		{
			return Resize(seg.B.X, seg.B.Y);
		}
		return false;
	}

	public bool Resize(Polyline pline)
	{
		return Resize(pline.GetExtent());
	}

	public bool Resize(double x, double y)
	{
		if (_raster.Contains(x, y))
		{
			return true;
		}
		int tilesLeft = default;
		int tilesRight = default;
		int tilesUp = default;
		int tilesDown = default;
		GetBinExpansion(x, y, ref tilesLeft, ref tilesRight, ref tilesUp, ref tilesDown);
		return Resize(tilesLeft, tilesRight, tilesUp, tilesDown);
	}

	public bool Resize(int tilesLeft, int tilesRight, int tilesUp, int tilesDown)
	{
		checked
		{
			int colsRasterTerm = _raster.Cols + tilesLeft + tilesRight;
			int rowsRasterTerm = _raster.Rows + tilesUp + tilesDown;
			int ordinal = colsRasterTerm * rowsRasterTerm;
			if (ordinal > MaxResizedBinCount)
			{
				return false;
			}
			double left = _raster.Left;
			double top = _raster.Top;
			double ax = left - (double)tilesLeft * _raster.CellSize;
			double ay = top + (double)tilesUp * _raster.CellSize;
			RasterM raster = new RasterM(new PointM(ax, ay), rowsRasterTerm, colsRasterTerm, _raster.CellSize);
			int sum = _bins.Sum([SpecialName] (List<T> a) => a.Count);
			List<T>[] bins = new List<T>[ordinal - 1 + 1];
			int count = rowsRasterTerm - 1;
			for (int ordinal2 = 0; ordinal2 <= count; ordinal2++)
			{
				int count2 = colsRasterTerm - 1;
				for (int ordinal3 = 0; ordinal3 <= count2; ordinal3++)
				{
					int ordinal4 = ordinal2 * colsRasterTerm + ordinal3;
					int tilesUpTerm = ordinal2 - tilesUp;
					int tilesLeftTerm = ordinal3 - tilesLeft;
					if (tilesLeftTerm < 0 || tilesLeftTerm >= _raster.Cols || tilesUpTerm < 0 || tilesUpTerm >= _raster.Rows)
					{
						bins[ordinal4] = new List<T>();
						continue;
					}
					int colsRasterTerm2 = tilesUpTerm * _raster.Cols + tilesLeftTerm;
					bins[ordinal4] = _bins[colsRasterTerm2];
				}
			}
			_raster = raster;
			_rasterizeSegment = new RasterizeSegment(_raster);
			_bins = bins;
			int sum2 = _bins.Sum([SpecialName] (List<T> a) => a.Count);
			return true;
		}
	}

	public int NewBinCount(double addX, double addY)
	{
		int tilesLeft = default;
		int tilesRight = default;
		int tilesUp = default;
		int tilesDown = default;
		GetBinExpansion(addX, addY, ref tilesLeft, ref tilesRight, ref tilesUp, ref tilesDown);
		return checked((_raster.Cols + tilesLeft + tilesRight) * (_raster.Rows + tilesUp + tilesDown));
	}

	private void GetBinExpansion(double x, double y, ref int tilesLeft, ref int tilesRight, ref int tilesUp, ref int tilesDown)
	{
		double cellSize = _raster.CellSize;
		checked
		{
			if (x < _raster.Left)
			{
				double leftRasterTerm = _raster.Left - x;
				tilesLeft = (int)Math.Ceiling(leftRasterTerm / cellSize);
			}
			else if (x > _raster.Right)
			{
				double rightRasterTerm = x - _raster.Right;
				tilesRight = (int)Math.Ceiling(rightRasterTerm / cellSize);
			}
			if (y < _raster.Bottom)
			{
				double bottomRasterTerm = _raster.Bottom - y;
				tilesDown = (int)Math.Ceiling(bottomRasterTerm / cellSize);
			}
			else if (y > _raster.Top)
			{
				double topRasterTerm = y - _raster.Top;
				tilesUp = (int)Math.Ceiling(topRasterTerm / cellSize);
			}
		}
	}

	public bool TryAddWithResize(PointM point, T key)
	{
		if (!Resize(point.X, point.Y))
		{
			return false;
		}
		_bins[_raster.CellIndex(point)].Add(key);
		return true;
	}

	public void Add(SegmentM segment, T key)
	{
		_rasterizeSegment.ComputeCells(segment, [SpecialName] (int n) =>
		{
			_bins[n].Add(key);
		});
	}

	public void Add(Polyline polyline, T key)
	{
		checked
		{
			int count = polyline.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Add(polyline.Segment(i), key);
			}
		}
	}

	public void Add(PointM point, T key)
	{
		_bins[_raster.CellIndex(point)].Add(key);
	}

	public void Add(Point point, T key)
	{
		if (!point.PointM().IsUndefined())
		{
			_bins[_raster.CellIndex(point)].Add(key);
		}
	}

	public void Add(Point2Double point, T key)
	{
		if (!point.IsUndefined())
		{
			_bins[_raster.CellIndex(point)].Add(key);
		}
	}

	public void Add(double x, double y, T key)
	{
		_bins[_raster.CellIndex(x, y)].Add(key);
	}

	public void Add(IPoints points, T key)
	{
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Add(points.PointM(i), key);
			}
		}
	}

	public void Add(Extent extent, T key)
	{
		RowColRangeM rowColRangeM = _raster.RowColRange(extent, limitToRaster: true);
		int minRow = rowColRangeM.MinRow;
		int maxRow = rowColRangeM.MaxRow;
		checked
		{
			for (int i = minRow; i <= maxRow; i++)
			{
				int minCol = rowColRangeM.MinCol;
				int maxCol = rowColRangeM.MaxCol;
				for (int j = minCol; j <= maxCol; j++)
				{
					int cellIndex = _raster.CellIndex(i, j);
					_bins[cellIndex].Add(key);
				}
			}
		}
	}

	public void Add(Polygon polygon, T key)
	{
		List<Geospatial.Vectors.Point> points = new List<Geospatial.Vectors.Point>();
		checked
		{
			int count = polygon.PointMCount() - 2;
			for (int i = 0; i <= count; i++)
			{
				points.Add(new Geospatial.Vectors.Point(polygon.PointM(i).X, polygon.PointM(i).Y));
			}
			Geospatial.Vectors.Polygon polygon2 = new Geospatial.Vectors.Polygon(points);
			bool[] boolValues = new bool[_raster.Cols * _raster.Rows - 1 + 1];
			RasterDefinition raster = new RasterDefinition(new Geospatial.Vectors.Point(_raster.Left, _raster.Top), _raster.CellSize, _raster.Rows, _raster.Cols);
			if (polygon2.Rasterize(raster, boolValues, PixelRangeOptions.PartialPixelCovered))
			{
				int count2 = _bins.Length - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (boolValues[j])
					{
						_bins[j].Add(key);
					}
				}
			}
			else
			{
				polygon.ComputeExtent();
				Add(polygon.ComputeExtent(), key);
			}
		}
	}

	public void Add(Feature feature, T key)
	{
		if (feature is Extent)
		{
			Add((Extent)feature, key);
			return;
		}
		if (feature is Point)
		{
			Add((Point)feature, key);
			return;
		}
		if (feature is Polygon)
		{
			Add((Polygon)feature, key);
			return;
		}
		if (feature is Polyline)
		{
			Add((Polyline)feature, key);
			return;
		}
		if (feature is IPoints)
		{
			Add((IPoints)feature, key);
			return;
		}
		throw new NotImplementedException("This feature is not supported in SpatialIndex.");
	}

	public void Add(Feature feature, T key, Type overrideType)
	{
		if (overrideType == typeof(Extent))
		{
			Add((Extent)feature, key);
			return;
		}
		if (overrideType == typeof(Point))
		{
			Add((Point)feature, key);
			return;
		}
		if (overrideType == typeof(Polygon))
		{
			Add((Polygon)feature, key);
			return;
		}
		if (overrideType == typeof(Polyline))
		{
			Add((Polyline)feature, key);
			return;
		}
		if (overrideType == typeof(IPoints))
		{
			Add((IPoints)feature, key);
			return;
		}
		throw new NotImplementedException("This feature is not supported in SpatialIndex.");
	}

	public void AddInBounds(PointM pt, T key)
	{
		if (_raster.Contains(pt))
		{
			Add(pt, key);
		}
	}

	public List<T> GetElements(double x, double y)
	{
		if (_raster.Contains(x, y))
		{
			int cellIndex = _raster.CellIndex(x, y);
			return _bins[cellIndex];
		}
		return new List<T>();
	}

	public List<T> GetElements(PointM ptm)
	{
		return GetElements(ptm.X, ptm.Y);
	}

	public List<T> GetElements(SegmentM segment)
	{
		List<T> items = new List<T>();
		int ordinal = default;
		if (segment.ClipToExtent(_raster))
		{
			_rasterizeSegment.ComputeCells(segment, checked([SpecialName] (int n) =>
			{
				if (_bins[n] != null)
				{
					items.AddRange(_bins[n]);
					ordinal++;
				}
			}));
		}
		if (ordinal <= 1)
		{
			return items;
		}
		return items.Distinct().ToList();
	}

	public List<List<T>> GetElementsAndBinRange(SegmentM segment, ref List<int> binIndices)
	{
		List<List<T>> elementsAndBinRange = new List<List<T>>();
		List<int> binIndicesList = new List<int>();
		if (segment.ClipToExtent(_raster))
		{
			_rasterizeSegment.ComputeCells(segment, [SpecialName] (int n) =>
			{
				if (_bins[n] != null)
				{
					elementsAndBinRange.Add(new List<T>(_bins[n]));
					binIndicesList.Add(n);
				}
			});
		}
		binIndices = binIndicesList;
		return elementsAndBinRange;
	}

	public string Find(T localValue)
	{
		checked
		{
			int count = Bins.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Bins[i].Contains(localValue))
				{
					return string.Concat(string.Concat("Item " + localValue.ToString() + " found in bin " + i + ", at ", Raster.CellCenter(i).ToCSString()), " (cellsize = ", Raster.CellSize.ToString(), ")");
				}
			}
			return "Item not found";
		}
	}

	public List<T> GetElementsWithDupes(SegmentM segment)
	{
		List<T> elementsWithDupes = new List<T>();
		if (segment.ClipToExtent(_raster))
		{
			_rasterizeSegment.ComputeCells(segment, [SpecialName] (int n) =>
			{
				if (_bins[n] != null)
				{
					elementsWithDupes.AddRange(_bins[n]);
				}
			});
		}
		return elementsWithDupes;
	}

	public List<T> GetElements(Polyline pline)
	{
		_Closure_0024__43_002D0 capturedLocals = default;
		_Closure_0024__43_002D0 capturedLocals2 = new _Closure_0024__43_002D0(capturedLocals);
		List<T> items = new List<T>();
		capturedLocals2.intersectedBins = new List<int>();
		foreach (SegmentM segment in pline.Segments(0))
		{
			if (segment.ClipToExtent(_raster))
			{
				_rasterizeSegment.ComputeCells(segment, [SpecialName] (int n) =>
				{
					capturedLocals2.intersectedBins.Add(n);
				});
			}
		}
		foreach (int distinct in capturedLocals2.intersectedBins.Distinct())
		{
			if (_bins[distinct] != null)
			{
				items.AddRange(_bins[distinct]);
			}
		}
		return items.Distinct().ToList();
	}

	public List<T> GetElements(Polygon pgon)
	{
		throw new Exception("Don't call this. It doesn't work. It's here to make sure you don't accidentally call the polyline version.");
	}

	public List<T> GetElements(SegmentM segment, double distance)
	{
		if (double.IsNaN(distance) || distance <= 0.0)
		{
			throw new ArgumentException("distance");
		}
		if (segment.Length < _raster.CellSize * 4.0)
		{
			return GetElements(segment.MinX - distance, segment.MaxX + distance, segment.MinY - distance, segment.MaxY + distance);
		}
		_Closure_0024__45_002D0 capturedLocals = default;
		_Closure_0024__45_002D0 capturedLocals2 = new _Closure_0024__45_002D0(capturedLocals);
		List<T> items = new List<T>();
		checked
		{
			capturedLocals2.intersectedBins = new bool[_raster.Rows - 1 + 1, _raster.Cols - 1 + 1];
			capturedLocals2.startRow = int.MaxValue;
			capturedLocals2.endRow = int.MinValue;
			PointM pointM = segment.UnitVector().Ratio(distance);
			PointM pointM2 = segment.PerpendicularUnitVector().Ratio(distance);
			PointM pointM3 = segment.A.Translate(0.0 - pointM.X + pointM2.X, 0.0 - pointM.Y + pointM2.Y);
			PointM pointM4 = segment.B.Translate(pointM.X + pointM2.X, pointM.Y + pointM2.Y);
			PointM pointM5 = segment.B.Translate(pointM.X - pointM2.X, pointM.Y - pointM2.Y);
			PointM pointM6 = segment.A.Translate(0.0 - pointM.X - pointM2.X, 0.0 - pointM.Y - pointM2.Y);
			SegmentM segment2 = new SegmentM(pointM3, pointM4);
			SegmentM segment3 = new SegmentM(pointM4, pointM5);
			SegmentM segment4 = new SegmentM(pointM5, pointM6);
			SegmentM segment5 = new SegmentM(pointM6, pointM3);
			_rasterizeSegment.ComputeCells(segment2, [SpecialName] (int row, int col) =>
			{
				capturedLocals2.intersectedBins[row, col] = true;
				if (row < capturedLocals2.startRow)
				{
					capturedLocals2.startRow = row;
				}
				if (row > capturedLocals2.endRow)
				{
					capturedLocals2.endRow = row;
				}
			});
			_rasterizeSegment.ComputeCells(segment3, [SpecialName] (int row, int col) =>
			{
				capturedLocals2.intersectedBins[row, col] = true;
				if (row < capturedLocals2.startRow)
				{
					capturedLocals2.startRow = row;
				}
				if (row > capturedLocals2.endRow)
				{
					capturedLocals2.endRow = row;
				}
			});
			_rasterizeSegment.ComputeCells(segment4, [SpecialName] (int row, int col) =>
			{
				capturedLocals2.intersectedBins[row, col] = true;
				if (row < capturedLocals2.startRow)
				{
					capturedLocals2.startRow = row;
				}
				if (row > capturedLocals2.endRow)
				{
					capturedLocals2.endRow = row;
				}
			});
			_rasterizeSegment.ComputeCells(segment5, [SpecialName] (int row, int col) =>
			{
				capturedLocals2.intersectedBins[row, col] = true;
				if (row < capturedLocals2.startRow)
				{
					capturedLocals2.startRow = row;
				}
				if (row > capturedLocals2.endRow)
				{
					capturedLocals2.endRow = row;
				}
			});
			int startRow = capturedLocals2.startRow;
			int count = capturedLocals2.endRow;
			for (int ordinal = startRow; ordinal <= count; ordinal++)
			{
				int ordinal2 = -1;
				int ordinal3 = -1;
				int count2 = _raster.Cols - 1;
				for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
				{
					if (capturedLocals2.intersectedBins[ordinal, intervalIndex])
					{
						if (ordinal2 == -1)
						{
							ordinal2 = intervalIndex;
						}
						else
						{
							ordinal3 = intervalIndex;
						}
					}
				}
				if (ordinal2 != -1)
				{
					int ordinal4 = ordinal2 + 1;
					int count3 = ordinal3 - 1;
					for (int ordinal5 = ordinal4; ordinal5 <= count3; ordinal5++)
					{
						capturedLocals2.intersectedBins[ordinal, ordinal5] = true;
					}
				}
			}
			int startRow2 = capturedLocals2.startRow;
			int count4 = capturedLocals2.endRow;
			for (int ordinal6 = startRow2; ordinal6 <= count4; ordinal6++)
			{
				int count5 = _raster.Cols - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count5; intervalIndex2++)
				{
					if (capturedLocals2.intersectedBins[ordinal6, intervalIndex2])
					{
						items.AddRange(_bins[ordinal6 * _raster.Cols + intervalIndex2]);
					}
				}
			}
			return items.Distinct().ToList();
		}
	}

	public List<T> GetElements(double x, double y, double distance, bool ensureDistinct = true)
	{
		return GetElements(x - distance, x + distance, y - distance, y + distance, ensureDistinct);
	}

	public List<T> GetElements(PointM ptm, double distance, bool ensureDistinct = true)
	{
		return GetElements(ptm.X - distance, ptm.X + distance, ptm.Y - distance, ptm.Y + distance, ensureDistinct);
	}

	public List<T> GetElementsAtPoints(IPoints pts, bool ensureDistinct = true)
	{
		List<int> intList = new List<int>();
		checked
		{
			int count = pts.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = pts.PointM(i);
				if (_raster.Contains(pointM))
				{
					intList.Add(_raster.CellIndex(pointM));
				}
			}
			List<T> elementsAtPoints = new List<T>();
			foreach (int distinct in intList.Distinct())
			{
				elementsAtPoints.AddRange(_bins[distinct]);
			}
			if (ensureDistinct)
			{
				elementsAtPoints = elementsAtPoints.Distinct().ToList();
			}
			return elementsAtPoints;
		}
	}

	public void GetBinRange(IExtent ext, ref List<int> completelyCoveredBins, ref List<int> partiallyCoveredBins)
	{
		completelyCoveredBins = new List<int>();
		partiallyCoveredBins = new List<int>();
		if (!_raster.Overlaps(ext.MinX, ext.MaxX, ext.MinY, ext.MaxY))
		{
			return;
		}
		RowColRangeM rowColRangeM = _raster.RowColRange(ext.MinX, ext.MaxX, ext.MinY, ext.MaxY, limitToRaster: false);
		int minRow = rowColRangeM.MinRow;
		int maxRow = rowColRangeM.MaxRow;
		checked
		{
			for (int i = minRow; i <= maxRow; i++)
			{
				if (i < 0)
				{
					continue;
				}
				if (i >= Raster.Rows)
				{
					break;
				}
				bool minRowRowColRangeMTerm = i == rowColRangeM.MinRow || i == rowColRangeM.MaxRow;
				int minCol = rowColRangeM.MinCol;
				int maxCol = rowColRangeM.MaxCol;
				for (int j = minCol; j <= maxCol; j++)
				{
					if (j >= 0)
					{
						if (j >= Raster.Cols)
						{
							break;
						}
						if (minRowRowColRangeMTerm || j == rowColRangeM.MinCol || j == rowColRangeM.MaxCol)
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
		}
	}

	public void GetBinRange(Polygon pgon, ref List<int> completelyCoveredBins, ref List<int> partiallyCoveredBins)
	{
		completelyCoveredBins = new List<int>();
		partiallyCoveredBins = new List<int>();
		Extent extent = pgon.ComputeExtent();
		if (!_raster.Overlaps(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY))
		{
			return;
		}
		RowColRangeM rowColRangeM = _raster.RowColRange(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY, limitToRaster: false);
		int minRow = Math.Max(rowColRangeM.MinRow, 0);
		checked
		{
			int maxRowRowColRangeMTerm = Math.Min(rowColRangeM.MaxRow, Raster.Rows - 1);
			int minCol = Math.Max(rowColRangeM.MinCol, 0);
			int maxColRowColRangeMTerm = Math.Min(rowColRangeM.MaxCol, Raster.Cols - 1);
			int count = maxRowRowColRangeMTerm;
			for (int i = minRow; i <= count; i++)
			{
				int count2 = maxColRowColRangeMTerm;
				for (int j = minCol; j <= count2; j++)
				{
					Extent ext = _raster.CellExtent(i, j);
					if (pgon.Contains(ext))
					{
						completelyCoveredBins.Add(_raster.CellIndex(i, j));
					}
					else if (pgon.AreaIntersects(ext))
					{
						partiallyCoveredBins.Add(_raster.CellIndex(i, j));
					}
				}
			}
		}
	}

	public void GetBinRangeBuffered(Polygon pgon, double buffer, ref List<int> completelyCoveredBins, ref List<int> partiallyCoveredBins)
	{
		completelyCoveredBins = new List<int>();
		partiallyCoveredBins = new List<int>();
		Extent extent = pgon.ComputeExtent();
		extent = extent.Extend(buffer);
		if (!_raster.Overlaps(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY))
		{
			return;
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Raster);
		checked
		{
			int count = pgon.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(pgon.Segment(i), i);
			}
			DevelopmentTin developmentTin = PolygonFeatureLayer.ComputeDevelopmentTIN(pgon);
			int count2 = spatialIndex.Bins.Count() - 1;
			for (int j = 0; j <= count2; j++)
			{
				if (spatialIndex.Bins[j].Count > 0)
				{
					partiallyCoveredBins.Add(j);
					continue;
				}
				PointM point = spatialIndex.Raster.CellCenter(j);
				if (developmentTin.ContainsPoint(point))
				{
					completelyCoveredBins.Add(j);
				}
			}
		}
	}

	public List<T> GetElements(IExtent extent, bool ensureDistinct = true)
	{
		return GetElements(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY, ensureDistinct);
	}

	public List<T> GetElements(double minX, double maxX, double minY, double maxY, bool ensureDistinct = true)
	{
		List<T> items = new List<T>();
		if (!_raster.Overlaps(minX, maxX, minY, maxY))
		{
			return items;
		}
		RowColRangeM range = _raster.RowColRange(minX, maxX, minY, maxY, limitToRaster: true);
		AddRange(range, items);
		if (ensureDistinct)
		{
			return items.Distinct().ToList();
		}
		return items;
	}

	private void AddRange(RowColRangeM range, List<T> segmentList)
	{
		int minRow = range.MinRow;
		int maxRow = range.MaxRow;
		checked
		{
			for (int i = minRow; i <= maxRow; i++)
			{
				int minCol = range.MinCol;
				int maxCol = range.MaxCol;
				for (int j = minCol; j <= maxCol; j++)
				{
					int cellIndex = _raster.CellIndex(i, j);
					segmentList.AddRange(_bins[cellIndex]);
				}
			}
		}
	}

	public void Delete(PointM pt, T key)
	{
		_bins[_raster.CellIndex(pt)].Remove(key);
	}

	public void Delete(SegmentM seg, T key)
	{
		List<int> binIndices = null;
		GetElementsAndBinRange(seg, ref binIndices);
		checked
		{
			int count = binIndices.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				_bins[binIndices[i]].Remove(key);
			}
		}
	}

	public bool TryMoveWithResize(PointM prevPt, PointM newPt, T key)
	{
		_bins[_raster.CellIndex(prevPt)].Remove(key);
		return TryAddWithResize(newPt, key);
	}

	public void ModifyKeys(Func<T, T> modification)
	{
		List<T>[] bins = Bins;
		checked
		{
			foreach (List<T> bin in bins)
			{
				int count = bin.Count - 1;
				for (int j = 0; j <= count; j++)
				{
					bin[j] = modification(bin[j]);
				}
			}
		}
	}

	public void WriteRaster(string bilFilename)
	{
		checked
		{
			using (FileStream fileStream = new FileStream(bilFilename, FileMode.Create))
			{
				int[] intValues = new int[_raster.Cols - 1 + 1];
				byte[] byteValues = new byte[_raster.Cols * 4 - 1 + 1];
				int count = _raster.Rows - 1;
				int ordinal = default;
				for (int i = 0; i <= count; i++)
				{
					int count2 = _raster.Cols - 1;
					for (int j = 0; j <= count2; j++)
					{
						intValues[j] = _bins[ordinal].Count;
						ordinal++;
					}
					Buffer.BlockCopy(intValues, 0, byteValues, 0, byteValues.Count());
					fileStream.Write(byteValues, 0, byteValues.Count());
				}
			}
			_raster.WriteIntegerHeaderfile(bilFilename, 0);
		}
	}
}
[ComVisible(false)]
public abstract class SpatialIndex
{
	public static int ReasonableBinCount(int numValues)
	{
		return Math.Max(1, Math.Min(4096, checked((int)Math.Round((double)numValues / 32.0))));
	}

	public static int ReasonableSegmentBinCount(int numValues)
	{
		return checked(ReasonableBinCount(numValues) * 4);
	}

	public static SpatialIndex<int> GetPointSpatialIndex(MultiPoint pts)
	{
		Extent extent = pts.ComputeExtent();
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, ReasonableBinCount(pts.Count));
		checked
		{
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(pts.PointM(i), i);
			}
			return spatialIndex;
		}
	}

	public static SpatialIndex<int> GetAppropriateSpatialIndex(FeatureLayer ftl)
	{
		if (ftl is PointFeatureLayer)
		{
			return GetPointSpatialIndex((from pt in ftl.FeaturesOfExactType<Point>()
				select pt.PointM()).ToList());
		}
		return GetFeatureSpatialIndex(ftl.Features().ToList());
	}

	private static SpatialIndex<int> GetPointSpatialIndex(List<PointM> ptms)
	{
		Extent extent = new Extent(ptms);
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, ReasonableBinCount(ptms.Count));
		if (Extent.IsNullOrInfinite(extent))
		{
			return SpatialIndex<int>.DefaultSpatialIndex<int>();
		}
		checked
		{
			int count = ptms.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(ptms[i], i);
			}
			return spatialIndex;
		}
	}

	public static SpatialIndex<int> GetSegmentSpatialIndex(Polyline polyline)
	{
		Extent extent = polyline.ComputeExtent();
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, ReasonableBinCount(polyline.SegmentCount()));
		checked
		{
			int count = polyline.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(polyline.Segment(i), i);
			}
			return spatialIndex;
		}
	}

	private static SpatialIndex<int> GetFeatureSpatialIndex(List<Feature> feats)
	{
		if (feats.IsNullOrEmpty())
		{
			return SpatialIndex<int>.DefaultSpatialIndex<int>();
		}
		Extent extent = null;
		foreach (Feature feat in feats)
		{
			if (feat != null && (!(feat is IPoints) || ((IPoints)feat).PointMCount() != 0))
			{
				extent = ((extent != null) ? feat.GetExtent().UnionXY(extent) : feat.GetExtent());
			}
		}
		if (Extent.IsNullOrInfinite(extent))
		{
			return SpatialIndex<int>.DefaultSpatialIndex<int>();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, ReasonableBinCount(feats.Count));
		checked
		{
			int count = feats.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Extent extent2 = feats[i].GetExtent();
				if (extent2.IsFinite())
				{
					spatialIndex.Add(extent2, i);
				}
			}
			return spatialIndex;
		}
	}

	public static SpatialIndex<Tuple<int, int, int>> GetPerimeterEdgeSpatialIndex(List<Tin> tins, Extent ext = null)
	{
		if (tins.IsNullOrEmpty())
		{
			return null;
		}
		if (ext == null)
		{
			ext = tins.First().ComputeExtent();
			foreach (Tin tin2 in tins)
			{
				if (tin2 != null)
				{
					ext = tin2.ComputeExtent().UnionXY(ext);
				}
			}
			if (double.IsNaN(ext.MinX) || double.IsNaN(ext.MaxX) || double.IsNaN(ext.MinY) || double.IsNaN(ext.MaxY))
			{
				ext = new Extent(1.0, 0.0, 1.0, 0.0);
			}
		}
		SpatialIndex<Tuple<int, int, int>> spatialIndex = new SpatialIndex<Tuple<int, int, int>>(ext, ReasonableBinCount(tins.Sum([SpecialName] (Tin tin2) => tin2?.EdgeCount() ?? 0)));
		checked
		{
			int count = tins.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				Tin tin = tins[intervalIndex];
				if (tin == null)
				{
					continue;
				}
				int count2 = tin.TriangleCount() - 1;
				for (int ordinal = 0; ordinal <= count2; ordinal++)
				{
					TriangleStruct triangleStruct = tin.Triangle(ordinal);
					int ordinal2 = 0;
					do
					{
						int edgePoint0 = triangleStruct.EdgePoint0(ordinal2);
						int edgePoint1 = triangleStruct.EdgePoint1(ordinal2);
						if (tin.IsEdgePerimeterAndUncoincident(ordinal, edgePoint0, edgePoint1))
						{
							SegmentM segment = new SegmentM(tin.PointM(edgePoint0), tin.PointM(edgePoint1));
							spatialIndex.Add(segment, new Tuple<int, int, int>(intervalIndex, ordinal, ordinal2));
						}
						ordinal2++;
					}
					while (ordinal2 <= 2);
				}
			}
			return spatialIndex;
		}
	}

	public static SpatialIndex<Tuple<int, int>> GetSegmentSpatialIndex(List<Polyline> polylines, Extent ext = null)
	{
		if (polylines.IsNullOrEmpty())
		{
			return null;
		}
		if (ext == null)
		{
			ext = polylines.First().ComputeExtent();
			foreach (Polyline polyline2 in polylines)
			{
				if (polyline2 != null)
				{
					ext = polyline2.ComputeExtent().UnionXY(ext);
				}
			}
			if (double.IsNaN(ext.MinX) || double.IsNaN(ext.MaxX) || double.IsNaN(ext.MinY) || double.IsNaN(ext.MaxY))
			{
				ext = new Extent(1.0, 0.0, 1.0, 0.0);
			}
		}
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(ext, ReasonableBinCount(polylines.Sum([SpecialName] (Polyline pl) => pl?.SegmentCount() ?? 0)));
		checked
		{
			int count = polylines.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				Polyline polyline = polylines[intervalIndex];
				if (polyline != null)
				{
					int count2 = polyline.SegmentCount() - 1;
					for (int ordinal = 0; ordinal <= count2; ordinal++)
					{
						spatialIndex.Add(polyline.Segment(ordinal), new Tuple<int, int>(intervalIndex, ordinal));
					}
				}
			}
			return spatialIndex;
		}
	}

	public static SpatialIndex<int> GetPolygonSpatialIndex(Polygon polygon)
	{
		throw new NotImplementedException();
	}

	public static SpatialIndex<int> GetPolygonSpatialIndex(List<Polygon> polygons)
	{
		if (polygons.IsNullOrEmpty())
		{
			return null;
		}
		Extent extent = polygons.First().ComputeExtent();
		foreach (Polygon polygon2 in polygons)
		{
			extent = polygon2.ComputeExtent().UnionXY(extent);
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, ReasonableBinCount(polygons.Sum([SpecialName] (Polygon pl) => pl.SegmentCount())));
		checked
		{
			int count = polygons.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				Polygon polygon = polygons[intervalIndex];
				spatialIndex.Add(polygon.ComputeExtent(), intervalIndex);
			}
			return spatialIndex;
		}
	}
}
