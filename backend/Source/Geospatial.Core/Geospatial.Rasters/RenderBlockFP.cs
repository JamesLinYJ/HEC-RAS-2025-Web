using System;
using System.Numerics;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public struct RenderBlockFP<T> where T : unmanaged, IBinaryFloatingPointIeee754<T>
{
	private RasterDefinition _sourceRaster;

	private ReadOnlyMemory<T> _values;

	private int _maxRow;

	private int _maxCol;

	public int CurrentRow;

	public int CurrentCol;

	private int _n;

	public bool PriorReturnValue;

	private T _a;

	private T _b;

	private T _c;

	private T _d;

	private T _e2;

	private readonly TileWithNeighbors<T> _mrRogers;

	private readonly T _noDataValue;

	private static readonly T Half = T.CreateChecked(0.5);

	private static readonly T One = T.One;

	public RenderBlockFP(TileWithNeighbors<T> neighborInfo)
		: this(neighborInfo.Center)
	{
		_mrRogers = neighborInfo;
	}

	public RenderBlockFP(RasterData<T> rd)
		: this(rd.RasterDefinition, rd.Data, rd.NoDataValue.HasValue ? rd.NoDataValue.Value : T.NaN)
	{
	}

	public RenderBlockFP(RasterDefinition tile, ReadOnlyMemory<T> tileValues, T noDataValue)
	{
		this = default;
		_noDataValue = noDataValue;
		SetTile(tile, tileValues);
	}

	public void SetTile(RasterDefinition sourceRaster, ReadOnlyMemory<T> values)
	{
		_sourceRaster = sourceRaster;
		_values = values;
		_maxRow = sourceRaster.Rows - 1;
		_maxCol = sourceRaster.Cols - 1;
		CurrentRow = -1;
		CurrentCol = -1;
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
		if (rbRow == CurrentRow && rbCol == CurrentCol)
		{
			return PriorReturnValue;
		}
		CurrentRow = rbRow;
		CurrentCol = rbCol;
		_n = CurrentCol + CurrentRow * _sourceRaster.Cols;
		ReadOnlySpan<T> buffer = _values.Span;
		_a = buffer[_n];
		if (rbCol == _maxCol)
		{
			if (rbRow == _maxRow)
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = _noDataValue;
					}
					else
					{
						_b = _mrRogers.Right.Data.Span[rbRow * _sourceRaster.Cols];
					}
					if (_mrRogers.LowerRight == null)
					{
						_c = _noDataValue;
					}
					else
					{
						_c = _mrRogers.LowerRight.Data.Span[0];
					}
					if (_mrRogers.Bottom == null)
					{
						_d = _noDataValue;
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
						_b = _noDataValue;
						_c = _noDataValue;
					}
					else
					{
						ReadOnlySpan<T> bufferSpan = _mrRogers.Right.Data.Span;
						_b = bufferSpan[rbRow * _sourceRaster.Cols];
						_c = bufferSpan[(rbRow + 1) * _sourceRaster.Cols];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
				}
				_d = buffer[_n + _sourceRaster.Cols];
			}
		}
		else if (rbRow == _maxRow)
		{
			if (_mrRogers != null)
			{
				if (_mrRogers.Bottom == null)
				{
					_c = _noDataValue;
					_d = _noDataValue;
				}
				else
				{
					ReadOnlySpan<T> buffer2 = _mrRogers.Bottom.Data.Span;
					_d = buffer2[rbCol];
					_c = buffer2[rbCol + 1];
				}
			}
			else
			{
				_c = _a;
				_d = _a;
			}
			_b = buffer[_n + 1];
		}
		else
		{
			_b = buffer[_n + 1];
			_c = buffer[_n + _sourceRaster.Cols + 1];
			_d = buffer[_n + _sourceRaster.Cols];
		}
		if (_a == _noDataValue || _b == _noDataValue || _c == _noDataValue || _d == _noDataValue)
		{
			PriorReturnValue = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * Half;
		PriorReturnValue = true;
		return true;
	}

	public bool SetPixel_SkipPrior(int rbRow, int rbCol)
	{
		CurrentRow = rbRow;
		CurrentCol = rbCol;
		_n = CurrentCol + CurrentRow * _sourceRaster.Cols;
		ReadOnlySpan<T> buffer = _values.Span;
		_a = buffer[_n];
		if (rbCol == _maxCol)
		{
			if (rbRow == _maxRow)
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = _noDataValue;
					}
					else
					{
						_b = _mrRogers.Right.Data.Span[rbRow * _sourceRaster.Cols];
					}
					if (_mrRogers.LowerRight == null)
					{
						_c = _noDataValue;
					}
					else
					{
						_c = _mrRogers.LowerRight.Data.Span[0];
					}
					if (_mrRogers.Bottom == null)
					{
						_d = _noDataValue;
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
						_b = _noDataValue;
						_c = _noDataValue;
					}
					else
					{
						ReadOnlySpan<T> bufferSpan = _mrRogers.Right.Data.Span;
						_b = bufferSpan[rbRow * _sourceRaster.Cols];
						_c = bufferSpan[(rbRow + 1) * _sourceRaster.Cols];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
				}
				_d = buffer[_n + _sourceRaster.Cols];
			}
		}
		else if (rbRow == _maxRow)
		{
			if (_mrRogers != null)
			{
				if (_mrRogers.Bottom == null)
				{
					_c = _noDataValue;
					_d = _noDataValue;
				}
				else
				{
					ReadOnlySpan<T> buffer2 = _mrRogers.Bottom.Data.Span;
					_d = buffer2[rbCol];
					_c = buffer2[rbCol + 1];
				}
			}
			else
			{
				_c = _a;
				_d = _a;
			}
			_b = buffer[_n + 1];
		}
		else
		{
			_b = buffer[_n + 1];
			_c = buffer[_n + _sourceRaster.Cols + 1];
			_d = buffer[_n + _sourceRaster.Cols];
		}
		if (_a == _noDataValue || _b == _noDataValue || _c == _noDataValue || _d == _noDataValue)
		{
			PriorReturnValue = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * Half;
		PriorReturnValue = true;
		return true;
	}

	public bool SetPixel_NoPerimeterCheck(int rbRow, int rbCol)
	{
		if (rbRow == CurrentRow && rbCol == CurrentCol)
		{
			return PriorReturnValue;
		}
		CurrentRow = rbRow;
		CurrentCol = rbCol;
		_n = CurrentCol + CurrentRow * _sourceRaster.Cols;
		ReadOnlySpan<T> buffer = _values.Span;
		_a = buffer[_n];
		_b = buffer[_n + 1];
		_c = buffer[_n + _sourceRaster.Cols + 1];
		_d = buffer[_n + _sourceRaster.Cols];
		if (_a == _noDataValue || _b == _noDataValue || _c == _noDataValue || _d == _noDataValue)
		{
			PriorReturnValue = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * Half;
		PriorReturnValue = true;
		return true;
	}

	public T Interpolate(T xfrac, T yfrac)
	{
		T oneAboveYfrac = One - yfrac;
		if (xfrac > yfrac)
		{
			if (xfrac < oneAboveYfrac)
			{
				T localValue = _b - _a;
				T e2Term = _e2 - (_a + _b);
				return _a + localValue * xfrac + e2Term * yfrac;
			}
			T e2Term2 = _e2 - (_b + _c);
			T localValue2 = _c - _b;
			return _b + e2Term2 * (One - xfrac) + localValue2 * yfrac;
		}
		if (xfrac > oneAboveYfrac)
		{
			T localValue3 = _c - _d;
			T e2Term3 = _e2 - (_c + _d);
			return _d + localValue3 * xfrac + e2Term3 * oneAboveYfrac;
		}
		T e2Term4 = _e2 - (_d + _a);
		T localValue4 = _d - _a;
		return _a + e2Term4 * xfrac + localValue4 * yfrac;
	}

	public T Interpolate_Bilinear(T xfrac, T yfrac)
	{
		T oneAboveXfrac = One - xfrac;
		T oneAboveYfrac = One - yfrac;
		T yfracTerm = oneAboveXfrac * yfrac;
		T xfracTimesYfrac = xfrac * yfrac;
		T localValue = oneAboveXfrac * oneAboveYfrac;
		T xfracTerm = xfrac * oneAboveYfrac;
		return localValue * _a + xfracTerm * _b + yfracTerm * _d + xfracTimesYfrac * _c;
	}
}
