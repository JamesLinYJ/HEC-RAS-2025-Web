using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RasterM : IExtent
{
	double IExtent.MinZ => MinMaxZM;
	double IExtent.MaxZ => MinMaxZM;
	double IExtent.MinM => MinMaxZM;
	double IExtent.MaxM => MinMaxZM;

	public enum InterpolationMode
	{
		Nearest,
		Linear,
		Smooth
	}

	public struct InterpolationData
	{
		public int[] Index;

		public float[] Weight;

		public InterpolationData(int[] idx, float[] wts)
		{
			this = default;
			Index = idx;
			Weight = wts;
		}

		public InterpolationData(int idx, float wt)
		{
			this = default;
			Index = new int[1];
			Weight = new float[1];
			Index[0] = idx;
			Weight[0] = wt;
		}

		public void AddPoint(int idx, float wt)
		{
			checked
			{
				if (Index == null)
				{
					Index = new int[1];
					Weight = new float[1];
				}
				else
				{
					ref int[] index = ref Index;
					index = (int[])Utils.CopyArray(index, new int[Index.Count() + 1]);
					ref float[] weight = ref Weight;
					weight = (float[])Utils.CopyArray(weight, new float[Weight.Count() + 1]);
				}
				Index[Index.Count() - 1] = idx;
				Weight[Weight.Count() - 1] = wt;
			}
		}

		public void Normalize()
		{
			float weightSum = 0f;
			checked
			{
				int count = Weight.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					weightSum += Weight[i];
				}
				int count2 = Weight.Count() - 1;
				for (int j = 0; j <= count2; j++)
				{
					Weight[j] /= weightSum;
				}
			}
		}

		public int Count()
		{
			if (Index == null)
			{
				return 0;
			}
			if (Index == null)
			{
				return 0;
			}
			return Index.Count();
		}
	}

	protected int _rows;

	protected int _cols;

	protected int _maxCell;

	protected double _maxX;

	protected double _minX;

	protected double _maxY;

	protected double _minY;

	protected double _cellSize;

	public int Rows => _rows;

	public int Cols => _cols;

	public int CellCount => checked(_rows * _cols);

	public double Area => Width * Height;

	// C# has no syntax for parameterized property 'InRange'.
	public bool get_InRange(RowColM rowcol)
	{
		return this.get_InRange(rowcol.Row, rowcol.Col);
	}

	// C# has no syntax for parameterized property 'InRange'.
	public bool get_InRange(int row, int col)
	{
		if (row >= 0 && row < _rows && col >= 0 && col < _cols)
		{
			return true;
		}
		return false;
	}

	public double MaxX => _maxX;

	public double MinX => _minX;

	public double MaxY => _maxY;

	public double MinY => _minY;

	public double Left => _minX;

	public double Right => _maxX;

	public double Top => _maxY;

	public double Bottom => _minY;

	public PointM Center
	{
		get
		{
			double ax = (MinX + MaxX) / 2.0;
			double ay = (MinY + MaxY) / 2.0;
			return new PointM(ax, ay);
		}
	}

	public double Height => MaxY - MinY;

	public double Width => MaxX - MinX;

	public double MinMaxZM => double.NaN;

	public bool MaxMinXYDefined => !double.IsNaN(_minX);

	public double CellSize => _cellSize;

	public double CellArea => _cellSize * _cellSize;

	public RasterM()
	{
		Assign(-1, -1, double.MinValue, double.MaxValue, double.MinValue, double.MaxValue);
	}

	public RasterM(int rows, int cols, Extent ext)
	{
		Assign(rows, cols, ext);
	}

	public RasterM(int rows, int cols, double maxX, double minX, double maxY, double minY)
	{
		Assign(rows, cols, maxX, minX, maxY, minY);
	}

	public RasterM(RasterM raster)
	{
		Assign(raster);
	}

	public RasterM(GDALRaster gdr)
	{
		int pixelsTall = gdr.PixelsTall;
		int pixelsWide = gdr.PixelsWide;
		Extent extent = Converter.Convert(gdr.GetFullNativeExtents());
		Assign(pixelsTall, pixelsWide, extent);
	}

	internal RasterM(Geospatial.Vectors.Extent corners, int rows, int cols)
		: this(new PointM(corners.Left, corners.Top), rows, cols, corners.Width / (double)cols)
	{
	}

	public RasterM(PointM upperLeft, int rows, int cols, double cellSize)
	{
		_minX = upperLeft.X;
		_maxY = upperLeft.Y;
		_rows = rows;
		_cols = cols;
		_cellSize = cellSize;
		_maxX = _minX + (double)_cols * _cellSize;
		_minY = _maxY - (double)_rows * _cellSize;
	}

	public void Assign(int rows, int cols, Extent extent)
	{
		Assign(rows, cols, extent.MaxX, extent.MinX, extent.MaxY, extent.MinY);
	}

	public void Assign(RasterM raster)
	{
		Assign(raster.Rows, raster.Cols, raster.MaxX, raster.MinX, raster.MaxY, raster.MinY, raster.CellSize);
	}

	public void Assign(int rows, int cols, double maxX, double minX, double maxY, double minY)
	{
		_rows = rows;
		_cols = cols;
		_maxX = maxX;
		_minX = minX;
		_maxY = maxY;
		_minY = minY;
		_cellSize = System.Math.Max((_maxX - _minX) / (double)_cols, (_maxY - _minY) / (double)_rows);
		_maxCell = checked(_rows * _cols - 1);
	}

	public void Assign(int rows, int cols, double maxX, double minX, double maxY, double minY, double cellSize)
	{
		_rows = rows;
		_cols = cols;
		_maxX = maxX;
		_minX = minX;
		_maxY = maxY;
		_minY = minY;
		_cellSize = cellSize;
		_maxCell = checked(_rows * _cols - 1);
	}

	public int CellIndex(int row, int col)
	{
		return checked(col + row * _cols);
	}

	public int CellIndex(RowColM rowcol)
	{
		return CellIndex(rowcol.Row, rowcol.Col);
	}

	public int CellIndex(PointM pt)
	{
		return CellIndex(Row(pt.Y), Col(pt.X));
	}

	public int CellIndex(Point pt)
	{
		return CellIndex(Row(pt.Y), Col(pt.X));
	}

	public int CellIndex(Point2Double pt)
	{
		return CellIndex(Row(pt.Y), Col(pt.X));
	}

	public int CellIndex(double x, double y)
	{
		return CellIndex(Row(y), Col(x));
	}

	public void Cell2DIndex(int idx1D, ref int r, ref int c)
	{
		r = checked((int)System.Math.Floor((double)idx1D / (double)Cols));
		c = Convert.ToInt32(System.Math.Floor(new decimal(idx1D % Cols)));
	}

	public int CellMaxIndex()
	{
		return _maxCell;
	}

	public bool Overlaps(Feature feature)
	{
		if (feature == null)
		{
			return false;
		}
		if (double.IsNaN(feature.MinX) || double.IsNaN(feature.MinY))
		{
			return false;
		}
		if (_maxX < feature.MinX)
		{
			return false;
		}
		if (_minX > feature.MaxX)
		{
			return false;
		}
		if (_minY > feature.MaxY)
		{
			return false;
		}
		if (_maxY < feature.MinY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps(IEnumerable<SegmentM> segments)
	{
		foreach (SegmentM segment in segments)
		{
			if (Overlaps(segment.MinX, segment.MaxX, segment.MinY, segment.MaxY))
			{
				return true;
			}
		}
		return false;
	}

	public bool Overlaps(RasterM raster)
	{
		if (raster == null)
		{
			return false;
		}
		if (_maxX < raster.MinX)
		{
			return false;
		}
		if (_minX > raster.MaxX)
		{
			return false;
		}
		if (_minY > raster.MaxY)
		{
			return false;
		}
		if (_maxY < raster.MinY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps(double minX, double maxX, double minY, double maxY)
	{
		if (_maxX < minX)
		{
			return false;
		}
		if (_minX > maxX)
		{
			return false;
		}
		if (_minY > maxY)
		{
			return false;
		}
		if (_maxY < minY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps(PointM pt)
	{
		if (_maxX < pt.X)
		{
			return false;
		}
		if (_minX > pt.X)
		{
			return false;
		}
		if (_minY > pt.Y)
		{
			return false;
		}
		if (_maxY < pt.Y)
		{
			return false;
		}
		return true;
	}

	public bool EqualXY(Extent extent)
	{
		if (extent == null)
		{
			return false;
		}
		if (_maxX == extent.MaxX && _minX == extent.MinX && _maxY == extent.MaxY && _minY == extent.MinY)
		{
			return true;
		}
		return false;
	}

	public bool EqualRasterM(RasterM rast)
	{
		if (rast == null)
		{
			return false;
		}
		if (_rows != rast.Rows || _cols != rast.Cols)
		{
			return false;
		}
		if (_maxX != rast.MaxX || _minX != rast.MinX || _maxY != rast.MaxY || _minY != rast.MinY)
		{
			return false;
		}
		return true;
	}

	public RasterM Copy()
	{
		return new RasterM(this);
	}

	public void EnsureArraySize(ref float[] values)
	{
		checked
		{
			if (values == null || values.Count() != _rows * _cols)
			{
				Array.Resize(ref values, _rows * _cols);
			}
		}
	}

	public void EnsureArraySize(ref byte[] values)
	{
		checked
		{
			if (values == null || values.Count() != _rows * _cols)
			{
				Array.Resize(ref values, _rows * _cols);
			}
		}
	}

	public void SetCellTileRasterM(int n, int tileSize, RasterM raster)
	{
		SetCellTileRasterM(n / _cols, n % _cols, tileSize, raster);
	}

	public void SetCellTileRasterM(int row, int col, int tileSize, RasterM raster)
	{
		raster.Assign(tileSize, tileSize, CellMaxX(col), CellMinX(col), CellMaxY(row), CellMinY(row));
	}

	public RasterM ComputeSubRaster(int rowIdx, int colIdx, int newRasterWidth)
	{
		return new RasterM(new PointM(CellMinX(colIdx), CellMaxY(rowIdx)), newRasterWidth, newRasterWidth, CellSize / (double)newRasterWidth);
	}

	public RasterM GetCellRasterM(int cellIdx, int numCells)
	{
		RasterM rasterM = new RasterM();
		SetCellTileRasterM(cellIdx, numCells, rasterM);
		return rasterM;
	}

	public RasterM ExtendRaster(int addLeft, int addRight, int addTop, int addBottom)
	{
		return checked(new RasterM(_rows + addTop + addBottom, _cols + addRight + addLeft, _maxX + _cellSize * (double)addRight, _minX - _cellSize * (double)addLeft, _maxY + _cellSize * (double)addTop, _minY - _cellSize * (double)addBottom));
	}

	public RasterM CreateRegisteredRaster(PointM newUL, PointM newLR)
	{
		double a = MinX - newUL.X;
		double a2 = MaxY - newUL.Y;
		double a3 = newLR.X - MaxX;
		double a4 = newLR.Y - MinY;
		return checked(ExtendRaster((int)System.Math.Ceiling(a), (int)System.Math.Ceiling(a3), (int)System.Math.Ceiling(a2), (int)System.Math.Ceiling(a4)));
	}

	public int Row(double y)
	{
		double systemMaxYTerm = System.Math.Floor((_maxY - y) / _cellSize);
		if (systemMaxYTerm >= 2147483647.0)
		{
			return int.MaxValue;
		}
		if (systemMaxYTerm <= -2147483648.0)
		{
			return int.MinValue;
		}
		return checked((int)System.Math.Round(systemMaxYTerm));
	}

	public int Col(double x)
	{
		double systemMinXTerm = System.Math.Floor((x - _minX) / _cellSize);
		if (systemMinXTerm >= 2147483647.0)
		{
			return int.MaxValue;
		}
		if (systemMinXTerm <= -2147483648.0)
		{
			return int.MinValue;
		}
		return checked((int)System.Math.Round(systemMinXTerm));
	}

	public double RowD(double y)
	{
		return (_maxY - y) / _cellSize;
	}

	public double ColD(double x)
	{
		return (x - _minX) / _cellSize;
	}

	public RowColM RowCol(PointM pt)
	{
		RowColM result = default;
		result.Row = Row(pt.Y);
		result.Col = Col(pt.X);
		return result;
	}

	public void ColAndFraction(double x, ref int c, ref float frac)
	{
		c = Col(x);
		frac = (float)((x - CellMinX(c)) / CellSize);
		if (c < 0)
		{
			c = 0;
			frac = 0f;
		}
		else if (c >= _cols)
		{
			c = checked(_cols - 1);
			frac = 1f;
		}
	}

	public void RowAndFraction(double y, ref int r, ref float frac)
	{
		r = Row(y);
		frac = (float)((CellMaxY(r) - y) / CellSize);
		if (r < 0)
		{
			r = 0;
			frac = 0f;
		}
		else if (r >= _rows)
		{
			r = checked(_rows - 1);
			frac = 1f;
		}
	}

	public void GetBilinearWeights(PointM ptm, ref List<int> indexes, ref List<float> weights)
	{
		indexes = new List<int>();
		weights = new List<float>();
		if (!Contains(ptm))
		{
			return;
		}
		if (CellCount == 1)
		{
			indexes.Add(0);
			weights.Add(1f);
			return;
		}
		double rowD = RowD(ptm.Y);
		double colD = ColD(ptm.X);
		checked
		{
			int systemRowDTerm = (int)System.Math.Floor(rowD);
			int systemColDTerm = (int)System.Math.Floor(colD);
			int ordinal;
			int ordinal2;
			if (rowD - (double)systemRowDTerm >= 0.5)
			{
				ordinal = systemRowDTerm;
				ordinal2 = systemRowDTerm + 1;
			}
			else
			{
				ordinal = systemRowDTerm - 1;
				ordinal2 = systemRowDTerm;
			}
			int ordinal3;
			int ordinal4;
			if (colD - (double)systemColDTerm >= 0.5)
			{
				ordinal3 = systemColDTerm;
				ordinal4 = systemColDTerm + 1;
			}
			else
			{
				ordinal3 = systemColDTerm - 1;
				ordinal4 = systemColDTerm;
			}
			double ay = Top - (0.5 + (double)ordinal) * CellSize;
			double ay2 = Top - (0.5 + (double)ordinal2) * CellSize;
			double ax = Left + (0.5 + (double)ordinal3) * CellSize;
			double ax2 = Left + (0.5 + (double)ordinal4) * CellSize;
			double TopLeftWeight = default;
			double TopRightWeight = default;
			double BottomRightWeight = default;
			double BottomLeftWeight = default;
			new QuadrilateralShape(new PointM(ax, ay), new PointM(ax2, ay), new PointM(ax2, ay2), new PointM(ax, ay2)).InterpolationWeights(ptm, ref TopLeftWeight, ref TopRightWeight, ref BottomRightWeight, ref BottomLeftWeight);
			if (ordinal >= 0 && ordinal < Rows)
			{
				if (ordinal3 >= 0 && ordinal3 < Cols)
				{
					int cellIndex = CellIndex(ordinal, ordinal3);
					indexes.Add(cellIndex);
					weights.Add((float)TopLeftWeight);
				}
				if (ordinal4 >= 0 && ordinal4 < Cols)
				{
					int cellIndex2 = CellIndex(ordinal, ordinal4);
					indexes.Add(cellIndex2);
					weights.Add((float)TopRightWeight);
				}
			}
			if (ordinal2 >= 0 && ordinal2 < Rows)
			{
				if (ordinal3 >= 0 && ordinal3 < Cols)
				{
					int cellIndex3 = CellIndex(ordinal2, ordinal3);
					indexes.Add(cellIndex3);
					weights.Add((float)BottomLeftWeight);
				}
				if (ordinal4 >= 0 && ordinal4 < Cols)
				{
					int cellIndex4 = CellIndex(ordinal2, ordinal4);
					indexes.Add(cellIndex4);
					weights.Add((float)BottomRightWeight);
				}
			}
			if (indexes.Count != 0 && indexes.Count != 4)
			{
				float sum = weights.Sum();
				int count = weights.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					weights[i] /= sum;
				}
			}
		}
	}

	public void GetWeights(Polygon pgon, int rasterizerCellCount, ref List<int> indexes, ref List<float> weights)
	{
		indexes = new List<int>();
		weights = new List<float>();
		Extent extent = pgon.ComputeExtent();
		if (!ComputeExtent().Intersects(extent))
		{
			return;
		}
		RasterM rasterM = ComputeRasterM(rasterizerCellCount, extent);
		checked
		{
			bool[] boolValues = new bool[rasterM.CellCount - 1 + 1];
			if (!pgon.Rasterize(rasterM, boolValues))
			{
				return;
			}
			int ordinal = 0;
			Dictionary<int, int> intByInt = new Dictionary<int, int>();
			int count = boolValues.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!boolValues[i])
				{
					continue;
				}
				ordinal++;
				PointM pointM = rasterM.CellCenter(i);
				if (Contains(pointM))
				{
					int key = CellIndex(pointM);
					if (intByInt.TryGetValue(key, out var localValue))
					{
						intByInt[key] = localValue + 1;
					}
					else
					{
						intByInt[key] = 1;
					}
				}
			}
			foreach (KeyValuePair<int, int> localValue2 in intByInt)
			{
				indexes.Add(localValue2.Key);
				weights.Add((float)localValue2.Value / (float)ordinal);
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
			result = new InterpolationData(CellIndex(x, y), 1f);
			break;
		}
		return result;
	}

	public static double InterpolateMidsideValue(float length, float f0, float fp0, float f1, float fp1)
	{
		return (double)(f0 + f1) * 0.5 + (double)(length * (fp0 - fp1) * 0.125f);
	}

	public bool Contains(PointM PointM)
	{
		return Contains(PointM.X, PointM.Y);
	}

	public bool Contains(Point2Double pt)
	{
		return Contains(pt.X, pt.Y);
	}

	public bool Contains(double x, double y)
	{
		if (x < _minX || x > _maxX || y < _minY || y > _maxY)
		{
			return false;
		}
		return true;
	}

	public bool Contains(IExtent extent)
	{
		if (extent.MinX < _minX || extent.MaxX > _maxX || extent.MinY < _minY || extent.MaxY > _maxY)
		{
			return false;
		}
		return true;
	}

	public bool Contains(double minX, double maxX, double minY, double maxY)
	{
		if (minX < _minX || maxX > _maxX || minY < _minY || maxY > _maxY)
		{
			return false;
		}
		return true;
	}

	public double CellMaxX(int col)
	{
		return _minX + _cellSize * (double)checked(col + 1);
	}

	public double CellMinX(int col)
	{
		return _minX + _cellSize * (double)col;
	}

	public double CellCenterX(int col)
	{
		return _minX + _cellSize * ((double)col + 0.5);
	}

	public double CellMaxY(int row)
	{
		return _maxY - _cellSize * (double)row;
	}

	public double CellMinY(int row)
	{
		return _maxY - _cellSize * (double)checked(row + 1);
	}

	public double CellCenterY(int row)
	{
		return _maxY - _cellSize * ((double)row + 0.5);
	}

	public PointM CellCenter(int row, int col)
	{
		PointM result = default;
		result.X = _minX + _cellSize * ((double)col + 0.5);
		result.Y = _maxY - _cellSize * ((double)row + 0.5);
		return result;
	}

	public PointM CellCenter(int cellIndex)
	{
		int r = default;
		int c = default;
		Cell2DIndex(cellIndex, ref r, ref c);
		return CellCenter(r, c);
	}

	public PointM CellCenter(System.Drawing.Point drawingPoint)
	{
		PointM result = default;
		result.X = _minX + _cellSize * ((double)drawingPoint.X + 0.5);
		result.Y = _maxY - _cellSize * ((double)drawingPoint.Y + 0.5);
		return result;
	}

	public Extent CellExtent(int cellIdx)
	{
		return CellExtent(cellIdx / _cols, cellIdx % _cols);
	}

	public Extent CellExtent(int row, int col)
	{
		return new Extent(CellMaxX(col), CellMinX(col), CellMaxY(row), CellMinY(row));
	}

	public Extent ComputeExtent()
	{
		return new Extent(_maxX, _minX, _maxY, _minY);
	}

	public bool CellOverlaps(int row, int col, Feature feature)
	{
		if (feature == null)
		{
			return false;
		}
		if (CellMaxX(col) < feature.MinX)
		{
			return false;
		}
		if (CellMinX(col) > feature.MaxX)
		{
			return false;
		}
		if (CellMinY(row) > feature.MaxY)
		{
			return false;
		}
		if (CellMaxY(row) < feature.MinY)
		{
			return false;
		}
		return true;
	}

	public RowColRangeM RowColRange(RasterM otherRaster, int otherCol, int otherRow)
	{
		double minX = otherRaster.CellMinX(otherCol);
		double maxY = otherRaster.CellMaxY(otherRow);
		double maxX = otherRaster.CellMaxX(otherCol);
		double minY = otherRaster.CellMinY(otherRow);
		return RowColRange(minX, maxX, minY, maxY, limitToRaster: true);
	}

	public RowColRangeM RowColRange(Feature feature, bool limitToRaster)
	{
		return RowColRange(feature.MinX, feature.MaxX, feature.MinY, feature.MaxY, limitToRaster);
	}

	public RowColRangeM RowColRange(Extent extent, bool limitToRaster)
	{
		return RowColRange(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY, limitToRaster);
	}

	public RowColRangeM RowColRange(RasterM raster, bool limitToRaster)
	{
		return RowColRange(raster.MinX, raster.MaxX, raster.MinY, raster.MaxY, limitToRaster);
	}

	public RowColRangeM RowColRange(PointM point, double offset, bool limitToRaster)
	{
		return RowColRange(point.X - offset, point.X + offset, point.Y - offset, point.Y + offset, limitToRaster);
	}

	public RowColRangeM RowColRange(double minX, double maxX, double minY, double maxY, bool limitToRaster)
	{
		checked
		{
			RowColRangeM result = default;
			if (limitToRaster)
			{
				if (minX <= _minX)
				{
					result.MinCol = 0;
				}
				else
				{
					result.MinCol = Col(minX);
				}
				if (maxX >= _maxX)
				{
					result.MaxCol = _cols - 1;
				}
				else
				{
					result.MaxCol = Col(maxX);
				}
				if (minY <= _minY)
				{
					result.MaxRow = _rows - 1;
				}
				else
				{
					result.MaxRow = Row(minY);
				}
				if (maxY >= _maxY)
				{
					result.MinRow = 0;
				}
				else
				{
					result.MinRow = Row(maxY);
				}
			}
			else
			{
				result.MinCol = Col(minX);
				result.MaxCol = Col(maxX);
				result.MinRow = Row(maxY);
				result.MaxRow = Row(minY);
			}
			return result;
		}
	}

	public bool RectangleInRange(int x, int y, int width, int height, ref System.Drawing.Rectangle rect)
	{
		int ordinal = y;
		checked
		{
			int rowsOffset = y + height;
			int ordinal2 = x;
			int colsOffset = x + width;
			if (rowsOffset < 0 || rowsOffset >= _rows || colsOffset < 0 || ordinal2 >= _cols)
			{
				return false;
			}
			if (ordinal < 0)
			{
				ordinal = 0;
			}
			if (rowsOffset >= _rows)
			{
				rowsOffset = _rows - 1;
			}
			if (ordinal2 < 0)
			{
				ordinal2 = 0;
			}
			if (colsOffset >= _cols)
			{
				colsOffset = _cols - 1;
			}
			rect = new System.Drawing.Rectangle(ordinal2, ordinal, colsOffset - ordinal2 + 1, rowsOffset - ordinal + 1);
			return true;
		}
	}

	public System.Drawing.Point PixelPoint(PointM pointM)
	{
		System.Drawing.Point result = default;
		result.X = Col(pointM.X);
		result.Y = Row(pointM.Y);
		return result;
	}

	public System.Drawing.Point CreatePixelPoint(PointM point)
	{
		return new System.Drawing.Point(Col(point.X), Row(point.Y));
	}

	public System.Drawing.Point CreatePixelPoint(Point2Double point)
	{
		return new System.Drawing.Point(Col(point.X), Row(point.Y));
	}

	public System.Drawing.Point CreatePixelPoint(Point Point)
	{
		return new System.Drawing.Point(Col(Point.X), Row(Point.Y));
	}

	public System.Drawing.Point CreatePixelPoint(double x, double y)
	{
		return new System.Drawing.Point(Col(x), Row(y));
	}

	public System.Drawing.Point[] CreatePixelPoints(MultiPoint points, bool skipRepeats = true)
	{
		checked
		{
			System.Drawing.Point[] pointsArray = new System.Drawing.Point[points.Count - 1 + 1];
			if (skipRepeats)
			{
				int ordinal = 0;
				int count = points.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					double d = points.get_X(i);
					double d2 = points.get_Y(i);
					if (!double.IsNaN(d) && !double.IsNaN(d2))
					{
						int xComponent = Col(points.get_X(i));
						int yComponent = Row(points.get_Y(i));
						if (ordinal <= 0 || pointsArray[ordinal - 1].X != xComponent || pointsArray[ordinal - 1].Y != yComponent)
						{
							pointsArray[ordinal] = new System.Drawing.Point(xComponent, yComponent);
							ordinal++;
						}
					}
				}
				if (ordinal != points.Count)
				{
					Array.Resize(ref pointsArray, ordinal);
				}
			}
			else
			{
				int count2 = points.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					pointsArray[j] = new System.Drawing.Point(Col(points.get_X(j)), Row(points.get_Y(j)));
				}
			}
			return pointsArray;
		}
	}

	public System.Drawing.Point[] CreatePixelPoints(MultiPoint points, Transform transform, bool skipRepeats = true)
	{
		checked
		{
			System.Drawing.Point[] pointsArray = new System.Drawing.Point[points.Count - 1 + 1];
			if (skipRepeats)
			{
				int ordinal = 0;
				int count = points.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM PointM = points.PointM(i);
					transform.TransformPointM(ref PointM);
					int xComponent = Col(PointM.X);
					int yComponent = Row(PointM.Y);
					if (ordinal <= 0 || pointsArray[ordinal - 1].X != xComponent || pointsArray[ordinal - 1].Y != yComponent)
					{
						pointsArray[ordinal] = new System.Drawing.Point(xComponent, yComponent);
						ordinal++;
					}
				}
				if (ordinal != points.Count)
				{
					Array.Resize(ref pointsArray, ordinal);
				}
			}
			else
			{
				int count2 = points.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					PointM PointM2 = points.PointM(j);
					transform.TransformPointM(ref PointM2);
					int x = Col(PointM2.X);
					int y = Row(PointM2.Y);
					pointsArray[j] = new System.Drawing.Point(x, y);
				}
			}
			return pointsArray;
		}
	}

	public System.Drawing.Point[] CreatePixelPoints(MultiPoint points, int startIndex, int endIndex)
	{
		checked
		{
			System.Drawing.Point[] pointsArray = new System.Drawing.Point[endIndex - startIndex + 1];
			int ordinal = 0;
			for (int i = startIndex; i <= endIndex; i++)
			{
				int xComponent = Col(points.get_X(i));
				int yComponent = Row(points.get_Y(i));
				if (ordinal <= 0 || pointsArray[ordinal].X != xComponent || pointsArray[ordinal].Y != yComponent)
				{
					pointsArray[ordinal] = new System.Drawing.Point(xComponent, yComponent);
					ordinal++;
				}
			}
			if (ordinal != pointsArray.Count())
			{
				Array.Resize(ref pointsArray, ordinal);
			}
			return pointsArray;
		}
	}

	public System.Drawing.Point[] CreatePixelPoints(MultiPoint points, Transform transform, int startIndex, int endIndex)
	{
		checked
		{
			System.Drawing.Point[] pointsArray = new System.Drawing.Point[endIndex - startIndex + 1];
			int ordinal = 0;
			for (int i = startIndex; i <= endIndex; i++)
			{
				PointM PointM = points[i];
				transform.TransformPointM(ref PointM);
				int xComponent = Col(PointM.X);
				int yComponent = Row(PointM.Y);
				if (ordinal <= 0 || pointsArray[ordinal].X != xComponent || pointsArray[ordinal].Y != yComponent)
				{
					pointsArray[ordinal] = new System.Drawing.Point(xComponent, yComponent);
					ordinal++;
				}
			}
			if (ordinal != pointsArray.Count())
			{
				Array.Resize(ref pointsArray, ordinal);
			}
			return pointsArray;
		}
	}

	public System.Drawing.Point[] CreatePixelPoints(int count, double[] x, double[] y, bool skipRepeats = true)
	{
		checked
		{
			System.Drawing.Point[] points = new System.Drawing.Point[count - 1 + 1];
			if (skipRepeats)
			{
				int ordinal = 0;
				int count2 = count - 1;
				for (int i = 0; i <= count2; i++)
				{
					int xComponent = Col(x[i]);
					int yComponent = Row(y[i]);
					if (ordinal <= 0 || points[ordinal - 1].X != xComponent || points[ordinal - 1].Y != yComponent)
					{
						points[ordinal] = new System.Drawing.Point(xComponent, yComponent);
						ordinal++;
					}
				}
				if (ordinal != count)
				{
					Array.Resize(ref points, ordinal);
				}
			}
			else
			{
				int count3 = count - 1;
				for (int j = 0; j <= count3; j++)
				{
					points[j] = new System.Drawing.Point(Col(x[j]), Row(y[j]));
				}
			}
			return points;
		}
	}

	public bool RowsInRange(double minY, double maxY, ref int minRow, ref int maxRow)
	{
		if (minY >= _maxY || maxY <= _minY)
		{
			return false;
		}
		minRow = Row(maxY);
		maxRow = Row(minY);
		if (minRow < 0)
		{
			minRow = 0;
		}
		checked
		{
			if (maxRow > _rows - 1)
			{
				maxRow = _rows - 1;
			}
			return true;
		}
	}

	public RowColMs SegmentRowCols(SegmentM segment)
	{
		RowColMs rowColMs = new RowColMs();
		int row = Row(segment.A.Y);
		int col = Col(segment.A.X);
		int row2 = Row(segment.B.Y);
		int col2 = Col(segment.B.X);
		checked
		{
			int col2AboveCol = col2 - col;
			int row2AboveRow = row2 - row;
			double deltaX = segment.DeltaX();
			double deltaY = segment.DeltaY();
			if ((col2AboveCol == 0) & (row2AboveRow == 0))
			{
				rowColMs.Add(row, col);
			}
			else if (col2AboveCol == 0)
			{
				int row3 = row2;
				int systemTerm = System.Math.Sign(row2AboveRow);
				for (int i = row; ((systemTerm >> 31) ^ i) <= ((systemTerm >> 31) ^ row3); i += systemTerm)
				{
					rowColMs.Add(i, col);
				}
			}
			else if (row2AboveRow == 0)
			{
				int col3 = col2;
				int systemTerm2 = System.Math.Sign(col2AboveCol);
				for (int j = col; ((systemTerm2 >> 31) ^ j) <= ((systemTerm2 >> 31) ^ col3); j += systemTerm2)
				{
					rowColMs.Add(row, j);
				}
			}
			else
			{
				int col4 = col;
				int row4 = row;
				rowColMs.Add(row4, col4);
				if (col2AboveCol > 0)
				{
					if (row2AboveRow > 0)
					{
						while (!((col4 == col2) & (row4 == row2)))
						{
							double cellMinYRow4Term = (CellMinY(row4) - segment.A.Y) / deltaY;
							double cellMaxXCol4Term = (CellMaxX(col4) - segment.A.X) / deltaX;
							if (((cellMinYRow4Term < cellMaxXCol4Term) & (row4 < row2)) | (col4 == col2))
							{
								row4++;
							}
							else
							{
								col4++;
							}
							rowColMs.Add(row4, col4);
						}
					}
					else
					{
						while (!((col4 == col2) & (row4 == row2)))
						{
							double cellMaxYRow4Term = (CellMaxY(row4) - segment.A.Y) / deltaY;
							double cellMaxXCol4Term2 = (CellMaxX(col4) - segment.A.X) / deltaX;
							if (((cellMaxYRow4Term < cellMaxXCol4Term2) & (row4 > row2)) | (col4 == col2))
							{
								row4--;
							}
							else
							{
								col4++;
							}
							rowColMs.Add(row4, col4);
						}
					}
				}
				else if (row2AboveRow > 0)
				{
					while (!((col4 == col2) & (row4 == row2)))
					{
						double cellMinYRow4Term2 = (CellMinY(row4) - segment.A.Y) / deltaY;
						double cellMinXCol4Term = (CellMinX(col4) - segment.A.X) / deltaX;
						if (((cellMinYRow4Term2 < cellMinXCol4Term) & (row4 < row2)) | (col4 == col2))
						{
							row4++;
						}
						else
						{
							col4--;
						}
						rowColMs.Add(row4, col4);
					}
				}
				else
				{
					while (!((col4 == col2) & (row4 == row2)))
					{
						double cellMaxYRow4Term2 = (CellMaxY(row4) - segment.A.Y) / deltaY;
						double cellMinXCol4Term2 = (CellMinX(col4) - segment.A.X) / deltaX;
						if (((cellMaxYRow4Term2 < cellMinXCol4Term2) & (row4 > row2)) | (col4 == col2))
						{
							row4--;
						}
						else
						{
							col4--;
						}
						rowColMs.Add(row4, col4);
					}
				}
			}
			return rowColMs;
		}
	}

	public void SegmentCells(SegmentM segment, List<int> cells)
	{
		cells.Clear();
		int row = Row(segment.A.Y);
		int col = Col(segment.A.X);
		int row2 = Row(segment.B.Y);
		int col2 = Col(segment.B.X);
		checked
		{
			int col2AboveCol = col2 - col;
			int row2AboveRow = row2 - row;
			double deltaX = segment.DeltaX();
			double deltaY = segment.DeltaY();
			if ((col2AboveCol == 0) & (row2AboveRow == 0))
			{
				AddCell(row, col, cells);
				return;
			}
			if (col2AboveCol == 0)
			{
				int row3 = row2;
				int systemTerm = System.Math.Sign(row2AboveRow);
				for (int i = row; ((systemTerm >> 31) ^ i) <= ((systemTerm >> 31) ^ row3); i += systemTerm)
				{
					AddCell(i, col, cells);
				}
				return;
			}
			if (row2AboveRow == 0)
			{
				int col3 = col2;
				int systemTerm2 = System.Math.Sign(col2AboveCol);
				for (int j = col; ((systemTerm2 >> 31) ^ j) <= ((systemTerm2 >> 31) ^ col3); j += systemTerm2)
				{
					AddCell(row, j, cells);
				}
				return;
			}
			int col4 = col;
			int row4 = row;
			AddCell(row4, col4, cells);
			if (col2AboveCol > 0)
			{
				if (row2AboveRow > 0)
				{
					while (!((col4 == col2) & (row4 == row2)))
					{
						double cellMinYRow4Term = (CellMinY(row4) - segment.A.Y) / deltaY;
						double cellMaxXCol4Term = (CellMaxX(col4) - segment.A.X) / deltaX;
						if (((cellMinYRow4Term < cellMaxXCol4Term) & (row4 < row2)) | (col4 == col2))
						{
							row4++;
						}
						else
						{
							col4++;
						}
						AddCell(row4, col4, cells);
					}
					return;
				}
				while (!((col4 == col2) & (row4 == row2)))
				{
					double cellMaxYRow4Term = (CellMaxY(row4) - segment.A.Y) / deltaY;
					double cellMaxXCol4Term2 = (CellMaxX(col4) - segment.A.X) / deltaX;
					if (((cellMaxYRow4Term < cellMaxXCol4Term2) & (row4 > row2)) | (col4 == col2))
					{
						row4--;
					}
					else
					{
						col4++;
					}
					AddCell(row4, col4, cells);
				}
				return;
			}
			if (row2AboveRow > 0)
			{
				while (!((col4 == col2) & (row4 == row2)))
				{
					double cellMinYRow4Term2 = (CellMinY(row4) - segment.A.Y) / deltaY;
					double cellMinXCol4Term = (CellMinX(col4) - segment.A.X) / deltaX;
					if (((cellMinYRow4Term2 < cellMinXCol4Term) & (row4 < row2)) | (col4 == col2))
					{
						row4++;
					}
					else
					{
						col4--;
					}
					AddCell(row4, col4, cells);
				}
				return;
			}
			while (!((col4 == col2) & (row4 == row2)))
			{
				double cellMaxYRow4Term2 = (CellMaxY(row4) - segment.A.Y) / deltaY;
				double cellMinXCol4Term2 = (CellMinX(col4) - segment.A.X) / deltaX;
				if (((cellMaxYRow4Term2 < cellMinXCol4Term2) & (row4 > row2)) | (col4 == col2))
				{
					row4--;
				}
				else
				{
					col4--;
				}
				AddCell(row4, col4, cells);
			}
		}
	}

	private void AddCell(int r, int c, List<int> cells)
	{
		int cellIndex = CellIndex(r, c);
		if (cellIndex >= 0 && cellIndex <= _maxCell)
		{
			cells.Add(cellIndex);
		}
	}

	public void PlotCellGridlines(RasterM plotRaster, int[] colors, int color)
	{
		checked
		{
			if (plotRaster.Overlaps(this))
			{
				RowColRangeM rowColRangeM = RowColRange(plotRaster, limitToRaster: true);
				int minRow = rowColRangeM.MinRow;
				int count = rowColRangeM.MaxRow + 1;
				for (int i = minRow; i <= count; i++)
				{
					plotRaster.HorizontalLine(colors, color, CellMinX(rowColRangeM.MinCol), CellMaxX(rowColRangeM.MaxCol), CellMaxY(i));
				}
				int minCol = rowColRangeM.MinCol;
				int count2 = rowColRangeM.MaxCol + 1;
				for (int j = minCol; j <= count2; j++)
				{
					plotRaster.VerticalLine(colors, color, CellMaxY(rowColRangeM.MinRow), CellMinY(rowColRangeM.MaxRow), CellMinX(j));
				}
			}
		}
	}

	public void HorizontalLine(int[] colors, int color, double left, double right, double y)
	{
		checked
		{
			if (!(y < _minY) && !(y > _maxY))
			{
				int rowColsTerm = Row(y) * _cols;
				int col = System.Math.Max(Col(left), 0);
				int systemColTerm = System.Math.Min(Col(right), _cols - 1);
				int count = systemColTerm;
				for (int i = col; i <= count; i++)
				{
					colors[rowColsTerm + i] = color;
				}
			}
		}
	}

	public void VerticalLine(int[] colors, int color, double top, double bottom, double x)
	{
		checked
		{
			if (!(x < _minX) && !(x > _maxX))
			{
				int col = Col(x);
				int row = System.Math.Max(Row(top), 0);
				int systemRowTerm = System.Math.Min(Row(bottom), _rows - 1);
				int colsSum = row * _cols + col;
				int count = systemRowTerm;
				for (int i = row; i <= count; i++)
				{
					colors[colsSum] = color;
					colsSum += _cols;
				}
			}
		}
	}

	public static RasterM ComputeRasterM(int maxCellCount, IExtent extent)
	{
		return ComputeRasterM(maxCellCount, extent.MaxX, extent.MinX, extent.MaxY, extent.MinY);
	}

	public static RasterM ComputeRasterMOld(int maxCellCount, double maxX, double minX, double maxY, double minY)
	{
		double maxXAboveMinX = maxX - minX;
		double maxYAboveMinY = maxY - minY;
		checked
		{
			int unity;
			int unity2;
			double systemTerm;
			if (maxCellCount == 1)
			{
				unity = 1;
				unity2 = 1;
				systemTerm = System.Math.Max(maxXAboveMinX, maxYAboveMinY) * 1.0001;
			}
			else if (maxXAboveMinX > maxYAboveMinY)
			{
				unity2 = (int)System.Math.Floor(System.Math.Sqrt((double)maxCellCount * maxXAboveMinX / maxYAboveMinY));
				unity = unchecked(maxCellCount / unity2);
				if (unity < 2)
				{
					unity2 = maxCellCount;
					unity = 1;
					systemTerm = System.Math.Max(maxXAboveMinX / (double)(unity2 - 1), maxYAboveMinY * 1.01);
				}
				else
				{
					systemTerm = System.Math.Max(maxXAboveMinX / (double)(unity2 - 1), maxYAboveMinY / (double)(unity - 1));
				}
			}
			else
			{
				unity = (int)System.Math.Floor(System.Math.Sqrt((double)maxCellCount * maxYAboveMinY / maxXAboveMinX));
				unity2 = unchecked(maxCellCount / unity);
				if (unity2 < 2)
				{
					unity2 = 1;
					unity = maxCellCount;
					systemTerm = System.Math.Max(maxYAboveMinY / (double)(unity - 1), maxXAboveMinX * 1.01);
				}
				else
				{
					systemTerm = System.Math.Max(maxXAboveMinX / (double)(unity2 - 1), maxYAboveMinY / (double)(unity - 1));
				}
			}
			double minXTerm = minX + 0.5 * maxXAboveMinX;
			double minYTerm = minY + 0.5 * maxYAboveMinY;
			double computed = 0.5 * (double)unity2 * systemTerm;
			double computed2 = 0.5 * (double)unity * systemTerm;
			maxX = minXTerm + computed;
			minX = minXTerm - computed;
			maxY = minYTerm + computed2;
			minY = minYTerm - computed2;
			return new RasterM(unity, unity2, maxX, minX, maxY, minY);
		}
	}

	public static RasterM ComputeRasterM(int maxCellCount, double maxX, double minX, double maxY, double minY)
	{
		if (maxX == minX && maxY == minY)
		{
			maxCellCount = 1;
		}
		if (maxX == minX)
		{
			if (maxX == 0.0)
			{
				maxX += 0.01;
				minX -= 0.01;
			}
			else if (maxX > 0.0)
			{
				maxX *= 1.01;
				minX *= 0.99;
			}
			else
			{
				maxX *= 0.99;
				minX *= 1.01;
			}
		}
		if (maxY == minY)
		{
			if (maxY == 0.0)
			{
				maxY += 0.01;
				minY -= 0.01;
			}
			else if (maxY > 0.0)
			{
				maxY *= 1.01;
				minY *= 0.99;
			}
			else
			{
				maxY *= 0.99;
				minY *= 1.01;
			}
		}
		double maxXAboveMinX = maxX - minX;
		double maxYAboveMinY = maxY - minY;
		double maxXAboveMinXOverMaxYAboveMinY = maxXAboveMinX / maxYAboveMinY;
		checked
		{
			int unity;
			int unity2;
			if (maxCellCount <= 1)
			{
				unity = 1;
				unity2 = 1;
				maxCellCount = 1;
			}
			else if (maxYAboveMinY == 0.0)
			{
				unity = 1;
				unity2 = maxCellCount;
			}
			else if (maxXAboveMinX == 0.0)
			{
				unity = maxCellCount;
				unity2 = 1;
			}
			else
			{
				double systemMaxCellCountTerm = System.Math.Sqrt((double)maxCellCount / maxXAboveMinXOverMaxYAboveMinY);
				unity2 = (int)System.Math.Floor(systemMaxCellCountTerm * maxXAboveMinXOverMaxYAboveMinY);
				unity = (int)System.Math.Floor(systemMaxCellCountTerm);
				if (unity == 0)
				{
					unity = 1;
					unity2 = maxCellCount;
				}
				else if (unity2 == 0)
				{
					unity2 = 1;
					unity = maxCellCount;
				}
			}
			double systemTerm = System.Math.Max(maxYAboveMinY / (double)unity, maxXAboveMinX / (double)unity2) * 1.01;
			double minXTerm = minX + 0.5 * maxXAboveMinX;
			double minYTerm = minY + 0.5 * maxYAboveMinY;
			double computed = 0.5 * (double)unity2 * systemTerm;
			double computed2 = 0.5 * (double)unity * systemTerm;
			maxX = minXTerm + computed;
			minX = minXTerm - computed;
			maxY = minYTerm + computed2;
			minY = minYTerm - computed2;
			return new RasterM(unity, unity2, maxX, minX, maxY, minY);
		}
	}

	public void EnsureBufferSize<T>(ref T[] a)
	{
		checked
		{
			if (a == null)
			{
				a = new T[_maxCell + 1];
			}
			else if (a.Count() != _maxCell + 1)
			{
				a = new T[_maxCell + 1];
			}
		}
	}

	public void WriteWorldFile(string filename)
	{
		string[] strings = new string[6];
		strings[0] = _cellSize.ToString();
		strings[1] = "0";
		strings[2] = "0";
		strings[3] = "-" + strings[0];
		strings[4] = Strings.Format(MinX + _cellSize * 0.5);
		strings[5] = Strings.Format(MaxY - _cellSize * 0.5);
		File.WriteAllLines(RasMapperLib.Utilities.Path.CreateWorldFilename(filename), strings);
	}

	public void WriteFloatHeaderfile(string filename, float noData)
	{
		List<string> stringList = new List<string>();
		stringList.Add("ncols          " + Conversions.ToString(_cols));
		stringList.Add("nrows          " + Conversions.ToString(_rows));
		stringList.Add("xllcorner      " + Conversions.ToString(_minX));
		stringList.Add("yllcorner      " + Conversions.ToString(_minY));
		stringList.Add("cellsize       " + Conversions.ToString(_cellSize));
		stringList.Add("NODATA_value   " + Conversions.ToString(noData));
		stringList.Add("byteorder       LSBFIRST");
		stringList.Add("NoData         " + Conversions.ToString(noData));
		string? path = System.IO.Path.ChangeExtension(filename, "hdr");
		File.Delete(path);
		File.WriteAllLines(path, stringList);
	}

	public void WriteIntegerHeaderfile(string filename, int noData)
	{
		List<string> stringList = new List<string>();
		stringList.Add("ncols          " + Conversions.ToString(_cols));
		stringList.Add("nrows          " + Conversions.ToString(_rows));
		stringList.Add("nband          1");
		stringList.Add("nbits          32");
		stringList.Add("byteorder      I");
		stringList.Add("layout         bil");
		stringList.Add("skipbytes      0");
		stringList.Add("ulxmap         " + Conversions.ToString(_minX + _cellSize * 0.5));
		stringList.Add("ulymap         " + Conversions.ToString(_maxY - _cellSize * 0.5));
		stringList.Add("xdim           " + Conversions.ToString(_cellSize));
		stringList.Add("ydim           " + Conversions.ToString(_cellSize));
		string? path = System.IO.Path.ChangeExtension(filename, "hdr");
		File.Delete(path);
		File.WriteAllLines(path, stringList);
	}

	public void ExportAsFLT(string filename, float[] data, float nodata)
	{
		checked
		{
			if (Rows * Cols != data.Length)
			{
				throw new ArgumentException("Data isn't the appropriate length.");
			}
			byte[] byteValues = new byte[data.Length * 4 - 1 + 1];
			Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Count());
			if (!Directory.Exists(System.IO.Path.GetDirectoryName(filename)))
			{
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filename));
			}
			File.Delete(filename);
			File.WriteAllBytes(filename, byteValues);
			WriteFloatHeaderfile(filename, nodata);
		}
	}

	public void ExportAsFLT(string filename, BitArray mask)
	{
		checked
		{
			float[] floatValues = new float[Rows * Cols - 1 + 1];
			int count = Rows - 1;
			for (int i = 0; i <= count; i++)
			{
				int count2 = Cols - 1;
				for (int j = 0; j <= count2; j++)
				{
					floatValues[i * Cols + j] = unchecked(0 - (mask[checked(i * Cols + j)] ? 1 : 0));
				}
			}
			ExportAsFLT(filename, floatValues, -9999f);
		}
	}

	public void ExportAsFLT(string filename, byte[] mask)
	{
		checked
		{
			float[] floatValues = new float[Rows * Cols - 1 + 1];
			int count = Rows - 1;
			for (int i = 0; i <= count; i++)
			{
				int count2 = Cols - 1;
				for (int j = 0; j <= count2; j++)
				{
					floatValues[i * Cols + j] = unchecked((int)mask[checked(i * Cols + j)]);
				}
			}
			ExportAsFLT(filename, floatValues, -9999f);
		}
	}

	public void ExportAsFLT(string filename, bool[] mask)
	{
		checked
		{
			float[] floatValues = new float[Rows * Cols - 1 + 1];
			int count = Rows - 1;
			for (int i = 0; i <= count; i++)
			{
				int count2 = Cols - 1;
				for (int j = 0; j <= count2; j++)
				{
					floatValues[i * Cols + j] = unchecked(0 - (mask[checked(i * Cols + j)] ? 1 : 0));
				}
			}
			ExportAsFLT(filename, floatValues, -9999f);
		}
	}
}
