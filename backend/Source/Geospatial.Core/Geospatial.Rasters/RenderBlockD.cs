using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public struct RenderBlockD
{
	private RasterDefinition _sourceRaster;

	private ReadOnlyMemory<double> _values;

	private int _maxRow;

	private int _maxCol;

	private int _row;

	private int _col;

	private int _n;

	private bool _retnVal;

	private double _a;

	private double _b;

	private double _c;

	private double _d;

	private double _e2;

	private readonly TileWithNeighbors<double> _mrRogers;

	public RenderBlockD(TileWithNeighbors<double> neighborInfo)
		: this(neighborInfo.Center)
	{
		_mrRogers = neighborInfo;
	}

	public RenderBlockD(RasterData<double> rd)
		: this(rd.RasterDefinition, rd.Data)
	{
	}

	public RenderBlockD(RasterDefinition tile, ReadOnlyMemory<double> tileValues)
	{
		this = default;
		SetTile(tile, tileValues);
	}

	public void SetTile(RasterDefinition sourceRaster, ReadOnlyMemory<double> values)
	{
		_sourceRaster = sourceRaster;
		_values = values;
		_maxRow = sourceRaster.Rows - 1;
		_maxCol = sourceRaster.Cols - 1;
		_row = -1;
		_col = -1;
		_n = -1;
	}

	public RasterDefinition GetRenderBlockRasterDefinition()
	{
		double scaledCellSize = 0.5 * _sourceRaster.CellSize;
		Point upperLeftCorner = _sourceRaster.Extent.UpperLeft.Translate(scaledCellSize, 0.0 - scaledCellSize);
		if (_mrRogers != null)
		{
			return new RasterDefinition(upperLeftCorner, _sourceRaster.CellSize, _sourceRaster.Rows, _sourceRaster.Cols);
		}
		return new RasterDefinition(upperLeftCorner, _sourceRaster.CellSize, _sourceRaster.Rows - 1, _sourceRaster.Cols - 1);
	}

	public bool SetPixel(int rbRow, int rbCol)
	{
		if (rbRow == _row && rbCol == _col)
		{
			return _retnVal;
		}
		_row = rbRow;
		_col = rbCol;
		_n = _col + _row * _sourceRaster.Cols;
		ReadOnlySpan<double> doubleSpan = _values.Span;
		_a = doubleSpan[_n];
		if (rbCol == _maxCol)
		{
			if (rbRow == _maxRow)
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = -9999.0;
					}
					else
					{
						_b = _mrRogers.Right.Data.Span[rbRow * _sourceRaster.Cols];
					}
					if (_mrRogers.LowerRight == null)
					{
						_c = -9999.0;
					}
					else
					{
						_c = _mrRogers.LowerRight.Data.Span[0];
					}
					if (_mrRogers.Bottom == null)
					{
						_d = -9999.0;
					}
					else
					{
						_d = _mrRogers.Bottom.Data.Span[_maxCol];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
					_d = _a;
				}
			}
			else
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = -9999.0;
						_c = -9999.0;
					}
					else
					{
						ReadOnlySpan<double> doubleSpan2 = _mrRogers.Right.Data.Span;
						_b = doubleSpan2[rbRow * _sourceRaster.Cols];
						_c = doubleSpan2[(rbRow + 1) * _sourceRaster.Cols];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
				}
				_d = doubleSpan[_n + _sourceRaster.Cols];
			}
		}
		else if (rbRow == _maxRow)
		{
			if (_mrRogers != null)
			{
				if (_mrRogers.Bottom == null)
				{
					_c = -9999.0;
					_d = -9999.0;
				}
				else
				{
					ReadOnlySpan<double> doubleSpan3 = _mrRogers.Bottom.Data.Span;
					_d = doubleSpan3[rbCol];
					_c = doubleSpan3[rbCol + 1];
				}
			}
			else
			{
				_c = _a;
				_d = _a;
			}
			_b = doubleSpan[_n + 1];
		}
		else
		{
			_b = doubleSpan[_n + 1];
			_c = doubleSpan[_n + _sourceRaster.Cols + 1];
			_d = doubleSpan[_n + _sourceRaster.Cols];
		}
		if (_a == -9999.0 || _b == -9999.0 || _c == -9999.0 || _d == -9999.0)
		{
			_retnVal = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * 0.5;
		_retnVal = true;
		return true;
	}

	public bool SetPixel_NoPerimeterCheck(int rbRow, int rbCol)
	{
		if (rbRow == _row && rbCol == _col)
		{
			return _retnVal;
		}
		_row = rbRow;
		_col = rbCol;
		_n = _col + _row * _sourceRaster.Cols;
		ReadOnlySpan<double> doubleSpan = _values.Span;
		_a = doubleSpan[_n];
		_b = doubleSpan[_n + 1];
		_c = doubleSpan[_n + _sourceRaster.Cols + 1];
		_d = doubleSpan[_n + _sourceRaster.Cols];
		if (_a == -9999.0 || _b == -9999.0 || _c == -9999.0 || _d == -9999.0)
		{
			_retnVal = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * 0.5;
		_retnVal = true;
		return true;
	}

	public double Interpolate(double xfrac, double yfrac)
	{
		double yfracOffset = 1.0 - yfrac;
		if (xfrac > yfrac)
		{
			if (xfrac < yfracOffset)
			{
				double computed = _b - _a;
				double e2Term = _e2 - (_a + _b);
				return _a + computed * xfrac + e2Term * yfrac;
			}
			double e2Term2 = _e2 - (_b + _c);
			double computed2 = _c - _b;
			return _b + e2Term2 * (1.0 - xfrac) + computed2 * yfrac;
		}
		if (xfrac > yfracOffset)
		{
			double computed3 = _c - _d;
			double e2Term3 = _e2 - (_c + _d);
			return _d + computed3 * xfrac + e2Term3 * yfracOffset;
		}
		double e2Term4 = _e2 - (_d + _a);
		double computed4 = _d - _a;
		return _a + e2Term4 * xfrac + computed4 * yfrac;
	}
}
