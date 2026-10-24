using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Geospatial.Rasters.Convolution;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility;
using Utility.Unsafe;

namespace Geospatial.Rasters;

[DebuggerDisplay("X:({Left} to {Right}) Y:({Bottom} to {Top}) CellSize:{CellSize}")]
public readonly struct RasterDefinition : IExtent, IEquatable<RasterDefinition>
{
	public const double DefaultRegistrationCellSizeAbsoluteTolerance = 1E-05;

	public const double DefaultRegistrationCellSizeRatioTolerance = 0.001;

	public const double DefaultRegistrationPixelShiftTolerance = 0.1;

	public static readonly RasterDefinition NullRaster = new RasterDefinition(Extent.NullExtent, double.NaN, 0, 0);

	private readonly int _rows;

	private readonly int _cols;

	private static bool _logAllocations = false;

	private static AllocationTracker _tracker;

	public Extent Extent { get; }

	public double MinX => Extent.MinX;

	public double MaxX => Extent.MaxX;

	public double MinY => Extent.MinY;

	public double MaxY => Extent.MaxY;

	public double Left => MinX;

	public double Right => MaxX;

	public double Top => MaxY;

	public double Bottom => MinY;

	public double Width => Extent.Width;

	public double Height => Extent.Height;

	public double Area => Extent.Area;

	public int Rows => _rows;

	public int Cols => _cols;

	public int CellCount
	{
		get
		{
			long cellCountLong = CellCountLong;
			if (cellCountLong > int.MaxValue)
			{
				return -1;
			}
			return (int)cellCountLong;
		}
	}

	public long CellCountLong => (long)_rows * (long)_cols;

	public double CellSize { get; }

	public double CellArea => CellSize * CellSize;

	public double RoughlyEpsilon => Extent.RoughlyEpsilon;

	public double CenterX => (MinX + MaxX) / 2.0;

	public double CenterY => (MinY + MaxY) / 2.0;

	public Point Center => new Point(CenterX, CenterY);

	public bool IsNull()
	{
		return double.IsNaN(CellSize);
	}

	public static RasterDefinition InfinitePlane(double cellSize)
	{
		return new RasterDefinition(Extent.EffectivelyInfinite, cellSize);
	}

	public static RasterDefinition InfinitePlane()
	{
		return new RasterDefinition(Extent.EffectivelyInfinite, 1, 1);
	}

	public static RasterDefinition UnitRaster(int numCellsPerSide = 1)
	{
		return new RasterDefinition(Extent.UnitExtent(), numCellsPerSide, numCellsPerSide);
	}

	public bool IsValid()
	{
		if (Width > 0.0 && Height > 0.0 && Rows > 0)
		{
			return Cols > 0;
		}
		return false;
	}

	public RasterDefinition(Extent targetExtent, double cellSize)
	{
		if (cellSize <= 0.0)
		{
			throw new ArgumentException("cellSize");
		}
		if (targetExtent.IsNull())
		{
			throw new ArgumentException("targetExtent");
		}
		int rows = (int)Math.Round(targetExtent.Height / cellSize);
		int cols = (int)Math.Round(targetExtent.Width / cellSize);
		rows = Math.Max(rows, 1);
		cols = Math.Max(cols, 1);
		_rows = rows;
		_cols = cols;
		CellSize = cellSize;
		double left = targetExtent.Left;
		double top = targetExtent.Top;
		double maxX = left + (double)_cols * cellSize;
		double minY = top - (double)_rows * cellSize;
		Extent = new Extent(left, maxX, minY, top);
		Initialize();
	}

	public RasterDefinition(Extent targetExtent, int rows, int cols)
		: this(targetExtent.UpperLeft, Math.Max(targetExtent.Width / (double)cols, targetExtent.Height / (double)rows), rows, cols)
	{
	}

	public RasterDefinition(Point upperLeftCorner, double cellSize, int rows, int cols)
	{
		_rows = rows;
		_cols = cols;
		CellSize = cellSize;
		double x = upperLeftCorner.X;
		double y = upperLeftCorner.Y;
		double maxX = x + (double)cols * cellSize;
		double minY = y - (double)rows * cellSize;
		Extent = new Extent(x, maxX, minY, y);
		Initialize();
	}

	private RasterDefinition(Extent ext, double cs, int rows, int cols)
	{
		Extent = ext;
		CellSize = cs;
		_rows = rows;
		_cols = cols;
		Initialize();
	}

	public void Initialize()
	{
		if (_logAllocations)
		{
			if (_tracker == null)
			{
				_tracker = new AllocationTracker();
			}
			_tracker.LogAllocation();
		}
	}

	public int Row(in double y)
	{
		double maxYExtentTerm = Math.Floor((Extent.MaxY - y) / CellSize);
		if (maxYExtentTerm >= 2147483647.0)
		{
			return int.MaxValue;
		}
		if (maxYExtentTerm <= -2147483648.0)
		{
			return int.MinValue;
		}
		return (int)maxYExtentTerm;
	}

	public int Col(in double x)
	{
		double minXExtentTerm = Math.Floor((x - Extent.MinX) / CellSize);
		if (minXExtentTerm >= 2147483647.0)
		{
			return int.MaxValue;
		}
		if (minXExtentTerm <= -2147483648.0)
		{
			return int.MinValue;
		}
		return (int)minXExtentTerm;
	}

	public double FractionalRow(double y)
	{
		return (MaxY - y) / CellSize;
	}

	public double FractionalCol(double x)
	{
		return (x - MinX) / CellSize;
	}

	public int CellIndex(Point pt)
	{
		return CellIndex(Row(in pt.Y), Col(in pt.X));
	}

	public int CellIndex(Pixel rowcol)
	{
		return CellIndex(rowcol.Row, rowcol.Column);
	}

	public int CellIndex(int row, int col)
	{
		return col + row * _cols;
	}

	public long CellIndexLong(int row, int col)
	{
		return col + (long)row * (long)_cols;
	}

	public int CellIndex(double x, double y)
	{
		return CellIndex(Row(in y), Col(in x));
	}

	public static int Compute1DIndex(int nCols, int row, int col)
	{
		return col + row * nCols;
	}

	public static void Compute2DIndex(int nCols, int idx1D, out int row, out int col)
	{
		row = (int)Math.Floor((double)idx1D / (double)nCols);
		col = idx1D % nCols;
	}

	public int CellIndexBounded(Point pt)
	{
		Pixel px = CellIndex2D(pt);
		if (InBounds(px))
		{
			return px.To1D(Cols);
		}
		return -1;
	}

	public int CellIndexBounded_FavorInBounds(Point pt)
	{
		int row = Row(in pt.Y);
		int col = Col(in pt.X);
		if (row == -1)
		{
			row = Row(pt.Y - RoughlyEpsilon);
		}
		else if (row == Rows)
		{
			row = Row(pt.Y + RoughlyEpsilon);
		}
		if (col == -1)
		{
			col = Col(pt.X + RoughlyEpsilon);
		}
		else if (col == Cols)
		{
			col = Col(pt.X - RoughlyEpsilon);
		}
		Pixel px = new Pixel(row, col);
		if (InBounds(px))
		{
			return px.To1D(Cols);
		}
		return -1;
	}

	public void CellIndex2D(int idx1D, out int r, out int c)
	{
		r = (int)Math.Floor((double)idx1D / (double)Cols);
		c = idx1D % Cols;
	}

	public int BoundedRow(int r)
	{
		return Math.Max(Math.Min(r, Rows - 1), 0);
	}

	public int BoundedColumn(int c)
	{
		return Math.Max(Math.Min(c, Cols - 1), 0);
	}

	public void CellIndex2D(long idx1D, out int r, out int c)
	{
		if (Cols == 0)
		{
			throw new InvalidOperationException("RasterDefinition cannot identify CellIndex2D because there Cols is 0.");
		}
		r = (int)Math.Floor((double)idx1D / (double)Cols);
		c = (int)(idx1D % Cols);
	}

	public void CellIndex2D(Point pt, out int r, out int c)
	{
		r = Row(in pt.Y);
		c = Col(in pt.X);
	}

	public Pixel CellIndex2D(int idx1D)
	{
		CellIndex2D(idx1D, out var r, out var c);
		return new Pixel(r, c);
	}

	public Pixel CellIndex2D(long idx1D)
	{
		CellIndex2D(idx1D, out var r, out var c);
		return new Pixel(r, c);
	}

	public Pixel CellIndex2D(Point pt)
	{
		return new Pixel(Row(in pt.Y), Col(in pt.X));
	}

	public bool InBounds(Pixel px)
	{
		if (px.Row >= 0 && px.Row < Rows && px.Column >= 0)
		{
			return px.Column < Cols;
		}
		return false;
	}

	public Pixel TransformIndexingToSubSpace(PixelRange pxRange, Pixel globalSpacePixel)
	{
		return new Pixel(globalSpacePixel.Row - pxRange.Top, globalSpacePixel.Column - pxRange.Left);
	}

	public Pixel TransformIndexingToGlobalSpace(PixelRange pxRange, Pixel subspacePixel)
	{
		return new Pixel(subspacePixel.Row + pxRange.Top, subspacePixel.Column + pxRange.Left);
	}

	public bool InRange(Pixel i2d)
	{
		return InRange(i2d.Row, i2d.Column);
	}

	public bool InRange(int row, int col)
	{
		if (row >= 0 && row < _rows && col >= 0 && col < _cols)
		{
			return true;
		}
		return false;
	}

	public void ColAndFraction(double x, out int c, out double frac)
	{
		c = Col(in x);
		frac = (x - CellMinX(c)) / CellSize;
		if (c < 0)
		{
			c = 0;
			frac = 0.0;
		}
		else if (c >= _cols)
		{
			c = _cols - 1;
			frac = 1.0;
		}
	}

	public void RowAndFraction(double y, out int r, ref double frac)
	{
		r = Row(in y);
		frac = (CellMaxY(r) - y) / CellSize;
		if (r < 0)
		{
			r = 0;
			frac = 0.0;
		}
		else if (r >= _rows)
		{
			r = _rows - 1;
			frac = 1.0;
		}
	}

	public bool IsRegistered(RasterDefinition otherRaster, double cellSizeAbsoluteTol = 1E-05, double pixelShiftTol = 0.1, double cellSizeRatioTol = 0.001)
	{
		if (otherRaster.IsNull())
		{
			throw new ArgumentNullException("otherRaster");
		}
		double absoluteCellSizeDifference = Math.Abs(CellSize - otherRaster.CellSize);
		if (absoluteCellSizeDifference > cellSizeAbsoluteTol)
		{
			return false;
		}
		if (absoluteCellSizeDifference / CellSize > cellSizeRatioTol)
		{
			return false;
		}
		if (Extent.UpperLeft == otherRaster.Extent.UpperLeft)
		{
			return true;
		}
		double upperLeftExtentTerm = (Extent.UpperLeft.X - otherRaster.Extent.UpperLeft.X) / CellSize;
		if (Math.Abs(Math.Round(upperLeftExtentTerm) - upperLeftExtentTerm) > pixelShiftTol)
		{
			return false;
		}
		double upperLeftExtentTerm2 = (Extent.UpperLeft.Y - otherRaster.Extent.UpperLeft.Y) / CellSize;
		if (Math.Abs(Math.Round(upperLeftExtentTerm2) - upperLeftExtentTerm2) > pixelShiftTol)
		{
			return false;
		}
		return true;
	}

	public static List<List<int>> GetRegisteredCollections(IList<RasterDefinition> rasters, double cellSizeTol = 1E-05, double pixelShiftTol = 0.1)
	{
		List<List<int>> registeredCollections = new List<List<int>>();
		for (int i = 0; i < rasters.Count; i++)
		{
			RasterDefinition otherRaster = rasters[i];
			bool condition = false;
			foreach (List<int> registeredCollection in registeredCollections)
			{
				if (rasters[registeredCollection[0]].IsRegistered(otherRaster, cellSizeTol, pixelShiftTol))
				{
					registeredCollection.Add(i);
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				int unity = 1;
				List<int> intList = new List<int>(unity);
				CollectionsMarshal.SetCount(intList, unity);
				Span<int> intListSpan = CollectionsMarshal.AsSpan(intList);
				int index = 0;
				intListSpan[index] = i;
				registeredCollections.Add(intList);
			}
		}
		return registeredCollections;
	}

	public static RasterDefinition ComputeRegisteredRaster(Extent extent, Point anchor, double cellSize)
	{
		double minXExtentTerm = extent.MinX - anchor.X;
		double maxXExtentTerm = extent.MaxX - anchor.X;
		double minYExtentTerm = extent.MinY - anchor.Y;
		double maxYExtentTerm = extent.MaxY - anchor.Y;
		int cellSizeTerm = (int)Math.Floor(minXExtentTerm / cellSize);
		int cellSizeTerm2 = (int)Math.Ceiling(maxXExtentTerm / cellSize);
		int cellSizeTerm3 = (int)Math.Floor((0.0 - maxYExtentTerm) / cellSize);
		int cellSizeTerm4 = (int)Math.Ceiling((0.0 - minYExtentTerm) / cellSize);
		double x = anchor.X + (double)cellSizeTerm * cellSize;
		double y = anchor.Y - (double)cellSizeTerm3 * cellSize;
		int cols = cellSizeTerm2 - cellSizeTerm;
		int rows = cellSizeTerm4 - cellSizeTerm3;
		return new RasterDefinition(new Point(x, y), cellSize, rows, cols);
	}

	public int GetZOrderIndex(int cellIdx)
	{
		if (cellIdx <= 1)
		{
			return cellIdx;
		}
		Pixel pixel = CellIndex2D(cellIdx);
		int x = pixel.X;
		int y = pixel.Y;
		return (x & 1) | ((x & 2) << 1) | ((x & 4) << 2) | ((x & 8) << 3) | ((x & 0x10) << 4) | ((x & 0x20) << 5) | ((x & 0x40) << 6) | ((x & 0x80) << 7) | ((x & 0x100) << 8) | ((x & 0x200) << 9) | ((x & 0x400) << 10) | ((x & 0x800) << 11) | ((x & 0x1000) << 12) | ((x & 0x2000) << 13) | ((x & 0x4000) << 14) | ((x & 0x8000) << 15) | ((y & 1) << 1) | ((y & 2) << 2) | ((y & 4) << 3) | ((y & 8) << 4) | ((y & 0x10) << 5) | ((y & 0x20) << 6) | ((y & 0x40) << 7) | ((y & 0x80) << 8) | ((y & 0x100) << 9) | ((y & 0x200) << 10) | ((y & 0x400) << 11) | ((y & 0x800) << 12) | ((y & 0x1000) << 13) | ((y & 0x2000) << 14) | ((y & 0x4000) << 15) | ((y & 0x8000) << 16);
	}

	public int InvertZOrderIndex(int z)
	{
		int ordinal = 0;
		int ordinal2 = 0;
		for (int i = 0; i < 16; i++)
		{
			if ((z & (1 << i * 2)) > 0)
			{
				ordinal |= 1 << i;
			}
			if ((z & (1 << i * 2 + 1)) > 0)
			{
				ordinal2 |= 1 << i;
			}
		}
		return CellIndex(ordinal2, ordinal);
	}

	private static bool IsBase2(int ordinal)
	{
		return (ordinal & (ordinal - 1)) == 0;
	}

	public bool Contains(IPoint pt)
	{
		return Extent.Contains(pt.X, pt.Y);
	}

	public bool Contains(Point pt)
	{
		return Extent.Contains(pt.X, pt.Y);
	}

	public bool ContainsConfidently(Point pt)
	{
		return Extent.ContainsConfidently(pt.X, pt.Y);
	}

	public bool Contains(double x, double y)
	{
		return Extent.Contains(x, y);
	}

	public bool ContainsAll(params Point[] pts)
	{
		for (int i = 0; i < pts.Length; i++)
		{
			if (!Contains(pts[i]))
			{
				return false;
			}
		}
		return true;
	}

	public bool ContainsAny(params Point[] pts)
	{
		for (int i = 0; i < pts.Length; i++)
		{
			if (Contains(pts[i]))
			{
				return true;
			}
		}
		return false;
	}

	public bool ContainsExtent(IExtent extent)
	{
		Extent extent2 = extent.Extent;
		if (extent2.MinX < MinX || extent2.MaxX > MaxX || extent2.MinY < MinY || extent2.MaxY > MaxY)
		{
			return false;
		}
		return true;
	}

	public bool ContainsExtent(double minX, double maxX, double minY, double maxY)
	{
		if (minX < MinX || maxX > MaxX || minY < MinY || maxY > MaxY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps(Polygon pgon)
	{
		return pgon.AreaIntersects(Extent);
	}

	public bool Overlaps(RasterDefinition raster)
	{
		if (raster.IsNull())
		{
			return false;
		}
		if (MaxX < raster.MinX)
		{
			return false;
		}
		if (MinX > raster.MaxX)
		{
			return false;
		}
		if (MinY > raster.MaxY)
		{
			return false;
		}
		if (MaxY < raster.MinY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps(Extent ext)
	{
		return Overlaps(ext.MinX, ext.MaxX, ext.MinY, ext.MaxY);
	}

	public bool Overlaps(double minX, double maxX, double minY, double maxY)
	{
		if (MaxX < minX)
		{
			return false;
		}
		if (MinX > maxX)
		{
			return false;
		}
		if (MinY > maxY)
		{
			return false;
		}
		if (MaxY < minY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps(Point pt)
	{
		if (MaxX < pt.X)
		{
			return false;
		}
		if (MinX > pt.X)
		{
			return false;
		}
		if (MinY > pt.Y)
		{
			return false;
		}
		if (MaxY < pt.Y)
		{
			return false;
		}
		return true;
	}

	public PixelRange GetUnboundedPixelRange(Extent ext)
	{
		return new PixelRange(Row(in ext.MaxY), Row(in ext.MinY), Col(in ext.MinX), Col(in ext.MaxX));
	}

	public PixelRange GetPixelRange(double minX, double maxX, double minY, double maxY, PixelRangeOptions rangeOptions = PixelRangeOptions.PartialPixelCovered)
	{
		return GetPixelRange(new Extent(minX, maxX, minY, maxY), rangeOptions);
	}

	public PixelRange GetPixelRange(Extent ext, PixelRangeOptions rangeOptions = PixelRangeOptions.PartialPixelCovered)
	{
		if (!RowsInRange(ext.MinY, ext.MaxY, out var minRow, out var maxRow, rangeOptions))
		{
			return PixelRange.NullRange;
		}
		if (!ColsInRange(ext.MinX, ext.MaxX, out var minCol, out var maxCol, rangeOptions))
		{
			return PixelRange.NullRange;
		}
		return new PixelRange(minRow, maxRow, minCol, maxCol);
	}

	public bool InRange(PixelRange pr)
	{
		if (pr.MinRow >= 0 && pr.MinCol >= 0 && pr.MaxRow < Rows)
		{
			return pr.MaxCol < Cols;
		}
		return false;
	}

	private static int Round_HalfGoesLower(double computed)
	{
		if (Math.Abs(computed - Math.Truncate(computed)) == 0.5)
		{
			return (int)Math.Floor(computed);
		}
		return (int)Math.Round(computed);
	}

	private static int Round_HalfGoesHigher(double computed)
	{
		if (Math.Abs(computed - Math.Truncate(computed)) == 0.5)
		{
			return (int)Math.Ceiling(computed);
		}
		return (int)Math.Round(computed);
	}

	public bool RowsInRange(double minY, double maxY, out int minRow, out int maxRow, PixelRangeOptions rangeOptions = PixelRangeOptions.PartialPixelCovered)
	{
		if (minY >= MaxY || maxY <= MinY)
		{
			minRow = -1;
			maxRow = -1;
			return false;
		}
		switch (rangeOptions)
		{
		case PixelRangeOptions.PartialPixelCovered:
		{
			minRow = Row(in maxY);
			maxRow = Row(in minY);
			double fractionalRow = FractionalRow(minY);
			if (maxRow > minRow && fractionalRow - Math.Floor(fractionalRow) < RoughlyEpsilon)
			{
				maxRow--;
			}
			break;
		}
		case PixelRangeOptions.CenterPixelCovered:
			minRow = Round_HalfGoesLower(FractionalRow(maxY));
			maxRow = Round_HalfGoesLower(FractionalRow(minY)) - 1;
			if (maxRow < minRow || minRow >= Rows || maxRow < 0)
			{
				minRow = -1;
				maxRow = -1;
				return false;
			}
			break;
		case PixelRangeOptions.CompletePixelCovered:
			minRow = (int)Math.Ceiling(FractionalRow(maxY));
			maxRow = (int)Math.Floor(FractionalRow(minY)) - 1;
			if (maxRow < minRow)
			{
				minRow = -1;
				maxRow = -1;
				return false;
			}
			break;
		default:
			throw new NotImplementedException();
		}
		if (minRow < 0)
		{
			minRow = 0;
		}
		if (maxRow > _rows - 1)
		{
			maxRow = _rows - 1;
			if (minRow > maxRow)
			{
				minRow = maxRow;
			}
		}
		return true;
	}

	public bool ColsInRange(double minX, double maxX, out int minCol, out int maxCol, PixelRangeOptions rangeOptions = PixelRangeOptions.PartialPixelCovered)
	{
		if (minX >= MaxX || maxX <= MinX)
		{
			minCol = -1;
			maxCol = -1;
			return false;
		}
		switch (rangeOptions)
		{
		case PixelRangeOptions.PartialPixelCovered:
		{
			minCol = Col(in minX);
			maxCol = Col(in maxX);
			double fractionalCol = FractionalCol(maxX);
			if (maxCol > minCol && fractionalCol - Math.Floor(fractionalCol) < RoughlyEpsilon)
			{
				maxCol--;
			}
			break;
		}
		case PixelRangeOptions.CenterPixelCovered:
			minCol = Round_HalfGoesLower(FractionalCol(minX));
			maxCol = Round_HalfGoesLower(FractionalCol(maxX)) - 1;
			if (maxCol < minCol || minCol >= Cols || maxCol < 0)
			{
				minCol = -1;
				maxCol = -1;
				return false;
			}
			break;
		case PixelRangeOptions.CompletePixelCovered:
			minCol = (int)Math.Ceiling(FractionalCol(minX));
			maxCol = (int)Math.Floor(FractionalCol(maxX)) - 1;
			if (maxCol < minCol)
			{
				minCol = -1;
				maxCol = -1;
				return false;
			}
			break;
		default:
			throw new NotImplementedException();
		}
		if (minCol < 0)
		{
			minCol = 0;
		}
		if (maxCol > _cols - 1)
		{
			maxCol = _cols - 1;
		}
		return true;
	}

	public BlockSize FullBlockSize()
	{
		return new BlockSize(Rows, Cols);
	}

	public PixelRange GetBlockPixelRange(BlockSize bs, int blockIndex)
	{
		GetBlockDimensions(bs, out var blocksWide, out var blocksTall);
		int blockIndexWithBlocksWide = blockIndex % blocksWide;
		int blockIndexOverBlocksWide = blockIndex / blocksWide;
		if (blockIndex < 0 || blockIndexOverBlocksWide >= blocksTall)
		{
			throw new ArgumentOutOfRangeException("blockIndex");
		}
		int left = blockIndexWithBlocksWide * bs.Width;
		int top = blockIndexOverBlocksWide * bs.Height;
		return PixelRange.FromRectangle(left, top, bs.Width, bs.Height);
	}

	public RasterDefinition GetBlockRaster(BlockSize bs, int blockIndex)
	{
		return GetRegisteredRaster(GetBlockPixelRange(bs, blockIndex));
	}

	public bool InRange(BlockSize bs, int blockIndex)
	{
		GetBlockDimensions(bs, out var blocksWide, out var blocksTall);
		if (blockIndex >= 0)
		{
			return blockIndex < blocksWide * blocksTall;
		}
		return false;
	}

	public void GetBlockDimensions(BlockSize bs, out int blocksWide, out int blocksTall)
	{
		blocksWide = GetBlocksWide(bs);
		blocksTall = GetBlocksTall(bs);
	}

	public int GetBlocksWide(BlockSize bs)
	{
		int blocksWide = Cols / bs.Width;
		if (Cols % bs.Width > 0)
		{
			blocksWide++;
		}
		return blocksWide;
	}

	public int GetBlocksTall(BlockSize bs)
	{
		int blocksTall = Rows / bs.Height;
		if (Rows % bs.Height > 0)
		{
			blocksTall++;
		}
		return blocksTall;
	}

	public int GetBlockIndex(BlockSize bs, int pxIdx)
	{
		return GetBlockIndex(bs, CellIndex2D(pxIdx));
	}

	public int GetBlockIndex(BlockSize bs, Pixel px)
	{
		if (!InBounds(px))
		{
			throw new ArgumentOutOfRangeException("px");
		}
		GetBlockDimensions(bs, out var blocksWide, out var _);
		int pxWidthTerm = px.X / bs.Width;
		return px.Y / bs.Height * blocksWide + pxWidthTerm;
	}

	public Pixel GetBlockIndex2D(BlockSize bs, Pixel px)
	{
		if (!InBounds(px))
		{
			throw new ArgumentOutOfRangeException("px");
		}
		GetBlockDimensions(bs, out var _, out var _);
		int c = px.X / bs.Width;
		return new Pixel(px.Y / bs.Height, c);
	}

	public PixelRange GetBlockRange(BlockSize bs, PixelRange pr)
	{
		Pixel blockIndex2D = GetBlockIndex2D(bs, pr.UpperLeft);
		Pixel blockIndex2D2 = GetBlockIndex2D(bs, pr.LowerRight);
		return new PixelRange(blockIndex2D, blockIndex2D2);
	}

	public Dictionary<int, List<int>> GetBlockPointMap(BlockSize bs, Memory<Point> points)
	{
		Dictionary<int, List<int>> blockPointMap = new Dictionary<int, List<int>>();
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point pt = pointsSpan[i];
			int pxIdx = CellIndexBounded(pt);
			int blockIndex = GetBlockIndex(bs, pxIdx);
			if (blockPointMap.TryGetValue(blockIndex, out var blockPointMap2))
			{
				blockPointMap2.Add(i);
				continue;
			}
			blockPointMap2 = new List<int>();
			blockPointMap2.Add(i);
			blockPointMap[blockIndex] = blockPointMap2;
		}
		return blockPointMap;
	}

	public Extent RangeExtent(PixelRange range)
	{
		if (range.IsNull())
		{
			return Extent.NullExtent;
		}
		return new Extent(CellMinX(range.MinCol), CellMaxX(range.MaxCol), CellMinY(range.MaxRow), CellMaxY(range.MinRow));
	}

	public PixelRange FullRange()
	{
		return new PixelRange(0, Rows - 1, 0, Cols - 1);
	}

	public bool IsValidRange(PixelRange pr)
	{
		if (!pr.IsNull() && pr.Left >= 0 && pr.Right < Cols && pr.Top >= 0)
		{
			return pr.Bottom < Rows;
		}
		return false;
	}

	public bool EqualXY(Extent extent)
	{
		if (MaxX == extent.MaxX && MinX == extent.MinX && MaxY == extent.MaxY && MinY == extent.MinY)
		{
			return true;
		}
		return false;
	}

	public bool EqualRasterDefinition(RasterDefinition rast)
	{
		if (_rows != rast.Rows || _cols != rast.Cols)
		{
			return false;
		}
		if (MaxX != rast.MaxX || MinX != rast.MinX || MaxY != rast.MaxY || MinY != rast.MinY)
		{
			return false;
		}
		return true;
	}

	public double CellMinX(int col)
	{
		return MinX + CellSize * (double)col;
	}

	public double CellCenterX(int col)
	{
		return MinX + CellSize * ((double)col + 0.5);
	}

	public double CellMaxX(int col)
	{
		return MinX + CellSize * (double)(col + 1);
	}

	public double CellMinY(int row)
	{
		return MaxY - CellSize * (double)(row + 1);
	}

	public double CellCenterY(int row)
	{
		return MaxY - CellSize * ((double)row + 0.5);
	}

	public double CellMaxY(int row)
	{
		return MaxY - CellSize * (double)row;
	}

	public Point CellCenter(int cellIndex)
	{
		CellIndex2D(cellIndex, out var r, out var c);
		return CellCenter(r, c);
	}

	public Point CellCenter(Pixel px)
	{
		return CellCenter(px.Row, px.Column);
	}

	public Point CellCenter(int row, int col)
	{
		return new Point(CellCenterX(col), CellCenterY(row));
	}

	public Extent CellExtent(Pixel px)
	{
		return CellExtent(px.Row, px.Column);
	}

	public Extent CellExtent(int cellIdx)
	{
		return CellExtent(cellIdx / _cols, cellIdx % _cols);
	}

	public Extent CellExtent(long cellIdx)
	{
		return CellExtent((int)(cellIdx / _cols), (int)(cellIdx % _cols));
	}

	public Extent CellExtent(int row, int col)
	{
		return new Extent(CellMinX(col), CellMaxX(col), CellMinY(row), CellMaxY(row));
	}

	public Extent CellCenterExtent()
	{
		return new Extent(CellCenter(0), Width - CellSize, Height - CellSize);
	}

	public RasterDefinition Clone()
	{
		return new RasterDefinition(new Point(MinX, MaxY), CellSize, Rows, Cols);
	}

	public RasterDefinition Translate(double dx, double dy)
	{
		return Translate(new Geospatial.Vectors.Vector(dx, dy));
	}

	public RasterDefinition Translate(Geospatial.Vectors.Vector vec)
	{
		return new RasterDefinition(Extent.UpperLeft.Translate(vec), CellSize, Rows, Cols);
	}

	public RasterDefinition GetCellRasterDefinition(int cellIdx, int cellsPerSide)
	{
		return new RasterDefinition(CellExtent(cellIdx), cellsPerSide, cellsPerSide);
	}

	public RasterDefinition GetCellRasterDefinition(long cellIdx, int cellsPerSide)
	{
		return new RasterDefinition(CellExtent(cellIdx), cellsPerSide, cellsPerSide);
	}

	public RasterDefinition GetCellRasterDefinition(int row, int col, int cellsPerSide)
	{
		return new RasterDefinition(CellExtent(row, col), cellsPerSide, cellsPerSide);
	}

	public RasterDefinition GetCellRasterDefinition(Pixel px, int cellsPerSide)
	{
		return GetCellRasterDefinition(px.Row, px.Column, cellsPerSide);
	}

	public RasterDefinition ExtendRaster(int radiusCells)
	{
		return ExtendRaster(radiusCells, radiusCells, radiusCells, radiusCells);
	}

	public RasterDefinition ExtendRaster(int addLeft, int addRight, int addTop, int addBottom)
	{
		return new RasterDefinition(new Extent(MinX - (double)addLeft * CellSize, MaxX + (double)addRight * CellSize, MinY - (double)addBottom * CellSize, MaxY + (double)addTop * CellSize), _rows + addTop + addBottom, _cols + addRight + addLeft);
	}

	public RasterDefinition Subsample(int newCellsPerOldCell)
	{
		if (newCellsPerOldCell <= 0)
		{
			throw new ArgumentOutOfRangeException("newCellsPerOldCell");
		}
		if (newCellsPerOldCell == 1)
		{
			return Clone();
		}
		return new RasterDefinition(Extent, Rows * newCellsPerOldCell, Cols * newCellsPerOldCell);
	}

	public static RasterDefinition ComputeBoundingRaster(IExtent iext, int maxCellCount)
	{
		return ComputeBoundingRaster(iext.Extent, maxCellCount);
	}

	public static RasterDefinition ComputeBoundingRaster(Extent targetExtent, int maxCellCount)
	{
		return ComputeBoundingRaster(targetExtent, (long)maxCellCount);
	}

	public static RasterDefinition ComputeBoundingRaster(Extent targetExtent, long maxCellCount)
	{
		if (targetExtent.IsNull())
		{
			return NullRaster;
		}
		double xComponent = targetExtent.MinX;
		double maxX = targetExtent.MaxX;
		double minY = targetExtent.MinY;
		double yComponent = targetExtent.MaxY;
		if (maxX == xComponent && yComponent == minY)
		{
			maxCellCount = 1L;
		}
		if (maxX == xComponent)
		{
			if (maxX == 0.0)
			{
				maxX += 0.01;
				xComponent -= 0.01;
			}
			else if (maxX > 0.0)
			{
				maxX *= 1.01;
				xComponent *= 0.99;
			}
			else
			{
				maxX *= 0.99;
				xComponent *= 1.01;
			}
		}
		if (yComponent == minY)
		{
			if (yComponent == 0.0)
			{
				yComponent += 0.01;
				minY -= 0.01;
			}
			else if (yComponent > 0.0)
			{
				yComponent *= 1.01;
				minY *= 0.99;
			}
			else
			{
				yComponent *= 0.99;
				minY *= 1.01;
			}
		}
		double maxXAboveXComponent = maxX - xComponent;
		double yComponentAboveMinY = yComponent - minY;
		double maxXAboveXComponentOverYComponentAboveMinY = maxXAboveXComponent / yComponentAboveMinY;
		int unity;
		int unity2;
		if (maxCellCount <= 1)
		{
			unity = 1;
			unity2 = 1;
			maxCellCount = 1L;
		}
		else if (yComponentAboveMinY == 0.0)
		{
			unity = 1;
			unity2 = (int)maxCellCount;
		}
		else if (maxXAboveXComponent == 0.0)
		{
			unity = (int)maxCellCount;
			unity2 = 1;
		}
		else
		{
			double maxCellCountTerm = Math.Sqrt((double)maxCellCount / maxXAboveXComponentOverYComponentAboveMinY);
			unity2 = (int)Math.Floor(maxCellCountTerm * maxXAboveXComponentOverYComponentAboveMinY);
			unity = (int)Math.Floor(maxCellCountTerm);
			if (unity == 0)
			{
				unity = 1;
				unity2 = (int)maxCellCount;
			}
			else if (unity2 == 0)
			{
				unity = (int)maxCellCount;
				unity2 = 1;
			}
		}
		double computed = Math.Max(yComponentAboveMinY / (double)unity, maxXAboveXComponent / (double)unity2) * 1.01;
		double xComponentTerm = xComponent + 0.5 * maxXAboveXComponent;
		double minYTerm = minY + 0.5 * yComponentAboveMinY;
		double computed2 = 0.5 * (double)unity2 * computed;
		double computed3 = 0.5 * (double)unity * computed;
		xComponent = xComponentTerm - computed2;
		yComponent = minYTerm + computed3;
		return new RasterDefinition(new Point(xComponent, yComponent), computed, unity, unity2);
	}

	public RasterDefinition GetRegisteredRaster(Extent ext)
	{
		return GetRegisteredRaster(ext.UpperLeft, ext.LowerRight);
	}

	public RasterDefinition GetRegisteredRaster(Point newUL, Point newLR)
	{
		double a = MinX - newUL.X;
		double a2 = MaxY - newUL.Y;
		double a3 = newLR.X - MaxX;
		double a4 = newLR.Y - MinY;
		return ExtendRaster((int)Math.Ceiling(a), (int)Math.Ceiling(a3), (int)Math.Ceiling(a2), (int)Math.Ceiling(a4));
	}

	public RasterDefinition GetRegisteredRaster(PixelRange pxRange)
	{
		if (pxRange.IsNull())
		{
			return NullRaster;
		}
		double x = CellMinX(pxRange.MinCol);
		double y = CellMaxY(pxRange.MinRow);
		return new RasterDefinition(new Point(x, y), CellSize, pxRange.RowCount, pxRange.ColCount);
	}

	public RasterDefinition GetSubRaster(PixelRange pxRange, bool clipToBounds = true)
	{
		if (pxRange.IsNull())
		{
			return NullRaster;
		}
		if (clipToBounds)
		{
			pxRange = pxRange.ClipTo(this);
			if (pxRange.IsNull())
			{
				return NullRaster;
			}
		}
		double minX = CellMinX(pxRange.MinCol);
		double maxX = CellMaxX(pxRange.MaxCol);
		double minY = CellMinY(pxRange.MaxRow);
		double maxY = CellMaxY(pxRange.MinRow);
		return new RasterDefinition(new Extent(minX, maxX, minY, maxY), pxRange.RowCount, pxRange.ColCount);
	}

	public RasterDefinition GetSubRaster(Extent ext, PixelRangeOptions opt = PixelRangeOptions.PartialPixelCovered)
	{
		PixelRange pixelRange = GetPixelRange(ext, opt);
		if (pixelRange.IsNull())
		{
			return NullRaster;
		}
		return GetSubRaster(pixelRange);
	}

	public List<RasterDefinition> SplitIntoTiles(int tilePixelCount, bool clipToBounds)
	{
		int count = (int)Math.Ceiling((double)Cols / (double)tilePixelCount);
		int count2 = (int)Math.Ceiling((double)Rows / (double)tilePixelCount);
		List<RasterDefinition> rasterDefinitions = new List<RasterDefinition>();
		for (int i = 0; i < count2; i++)
		{
			int top = i * tilePixelCount;
			for (int j = 0; j < count; j++)
			{
				int left = j * tilePixelCount;
				rasterDefinitions.Add(GetSubRaster(PixelRange.FromRectangle(left, top, tilePixelCount, tilePixelCount), clipToBounds));
			}
		}
		return rasterDefinitions;
	}

	public List<RasterDefinition> DefaultOverviews(OverviewBehavior behavior = OverviewBehavior.ModifyExtentAlwaysGrow, OverviewAnchorPoint anchor = OverviewAnchorPoint.TopLeft)
	{
		List<RasterDefinition> rasterDefinitions = new List<RasterDefinition>();
		if (Rows <= 256 && Cols <= 256)
		{
			return rasterDefinitions;
		}
		RasterDefinition rasterDefinition = this;
		int scaledRows;
		int scaledCols;
		do
		{
			scaledRows = rasterDefinition.Rows / 2;
			scaledCols = rasterDefinition.Cols / 2;
			int rowsRasterDefinitionTerm = rasterDefinition.Rows % 2;
			int colsRasterDefinitionTerm = rasterDefinition.Cols % 2;
			double scaledCellSize = 0.0;
			Point upperLeft = Extent.UpperLeft;
			if (anchor != OverviewAnchorPoint.TopLeft)
			{
				throw new NotImplementedException();
			}
			switch (behavior)
			{
			case OverviewBehavior.ModifyCellsize:
				scaledRows += rowsRasterDefinitionTerm;
				scaledCols += colsRasterDefinitionTerm;
				throw new NotImplementedException();
			case OverviewBehavior.ModifyExtent:
				scaledCellSize = rasterDefinition.CellSize * 2.0;
				if (rowsRasterDefinitionTerm == 1)
				{
					double computed = (double)scaledRows * scaledCellSize;
					double computed2 = (double)(scaledRows + 1) * scaledCellSize;
					double heightTerm = Math.Abs(computed - Height);
					if (Math.Abs(computed2 - Height) < heightTerm)
					{
						scaledRows++;
					}
				}
				if (colsRasterDefinitionTerm == 1)
				{
					double computed3 = (double)scaledCols * scaledCellSize;
					double computed4 = (double)(scaledCols + 1) * scaledCellSize;
					double widthTerm = Math.Abs(computed3 - Width);
					if (Math.Abs(computed4 - Width) < widthTerm)
					{
						scaledCols++;
					}
				}
				break;
			case OverviewBehavior.ModifyExtentAlwaysGrow:
				scaledRows += rowsRasterDefinitionTerm;
				scaledCols += colsRasterDefinitionTerm;
				scaledCellSize = rasterDefinition.CellSize * 2.0;
				break;
			case OverviewBehavior.ModifyExtentAlwaysShrink:
				scaledCellSize = rasterDefinition.CellSize * 2.0;
				break;
			}
			rasterDefinition = new RasterDefinition(upperLeft, scaledCellSize, scaledRows, scaledCols);
			rasterDefinitions.Add(rasterDefinition);
		}
		while (scaledRows > 256 || scaledCols > 256);
		return rasterDefinitions;
	}

	public RasterDefinition TiledRaster(int tilePixelCount)
	{
		int cols = (int)Math.Ceiling((double)Cols / (double)tilePixelCount);
		int rows = (int)Math.Ceiling((double)Rows / (double)tilePixelCount);
		double cellSize = CellSize * (double)tilePixelCount;
		return new RasterDefinition(Extent.UpperLeft, cellSize, rows, cols);
	}

	public RasterDefinition Transform(ISpatialTransformer transformer)
	{
		Extent targetExtent = Extent.Transform(transformer);
		if (!targetExtent.IsFiniteAndValid())
		{
			return NullRaster;
		}
		return ComputeBoundingRaster(targetExtent, CellCount);
	}

	public bool TryTransform(ISpatialTransformer transformer, out RasterDefinition res)
	{
		try
		{
			Extent targetExtent = Extent.Transform(transformer);
			if (targetExtent.IsInfiniteOrInvalid())
			{
				res = NullRaster;
				return false;
			}
			res = ComputeBoundingRaster(targetExtent, CellCountLong);
			return true;
		}
		catch
		{
			res = NullRaster;
			return false;
		}
	}

	public List<Polygon> GetCellPolygons()
	{
		List<Polygon> polygons = new List<Polygon>(CellCount);
		for (int i = 0; i < CellCount; i++)
		{
			polygons.Add(Polygon.FromExtent(CellExtent(i)));
		}
		return polygons;
	}

	public JaggedWeights<T> GetBilinearInterpolationWeights<T>(Memory<Point> points) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		Span<Point> pointsSpan = points.Span;
		int length = pointsSpan.Length;
		int capacity = length * 4;
		List<int> intList = new List<int>(capacity);
		List<T> items = new List<T>(capacity);
		int[] intValues = new int[length];
		int[] intValuesArray = new int[length];
		Span<int> indexes = stackalloc int[4];
		Span<T> weights = stackalloc T[4];
		int nWeightsSum = 0;
		for (int i = 0; i < length; i++)
		{
			GetBilinearWeights(pointsSpan[i], indexes, weights, out var nWeights);
			intList.AddRange(indexes.Slice(0, nWeights));
			items.AddRange(weights.Slice(0, nWeights));
			intValues[i] = nWeightsSum;
			intValuesArray[i] = nWeights;
			nWeightsSum += nWeights;
		}
		return new JaggedWeights<T>(intValues, intValuesArray, UnsafeHelpers.GetListBackingBuffer(intList), UnsafeHelpers.GetListBackingBuffer(items));
	}

	public JaggedWeights<T> GetNearestNeighborWeights<T>(Memory<Point> points) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		Span<Point> pointsSpan = points.Span;
		int length = pointsSpan.Length;
		List<int> intList = new List<int>(length);
		List<T> zeros = new List<T>(length);
		int[] intValues = new int[length];
		int[] intValuesArray = new int[length];
		for (int i = 0; i < length; i++)
		{
			int cellIndexBounded = CellIndexBounded(pointsSpan[i]);
			if (cellIndexBounded >= 0)
			{
				intList.Add(cellIndexBounded);
				zeros.Add(T.One);
			}
			else
			{
				intList.Add(-1);
				zeros.Add(T.Zero);
			}
			intValues[i] = i;
			intValuesArray[i] = 1;
		}
		return new JaggedWeights<T>(intValues, intValuesArray, UnsafeHelpers.GetListBackingBuffer(intList), UnsafeHelpers.GetListBackingBuffer(zeros));
	}

	public void GetBilinearWeights<T>(Point ptm, Span<int> indexes, Span<T> weights, out int nWeights) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		if (indexes.Length < 4 || weights.Length < 4)
		{
			throw new ArgumentException();
		}
		if (!Contains(ptm))
		{
			nWeights = 0;
			return;
		}
		if (CellCount == 1)
		{
			indexes[0] = 0;
			weights[0] = T.One;
			nWeights = 1;
			return;
		}
		double fractionalRow = FractionalRow(ptm.Y);
		double fractionalCol = FractionalCol(ptm.X);
		int fractionalRowTerm = (int)Math.Floor(fractionalRow);
		int fractionalColTerm = (int)Math.Floor(fractionalCol);
		double fractionalRowTerm2 = fractionalRow - (double)fractionalRowTerm;
		double fractionalColTerm2 = fractionalCol - (double)fractionalColTerm;
		int ordinal;
		int ordinal2;
		T saturating;
		if (fractionalRowTerm2 >= 0.5)
		{
			ordinal = fractionalRowTerm;
			ordinal2 = fractionalRowTerm + 1;
			saturating = T.CreateSaturating(1.0 - (fractionalRowTerm2 - 0.5));
		}
		else
		{
			ordinal = fractionalRowTerm - 1;
			ordinal2 = fractionalRowTerm;
			saturating = T.CreateSaturating(1.0 - (fractionalRowTerm2 + 0.5));
		}
		int ordinal3;
		int ordinal4;
		T saturating2;
		if (fractionalColTerm2 >= 0.5)
		{
			ordinal3 = fractionalColTerm;
			ordinal4 = fractionalColTerm + 1;
			saturating2 = T.CreateSaturating(fractionalColTerm2 - 0.5);
		}
		else
		{
			ordinal3 = fractionalColTerm - 1;
			ordinal4 = fractionalColTerm;
			saturating2 = T.CreateSaturating(fractionalColTerm2 + 0.5);
		}
		T oneAboveSaturating2 = T.One - saturating2;
		T oneAboveSaturating = T.One - saturating;
		T weights2 = oneAboveSaturating2 * oneAboveSaturating;
		T weights3 = oneAboveSaturating2 * saturating;
		T weights4 = saturating2 * oneAboveSaturating;
		T weights5 = saturating2 * saturating;
		nWeights = 0;
		if (ordinal >= 0 && ordinal < Rows)
		{
			if (ordinal3 >= 0 && ordinal3 < Cols)
			{
				int indexes2 = CellIndex(ordinal, ordinal3);
				indexes[nWeights] = indexes2;
				weights[nWeights] = weights3;
				nWeights++;
			}
			if (ordinal4 >= 0 && ordinal4 < Cols)
			{
				int indexes3 = CellIndex(ordinal, ordinal4);
				indexes[nWeights] = indexes3;
				weights[nWeights] = weights5;
				nWeights++;
			}
		}
		if (ordinal2 >= 0 && ordinal2 < Rows)
		{
			if (ordinal3 >= 0 && ordinal3 < Cols)
			{
				int indexes4 = CellIndex(ordinal2, ordinal3);
				indexes[nWeights] = indexes4;
				weights[nWeights] = weights2;
				nWeights++;
			}
			if (ordinal4 >= 0 && ordinal4 < Cols)
			{
				int indexes5 = CellIndex(ordinal2, ordinal4);
				indexes[nWeights] = indexes5;
				weights[nWeights] = weights4;
				nWeights++;
			}
		}
		if (nWeights != 0 && nWeights != 4)
		{
			T zero = T.Zero;
			for (int i = 0; i < nWeights; i++)
			{
				zero += weights[i];
			}
			for (int j = 0; j < nWeights; j++)
			{
				weights[j] /= zero;
			}
		}
	}

	public InterpolationData InterpolationWeights(double x, double y, InterpolationMode mode)
	{
		if (!Contains(x, y))
		{
			return default;
		}
		InterpolationData result = default;
		switch (mode)
		{
		case InterpolationMode.Nearest:
			result = new InterpolationData(CellIndex(x, y), 1.0);
			break;
		case InterpolationMode.Linear:
			throw new NotImplementedException();
		}
		return result;
	}

	public static double InterpolateMidsideValue(double length, double f0, double fp0, double f1, double fp1)
	{
		return (f0 + f1) * 0.5 + length * (fp0 - fp1) * 0.125;
	}

	public Point[] GetBorderPoints()
	{
		return Extent.GetBorderPoints();
	}

	public Matrix3x3D AsTransformationMatrix3x3D()
	{
		Matrix3x3D matrix3x3D = Matrix3x3D.Translation(0.0 - Extent.UpperLeft.X, 0.0 - Extent.UpperLeft.Y);
		return Matrix3x3D.Scale(1.0 / CellSize, 1.0 / CellSize) * matrix3x3D;
	}

	public bool IsEquivalentTo(RasterDefinition other)
	{
		if (other.IsNull())
		{
			return false;
		}
		if (Extent.UpperLeft == other.Extent.UpperLeft && CellSize == other.CellSize && Rows == other.Rows)
		{
			return Cols == other.Cols;
		}
		return false;
	}

	public bool Equals(RasterDefinition other)
	{
		if (Extent.Equals(other.Extent) && CellSize.Equals(other.CellSize) && _rows == other._rows)
		{
			return _cols == other._cols;
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj is RasterDefinition other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Extent, CellSize, _rows, _cols);
	}

	public bool IsCorrectSize<T>(T[] buf)
	{
		if (buf != null)
		{
			return buf.Length == CellCount;
		}
		return false;
	}

	public bool IsCorrectSize<T>(T[,] buf)
	{
		if (buf != null && buf.GetLength(0) == Rows)
		{
			return buf.GetLength(1) == Cols;
		}
		return false;
	}

	public override string ToString()
	{
		return Extent.ToString() + " (" + Rows + " rows, " + Cols + " cols)";
	}
}
